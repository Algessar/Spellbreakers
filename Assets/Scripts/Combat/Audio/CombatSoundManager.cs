using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Turns combat results into sound, and doubles as the ear-side debugger for them.
    ///
    /// Balance numbers and console lines answer "what happened", but they answer it slowly and only if you
    /// are looking at the right line. A parry that is actually a deflected bind that then hit the body reads
    /// as three different sounds instantly, which is the whole point of this component: the feedback uses
    /// the same vocabulary the rules use, so a wrong sound *is* a bug report.
    ///
    /// It hears the whole fight, not one character. <see cref="CombatSoundEvents"/> is raised once per
    /// resolved contact by <see cref="WeaponDamage"/>, so one manager in the scene covers the player, every
    /// enemy and the training dummy, and nothing needs wiring per character.
    ///
    /// Nothing here is required for combat to work. With no manager in the scene, no profile, or a muted
    /// profile, every request is dropped silently.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class CombatSoundManager : MonoBehaviour
    {
        /// <summary>Conventional Resources location, so the library works without anyone wiring it up.</summary>
        public const string ProfileResourcesPath = "CombatSoundProfile";

        private const int MaxCueLogLines = 8;

        public static CombatSoundManager Instance { get; private set; }

        [Header("Content")]
        [Tooltip("The clip library. Left empty, the manager loads Resources/CombatSoundProfile if one exists. " +
                 "Swap the asset to change every combat sound at once.")]
        [SerializeField] private CombatSoundProfile _profile;

        [Header("Mix")]
        [Tooltip("Master volume for every combat cue.")]
        [SerializeField, Range(0f, 1f)] private float _masterVolume = 1f;

        [Tooltip("How many sounds can overlap. A bind with two attackers easily needs four or five.")]
        [SerializeField, Range(1, 32)] private int _voiceCount = 8;

        [Header("Spatial")]
        [Tooltip("1 is fully 3D (attenuates with distance from the listener), 0 is flat 2D. For debugging a " +
                 "duel up close, 2D is sometimes clearer; for locating a fight in the world, 3D.")]
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f;

        [SerializeField, Min(0f)] private float _minDistance = 2f;

        [SerializeField, Min(0.01f)] private float _maxDistance = 30f;

        [Header("Debug")]
        [Tooltip("Logs one line per played cue, with who hit whom and the result, and why a cue that was " +
                 "requested stayed silent.")]
        [SerializeField] private bool _logCues;

        [Tooltip("Draws the last few cues in the corner of the screen. Off by default because it is a " +
                 "debugging aid, not part of the game's presentation.")]
        [SerializeField] private bool _showCueLog;

        [Tooltip("Optional key that plays every excerpt in the library in turn, so the whole set can be " +
                 "auditioned without picking a fight - which is also the quickest way to hear whether a " +
                 "sliced excerpt starts and ends where it should. None disables it.")]
        [SerializeField] private Key _auditionKey = Key.None;

        [Tooltip("Seconds between sounds when auditioning.")]
        [SerializeField, Min(0f)] private float _auditionInterval = 0.45f;

        /// <summary>One pooled source plus the envelope needed to cut an excerpt out of a longer file.</summary>
        private sealed class Voice
        {
            public AudioSource Source;
            public float Elapsed;
            public float Duration;
            public float FadeIn;
            public float FadeOut;
            public float TargetVolume;
            public bool Playing;
        }

        private Voice[] _voices;
        private int _nextVoice;
        private readonly float[] _lastPlayed = new float[Enum.GetValues(typeof(CombatSoundCue)).Length];
        private readonly string[] _cueLog = new string[MaxCueLogLines];
        private int _cueLogCount;
        private int _cueLogHead;

        private bool _reportedMissingProfile;
        private bool _reportedMissingListener;
        private bool _auditioning;
        private GUIStyle _logStyle;

        /// <summary>The library in use, after the Resources fallback. Null means combat audio is silent.</summary>
        public CombatSoundProfile Profile => _profile;

        /// <summary>The last cue that actually produced a sound, for debug overlays and tests.</summary>
        public CombatSoundCue? LastCue { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[Sound] '{name}' is a second CombatSoundManager; '{Instance.name}' already " +
                                 "owns combat audio, so this one is disabled.", this);
                enabled = false;
                return;
            }

            Instance = this;

            if (_profile == null)
                _profile = Resources.Load<CombatSoundProfile>(ProfileResourcesPath);

            BuildVoices();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable()
        {
            CombatSoundEvents.CueRequested += HandleCueRequested;
        }

        private void OnDisable()
        {
            CombatSoundEvents.CueRequested -= HandleCueRequested;
            StopAll();
            _auditioning = false;
        }

        private void Update()
        {
            UpdateVoices();

            if (_auditionKey == Key.None || _auditioning)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[_auditionKey].wasPressedThisFrame)
                return;

            StartCoroutine(AuditionLibrary());
        }

        private void Start()
        {
            if (_profile == null)
            {
                if (!_reportedMissingProfile)
                {
                    _reportedMissingProfile = true;
                    Debug.Log($"[Sound] No combat sound profile assigned or found at " +
                              $"Resources/{ProfileResourcesPath}, so combat is silent. This is valid - create " +
                              "one via the Spellbreakers/Audio menu when you want to hear results.");
                }

                return;
            }

            if (_reportedMissingListener || FindAnyObjectByType<AudioListener>() != null)
                return;

            _reportedMissingListener = true;
            Debug.LogWarning($"[Sound] No AudioListener in the scene, so combat audio cannot be heard. " +
                             "Cameras usually carry one.", this);
        }

        /// <summary>
        /// Plays a random excerpt for a cue. Public so anything can trigger feedback - a UI button, a scene
        /// builder, a test - not only the automatic <see cref="CombatSoundEvents"/> path.
        /// </summary>
        /// <returns>True when a sound actually started.</returns>
        public bool Play(CombatSoundCue cue, Vector3 position)
        {
            return TryPlay(cue, position, out _);
        }

        /// <summary>
        /// Plays one specific excerpt. Used by the audition sweep and by the editor slicer, where "the next
        /// random variation" is exactly the wrong thing to hear while judging a cut.
        /// </summary>
        public bool PlaySlice(CombatSoundCue cue, CombatSoundSlice slice, Vector3 position)
        {
            return TryPlaySlice(cue, slice, position, out _);
        }

        /// <summary>Replaces the clip library at runtime, for scene builders and A/B testing.</summary>
        public void SetProfile(CombatSoundProfile profile)
        {
            _profile = profile;
            _reportedMissingProfile = profile == null;
        }

        private void HandleCueRequested(CombatSoundEvent request)
        {
            var played = TryPlay(request.Cue, request.Position, out var reason);

            if (_logCues)
            {
                Debug.Log(played
                    ? $"[Sound] {request.Describe()}"
                    : $"[Sound] {request.Describe()} - silent: {reason}", this);
            }

            if (played && _showCueLog)
                AppendToCueLog(request);
        }

        /// <summary>
        /// The random-excerpt play path, with the reason it did nothing. A dropped cue is exactly as
        /// interesting as a played one when the question is "why can I not hear the parry", so the reason is
        /// produced here rather than guessed at by the caller.
        /// </summary>
        private bool TryPlay(CombatSoundCue cue, Vector3 position, out string reason)
        {
            if (!TryResolveSet(cue, out var set, out reason))
                return false;

            if (IsThrottled(cue, set, out reason))
                return false;

            var slice = set.PickSlice();
            if (slice == null)
            {
                reason = $"every excerpt in {cue} is unusable";
                return false;
            }

            return TryPlaySlice(cue, slice, position, out reason);
        }

        private bool TryPlaySlice(CombatSoundCue cue, CombatSoundSlice slice, Vector3 position,
            out string reason)
        {
            reason = null;

            if (slice == null || !slice.IsUsable)
            {
                reason = "the excerpt has no clip or no length";
                return false;
            }

            if (!TryResolveSet(cue, out var set, out reason))
                return false;

            var voice = RentVoice();
            if (voice == null)
            {
                reason = "no free voice";
                return false;
            }

            // An excerpt cannot be seeked into before its data is resident, and a clip with "Preload Audio
            // Data" off is only loaded on first use. Asking for it here makes the first play of a session the
            // only one that could ever miss its offset, instead of every play until something loads it.
            if (slice.Clip.loadState != AudioDataLoadState.Loaded)
                slice.Clip.LoadAudioData();

            var pitch = set.PickPitch();
            var duration = slice.DurationSeconds / pitch;

            voice.Source.Stop();
            voice.Source.clip = slice.Clip;
            voice.Source.pitch = pitch;

            // Seek by sample before playing, so an excerpt that starts mid-file never lets a few milliseconds
            // of whatever came before it through.
            voice.Source.timeSamples = slice.StartSample;

            voice.TargetVolume = Mathf.Clamp01(set.Volume * _masterVolume * slice.Gain);
            voice.Duration = duration;
            voice.FadeIn = Mathf.Min(slice.FadeIn, duration);
            voice.FadeOut = Mathf.Min(slice.FadeOut, duration);
            voice.Elapsed = 0f;
            voice.Playing = true;
            voice.Source.volume = voice.FadeIn > 0f ? 0f : voice.TargetVolume;
            voice.Source.Play();

            var index = (int)cue;
            if (index >= 0 && index < _lastPlayed.Length)
                _lastPlayed[index] = Time.unscaledTime;

            LastCue = cue;
            return true;
        }

        private bool TryResolveSet(CombatSoundCue cue, out CombatSoundClips set, out string reason)
        {
            set = null;
            reason = null;

            if (_profile == null)
            {
                reason = "no profile";
                return false;
            }

            if (_profile.Muted)
            {
                reason = "profile is muted";
                return false;
            }

            if (!_profile.TryGet(cue, out set) || set == null)
            {
                reason = $"no row for {cue}";
                return false;
            }

            if (!set.HasSounds)
            {
                reason = $"no excerpts assigned to {cue}";
                return false;
            }

            return true;
        }

        private bool IsThrottled(CombatSoundCue cue, CombatSoundClips set, out string reason)
        {
            reason = null;

            var index = (int)cue;
            if (index < 0 || index >= _lastPlayed.Length)
                return false;

            if (Time.unscaledTime - _lastPlayed[index] >= set.MinInterval)
                return false;

            reason = "retriggered faster than its minimum interval";
            return true;
        }

        /// <summary>
        /// Applies the fade envelope and stops a voice when its excerpt ends. The envelope is driven here
        /// rather than with an animation curve because it has to follow unscaled time: audio keeps playing
        /// while the game is paused, so a scaled clock would freeze the fade and leave a sliced excerpt
        /// ringing past its end.
        /// </summary>
        private void UpdateVoices()
        {
            if (_voices == null)
                return;

            var delta = Time.unscaledDeltaTime;

            for (var i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];
                if (voice == null || !voice.Playing)
                    continue;

                voice.Elapsed += delta;
                var remaining = voice.Duration - voice.Elapsed;

                if (remaining <= 0f)
                {
                    StopVoice(voice);
                    continue;
                }

                var envelope = 1f;
                if (voice.FadeIn > 0f)
                    envelope *= Mathf.Clamp01(voice.Elapsed / voice.FadeIn);

                if (voice.FadeOut > 0f)
                    envelope *= Mathf.Clamp01(remaining / voice.FadeOut);

                voice.Source.volume = voice.TargetVolume * envelope;
            }
        }

        private void BuildVoices()
        {
            var count = Mathf.Clamp(_voiceCount, 1, 32);

            // A pool rather than one source: several contacts can resolve in the same frame, and reusing a
            // single source would cut the previous sound off mid-contact.
            _voices = new Voice[count];
            for (var i = 0; i < count; i++)
            {
                var voiceObject = new GameObject($"Voice {i}");
                voiceObject.transform.SetParent(transform, false);

                var source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = _spatialBlend;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = _minDistance;
                source.maxDistance = _maxDistance;

                // Doppler would fight the per-play pitch variation: a blade crossing the listener would be
                // pitch-shifted by physics on top of the deliberate randomisation, and the cue would stop
                // reading as its outcome.
                source.dopplerLevel = 0f;

                _voices[i] = new Voice { Source = source };
            }
        }

        private Voice RentVoice()
        {
            if (_voices == null || _voices.Length == 0)
                return null;

            for (var i = 0; i < _voices.Length; i++)
            {
                var index = (i + _nextVoice) % _voices.Length;
                var candidate = _voices[index];
                if (candidate != null && !candidate.Playing)
                {
                    _nextVoice = (index + 1) % _voices.Length;
                    return candidate;
                }
            }

            // Every voice is busy. Stealing the next one in order is better than dropping the cue: the
            // result that matters is usually the newest, and it is the one being debugged.
            var stolen = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            return stolen;
        }

        private void StopVoice(Voice voice)
        {
            if (voice == null)
                return;

            voice.Playing = false;

            if (voice.Source != null)
                voice.Source.Stop();
        }

        private void StopAll()
        {
            if (_voices == null)
                return;

            for (var i = 0; i < _voices.Length; i++)
                StopVoice(_voices[i]);
        }

        /// <summary>
        /// Plays every excerpt of every cue once, in order. Balancing a library by fighting for each sound is
        /// slow, and impossible to compare two variations that way; back to back is how you hear which one is
        /// wrong - and whether a sliced excerpt ends on the decay or after it.
        /// </summary>
        private System.Collections.IEnumerator AuditionLibrary()
        {
            if (_profile == null || _profile.Muted)
                yield break;

            _auditioning = true;

            // Played at the listener rather than in front of the player, so the audition is audible whatever
            // the spatial settings and wherever the character happens to be standing.
            var camera = Camera.main;
            var position = camera != null ? camera.transform.position : transform.position;
            var gap = new WaitForSecondsRealtime(Mathf.Max(0f, _auditionInterval));

            foreach (CombatSoundCue cue in Enum.GetValues(typeof(CombatSoundCue)))
            {
                if (!_profile.TryGet(cue, out var set) || set == null)
                    continue;

                var slices = set.GetSlices();
                for (var i = 0; i < slices.Length; i++)
                {
                    var slice = slices[i];
                    if (slice == null || !slice.IsUsable)
                        continue;

                    var played = TryPlaySlice(cue, slice, position, out var reason);

                    if (_logCues)
                    {
                        Debug.Log(played
                            ? $"[Sound] audition {cue} [{i}] {slice.DescribeWindow()}"
                            : $"[Sound] audition {cue} [{i}] silent ({reason})", this);
                    }

                    yield return gap;
                }
            }

            _auditioning = false;
        }

        private void AppendToCueLog(CombatSoundEvent request)
        {
            _cueLog[_cueLogHead] = $"{Time.time,6:0.00}s  {request.Describe()}";
            _cueLogHead = (_cueLogHead + 1) % MaxCueLogLines;
            _cueLogCount = Mathf.Min(_cueLogCount + 1, MaxCueLogLines);
        }

        private void OnGUI()
        {
            if (!_showCueLog || !Application.isPlaying || _cueLogCount == 0)
                return;

            if (_logStyle == null)
                _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false };

            const float lineHeight = 15f;
            var height = _cueLogCount * lineHeight + 8f;
            var top = Screen.height - height - 10f;

            // Unity's == rather than ?. here: a deleted profile is a "fake null" reference, and ?. would
            // call into it and throw.
            var profile = _profile;
            var library = profile != null
                ? $"{profile.AssignedCueCount} cues / {profile.SliceCount} excerpts"
                : "no profile";

            GUI.Label(new Rect(10f, top - lineHeight, 460f, lineHeight),
                $"COMBAT SOUND  [{library}]",
                _logStyle);

            // Oldest first, so the newest line is always last and does not move.
            for (var i = 0; i < _cueLogCount; i++)
            {
                var slot = (_cueLogHead - _cueLogCount + i + MaxCueLogLines * 2) % MaxCueLogLines;
                GUI.Label(new Rect(10f, top + i * lineHeight, 620f, lineHeight), _cueLog[slot], _logStyle);
            }
        }

        private void OnValidate()
        {
            _voiceCount = Mathf.Clamp(_voiceCount, 1, 32);
            _maxDistance = Mathf.Max(0.01f, _maxDistance);
            _minDistance = Mathf.Max(0f, _minDistance);

            if (_maxDistance < _minDistance)
                _maxDistance = _minDistance;

            // Spatial settings are baked into the voices, so retune live ones when they are edited at runtime.
            if (_voices == null)
                return;

            for (var i = 0; i < _voices.Length; i++)
            {
                var source = _voices[i]?.Source;
                if (source == null)
                    continue;

                source.spatialBlend = _spatialBlend;
                source.minDistance = _minDistance;
                source.maxDistance = _maxDistance;
            }
        }

        /// <summary>
        /// Creates a manager if the scene has none and a profile exists in Resources.
        ///
        /// The alternative is what happened to <see cref="CombatRulesBootstrap"/>: a component that exists,
        /// compiles, and is in no scene, so it silently does nothing. Dropping a profile into a Resources
        /// folder is then enough to hear the whole fight in any scene - including ones built by tooling that
        /// knows nothing about audio.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstanceForLoadedScenes()
        {
            if (Instance != null)
                return;

            if (Resources.Load<CombatSoundProfile>(ProfileResourcesPath) == null)
                return;

            var host = new GameObject("Combat Sound Manager");
            host.AddComponent<CombatSoundManager>();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// One playable excerpt of an audio file: a clip plus the window inside it to play.
    ///
    /// This exists because sound packs are sold as compilations - one 40-second file holding thirty sword
    /// hits - and because an imported file is one asset, so without a window there is no way to use eleven
    /// hits out of it. Cutting the file up into new assets would make the importer's settings, the licence
    /// and the version history all worse; instead the window is data, and the file stays as it arrived.
    ///
    /// A window with <see cref="LengthSeconds"/> of zero plays to the end of the clip, so converting a plain
    /// clip to a slice is just "clip, start 0, length 0".
    /// </summary>
    [Serializable]
    public sealed class CombatSoundSlice
    {
        [Tooltip("The audio file this excerpt lives in.")]
        [SerializeField] private AudioClip _clip;

        [Tooltip("Seconds into the clip where the excerpt starts.")]
        [SerializeField, Min(0f)] private float _start;

        [Tooltip("Length of the excerpt in seconds. 0 plays to the end of the file.")]
        [SerializeField, Min(0f)] private float _length;

        [Tooltip("Seconds of fade in. A hard start on a sliced excerpt usually clicks; a few milliseconds " +
                 "removes it without audible attack softening.")]
        [SerializeField, Min(0f)] private float _fadeIn = 0.005f;

        [Tooltip("Seconds of fade out. This is the tail, so it cuts the sound rather than the file.")]
        [SerializeField, Min(0f)] private float _fadeOut = 0.02f;

        [Tooltip("Level trim for this excerpt, for pulling one loud hit out of a compilation down to match " +
                 "its neighbours.")]
        [SerializeField, Range(0f, 2f)] private float _gain = 1f;

        public AudioClip Clip => _clip;

        public float StartSeconds => _clip == null ? 0f : Mathf.Clamp(_start, 0f, Mathf.Max(0f, _clip.length));

        public float FadeIn => Mathf.Max(0f, _fadeIn);

        public float FadeOut => Mathf.Max(0f, _fadeOut);

        public float Gain => Mathf.Clamp(_gain, 0f, 2f);

        /// <summary>Where the excerpt ends, resolved against the real clip length.</summary>
        public float EndSeconds
        {
            get
            {
                if (_clip == null)
                    return 0f;

                var start = StartSeconds;
                var clipLength = Mathf.Max(0f, _clip.length);
                return _length <= 0f ? clipLength : Mathf.Min(clipLength, start + _length);
            }
        }

        /// <summary>How long the excerpt actually plays. Zero means it is not usable.</summary>
        public float DurationSeconds => Mathf.Max(0f, EndSeconds - StartSeconds);

        public bool IsUsable => _clip != null && DurationSeconds > 0.0001f;

        /// <summary>
        /// Sample offset for <see cref="AudioSource.timeSamples"/>. Seeking by sample rather than by seconds
        /// keeps the cut on the frame the author saw rather than wherever float rounding lands.
        /// </summary>
        public int StartSample
        {
            get
            {
                if (_clip == null)
                    return 0;

                var frequency = Mathf.Max(1, _clip.frequency);
                var sample = Mathf.RoundToInt(StartSeconds * frequency);
                return Mathf.Clamp(sample, 0, Mathf.Max(0, _clip.samples - 1));
            }
        }

        /// <summary>Human-readable window, for console lines and the slicer window.</summary>
        public string DescribeWindow()
        {
            if (_clip == null)
                return "(no clip)";

            return $"{_clip.name} {StartSeconds:0.00}-{EndSeconds:0.00}s";
        }

        /// <summary>Builds a slice at runtime, for tooling and for the editor slicer.</summary>
        public static CombatSoundSlice CreateTransient(AudioClip clip, float start, float length,
            float fadeIn = 0.005f, float fadeOut = 0.02f)
        {
            return new CombatSoundSlice
            {
                _clip = clip,
                _start = Mathf.Max(0f, start),
                _length = Mathf.Max(0f, length),
                _fadeIn = Mathf.Max(0f, fadeIn),
                _fadeOut = Mathf.Max(0f, fadeOut),
            };
        }

        public void SetWindow(float start, float length)
        {
            _start = Mathf.Max(0f, start);
            _length = Mathf.Max(0f, length);
        }

        public void SetClip(AudioClip clip)
        {
            _clip = clip;
        }

        public void SetFades(float fadeIn, float fadeOut)
        {
            _fadeIn = Mathf.Max(0f, fadeIn);
            _fadeOut = Mathf.Max(0f, fadeOut);
        }
    }

    /// <summary>
    /// The excerpts that answer one <see cref="CombatSoundCue"/>, plus how they vary.
    ///
    /// A single sound per outcome becomes recognisable within a minute of playtesting, at which point it
    /// stops carrying information and starts being noise. So one cue owns a *set*: any number of slices,
    /// picked at random without repeating the same one twice in a row, with a pitch applied per play. Two
    /// parries in a row then sound like two parries rather than one sound played twice, which matters here
    /// because the parry is the thing being debugged.
    ///
    /// The set also owns a retrigger interval: a bind stall or a wide sweep can resolve several contacts in
    /// the same frame, and stacking six copies of the same sound is not more informative, just louder.
    /// </summary>
    [Serializable]
    public sealed class CombatSoundClips
    {
        [Tooltip("Excerpts for this cue. One is picked at random per play. Empty means silent.\n\n" +
                 "Use Spellbreakers/Audio/Combat Sound Slicer to cut excerpts out of a compilation file.")]
        [SerializeField] private CombatSoundSlice[] _slices = Array.Empty<CombatSoundSlice>();

        [Tooltip("Base volume for this cue, before the manager's master volume.")]
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;

        [Tooltip("Pitch range applied per play. Unity's pitch also changes duration, so a wide range is a " +
                 "cheap way to make one excerpt sound like several. Leave both at 1 to turn it off.")]
        [SerializeField] private Vector2 _pitchRange = new Vector2(0.94f, 1.06f);

        [Tooltip("Minimum seconds between two plays of this cue. Guards against several contacts in one " +
                 "frame stacking into a single loud click.")]
        [SerializeField, Min(0f)] private float _minInterval = 0.04f;

        [Tooltip("When on, the same excerpt is not chosen twice in a row while others are available.")]
        [SerializeField] private bool _avoidImmediateRepeat = true;

        /// <summary>Raw index of the excerpt played last, so the next pick can skip it.</summary>
        [NonSerialized] private int _lastIndex = -1;

        public float Volume => Mathf.Clamp01(_volume);

        public float MinInterval => Mathf.Max(0f, _minInterval);

        public float MinPitch => Mathf.Min(_pitchRange.x, _pitchRange.y);

        public float MaxPitch => Mathf.Max(_pitchRange.x, _pitchRange.y);

        /// <summary>True when at least one excerpt is actually playable.</summary>
        public bool HasSounds
        {
            get
            {
                for (var i = 0; i < SliceCount; i++)
                {
                    if (_slices[i].IsUsable)
                        return true;
                }

                return false;
            }
        }

        /// <summary>Number of usable excerpts, which is what "how many variations" means.</summary>
        public int SliceCount
        {
            get
            {
                if (_slices == null)
                    return 0;

                var count = 0;
                for (var i = 0; i < _slices.Length; i++)
                {
                    if (_slices[i] != null && _slices[i].IsUsable)
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        /// Chooses the next excerpt. Returns null when the set is empty or every entry is unusable, which
        /// callers treat as "this cue is silent" rather than as an error.
        /// </summary>
        public CombatSoundSlice PickSlice()
        {
            if (_slices == null || _slices.Length == 0)
                return null;

            var usable = SliceCount;
            if (usable == 0)
                return null;

            if (usable == 1)
            {
                for (var i = 0; i < _slices.Length; i++)
                {
                    if (_slices[i] == null || !_slices[i].IsUsable)
                        continue;

                    _lastIndex = i;
                    return _slices[i];
                }
            }

            // Only skip the previous excerpt when it is still a real candidate; otherwise the choice would be
            // silently narrowed for no reason.
            var skip = _avoidImmediateRepeat && _lastIndex >= 0 && _lastIndex < _slices.Length &&
                       _slices[_lastIndex] != null && _slices[_lastIndex].IsUsable
                ? _lastIndex
                : -1;

            var candidates = skip >= 0 ? usable - 1 : usable;
            var pick = Random.Range(0, candidates);

            var seen = 0;
            for (var i = 0; i < _slices.Length; i++)
            {
                if (_slices[i] == null || !_slices[i].IsUsable || i == skip)
                    continue;

                if (seen == pick)
                {
                    _lastIndex = i;
                    return _slices[i];
                }

                seen++;
            }

            // Unreachable for a consistent set; falling back keeps a broken edit silent rather than throwing
            // mid-strike.
            return null;
        }

        public float PickPitch()
        {
            var min = MinPitch;
            var max = MaxPitch;
            var pitch = Mathf.Approximately(min, max) ? min : Random.Range(min, max);

            // Zero pitch does not mean "unpitched", it means "paused"; and beyond 3 Unity clamps anyway.
            return Mathf.Clamp(pitch <= 0f ? 1f : pitch, 0.01f, 3f);
        }

        /// <summary>The authored excerpts, so tooling and tests can read them without serialization tricks.</summary>
        public CombatSoundSlice[] GetSlices()
        {
            return _slices ?? Array.Empty<CombatSoundSlice>();
        }

        public void SetSlices(CombatSoundSlice[] slices)
        {
            _slices = slices ?? Array.Empty<CombatSoundSlice>();
            _lastIndex = -1;
        }

        /// <summary>Appends excerpts, which is what the slicer does to a cue it has just cut up.</summary>
        public void AddSlices(IEnumerable<CombatSoundSlice> slices)
        {
            if (slices == null)
                return;

            var added = new List<CombatSoundSlice>(_slices ?? Array.Empty<CombatSoundSlice>());
            added.AddRange(slices);
            _slices = added.ToArray();
            _lastIndex = -1;
        }

        /// <summary>
        /// Replaces the set with whole-file excerpts. The convenience path for hand-assigned clips and for
        /// the auto-assign tooling: a file with no window is simply an excerpt covering all of it.
        /// </summary>
        public void SetClips(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                SetSlices(Array.Empty<CombatSoundSlice>());
                return;
            }

            var slices = new CombatSoundSlice[clips.Length];
            for (var i = 0; i < clips.Length; i++)
                slices[i] = clips[i] == null ? null : CombatSoundSlice.CreateTransient(clips[i], 0f, 0f);

            SetSlices(slices);
        }

        /// <summary>Each excerpt's clip, for tooling that wants to know what files a cue draws on.</summary>
        public AudioClip[] GetClips()
        {
            if (_slices == null || _slices.Length == 0)
                return Array.Empty<AudioClip>();

            var clips = new List<AudioClip>(_slices.Length);
            for (var i = 0; i < _slices.Length; i++)
            {
                if (_slices[i] != null && _slices[i].Clip != null)
                    clips.Add(_slices[i].Clip);
            }

            return clips.ToArray();
        }

        /// <summary>Builds a set at runtime, for tests and for scene builders.</summary>
        public static CombatSoundClips CreateTransient(AudioClip[] clips, float volume = 1f,
            Vector2 pitchRange = default, float minInterval = 0.04f)
        {
            var set = new CombatSoundClips
            {
                _volume = Mathf.Clamp01(volume),
                _pitchRange = pitchRange == default ? new Vector2(1f, 1f) : pitchRange,
                _minInterval = Mathf.Max(0f, minInterval),
            };

            set.SetClips(clips);
            return set;
        }
    }

    /// <summary>
    /// The whole combat sound library as data: one row per <see cref="CombatSoundCue"/>, each row holding
    /// the excerpts that answer it.
    ///
    /// It is an asset rather than fields on the manager so the library can be swapped in one drag ("let me
    /// hear the new pack"), shared between scenes, and version-controlled as data - the same reasoning as
    /// <see cref="CombatRulesAsset"/>. An empty or missing asset is a valid configuration: the game is
    /// silent, and nothing else changes.
    ///
    /// Rows are explicit rather than a cue-keyed dictionary because Unity cannot serialize a dictionary and
    /// because a designer should be able to read and diff the list.
    /// </summary>
    [CreateAssetMenu(menuName = "Spellbreakers/Combat Sound Profile", fileName = "Sound_Combat")]
    public sealed class CombatSoundProfile : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public CombatSoundCue Cue;

            [Tooltip("Excerpts answering this cue. Drag several in to get variations.")]
            public CombatSoundClips Clips;
        }

        [Tooltip("One row per cue. Rerun Spellbreakers/Audio/Create Combat Sound Profile to add rows for any " +
                 "cues missing after the enum grows - existing rows and assignments are untouched.")]
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        [Tooltip("Silences the whole profile without losing the assignments. Handy for A/B testing whether " +
                 "a cue is actually earning its place.")]
        [SerializeField] private bool _muted;

        [NonSerialized] private Dictionary<CombatSoundCue, CombatSoundClips> _lookup;

        public bool Muted => _muted;

        /// <summary>How many excerpts the whole library holds, for a quick sanity check in the inspector.</summary>
        public int SliceCount
        {
            get
            {
                if (_entries == null)
                    return 0;

                var total = 0;
                for (var i = 0; i < _entries.Length; i++)
                {
                    var clips = _entries[i].Clips;
                    if (clips != null)
                        total += clips.SliceCount;
                }

                return total;
            }
        }

        /// <summary>How many cues actually have something to play. Missing rows and empty sets are skipped.</summary>
        public int AssignedCueCount
        {
            get
            {
                if (_entries == null)
                    return 0;

                var total = 0;
                for (var i = 0; i < _entries.Length; i++)
                {
                    var clips = _entries[i].Clips;
                    if (clips != null && clips.HasSounds)
                        total++;
                }

                return total;
            }
        }

        public bool TryGet(CombatSoundCue cue, out CombatSoundClips clips)
        {
            EnsureLookup();

            if (_lookup.TryGetValue(cue, out clips) && clips != null)
                return true;

            clips = null;
            return false;
        }

        /// <summary>
        /// Adds a row for every cue that does not have one, so the inspector shows the full vocabulary even
        /// after the enum gains members. Existing rows, order and assignments are left alone.
        /// </summary>
        public void EnsureAllCues()
        {
            var existing = new HashSet<CombatSoundCue>();
            if (_entries != null)
            {
                for (var i = 0; i < _entries.Length; i++)
                    existing.Add(_entries[i].Cue);
            }

            var added = new List<Entry>();
            foreach (CombatSoundCue cue in Enum.GetValues(typeof(CombatSoundCue)))
            {
                if (existing.Contains(cue))
                    continue;

                added.Add(new Entry { Cue = cue, Clips = new CombatSoundClips() });
            }

            if (added.Count == 0)
                return;

            var combined = new Entry[(_entries?.Length ?? 0) + added.Count];
            if (_entries != null && _entries.Length > 0)
                Array.Copy(_entries, combined, _entries.Length);
            added.CopyTo(combined, _entries?.Length ?? 0);
            _entries = combined;

            _lookup = null;
        }

        /// <summary>Replaces the whole row list. Used by the editor tooling and by tests.</summary>
        public void SetEntries(Entry[] entries)
        {
            _entries = entries ?? Array.Empty<Entry>();
            _lookup = null;
        }

        /// <summary>Reads the rows in authoring order. Provided so tooling and tests do not need reflection.</summary>
        public Entry[] GetEntries()
        {
            return _entries ?? Array.Empty<Entry>();
        }

        private void OnEnable()
        {
            _lookup = null;

            // A freshly created asset starts empty, which would show the author nothing at all. Filling it
            // here only ever happens on an asset that has never been configured.
            if (_entries == null || _entries.Length == 0)
                EnsureAllCues();
        }

        private void OnValidate()
        {
            // Editing the rows in the inspector must not leave the manager playing stale clips.
            _lookup = null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
                return;

            _lookup = new Dictionary<CombatSoundCue, CombatSoundClips>();
            if (_entries == null)
                return;

            for (var i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i];

                // First row wins, so a duplicated cue is harmless rather than order-dependent guesswork.
                if (!_lookup.ContainsKey(entry.Cue))
                    _lookup.Add(entry.Cue, entry.Clips);
            }
        }
    }
}

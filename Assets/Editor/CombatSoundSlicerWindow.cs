using System.Collections.Generic;
using Spellbreakers.Combat;
using UnityEditor;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Cuts excerpts out of a compilation file and files them onto a combat cue, by ear.
    ///
    /// A sound pack arrives as one long file, so every hit in it is unusable until something can point at
    /// "11.32s for 0.18s". This window is that thing: it draws the file's level envelope, marks the sounds
    /// it found, lets each one be auditioned in place, and appends the ones worth keeping to the cue row in
    /// the profile. The audio file itself is never modified - the window is stored as data, which is what
    /// keeps the importer settings, the licence and the version history intact.
    ///
    /// Open with Spellbreakers / Audio / Combat Sound Slicer.
    /// </summary>
    internal sealed class CombatSoundSlicerWindow : EditorWindow
    {
        private const string ProfilePath = "Assets/Resources/" + CombatSoundManager.ProfileResourcesPath + ".asset";
        private const float WaveformHeight = 96f;
        private const float SegmentListHeight = 200f;

        private CombatSoundProfile _profile;
        private AudioClip _clip;
        private CombatSoundCue _cue = CombatSoundCue.Swing;

        private readonly CombatSoundSlicer.Settings _settings = new CombatSoundSlicer.Settings();
        private float _fadeIn = 0.005f;
        private float _fadeOut = 0.02f;

        private CombatSoundSlicer.Analysis _analysis;
        private string _message;
        private bool _messageIsError;
        private Vector2 _scroll;

        private Texture2D _waveform;
        private int _waveformWidth;
        private AudioClip _waveformClip;

        private float _previewStart;
        private float _previewLength;
        private double _previewStartedAt;
        private bool _previewing;

        [MenuItem("Spellbreakers/Audio/Combat Sound Slicer", priority = 13)]
        private static void Open()
        {
            var window = GetWindow<CombatSoundSlicerWindow>("Combat Slicer");
            window.minSize = new Vector2(560f, 460f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_profile == null)
                _profile = AssetDatabase.LoadAssetAtPath<CombatSoundProfile>(ProfilePath);

            AdoptSelectionAsClip();
        }

        private void OnDisable()
        {
            StopPreview();
            DestroyWaveform();
        }

        private void OnSelectionChange()
        {
            AdoptSelectionAsClip();
            Repaint();
        }

        private void Update()
        {
            if (!_previewing)
                return;

            // The playhead is the only reason to repaint while idle, and it stops being interesting once the
            // preview is over.
            if (EditorApplication.timeSinceStartup - _previewStartedAt > Mathf.Max(0.05f, _previewLength))
                _previewing = false;

            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Combat Sound Slicer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Cuts excerpts out of a compilation file without touching the file. Detected excerpts are " +
                "appended to the chosen cue as windows (clip + start + length), which the sound manager seeks " +
                "into and stops at the end of.", MessageType.None);

            DrawSources();
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.Space();
            DrawAnalysis();
        }

        private void DrawSources()
        {
            _profile = (CombatSoundProfile)EditorGUILayout.ObjectField(
                new GUIContent("Profile", "The library the excerpts are appended to."),
                _profile, typeof(CombatSoundProfile), false);

            EditorGUI.BeginChangeCheck();

            _clip = (AudioClip)EditorGUILayout.ObjectField(
                new GUIContent("Clip", "The file to cut up. Selecting an AudioClip in the Project window " +
                                       "also loads it here."),
                _clip, typeof(AudioClip), false);

            if (!EditorGUI.EndChangeCheck())
                return;

            // A different file means the previous analysis and its waveform are meaningless.
            _analysis = null;
            DestroyWaveform();

            if (_clip == null)
                _message = null;
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Detection", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _settings.ThresholdDb = EditorGUILayout.Slider(
                    new GUIContent("Silence gate (dB)", "Level below the file's own peak that counts as " +
                                                       "silence. Lower it if quiet tails are being cut off."),
                    _settings.ThresholdDb, -80f, -6f);

                _settings.MaxSegments = EditorGUILayout.IntSlider(
                    new GUIContent("Max excerpts", "Keeps the loudest when a file holds more than this."),
                    _settings.MaxSegments, 1, 128);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _settings.EndHysteresisDb = EditorGUILayout.Slider(
                    new GUIContent("Tail (dB below gate)", "How far a sound must fall before it counts as " +
                                                          "finished. Raise it to keep more decay; lower it " +
                                                          "if two sounds are being joined."),
                    _settings.EndHysteresisDb, 0f, 40f);

                _settings.MinGapSeconds = EditorGUILayout.Slider(
                    new GUIContent("Min gap (s)", "Silence shorter than this does not split one sound into two."),
                    _settings.MinGapSeconds, 0.01f, 0.5f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _settings.MinLengthSeconds = EditorGUILayout.Slider(
                    new GUIContent("Min length (s)", "Shorter blips are discarded."),
                    _settings.MinLengthSeconds, 0.005f, 0.5f);

                _settings.PaddingSeconds = EditorGUILayout.Slider(
                    new GUIContent("Padding (s)", "Kept either side of the detected sound."),
                    _settings.PaddingSeconds, 0f, 0.1f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_clip == null))
                {
                    if (GUILayout.Button("Analyse", GUILayout.Height(18f)))
                        Analyse();
                }

                _fadeIn = EditorGUILayout.Slider(
                    new GUIContent("Fade in (s)", "A cut that starts mid-waveform clicks without this."),
                    _fadeIn, 0f, 0.05f);

                _fadeOut = EditorGUILayout.Slider(
                    new GUIContent("Fade out (s)", "Trims the tail and stops a clipped excerpt from cutting hard."),
                    _fadeOut, 0f, 0.2f);
            }
        }

        private void DrawAnalysis()
        {
            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, _messageIsError ? MessageType.Warning : MessageType.Info);

            if (_analysis == null)
                return;

            // The file can be deleted from under an open window; dropping the analysis is better than a
            // MissingReferenceException on every repaint.
            if (_analysis.Clip == null)
            {
                _analysis = null;
                DestroyWaveform();
                return;
            }

            EditorGUILayout.LabelField("File", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"{_analysis.Clip.name}   {_analysis.ClipLength:0.00}s   " +
                    $"{_analysis.Segments.Count} excerpts found",
                    EditorStyles.miniLabel);

                if (GUILayout.Button("Play file", EditorStyles.miniButton, GUILayout.Width(70f)))
                    PlayWindow(0f, _analysis.ClipLength);
            }

            DrawWaveform();
            EditorGUILayout.Space();
            DrawPasteRow();
            DrawSegmentList();
        }

        private void DrawWaveform()
        {
            var rect = GUILayoutUtility.GetRect(10f, WaveformHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
                return;

            EnsureWaveform(Mathf.Max(8, Mathf.RoundToInt(rect.width)));
            GUI.DrawTexture(rect, _waveform, ScaleMode.StretchToFill);

            var length = Mathf.Max(0.0001f, _analysis.ClipLength);

            for (var i = 0; i < _analysis.Segments.Count; i++)
            {
                var segment = _analysis.Segments[i];
                var x = rect.x + segment.Start / length * rect.width;
                var width = Mathf.Max(2f, segment.Length / length * rect.width);
                EditorGUI.DrawRect(new Rect(x, rect.y, width, rect.height), new Color(0.25f, 0.95f, 0.45f, 0.18f));
            }

            if (_previewing)
            {
                var elapsed = (float)(EditorApplication.timeSinceStartup - _previewStartedAt);
                var progress = Mathf.Clamp01((_previewStart + elapsed) / length);
                var x = rect.x + progress * rect.width;
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), new Color(1f, 0.85f, 0.2f, 1f));
            }
        }

        private void DrawPasteRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _cue = (CombatSoundCue)EditorGUILayout.EnumPopup(
                    new GUIContent("Cue", "Which combat result these excerpts answer."), _cue);

                var existing = CountForCue(_cue);

                using (new EditorGUI.DisabledScope(_profile == null))
                {
                    if (GUILayout.Button($"Append all {_analysis.Segments.Count} to {_cue}", GUILayout.Width(240f)))
                        Append(_analysis.Segments, $"append {_analysis.Segments.Count} excerpts to {_cue}");

                    using (new EditorGUI.DisabledScope(existing == 0))
                    {
                        if (GUILayout.Button($"Clear {_cue} ({existing})", GUILayout.Width(140f)))
                            ClearCue();
                    }
                }
            }

            if (_profile == null)
                EditorGUILayout.HelpBox(
                    "No profile assigned, so excerpts cannot be filed. Run Spellbreakers/Audio/Create Combat " +
                    "Sound Profile.", MessageType.Warning);
        }

        private void DrawSegmentList()
        {
            EditorGUILayout.LabelField("Excerpts", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(SegmentListHeight));

            for (var i = 0; i < _analysis.Segments.Count; i++)
            {
                var segment = _analysis.Segments[i];

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"#{i + 1:00}   {segment.Start:0.000} - {segment.End:0.000}s   " +
                        $"{segment.Length:0.000}s   {segment.PeakDb:0.0} dB",
                        GUILayout.Width(330f));

                    if (GUILayout.Button("Play", EditorStyles.miniButton, GUILayout.Width(50f)))
                        PlayWindow(segment.Start, segment.Length);

                    using (new EditorGUI.DisabledScope(_profile == null))
                    {
                        if (GUILayout.Button($"+ {_cue}", EditorStyles.miniButton, GUILayout.Width(96f)))
                            Append(new[] { segment }, $"append one excerpt to {_cue}");
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void Analyse()
        {
            StopPreview();
            DestroyWaveform();

            if (!CombatSoundSlicer.TryAnalyze(_clip, _settings, out var analysis, out var error))
            {
                _analysis = null;
                SetMessage(error, true);
                return;
            }

            _analysis = analysis;
            SetMessage(
                $"Found {analysis.Segments.Count} excerpts in {analysis.ClipLength:0.00}s. Lift the silence " +
                "gate if sounds are being split, lower it if tails are cut.", false);
        }

        private void Append(IReadOnlyList<CombatSoundSlicer.Segment> segments, string undoName)
        {
            if (_profile == null)
                return;

            Undo.RecordObject(_profile, undoName);

            var added = CombatSoundSlicer.AppendToCue(_profile, _cue, _analysis.Clip, segments, _fadeIn, _fadeOut);
            if (added == 0)
            {
                SetMessage($"'{_cue}' has no row in the profile, so nothing was appended. Re-run " +
                           "Spellbreakers/Audio/Create Combat Sound Profile to fill in missing cues.", true);
                return;
            }

            EditorUtility.SetDirty(_profile);
            AssetDatabase.SaveAssets();

            SetMessage($"Appended {added} excerpt(s) to {_cue}. {_cue} now holds {CountForCue(_cue)}.", false);
        }

        private void ClearCue()
        {
            if (_profile == null)
                return;

            Undo.RecordObject(_profile, $"clear {_cue}");

            if (!CombatSoundSlicer.ClearCue(_profile, _cue))
                return;

            EditorUtility.SetDirty(_profile);
            AssetDatabase.SaveAssets();

            SetMessage($"Cleared {_cue}.", false);
        }

        private int CountForCue(CombatSoundCue cue)
        {
            if (_profile == null || !_profile.TryGet(cue, out var set) || set == null)
                return 0;

            return set.SliceCount;
        }

        /// <summary>
        /// Auditions a window of the file. In play mode the real manager plays it, so the pitch range and
        /// fades heard here are the ones heard in combat; stopped, the editor's own preview is used.
        /// </summary>
        private void PlayWindow(float start, float length)
        {
            var clip = _analysis != null ? _analysis.Clip : _clip;
            if (clip == null)
                return;

            // One audition at a time: an editor preview left running under a play-mode sound is just noise.
            EditorAudioPreview.Stop();

            _previewStart = start;
            _previewLength = length;
            _previewStartedAt = EditorApplication.timeSinceStartup;
            _previewing = true;
            Repaint();

            var manager = CombatSoundManager.Instance;
            if (Application.isPlaying && manager != null)
            {
                var slice = CombatSoundSlice.CreateTransient(clip, start, length, _fadeIn, _fadeOut);
                var camera = Camera.main;
                var position = camera != null ? camera.transform.position : manager.transform.position;

                if (!manager.PlaySlice(_cue, slice, position))
                    SetMessage($"Played nothing: {_cue} has no excerpts assigned yet.", true);

                return;
            }

            if (!EditorAudioPreview.TryPlay(clip, start, length))
                SetMessage("No editor audio preview available in this Unity build; press Play to audition.", true);
        }

        private void StopPreview()
        {
            EditorAudioPreview.Stop();
            _previewing = false;
            Repaint();
        }

        private void SetMessage(string message, bool isError)
        {
            _message = message;
            _messageIsError = isError;
            Repaint();
        }

        private void AdoptSelectionAsClip()
        {
            if (Selection.activeObject is AudioClip selected && selected != _clip)
            {
                _clip = selected;
                _analysis = null;
                DestroyWaveform();
            }
        }

        private void EnsureWaveform(int width)
        {
            if (_analysis == null)
                return;

            var clip = _analysis.Clip;
            if (_waveform != null && _waveformWidth == width && _waveformClip == clip)
                return;

            DestroyWaveform();

            var height = Mathf.RoundToInt(WaveformHeight);
            var pixels = new Color32[width * height];
            var background = new Color32(22, 24, 30, 255);
            var quiet = new Color32(60, 70, 90, 255);
            var loud = new Color32(120, 200, 255, 255);

            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = background;

            for (var x = 0; x < width; x++)
            {
                var column = Mathf.Clamp(
                    Mathf.FloorToInt(x * _analysis.Envelope.Length / (float)width),
                    0, _analysis.Envelope.Length - 1);

                var amplitude = Mathf.Clamp01(_analysis.Envelope[column]);
                var half = Mathf.RoundToInt(amplitude * 0.5f * height);
                var centre = height / 2;

                for (var y = centre - half; y <= centre + half; y++)
                {
                    if (y < 0 || y >= height)
                        continue;

                    pixels[y * width + x] = Color32.Lerp(quiet, loud, amplitude);
                }
            }

            _waveform = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _waveform.SetPixels32(pixels);
            _waveform.Apply(false);

            _waveformWidth = width;
            _waveformClip = clip;
        }

        private void DestroyWaveform()
        {
            if (_waveform == null)
                return;

            DestroyImmediate(_waveform);
            _waveform = null;
            _waveformWidth = 0;
            _waveformClip = null;
        }
    }
}

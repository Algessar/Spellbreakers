using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Plays an audio excerpt while the editor is stopped.
    ///
    /// Unity does not run the audio engine outside play mode, so a plain <see cref="AudioSource"/> is silent
    /// there. The editor's own audio browser still works, which means the mechanism it uses exists inside
    /// <c>UnityEditor.AudioUtil</c> - internal, undocumented, but stable enough that every audio tool in the
    /// ecosystem leans on it. This wraps that call in reflection and degrades to "no preview" rather than
    /// breaking the slicer window if a future Unity renames it.
    ///
    /// The reason it is worth the reflection: slicing is a cut-and-listen loop. Forcing a play-mode round
    /// trip between every adjustment would make the slicing tool slower than typing numbers, and edits made
    /// during play mode are discarded on exit anyway.
    /// </summary>
    internal static class EditorAudioPreview
    {
        private static MethodInfo _playClip;
        private static MethodInfo _stopAllClips;
        private static bool _resolved;
        private static bool _playing;
        private static double _stopAt;

        /// <summary>True when this Unity build exposes a usable preview call.</summary>
        public static bool IsAvailable
        {
            get
            {
                Resolve();
                return _playClip != null;
            }
        }

        public static bool IsPlaying => _playing;

        /// <summary>
        /// Starts the excerpt at <paramref name="startSeconds"/> and stops it after
        /// <paramref name="durationSeconds"/>. Any previously previewing sound is stopped first.
        /// </summary>
        public static bool TryPlay(AudioClip clip, float startSeconds, float durationSeconds)
        {
            Resolve();

            if (_playClip == null || clip == null)
                return false;

            var startSample = Mathf.Clamp(Mathf.RoundToInt(startSeconds * clip.frequency), 0,
                Mathf.Max(0, clip.samples - 1));

            object[] arguments;
            var parameters = _playClip.GetParameters();
            if (parameters.Length >= 3)
                arguments = new object[] { clip, startSample, false };
            else if (parameters.Length == 2)
                arguments = new object[] { clip, startSample };
            else
                arguments = new object[] { clip };

            try
            {
                _stopAllClips?.Invoke(null, null);
                _playClip.Invoke(null, arguments);
            }
            catch (Exception exception)
            {
                // A preview must never take the window down with it. Disable it and say so once.
                _playClip = null;
                _stopAllClips = null;
                Debug.LogWarning($"{FencingEditorSetup.LogTag} Editor audio preview is unavailable: {exception.Message}");
                return false;
            }

            _playing = true;
            _stopAt = EditorApplication.timeSinceStartup + Mathf.Max(0.05f, durationSeconds);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;

            return true;
        }

        public static void Stop()
        {
            if (!_playing && _stopAt <= 0d)
                return;

            _playing = false;
            _stopAt = 0d;
            EditorApplication.update -= Tick;

            try
            {
                _stopAllClips?.Invoke(null, null);
            }
            catch (Exception)
            {
                // Stopping is best-effort; there is nothing useful to report if it fails.
            }
        }

        private static void Tick()
        {
            if (!_playing)
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (EditorApplication.timeSinceStartup < _stopAt)
                return;

            Stop();
        }

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            var type = FindAudioUtil();

            if (type == null)
                return;

            _playClip = FindStatic(type, "PlayClip") ?? FindStatic(type, "PlayPreviewClip");
            _stopAllClips = FindStatic(type, "StopAllClips") ?? FindStatic(type, "StopPreviewClip");

            if (_playClip != null)
                return;

            Debug.LogWarning($"{FencingEditorSetup.LogTag} This Unity build exposes no editor audio preview, " +
                             "so the slicer cannot audition excerpts outside play mode. Press Play to hear them.");
        }

        /// <summary>
        /// Looks for the editor's audio helper by name rather than by assembly path: which module owns it has
        /// moved between Unity versions, and the type itself is internal either way.
        /// </summary>
        private static Type FindAudioUtil()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                Type found;
                try
                {
                    found = assemblies[i].GetType("UnityEditor.AudioUtil", false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (found != null)
                    return found;
            }

            return null;
        }

        private static MethodInfo FindStatic(Type type, string name)
        {
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name)
                    continue;

                var parameters = methods[i].GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(AudioClip))
                    continue;

                return methods[i];
            }

            return null;
        }
    }
}

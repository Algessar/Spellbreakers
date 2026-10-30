using System.Collections.Generic;
using Spellbreakers.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Sets combat audio up as data, in one click, and wires it into a scene.
    ///
    /// The pieces are deliberately separate - a profile asset holds the clips, a manager object plays them,
    /// and combat only raises cues - but three separate manual steps is enough friction that the sound
    /// system would end up unused. So the whole path is available from the menu:
    ///
    ///   Spellbreakers / Audio / Create Combat Sound Profile   -> Assets/Resources/CombatSoundProfile.asset
    ///   Spellbreakers / Audio / Auto-Assign Clips From Art/Sound
    ///   Spellbreakers / Audio / Add Manager To Open Scene
    ///
    /// Only the middle step guesses: it fills *empty* cue rows by matching file names, never overwriting an
    /// assignment, so anything it gets wrong is a drag away from right.
    /// </summary>
    internal static class CombatSoundSetup
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string ProfilePath = ResourcesFolder + "/" + CombatSoundManager.ProfileResourcesPath + ".asset";
        private const string SoundFolder = "Assets/Art/Sound";
        private const string ManagerName = "Combat Sound Manager";

        /// <summary>File-name fragments that reliably identify a sound, matched against the lower-cased name.</summary>
        private readonly struct KeywordRule
        {
            public KeywordRule(string keyword, CombatSoundCue cue)
            {
                Keyword = keyword;
                Cue = cue;
            }

            public string Keyword { get; }

            public CombatSoundCue Cue { get; }
        }

        /// <summary>Order matters: the first rule a file matches wins, so a clip is only ever filed once.</summary>
        private static readonly KeywordRule[] KeywordRules =
        {
            new KeywordRule("parry", CombatSoundCue.Parry),
            new KeywordRule("bind", CombatSoundCue.Bind),
            new KeywordRule("miss", CombatSoundCue.Miss),
            new KeywordRule("whoosh", CombatSoundCue.Swing),
            new KeywordRule("swing", CombatSoundCue.Swing),
            new KeywordRule("hit", CombatSoundCue.HitBody),
        };

        [MenuItem("Spellbreakers/Audio/Create Combat Sound Profile", priority = 10)]
        private static void CreateProfile()
        {
            var profile = EnsureProfileAsset();
            if (profile == null)
                return;

            profile.EnsureAllCues();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);

            Debug.Log($"{FencingEditorSetup.LogTag} Combat sound profile ready at {ProfilePath} " +
                      $"({profile.AssignedCueCount} of {profile.GetEntries().Length} cues assigned). " +
                      "Drag clips into the rows, then add the manager to a scene.");
        }

        [MenuItem("Spellbreakers/Audio/Auto-Assign Clips From Art/Sound", priority = 11)]
        private static void AutoAssignClips()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CombatSoundProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} No profile at {ProfilePath}. Run " +
                                 "'Spellbreakers/Audio/Create Combat Sound Profile' first.");
                return;
            }

            var found = FindClipsByCue(out var unclassified);
            if (found.Count == 0)
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} No audio clips in '{SoundFolder}' matched any " +
                                 $"known cue keyword. Assessed: {string.Join(", ", unclassified)}");
                return;
            }

            Undo.RecordObject(profile, "Auto-assign combat sound clips");

            profile.EnsureAllCues();
            var entries = profile.GetEntries();
            var assigned = new List<string>();
            var kept = new List<string>();

            for (var i = 0; i < entries.Length; i++)
            {
                var cue = entries[i].Cue;
                if (!found.TryGetValue(cue, out var clips) || clips.Count == 0)
                    continue;

                if (entries[i].Clips == null)
                    entries[i].Clips = new CombatSoundClips();

                // Never overwrite a human decision: an empty row is filled, an assigned one is left alone.
                if (entries[i].Clips.HasSounds)
                {
                    kept.Add(cue.ToString());
                    continue;
                }

                entries[i].Clips.SetClips(clips.ToArray());
                assigned.Add($"{cue} <- {clips.Count} clip(s)");
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var summary = $"{FencingEditorSetup.LogTag} Combat sound auto-assign\n" +
                          $"  filled: {(assigned.Count > 0 ? string.Join("; ", assigned) : "nothing")}\n" +
                          $"  already assigned, left alone: {(kept.Count > 0 ? string.Join(", ", kept) : "none")}\n" +
                          $"  no keyword match: {string.Join(", ", unclassified)}";

            Debug.Log(summary);
        }

        [MenuItem("Spellbreakers/Audio/Add Manager To Open Scene", priority = 12)]
        private static void AddManagerToOpenScene()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CombatSoundProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} No profile at {ProfilePath}, so the manager " +
                                 "would be silent. Run 'Create Combat Sound Profile' first if you want sound.");
            }

            var manager = EnsureManagerInScene(profile, enableDebugReadout: true);
            if (manager == null)
                return;

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = manager.gameObject;

            Debug.Log($"{FencingEditorSetup.LogTag} '{manager.name}' is in the open scene, with the console cue " +
                      "log and the on-screen cue list on. Set 'Audition Key' to hear every cue without " +
                      "fighting.");
        }

        /// <summary>
        /// Returns the profile at the conventional path, creating an empty one if it is missing. Nothing is
        /// created unless the author asked for it, so a fresh checkout stays silent by default.
        /// </summary>
        public static CombatSoundProfile EnsureProfileAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<CombatSoundProfile>(ProfilePath);
            if (existing != null)
                return existing;

            FencingEditorSetup.EnsureFolder(ResourcesFolder);

            var profile = ScriptableObject.CreateInstance<CombatSoundProfile>();
            profile.EnsureAllCues();

            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<CombatSoundProfile>(ProfilePath);
        }

        /// <summary>
        /// Puts one manager in a scene, wired to <paramref name="profile"/> when one is supplied, and returns
        /// the existing one if the scene already has it.
        /// </summary>
        public static CombatSoundManager EnsureManagerInScene(CombatSoundProfile profile, bool enableDebugReadout)
        {
            var existing = Object.FindAnyObjectByType<CombatSoundManager>();
            if (existing != null)
            {
                Debug.Log($"{FencingEditorSetup.LogTag} '{existing.name}' is already in the scene; wiring left as is.");
                return existing;
            }

            var host = new GameObject(ManagerName);
            var manager = host.AddComponent<CombatSoundManager>();

            var serialized = new SerializedObject(manager);

            if (profile != null)
                FencingEditorSetup.SetObjectReference(serialized, "_profile", profile, ManagerName);

            FencingEditorSetup.SetBoolProperty(serialized, "_logCues", enableDebugReadout, ManagerName);
            FencingEditorSetup.SetBoolProperty(serialized, "_showCueLog", enableDebugReadout, ManagerName);

            // F1 and F2 are the fencing debug views; F4 is unclaimed and next to them, so auditioning sits
            // next to the other debug keys. Set by name so the editor assembly does not need a reference to
            // the Input System just for one constant.
            var auditionKey = serialized.FindProperty("_auditionKey");
            if (auditionKey != null)
            {
                var names = auditionKey.enumNames;
                for (var i = 0; i < names.Length; i++)
                {
                    if (names[i] != "F4")
                        continue;

                    auditionKey.enumValueIndex = i;
                    break;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (Object.FindAnyObjectByType<AudioListener>() == null)
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} The scene has no AudioListener, so nothing will " +
                                 "be heard. Cameras normally carry one.");
            }

            return manager;
        }

        private static Dictionary<CombatSoundCue, List<AudioClip>> FindClipsByCue(out List<string> unclassified)
        {
            var byCue = new Dictionary<CombatSoundCue, List<AudioClip>>();
            unclassified = new List<string>();

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SoundFolder });
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    continue;

                var name = clip.name.ToLowerInvariant();
                var matchedCue = FindCueForName(name);

                if (matchedCue == null)
                {
                    unclassified.Add(clip.name);
                    continue;
                }

                if (!byCue.TryGetValue(matchedCue.Value, out var list))
                {
                    list = new List<AudioClip>();
                    byCue.Add(matchedCue.Value, list);
                }

                list.Add(clip);
            }

            // Stable ordering keeps the asset diff readable between runs.
            foreach (var pair in byCue)
                pair.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            return byCue;
        }

        private static CombatSoundCue? FindCueForName(string lowerCaseName)
        {
            for (var i = 0; i < KeywordRules.Length; i++)
            {
                if (lowerCaseName.Contains(KeywordRules[i].Keyword))
                    return KeywordRules[i].Cue;
            }

            return null;
        }
    }
}

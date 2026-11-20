// Copyright © Sascha Graeff/13Pixels.
// Extension by the Spellbreakers project: place a whole list of prefabs with a single click.

namespace ThirteenPixels.Placr
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;

    /// <summary>Serialized data holder for <see cref="PlacrPrefabSet"/>. Never saved as an asset.</summary>
    internal class PlacrPrefabSetSettings : ScriptableObject
    {
        [SerializeField] internal List<GameObject> prefabs = new List<GameObject>();
        [SerializeField] internal Vector3 spacing = new Vector3(2f, 0f, 0f);
        [SerializeField] internal bool centerOnCursor = true;
        [SerializeField] internal bool snapToGround = true;
        [SerializeField] internal bool isActive = true;
        [SerializeField] internal bool isExpanded = true;
    }

    /// <summary>
    /// A user-defined list of prefabs that Placr places all at once, spaced by <see cref="spacing"/>.
    /// While the set is enabled it replaces the "random prefab out of the Project selection" mode.
    /// The list and its options are persisted in the editor preferences.
    /// </summary>
    internal static class PlacrPrefabSet
    {
        private const string prefsKey = "Placr.Extended.PrefabSet.";

        private static PlacrPrefabSetSettings settings;
        private static SerializedObject serializedSettings;
        private static ReorderableList list;

        private static bool listDirty;
        private static bool isInitialized;

        /// <summary>True when a click places the whole set instead of a single prefab.</summary>
        internal static bool isEnabled => Settings.isActive && GetPrefabs().Count > 0;

        internal static Vector3 spacing => Settings.spacing;
        internal static bool centerOnCursor => Settings.centerOnCursor;
        internal static bool snapToGround => Settings.snapToGround;

        private static PlacrPrefabSetSettings Settings
        {
            get
            {
                EnsureInitialized();
                return settings;
            }
        }

        /// <summary>The subset of the list that can actually be placed (prefab and model assets).</summary>
        internal static List<GameObject> GetPrefabs()
        {
            var source = Settings.prefabs;
            var result = new List<GameObject>(source.Count);
            foreach (var prefab in source)
            {
                if (IsPlaceable(prefab))
                {
                    result.Add(prefab);
                }
            }

            return result;
        }

        /// <summary>Prefab and model assets live outside of any scene, so they are placeable.</summary>
        private static bool IsPlaceable(GameObject prefab)
        {
            return prefab && prefab.scene.path == null;
        }

        internal static void Display(Action onPlaceSet)
        {
            var current = Settings;

            var isExpanded = EditorGUILayout.Foldout(current.isExpanded, new GUIContent("Prefab Set", "Place a whole list of prefabs in one click."), true);
            if (isExpanded != current.isExpanded)
            {
                current.isExpanded = isExpanded;
                Save();
            }

            if (!isExpanded)
            {
                return;
            }

            var spacingBefore = current.spacing;
            var centerBefore = current.centerOnCursor;
            var snapBefore = current.snapToGround;
            var activeBefore = current.isActive;

            GUILayout.BeginVertical(EditorStyles.helpBox);

            list.DoLayoutList();
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            var selectablePrefabs = 0;
            foreach (var gameObject in Selection.gameObjects)
            {
                if (IsPlaceable(gameObject))
                {
                    selectablePrefabs++;
                }
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = selectablePrefabs > 0;
            if (GUILayout.Button(new GUIContent($"Add Project Selection ({selectablePrefabs})", "Append every prefab currently selected in the Project window to the set."), EditorStyles.miniButton))
            {
                AddSelectionToSet();
            }
            GUI.enabled = current.prefabs.Count > 0;
            if (GUILayout.Button(new GUIContent("Clear", "Remove every entry from the set."), EditorStyles.miniButton, GUILayout.Width(50)))
            {
                ClearSet();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Spacing", GUILayout.Width(50));
            current.spacing = EditorGUILayout.Vector3Field(GUIContent.none, current.spacing);
            GUILayout.EndHorizontal();

            current.centerOnCursor = GUILayout.Toggle(
                current.centerOnCursor,
                new GUIContent("Center On Cursor", "Center the set around the cursor instead of starting at it."));

            current.snapToGround = GUILayout.Toggle(
                current.snapToGround,
                new GUIContent("Snap Each To Ground", "Raycast every element down onto the ground, so a set follows slopes and uneven terrain."));

            var placeable = current.prefabs.Count == 0 ? 0 : GetPrefabs().Count;
            GUI.enabled = placeable > 0;
            var label = placeable > 0 ? $"Place Set ({placeable})" : "Add prefabs to the list above";
            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                onPlaceSet?.Invoke();
            }
            GUI.enabled = true;

            current.isActive = GUILayout.Toggle(
                current.isActive,
                new GUIContent("Use Prefab Set", "When enabled, left clicks place the whole set instead of a single prefab from the Project selection."));

            if (placeable < current.prefabs.Count)
            {
                EditorGUILayout.HelpBox($"{current.prefabs.Count - placeable} entr{(current.prefabs.Count - placeable == 1 ? "y is" : "ies are")} not a prefab or model asset and will be skipped.", MessageType.Warning);
            }

            GUILayout.EndVertical();

            if (listDirty || spacingBefore != current.spacing || centerBefore != current.centerOnCursor ||
                snapBefore != current.snapToGround || activeBefore != current.isActive)
            {
                listDirty = false;
                Save();
            }
        }

        private static void AddSelectionToSet()
        {
            // Deferred: mutating the list that a ReorderableList is currently drawing would be unsafe.
            EditorApplication.delayCall += () =>
            {
                foreach (var gameObject in Selection.gameObjects)
                {
                    if (IsPlaceable(gameObject))
                    {
                        settings.prefabs.Add(gameObject);
                    }
                }

                serializedSettings.Update();
                Save();
            };
        }

        private static void ClearSet()
        {
            EditorApplication.delayCall += () =>
            {
                settings.prefabs.Clear();
                serializedSettings.Update();
                Save();
            };
        }

        private static void DrawListHeader(Rect rect)
        {
            var total = settings.prefabs.Count;
            EditorGUI.LabelField(rect, total == 0 ? "Prefabs (drag prefabs here)" : $"Prefabs ({total})");
        }

        private static void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            var prefab = element.objectReferenceValue as GameObject;
            if (prefab && !IsPlaceable(prefab))
            {
                var color = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.4f);
                EditorGUI.PropertyField(rect, element, GUIContent.none);
                GUI.color = color;
            }
            else
            {
                EditorGUI.PropertyField(rect, element, GUIContent.none);
            }
        }

        private static void EnsureInitialized()
        {
            if (isInitialized) return;

            settings = ScriptableObject.CreateInstance<PlacrPrefabSetSettings>();
            serializedSettings = new SerializedObject(settings);

            Load();

            list = new ReorderableList(serializedSettings, serializedSettings.FindProperty(nameof(PlacrPrefabSetSettings.prefabs)), true, true, true, true)
            {
                drawHeaderCallback = DrawListHeader,
                drawElementCallback = DrawListElement,
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 4f,
                onChangedCallback = _ => listDirty = true,
            };

            isInitialized = true;
        }

        private static void Load()
        {
            settings.prefabs.Clear();

            var guids = EditorPrefs.GetString(prefsKey + "guids", string.Empty);
            foreach (var guid in guids.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab)
                {
                    settings.prefabs.Add(prefab);
                }
            }

            settings.spacing = new Vector3(
                EditorPrefs.GetFloat(prefsKey + "spacingX", 2f),
                EditorPrefs.GetFloat(prefsKey + "spacingY", 0f),
                EditorPrefs.GetFloat(prefsKey + "spacingZ", 0f));
            settings.centerOnCursor = EditorPrefs.GetBool(prefsKey + "centerOnCursor", true);
            settings.snapToGround = EditorPrefs.GetBool(prefsKey + "snapToGround", true);
            settings.isActive = EditorPrefs.GetBool(prefsKey + "isActive", true);
            settings.isExpanded = EditorPrefs.GetBool(prefsKey + "isExpanded", true);

            serializedSettings.Update();
        }

        private static void Save()
        {
            var guids = new StringBuilder();
            foreach (var prefab in settings.prefabs)
            {
                if (guids.Length > 0)
                {
                    guids.Append(';');
                }

                if (prefab)
                {
                    guids.Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
                }
            }

            EditorPrefs.SetString(prefsKey + "guids", guids.ToString());
            EditorPrefs.SetFloat(prefsKey + "spacingX", settings.spacing.x);
            EditorPrefs.SetFloat(prefsKey + "spacingY", settings.spacing.y);
            EditorPrefs.SetFloat(prefsKey + "spacingZ", settings.spacing.z);
            EditorPrefs.SetBool(prefsKey + "centerOnCursor", settings.centerOnCursor);
            EditorPrefs.SetBool(prefsKey + "snapToGround", settings.snapToGround);
            EditorPrefs.SetBool(prefsKey + "isActive", settings.isActive);
            EditorPrefs.SetBool(prefsKey + "isExpanded", settings.isExpanded);
        }
    }
}

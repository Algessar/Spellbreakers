// Copyright © Sascha Graeff/13Pixels.
// Extension by the Spellbreakers project: persistent placement parent.

namespace ThirteenPixels.Placr
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// The Transform that newly placed objects are parented to.
    /// Unlike the original implementation (a plain static field that was lost on every domain reload),
    /// the choice is stored as a <see cref="GlobalObjectId"/> in the editor preferences, so it
    /// survives tool switches, scene reloads and editor restarts.
    /// </summary>
    internal static class PlacrParentSettings
    {
        private const string prefsKey = "Placr.Extended.Parent.GlobalObjectId";

        private static bool isLoaded;
        private static Transform current;

        /// <summary>The transform that new instances are parented to. May be null (no parenting).</summary>
        internal static Transform parent
        {
            get
            {
                EnsureLoaded();
                Validate();
                return current;
            }
        }

        internal static void Set(Transform value)
        {
            EnsureLoaded();
            current = value && !IsPrefabAsset(value.gameObject) ? value : null;
            Save();
        }

        internal static void DisplayOptionsGUI()
        {
            EnsureLoaded();
            Validate();

            GUILayout.BeginHorizontal();
            var newParent = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("New Instance Parent", "Placr parents every object it places to this transform."),
                current,
                typeof(Transform),
                true);

            if (newParent != current)
            {
                Set(newParent);
            }

            GUI.enabled = current != null;
            if (GUILayout.Button(new GUIContent("Root", "Clear the parent so instances are placed at the scene root."), EditorStyles.miniButton, GUILayout.Width(40)))
            {
                Set(null);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            var selectedSceneObject = Selection.activeTransform;
            var canUseSelection = selectedSceneObject && !IsPrefabAsset(selectedSceneObject.gameObject) && selectedSceneObject != current;

            GUI.enabled = canUseSelection;
            if (GUILayout.Button("Use Selected Object As Parent", EditorStyles.miniButton))
            {
                Set(selectedSceneObject);
            }
            GUI.enabled = true;

            var activeParent = current;
            if (activeParent)
            {
                EditorGUILayout.LabelField("Placing under: " + GetPath(activeParent), EditorStyles.miniLabel);
            }
        }

        private static string GetPath(Transform transform)
        {
            var path = transform.name;
            var node = transform.parent;
            while (node != null && path.Length < 56)
            {
                path = node.name + "/" + path;
                node = node.parent;
            }

            return path;
        }

        /// <summary>Prefab assets and 3D models live outside of any scene, so they cannot be a placement parent.</summary>
        private static bool IsPrefabAsset(GameObject gameObject)
        {
            return gameObject && gameObject.scene.path == null;
        }

        /// <summary>Forgets references that became invalid (destroyed object or a prefab asset).</summary>
        private static void Validate()
        {
            if (current == null)
            {
                return;
            }

            if (!current || IsPrefabAsset(current.gameObject))
            {
                current = null;
                Save();
            }
        }

        private static void EnsureLoaded()
        {
            if (isLoaded) return;
            isLoaded = true;

            var id = EditorPrefs.GetString(prefsKey, string.Empty);
            if (!string.IsNullOrEmpty(id) && GlobalObjectId.TryParse(id, out var globalId))
            {
                current = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as Transform;
            }
        }

        private static void Save()
        {
            if (!current)
            {
                EditorPrefs.DeleteKey(prefsKey);
                return;
            }

            EditorPrefs.SetString(prefsKey, GlobalObjectId.GetGlobalObjectIdSlow(current).ToString());
        }
    }
}

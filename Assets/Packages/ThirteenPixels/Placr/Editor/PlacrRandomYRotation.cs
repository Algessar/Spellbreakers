// Copyright © Sascha Graeff/13Pixels.
// Extension by the Spellbreakers project: quick random yaw for every placed object.

namespace ThirteenPixels.Placr
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Gives every placed object a random rotation around its own up axis.
    /// Unlike the <see cref="PlacrModifierRandomRotationOffset"/> modifier this is a first-class
    /// tool option (always visible in the Placr panel), supports a min/max range and an optional snap step.
    /// </summary>
    internal static class PlacrRandomYRotation
    {
        private const string prefsKey = "Placr.Extended.RandomYaw.";

        private static bool isLoaded;
        private static bool isActive;
        private static float minAngle;
        private static float maxAngle = 360f;
        private static float snapStep;

        /// <summary>Returns the yaw in degrees that should be added to the next placed object.</summary>
        internal static float GetRandomYaw()
        {
            EnsureLoaded();

            if (!isActive)
            {
                return 0f;
            }

            var low = Mathf.Min(minAngle, maxAngle);
            var high = Mathf.Max(minAngle, maxAngle);
            var yaw = high > low ? Random.Range(low, high) : low;

            if (snapStep > 0f)
            {
                yaw = Mathf.Round(yaw / snapStep) * snapStep;
            }

            return yaw;
        }

        internal static void DisplayOptionsGUI()
        {
            EnsureLoaded();

            GUILayout.BeginVertical(EditorStyles.helpBox);

            var wasActive = isActive;
            isActive = GUILayout.Toggle(isActive, new GUIContent("Randomize Y Rotation", "Give every placed object a random yaw around its own up axis."));
            if (isActive != wasActive)
            {
                Save();
            }

            GUI.enabled = isActive;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Yaw", GUILayout.Width(50));
            var min = minAngle;
            var max = maxAngle;
            EditorGUILayout.MinMaxSlider(ref min, ref max, 0f, 360f);
            GUILayout.Label($"{min:0}° - {max:0}°", EditorStyles.miniLabel, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(min, minAngle) || !Mathf.Approximately(max, maxAngle))
            {
                minAngle = min;
                maxAngle = max;
                Save();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Snap To", GUILayout.Width(50));
            var step = EditorGUILayout.FloatField(Mathf.Max(0f, snapStep), GUILayout.Width(50));
            if (!Mathf.Approximately(step, snapStep))
            {
                snapStep = Mathf.Max(0f, step);
                Save();
            }
            GUILayout.Label(snapStep > 0f ? "degrees per step" : "0 = fully random", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private static void EnsureLoaded()
        {
            if (isLoaded) return;
            isLoaded = true;

            isActive = EditorPrefs.GetBool(prefsKey + "isActive", false);
            minAngle = EditorPrefs.GetFloat(prefsKey + "minAngle", 0f);
            maxAngle = EditorPrefs.GetFloat(prefsKey + "maxAngle", 360f);
            snapStep = EditorPrefs.GetFloat(prefsKey + "snapStep", 0f);
        }

        private static void Save()
        {
            EditorPrefs.SetBool(prefsKey + "isActive", isActive);
            EditorPrefs.SetFloat(prefsKey + "minAngle", minAngle);
            EditorPrefs.SetFloat(prefsKey + "maxAngle", maxAngle);
            EditorPrefs.SetFloat(prefsKey + "snapStep", snapStep);
        }
    }
}

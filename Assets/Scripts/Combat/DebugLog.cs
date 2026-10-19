using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Logging helpers that compile out of player builds entirely.
    ///
    /// A <c>[Conditional]</c> method removes the call *and its argument evaluation*, so an interpolated
    /// string built inside a combat loop costs nothing in a build. A plain
    /// <c>if (_flag) Debug.Log($"...")</c> still builds the string.
    /// </summary>
    public static class DebugLog
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Info(bool enabled, string message, Object context = null)
        {
            if (!enabled)
                return;

            Debug.Log(message, context);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Warning(bool enabled, string message, Object context = null)
        {
            if (!enabled)
                return;

            Debug.LogWarning(message, context);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Always(string message, Object context = null)
        {
            Debug.Log(message, context);
        }
    }
}

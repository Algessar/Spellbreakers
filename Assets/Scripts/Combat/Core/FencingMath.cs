using System;

namespace Spellbreakers.Combat
{
    /// <summary>Small numeric helpers. The core deliberately has no dependency on UnityEngine.</summary>
    public static class FencingMath
    {
        public static float Clamp01(float value)
        {
            if (value <= 0f)
                return 0f;
            return value >= 1f ? 1f : value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value <= min)
                return min;
            return value >= max ? max : value;
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        /// <summary>
        /// Maps a value from one range to 0..1 and clamps it. Used by the AI considerations so every
        /// score lands in the same range regardless of the unit it was measured in.
        /// </summary>
        public static float InverseLerpClamped(float a, float b, float value)
        {
            if (Math.Abs(b - a) < 1e-6f)
                return value >= b ? 1f : 0f;

            return Clamp01((value - a) / (b - a));
        }
    }
}

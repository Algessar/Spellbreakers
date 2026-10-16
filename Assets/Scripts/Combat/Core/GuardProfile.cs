using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// What a guard is worth defensively and how far its point reaches.
    ///
    /// The parry window deliberately belongs to the *arrival* in a guard, not to holding it. The design
    /// doc asks for timed defensive actions rather than a permanent block, so a guard only parries for
    /// a short grace period after it is entered; after that the same contact resolves as a hit. The
    /// grace period is what AI must learn to time against.
    /// </summary>
    public sealed class GuardProfile
    {
        /// <summary>How much of the parry grace survives in Krieg, where everything happens faster.</summary>
        public const float KriegParryGraceScale = 0.6f;

        /// <summary>Fallback point reach in metres when a guard has no authored profile.</summary>
        public const float DefaultTipReach = 1.4f;

        public GuardProfile(GuardId guard, float tipReach = DefaultTipReach, float parryGraceSeconds = 0.25f)
        {
            Guard = guard;
            TipReach = FencingMath.Max(0.1f, tipReach);
            ParryGraceSeconds = FencingMath.Max(0f, parryGraceSeconds);
        }

        public GuardId Guard { get; }

        /// <summary>Distance from the combatant's centre at which this guard's point can reach.</summary>
        public float TipReach { get; }

        /// <summary>Seconds after arriving in this guard during which a contact parries instead of hitting.</summary>
        public float ParryGraceSeconds { get; }

        public float ResolveParryGrace(Measure measure)
        {
            return measure == Measure.Krieg
                ? ParryGraceSeconds * KriegParryGraceScale
                : ParryGraceSeconds;
        }

        /// <summary>
        /// Classifies a distance into a measure band using this guard's reach. Kept here so the player,
        /// the AI and the tests all band distance the same way.
        /// </summary>
        public Measure Classify(float distance)
        {
            if (distance <= TipReach * 0.55f)
                return Measure.Ringen;

            if (distance <= TipReach)
                return Measure.Krieg;

            if (distance <= TipReach * 2f)
                return Measure.Zufechten;

            return Measure.OutOfRange;
        }
    }
}

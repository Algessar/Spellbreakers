using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The physical facts about a blade contact that decide how well a defence holds up.
    ///
    /// All of these come from what the hit test already samples each frame - the blade's base and tip
    /// before and after the sweep - plus the contact normal, so nothing here needs new simulation.
    /// </summary>
    public readonly struct DeflectionInput
    {
        public DeflectionInput(
            float timingQuality,
            float contactAngle,
            float relativeSpeed,
            float referenceSpeed,
            CombatOutcome baseline)
        {
            TimingQuality = FencingMath.Clamp01(timingQuality);
            ContactAngle = FencingMath.Clamp01(contactAngle);
            RelativeSpeed = FencingMath.Max(0f, relativeSpeed);
            ReferenceSpeed = FencingMath.Max(0.1f, referenceSpeed);
            Baseline = baseline;
        }

        /// <summary>How centrally inside the defender's parry window the contact landed. 1 is perfect.</summary>
        public float TimingQuality { get; }

        /// <summary>
        /// How squarely the two volumes met, 0..1. 1 means the blade arrived perpendicular to the
        /// defending surface; 0 means it came in edge-on or glancing.
        /// </summary>
        public float ContactAngle { get; }

        /// <summary>Speed of the attacking blade at the moment of contact, in metres per second.</summary>
        public float RelativeSpeed { get; }

        /// <summary>The attacking speed that counts as "full force" for this opponent.</summary>
        public float ReferenceSpeed { get; }

        /// <summary>
        /// The parry table's disposition for this guard-against-line pairing. It is a prior, not the
        /// answer: a favourable matchup still fails on bad timing, and a poor one can still be salvaged.
        /// </summary>
        public CombatOutcome Baseline { get; }
    }

    /// <summary>
    /// The outcome of a contact plus the continuous grade it was derived from, kept separately so the
    /// gradient stays inspectable - both for the debug overlay and for tuning.
    /// </summary>
    public readonly struct DeflectionResult
    {
        public DeflectionResult(
            CombatOutcome outcome,
            float grade,
            float timingContribution,
            float angleContribution,
            float speedContribution,
            bool defenderCanParry)
        {
            Outcome = outcome;
            Grade = grade;
            TimingContribution = timingContribution;
            AngleContribution = angleContribution;
            SpeedContribution = speedContribution;
            DefenderCanParry = defenderCanParry;
        }

        public CombatOutcome Outcome { get; }

        /// <summary>Continuous 0..1 deflection quality. Higher is better for the defender.</summary>
        public float Grade { get; }

        public float TimingContribution { get; }

        public float AngleContribution { get; }

        public float SpeedContribution { get; }

        public bool DefenderCanParry { get; }

        public bool IsBladeOnBlade =>
            Outcome == CombatOutcome.Parry ||
            Outcome == CombatOutcome.Bind ||
            Outcome == CombatOutcome.Deflect;

        public static DeflectionResult None =>
            new DeflectionResult(CombatOutcome.HitBody, 0f, 0f, 0f, 0f, false);

        public override string ToString()
        {
            return $"{Outcome} ({Grade:0.00}) timing={TimingContribution:0.00} " +
                   $"angle={AngleContribution:0.00} speed={SpeedContribution:0.00}";
        }
    }

    /// <summary>
    /// Turns "blade met blade" into a graded result rather than a binary lookup.
    ///
    /// The design doc asks for exactly this: good timing and angle produce a clean deflection, worse
    /// ones degrade to a partial deflection, then a knockback, then a hit - and mass, momentum and
    /// contact geometry influence where on that scale the contact lands. So the parry table is treated
    /// as a *prior* (this guard answers this line well or badly) and the physics decides the final grade,
    /// rather than the table alone deciding the outcome.
    ///
    /// Timing is weighted highest because the doc is explicit that the player should be rewarded for
    /// correctly reading and timing a defence. A creature that cannot parry at all never reaches the
    /// gradient - it has to avoid the attack instead.
    /// </summary>
    public static class DeflectionResolver
    {
        /// <summary>Timing dominates, per the doc's emphasis on reading and timing a defence.</summary>
        public const float TimingWeight = 0.5f;

        /// <summary>Contact angle decides whether the blade was actually turned or just grazed.</summary>
        public const float AngleWeight = 0.3f;

        /// <summary>Speed matters least, because a well-timed clean meet handles speed.</summary>
        public const float SpeedWeight = 0.2f;

        /// <summary>How much the authored guard/line matchup counts as a prior versus the physics.</summary>
        public const float BaselineShare = 0.35f;

        public const float CleanDeflectionThreshold = 0.85f;
        public const float PartialDeflectionThreshold = 0.65f;
        public const float KnockbackThreshold = 0.45f;

        /// <summary>
        /// Resolves one blade contact. <paramref name="defenderMode"/> decides whether the defender can
        /// parry at all; avoidance is resolved geometrically by the hit test, not here.
        /// </summary>
        public static DeflectionResult Resolve(in DeflectionInput input, DefenceMode defenderMode)
        {
            if ((defenderMode & DefenceMode.Parry) == 0)
            {
                // Nothing to meet the blade with. The attack stands, whatever the timing.
                return new DeflectionResult(CombatOutcome.HitBody, 0f, 0f, 0f, 0f, false);
            }

            var timing = input.TimingQuality * TimingWeight;
            var angle = input.ContactAngle * AngleWeight;
            var speed = SpeedQuality(input) * SpeedWeight;

            var physical = timing + angle + speed;
            var baseline = BaselineGrade(input.Baseline);

            var grade = FencingMath.Clamp01(BaselineShare * baseline + (1f - BaselineShare) * physical);

            return new DeflectionResult(
                Bucket(grade),
                grade,
                timing,
                angle,
                speed,
                true);
        }

        /// <summary>
        /// A fast incoming blade is harder to turn aside, but the effect is strongly diminishing: a
        /// clean meet still works. Modelled as a reciprocal so it never reaches zero.
        /// </summary>
        private static float SpeedQuality(in DeflectionInput input)
        {
            var ratio = input.RelativeSpeed / input.ReferenceSpeed;
            return 1f / (1f + ratio);
        }

        /// <summary>The table's disposition expressed as a prior grade.</summary>
        public static float BaselineGrade(CombatOutcome baseline)
        {
            switch (baseline)
            {
                case CombatOutcome.Parry: return 0.90f;
                case CombatOutcome.Deflect: return 0.70f;
                case CombatOutcome.Bind: return 0.55f;
                case CombatOutcome.HitBody: return 0.15f;
                default: return 0.15f;
            }
        }

        /// <summary>Maps the continuous grade onto the doc's gradient of outcomes.</summary>
        public static CombatOutcome Bucket(float grade)
        {
            if (grade >= CleanDeflectionThreshold) return CombatOutcome.Parry;
            if (grade >= PartialDeflectionThreshold) return CombatOutcome.Deflect;
            if (grade >= KnockbackThreshold) return CombatOutcome.Bind;
            return CombatOutcome.HitBody;
        }

        /// <summary>
        /// Timing quality from how far into a window a contact landed, where the centre is best. A
        /// defender who barely catches the edge of the window gets a low score, which is what makes
        /// "just in time" read differently from "perfectly timed".
        /// </summary>
        public static float TimingQualityFromWindow(float progress01, float windowStart, float windowEnd)
        {
            if (windowEnd <= windowStart)
                return 0f;

            var t = FencingMath.Clamp01((progress01 - windowStart) / (windowEnd - windowStart));

            // Peak at the middle of the window, falling to 0 at either edge.
            return 1f - Math.Abs(t * 2f - 1f);
        }
    }
}

using System;

namespace Spellbreakers.Combat.Ai
{
    /// <summary>
    /// An enemy's fighting personality as data: how fast it reacts, how much it commits, which guards it
    /// likes, and how much each consideration matters to it.
    ///
    /// Difficulty lives here rather than in stats. The design doc asks for difficulty that comes from
    /// understanding and executing the combat system, so an archetype changes how the enemy reads and
    /// times a duel, not how much damage it shrugs off.
    /// </summary>
    public sealed class FencingArchetype
    {
        public string Name { get; set; } = "Fencer";

        /// <summary>Delay before the brain responds to a change in the opponent's state.</summary>
        public float ReactionSeconds { get; set; } = 0.28f;

        /// <summary>How often the brain re-evaluates. Ten decisions a second is plenty and cheap.</summary>
        public float DecisionInterval { get; set; } = 0.1f;

        /// <summary>0 = purely reactive, 1 = attacks whenever it can.</summary>
        public float Aggression { get; set; } = 0.5f;

        /// <summary>How far a single step carries the enemy.</summary>
        public float StepDistance { get; set; } = 0.6f;

        /// <summary>Near edge of the distance band this enemy wants to fight in.</summary>
        public float PreferredMinDistance { get; set; } = 1.1f;

        /// <summary>Far edge of the distance band this enemy wants to fight in.</summary>
        public float PreferredMaxDistance { get; set; } = 2.1f;

        /// <summary>How hard it corrects distance error. High values look disciplined, low ones sloppy.</summary>
        public float MeasureDiscipline { get; set; } = 0.6f;

        /// <summary>Noise added to every score, so the enemy is not a machine.</summary>
        public float Jitter { get; set; } = 0.12f;

        public int RandomSeed { get; set; } = 20261016;

        /// <summary>Below this health fraction the enemy prefers to disengage and reset.</summary>
        public float WoundedHealthFraction { get; set; } = 0.3f;

        public float HitFlinchSeconds { get; set; } = 0.28f;

        public float StaggerSeconds { get; set; } = 0.55f;

        public float GuardBreakSeconds { get; set; } = 0.9f;

        // --- Consideration weights. Zero disables a consideration entirely. ---
        public float MeasureFitWeight { get; set; } = 1.0f;
        public float GuardMatchupWeight { get; set; } = 1.2f;
        public float OpeningReadWeight { get; set; } = 1.0f;
        public float InitiativeWeight { get; set; } = 0.7f;
        public float CommitRiskWeight { get; set; } = 0.9f;
        public float SpacingWeight { get; set; } = 0.8f;
        public float PredictabilityWeight { get; set; } = 0.5f;
        public float BaselineWeight { get; set; } = 0.25f;

        /// <summary>
        /// How readily this combatant slips out of an attack's line instead of meeting it. Only bites on
        /// creatures whose defence mode actually includes avoidance, so it is inert for a swordsman.
        /// </summary>
        public float EvadeWeight { get; set; } = 1.1f;

        /// <summary>How far a single evade carries the combatant across the attack's line.</summary>
        public float EvadeDistance { get; set; } = 0.9f;

        /// <summary>
        /// Seconds of recovery after an evade. This is what stops avoidance from strictly dominating
        /// parrying: a slip gets you out of the way, but it costs you the beat that follows.
        /// </summary>
        public float EvadeRecoverySeconds { get; set; } = 0.35f;

        /// <summary>Guards this enemy will transition into. null means every guard in the table.</summary>
        public GuardId[] AllowedTargetGuards { get; set; }

        public bool IsGuardAllowed(GuardId guard)
        {
            if (guard == GuardId.None)
                return false;

            if (AllowedTargetGuards == null || AllowedTargetGuards.Length == 0)
                return true;

            for (var i = 0; i < AllowedTargetGuards.Length; i++)
            {
                if (AllowedTargetGuards[i] == guard)
                    return true;
            }

            return false;
        }

        /// <summary>A beginner: slow to read, hesitant, and sloppy about distance.</summary>
        public static FencingArchetype CreateRecruit()
        {
            return new FencingArchetype
            {
                Name = "Recruit",
                ReactionSeconds = 0.45f,
                DecisionInterval = 0.14f,
                Aggression = 0.42f,
                MeasureDiscipline = 0.35f,
                Jitter = 0.18f,
                RandomSeed = 101,
                AllowedTargetGuards = new[] { GuardId.VomTag, GuardId.Pflug, GuardId.Alber, GuardId.Ochs, GuardId.Langort },
            };
        }

        /// <summary>The baseline duel enemy: reacts on time, keeps measure, punishes openings.</summary>
        public static FencingArchetype CreateFencer()
        {
            return new FencingArchetype
            {
                Name = "Fencer",
                ReactionSeconds = 0.28f,
                DecisionInterval = 0.1f,
                Aggression = 0.55f,
                MeasureDiscipline = 0.65f,
                Jitter = 0.1f,
                RandomSeed = 202,
                AllowedTargetGuards = new[] { GuardId.VomTag, GuardId.Pflug, GuardId.Alber, GuardId.Ochs, GuardId.Langort },
            };
        }

        /// <summary>A veteran: quick, disciplined, patient, and much harder to bait.</summary>
        public static FencingArchetype CreateVeteran()
        {
            return new FencingArchetype
            {
                Name = "Veteran",
                ReactionSeconds = 0.16f,
                DecisionInterval = 0.08f,
                Aggression = 0.58f,
                MeasureDiscipline = 0.9f,
                Jitter = 0.05f,
                RandomSeed = 303,
                OpeningReadWeight = 1.4f,
                CommitRiskWeight = 1.3f,
                AllowedTargetGuards = new[] { GuardId.VomTag, GuardId.Pflug, GuardId.Ochs, GuardId.Langort },
            };
        }

        /// <summary>Deep copy, so an asset cannot be mutated by a running agent.</summary>
        public FencingArchetype Clone()
        {
            var copy = (FencingArchetype)MemberwiseClone();
            if (AllowedTargetGuards != null)
                copy.AllowedTargetGuards = (GuardId[])AllowedTargetGuards.Clone();

            return copy;
        }

        /// <summary>Total weight, used to normalise the weighted consideration sum.</summary>
        public float TotalWeight =>
            Math.Abs(MeasureFitWeight) + Math.Abs(GuardMatchupWeight) + Math.Abs(OpeningReadWeight) +
            Math.Abs(InitiativeWeight) + Math.Abs(CommitRiskWeight) + Math.Abs(SpacingWeight) +
            Math.Abs(PredictabilityWeight) + Math.Abs(BaselineWeight) + Math.Abs(EvadeWeight);
    }
}

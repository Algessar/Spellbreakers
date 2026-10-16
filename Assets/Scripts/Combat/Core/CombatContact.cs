namespace Spellbreakers.Combat
{
    /// <summary>
    /// Everything a damage receiver needs to know about a hit. The previous float-only signature made it
    /// impossible for a target to react to *how* it was hit, which is exactly what parry reactions and
    /// readable enemy behaviour need.
    /// </summary>
    public readonly struct HitInfo
    {
        public HitInfo(
            float amount,
            GuardId sourceGuard,
            GuardTransitionState sourceTransition,
            CombatOutcome outcome,
            float progress01)
        {
            Amount = amount;
            SourceGuard = sourceGuard;
            SourceTransition = sourceTransition;
            Outcome = outcome;
            Progress01 = progress01;
        }

        public float Amount { get; }

        /// <summary>The guard the attacker started the winning transition from.</summary>
        public GuardId SourceGuard { get; }

        public GuardTransitionState SourceTransition { get; }

        public CombatOutcome Outcome { get; }

        /// <summary>How far through the source transition the blow landed.</summary>
        public float Progress01 { get; }

        public bool IsMeaningful => Amount > 0f;

        public override string ToString()
        {
            return $"HitInfo({Amount:0.#} from {GuardIds.ToDisplayName(SourceGuard)} via {SourceTransition}, {Outcome})";
        }
    }

    /// <summary>
    /// The result of two blades meeting, or of a blade reaching a body. Produced by
    /// <see cref="ParryResolver"/> and consumed by the presentation layer, the AI and feedback effects.
    /// </summary>
    public readonly struct CombatContact
    {
        public CombatContact(
            CombatOutcome outcome,
            GuardId attackerGuard,
            GuardTransitionState attackerTransition,
            GuardId defenderGuard,
            bool defenderWasTransitioning,
            float progress01)
        {
            Outcome = outcome;
            AttackerGuard = attackerGuard;
            AttackerTransition = attackerTransition;
            DefenderGuard = defenderGuard;
            DefenderWasTransitioning = defenderWasTransitioning;
            Progress01 = progress01;
        }

        public CombatOutcome Outcome { get; }

        public GuardId AttackerGuard { get; }

        public GuardTransitionState AttackerTransition { get; }

        public GuardId DefenderGuard { get; }

        public bool DefenderWasTransitioning { get; }

        public float Progress01 { get; }

        /// <summary>True when the two blades met, as opposed to a blade reaching a body.</summary>
        public bool IsBladeOnBlade =>
            Outcome == CombatOutcome.Parry ||
            Outcome == CombatOutcome.Bind ||
            Outcome == CombatOutcome.Deflect;

        public string Describe()
        {
            var attacker = GuardIds.ToDisplayName(AttackerGuard);
            var defender = GuardIds.ToDisplayName(DefenderGuard);
            var mode = DefenderWasTransitioning ? "meeting" : "holding";
            return $"{Outcome}: {attacker} attacking {defender} ({mode}) at {Progress01:0.00}";
        }
    }

    /// <summary>
    /// What the combat core needs from anything that can be hit. Kept in the core so damage resolution
    /// can be tested with a fake target and so nothing in the rules depends on MonoBehaviour.
    /// </summary>
    public interface ICombatTarget
    {
        CombatTeam Team { get; }

        bool IsAlive { get; }

        float CurrentHealth { get; }

        float MaxHealth { get; }

        /// <summary>Returns the amount of health actually removed, so callers can log or scale feedback.</summary>
        float ApplyDamage(in HitInfo hit);
    }
}

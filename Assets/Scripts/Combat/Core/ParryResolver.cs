namespace Spellbreakers.Combat
{
    /// <summary>
    /// Decides how an incoming attack resolves against a defender, using the authored
    /// <see cref="ParryTable"/>.
    ///
    /// The only rule this adds on top of the table is timing: a defender who is not inside a parry
    /// window cannot parry at all, no matter how favourable the matchup. That is what keeps the design
    /// doc's "reward reading and timing rather than holding block" promise - simply standing in Ochs
    /// does not make a combatant safe.
    /// </summary>
    public static class ParryResolver
    {
        /// <summary>
        /// Resolves an attack against a defender. <paramref name="defenderParryWindowOpen"/> comes from
        /// <see cref="FencingAgent.IsInParryWindow"/> so the resolver and the agent cannot disagree.
        /// </summary>
        public static CombatContact Resolve(
            GuardTransitionState attackerTransition,
            GuardId defenderGuard,
            GuardTransitionState defenderTransition,
            bool defenderParryWindowOpen,
            ParryTable table,
            float attackerProgress01)
        {
            if (!attackerTransition.IsValid || table == null)
            {
                return new CombatContact(
                    CombatOutcome.HitBody,
                    attackerTransition.From,
                    attackerTransition,
                    defenderGuard,
                    defenderTransition.IsValid,
                    attackerProgress01);
            }

            var defenderTransitioning = defenderTransition.IsValid;

            if (!defenderParryWindowOpen)
            {
                // Off time: the defence does not count, so the blade goes through.
                return new CombatContact(
                    CombatOutcome.HitBody,
                    attackerTransition.From,
                    attackerTransition,
                    defenderGuard,
                    defenderTransitioning,
                    attackerProgress01);
            }

            var outcome = defenderTransitioning
                ? table.ResolveTransition(defenderTransition.Direction, attackerTransition.Direction)
                : table.ResolveGuard(defenderGuard, attackerTransition.Direction);

            return new CombatContact(
                outcome,
                attackerTransition.From,
                attackerTransition,
                defenderGuard,
                defenderTransitioning,
                attackerProgress01);
        }

        /// <summary>The reaction the attacker suffers for a given outcome. None means the attack stands.</summary>
        public static ReactionState AttackerReactionFor(CombatOutcome outcome, float deflectionSeconds)
        {
            switch (outcome)
            {
                case CombatOutcome.Deflect:
                    return ReactionState.Staggered;
                case CombatOutcome.Bind:
                    return ReactionState.None;
                default:
                    return ReactionState.None;
            }
        }

        /// <summary>The reaction the defender suffers. A parried attacker leaves the defender working.</summary>
        public static ReactionState DefenderReactionFor(CombatOutcome outcome)
        {
            switch (outcome)
            {
                case CombatOutcome.HitBody:
                    return ReactionState.Hit;
                case CombatOutcome.Bind:
                    return ReactionState.None;
                default:
                    return ReactionState.None;
            }
        }
    }
}

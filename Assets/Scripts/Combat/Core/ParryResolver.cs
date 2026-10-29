namespace Spellbreakers.Combat
{
    /// <summary>
    /// Decides how an incoming attack resolves against a defender, using the authored
    /// <see cref="ParryTable"/>.
    ///
    /// Two rules are added on top of the table. The first is timing: a defender who is not inside a parry
    /// window cannot parry at all, no matter how favourable the matchup. That is what keeps the design
    /// doc's "reward reading and timing rather than holding block" promise - simply standing in Ochs
    /// does not make a combatant safe. The second is side: a static guard only intercepts a strike thrown
    /// from the opposite side, which the table applies for the guards that are authored side-sensitive.
    /// </summary>
    public static class ParryResolver
    {
        /// <summary>
        /// Resolves an attack against a defender. <paramref name="defenderParryWindowOpen"/> comes from
        /// <see cref="FencingAgent.IsInParryWindow"/> so the resolver and the agent cannot disagree.
        ///
        /// The defender is passed as a <see cref="GuardKey"/> because the side it is held on now decides
        /// whether a static guard is on the incoming line at all.
        /// </summary>
        public static CombatContact Resolve(
            GuardTransitionState attackerTransition,
            GuardKey defenderGuard,
            GuardTransitionState defenderTransition,
            bool defenderParryWindowOpen,
            ParryTable table,
            float attackerProgress01)
        {
            var attackerSide = attackerTransition.FromKey.Side;
            var defenderSide = defenderGuard.Side;

            if (!attackerTransition.IsValid || table == null)
            {
                return new CombatContact(
                    CombatOutcome.HitBody,
                    attackerTransition.From,
                    attackerTransition,
                    defenderGuard.Id,
                    defenderTransition.IsValid,
                    attackerProgress01,
                    attackerSide,
                    defenderSide);
            }

            var defenderTransitioning = defenderTransition.IsValid;

            if (!defenderParryWindowOpen)
            {
                // Off time: the defence does not count, so the blade goes through.
                return new CombatContact(
                    CombatOutcome.HitBody,
                    attackerTransition.From,
                    attackerTransition,
                    defenderGuard.Id,
                    defenderTransitioning,
                    attackerProgress01,
                    attackerSide,
                    defenderSide);
            }

            var outcome = defenderTransitioning
                ? table.ResolveTransition(defenderTransition.Direction, attackerTransition.Direction)
                : table.ResolveGuard(defenderGuard, attackerTransition.Direction, attackerSide);

            return new CombatContact(
                outcome,
                attackerTransition.From,
                attackerTransition,
                defenderGuard.Id,
                defenderTransitioning,
                attackerProgress01,
                attackerSide,
                defenderSide);
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

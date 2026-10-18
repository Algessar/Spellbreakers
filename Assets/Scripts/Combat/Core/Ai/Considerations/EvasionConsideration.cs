namespace Spellbreakers.Combat.Ai.Considerations
{
    /// <summary>
    /// The avoidance answer: how much this combatant should get out of the attack's line rather than
    /// meet it.
    ///
    /// The design doc wants deflection to work on the physical relationship between the attack and the
    /// defender, which means a creature with nothing hard enough to turn a blade must have a different
    /// defensive option - otherwise it simply cannot defend. Avoidance is that option, and it is resolved
    /// geometrically by the hit test rather than by the parry table, so this consideration only decides
    /// *whether to slip*, never *whether the slip worked*.
    ///
    /// The threat curve deliberately peaks as the incoming blade is about to go live rather than only
    /// once it already is: evading a blade that is already swinging is too late in practice, and reading
    /// the telegraph is exactly the skill the doc wants enemies to reward.
    /// </summary>
    public sealed class EvasionConsideration : Consideration
    {
        /// <summary>How much of the threat remains worth acting on when the combatant can also parry.</summary>
        private const float ParryConfidenceDiscount = 0.7f;

        /// <summary>How tempting staying in the line is while a threat is live. Not zero - attacking through is a real choice.</summary>
        private const float StayInLineFactor = 0.3f;

        public EvasionConsideration() : base("Evasion", ResponseCurve.EaseIn)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.EvadeWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            if (!context.CanAvoid)
                return 0f;

            var threat = Threat(context);

            // A combatant that can parry already has an answer, so it should not also be pulled towards
            // slipping by the full weight of the same threat.
            if (context.CanParry)
                threat *= ParryConfidenceDiscount;

            return context.Action.Kind == FencingActionKind.Evade
                ? threat
                : threat * StayInLineFactor;
        }

        /// <summary>How dangerous standing still in the line currently is, 0..1.</summary>
        public static float Threat(in FencingContext context)
        {
            var opponent = context.Opponent;
            if (!opponent.HasOpponent)
                return 0f;

            if (opponent.BladeLive)
                return 1f;

            if (opponent.InTransition)
            {
                var profile = opponent.Transition.Profile;
                if (profile == null || !profile.HasDamageWindow)
                    return 0.15f;

                if (opponent.Progress01 >= profile.DamageWindowStart)
                    return 1f;

                // Imminence: rises as the blade approaches going live.
                var start = profile.DamageWindowStart;
                var imminence = start <= 0.0001f ? 1f : FencingMath.Clamp01(opponent.Progress01 / start);
                return 0.15f + 0.7f * imminence;
            }

            // Holding a guard is only threatening while that guard could still answer an attack.
            return opponent.InParryWindow ? 0.1f : 0.3f;
        }
    }
}

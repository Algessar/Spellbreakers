using System.Collections.Generic;

namespace Spellbreakers.Combat.Ai.Considerations
{
    /// <summary>
    /// Scores the guard the candidate would leave the combatant in by asking the parry table a direct
    /// question: if the opponent attacks now, how well does this guard answer the lines they can
    /// actually come along?
    ///
    /// This is the consideration that makes an enemy cautious against certain guards at range, which the
    /// design notes call out as the place where the whole system lives or dies.
    /// </summary>
    public sealed class GuardMatchupConsideration : Consideration
    {
        private readonly List<TransitionDirection> _directions = new List<TransitionDirection>();

        public GuardMatchupConsideration() : base("Guard matchup", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.GuardMatchupWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            if (!context.Opponent.HasOpponent || context.ParryTable == null)
                return 0.5f;

            var guard = context.ProjectedGuard;
            if (guard == GuardId.None)
                return 0.5f;

            ConsiderationMath.OpponentThreatDirections(context, _directions);
            if (_directions.Count == 0)
                return 0.5f;

            var total = 0f;
            for (var i = 0; i < _directions.Count; i++)
                total += ConsiderationMath.DefensiveValue(context.ParryTable.ResolveGuard(guard, _directions[i]));

            return total / _directions.Count;
        }
    }

    /// <summary>
    /// How much of an opening the opponent is currently showing, multiplied by how much the candidate
    /// actually uses it. Attacking into a live blade scores badly; attacking into recovery scores well.
    /// </summary>
    public sealed class OpeningReadConsideration : Consideration
    {
        public OpeningReadConsideration() : base("Opening read", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.OpeningReadWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            var openness = MeasureOpenness(context);
            var usage = UsageFor(context.Action.Kind);
            return FencingMath.Clamp01(openness * usage);
        }

        /// <summary>The opening itself, independent of what the combatant does with it.</summary>
        public static float MeasureOpenness(in FencingContext context)
        {
            var opponent = context.Opponent;
            if (!opponent.HasOpponent)
                return 0.5f;

            if (opponent.InTransition)
            {
                var profile = opponent.Transition.Profile;
                if (profile == null)
                    return 0.5f;

                if (profile.HasDamageWindow && opponent.Progress01 >= profile.DamageWindowEnd)
                    return 1f; // recovery: the classic punish window

                if (opponent.BladeLive)
                    return 0.05f; // walking into a live blade

                if (opponent.IsCommitted)
                    return 0.35f;

                return 0.4f;
            }

            // Holding a guard is only safe during its arrival grace. After that, it is just exposed.
            return opponent.InParryWindow ? 0.15f : 0.7f;
        }

        private static float UsageFor(FencingActionKind kind)
        {
            switch (kind)
            {
                case FencingActionKind.ChangeGuard: return 1f;
                case FencingActionKind.Step: return 0.45f;
                case FencingActionKind.Retreat: return 0.3f;
                default: return 0.2f;
            }
        }
    }

    /// <summary>
    /// Rewards acting while holding the initiative, and acting while the opponent cannot answer.
    /// The doc's Vor/Indes, expressed as a preference rather than as a hidden stat.
    /// </summary>
    public sealed class InitiativeConsideration : Consideration
    {
        public InitiativeConsideration() : base("Initiative", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.InitiativeWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            var initiative = context.SelfVor ? 0.8f : 0.4f;

            if (context.Opponent.HasOpponent && !context.Opponent.BladeLive)
                initiative += 0.2f;

            if (context.Opponent.IsCommitted)
                initiative += 0.15f;

            var usage = context.Action.Kind == FencingActionKind.ChangeGuard
                ? 1f
                : context.Action.IsMovement ? 0.6f : 0.3f;

            return FencingMath.Clamp01(initiative) * usage;
        }
    }

    /// <summary>
    /// The inverse of risk: how safe the candidate is if the opponent's blade is live right now.
    /// A guard change into a blade the parry table cannot answer is scored as the mistake it is.
    /// </summary>
    public sealed class CommitRiskConsideration : Consideration
    {
        public CommitRiskConsideration() : base("Commit risk", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.CommitRiskWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            if (context.Action.Kind != FencingActionKind.ChangeGuard)
            {
                // Nothing is committed, so there is no commitment risk. Slight preference for stillness
                // while a blade is live keeps the enemy from walking into an attack.
                return context.Opponent.BladeLive ? 0.75f : 1f;
            }

            if (!context.Opponent.BladeLive || context.ParryTable == null)
                return 1f;

            var incoming = context.Opponent.InTransition
                ? context.Opponent.Transition.Direction
                : TransitionDirection.Neutral;

            var answer = ConsiderationMath.DefensiveValue(
                context.ParryTable.ResolveGuard(context.Action.TargetGuard, incoming));

            return FencingMath.Clamp01(answer);
        }
    }
}

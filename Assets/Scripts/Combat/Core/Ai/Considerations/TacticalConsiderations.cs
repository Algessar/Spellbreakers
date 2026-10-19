using System;

namespace Spellbreakers.Combat.Ai.Considerations
{
    /// <summary>
    /// How good the distance is for the candidate.
    ///
    /// For a guard change it also asks whether that specific transition can actually reach: out of
    /// measure the attack becomes a feint, and inside the minimum it is refused outright. This is the
    /// design doc's Zufechten rule expressed as a score, and it is what makes an enemy hesitate at
    /// range instead of flailing.
    /// </summary>
    public sealed class MeasureFitConsideration : Consideration
    {
        public MeasureFitConsideration() : base("Measure fit", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.MeasureFitWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            if (!context.Opponent.HasOpponent)
                return 0.5f;

            ConsiderationMath.PreferredRange(context, out var min, out var max);
            var distance = context.ProjectedDistance;
            var error = ConsiderationMath.DistanceError(distance, min, max);
            var band = FencingMath.Max(0.4f, max - min);

            // Discipline stretches how quickly the score falls away from the preferred band.
            var discipline = FencingMath.Lerp(0.6f, 1.4f, FencingMath.Clamp01(context.Archetype.MeasureDiscipline));

            // A reciprocal rather than a clamped subtraction. A subtraction bottoms out at zero once the
            // combatant is far enough outside its band, which makes the consideration go silent exactly
            // when distance matters most - it can no longer tell "step in" from "walk away". This form
            // stays strictly positive and strictly decreasing, so it always discriminates.
            var scaled = error / band * discipline;
            var fit = 1f / (1f + scaled);

            if (context.Action.Kind != FencingActionKind.ChangeGuard)
                return fit;

            if (context.TransitionTable == null ||
                !context.TransitionTable.TryGet(context.SelfGuard, context.Action.TargetGuard, out var profile) ||
                profile == null)
            {
                return fit * 0.2f;
            }

            if (distance < profile.MinDistance)
                return fit * 0.2f;

            if (distance > profile.MaxDistance)
            {
                // Reachable only as a feint: still tempting, but the brain should prefer to close first.
                var overshoot = FencingMath.Clamp01((distance - profile.MaxDistance) / FencingMath.Max(0.5f, profile.MaxDistance));
                return fit * FencingMath.Lerp(0.7f, 0.25f, overshoot);
            }

            return fit;
        }
    }

    /// <summary>Rewards movement that fixes the distance error, and treats standing still as neutral.</summary>
    public sealed class SpacingConsideration : Consideration
    {
        public SpacingConsideration() : base("Spacing", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.SpacingWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            if (!context.Opponent.HasOpponent)
                return 0.5f;

            ConsiderationMath.PreferredRange(context, out var min, out var max);

            if (!context.Action.IsMovement)
            {
                // Holding still is fine while the distance is acceptable, and quietly bad when it is not.
                var error = ConsiderationMath.DistanceError(context.Opponent.Distance, min, max);
                return 0.6f - FencingMath.Clamp01(error / FencingMath.Max(0.4f, max - min)) * 0.4f;
            }

            var before = ConsiderationMath.DistanceError(context.Opponent.Distance, min, max);
            var after = ConsiderationMath.DistanceError(context.ProjectedDistance, min, max);
            var step = FencingMath.Max(0.1f, Math.Abs(context.Action.StepDistance));
            var improvement = before - after;

            return FencingMath.Clamp01(0.5f + improvement / step * 0.5f);
        }
    }

    /// <summary>Keeps the enemy from cycling the same guard over and over, which would read as a bug.</summary>
    public sealed class PredictabilityConsideration : Consideration
    {
        public PredictabilityConsideration() : base("Predictability", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.PredictabilityWeight;

        protected override float EvaluateInput(in FencingContext context)
        {
            if (context.Action.Kind != FencingActionKind.ChangeGuard)
                return 0.75f;

            return context.Action.TargetGuard == context.LastRequestedGuard ? 0.15f : 0.8f;
        }
    }

    /// <summary>A flat score so that doing nothing remains a genuine option rather than a last resort.</summary>
    public sealed class BaselineConsideration : Consideration
    {
        public BaselineConsideration() : base("Baseline", ResponseCurve.Linear)
        {
        }

        public override float WeightIn(FencingArchetype archetype) => archetype.BaselineWeight;

        protected override float EvaluateInput(in FencingContext context) => 0.5f;
    }
}

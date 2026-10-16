namespace Spellbreakers.Combat.Ai
{
    /// <summary>
    /// One axis on which every candidate action is judged. A consideration returns a 0..1 score where
    /// higher is better, for *any* candidate - including movement and waiting. That uniformity is what
    /// lets the brain compare "attack now" against "step back" on the same scale instead of using
    /// special cases, and it is what makes the debug overlay's breakdown meaningful.
    ///
    /// The weight comes from the archetype, so the same consideration behaves differently for a
    /// hesitant recruit and an aggressive veteran without any code change.
    /// </summary>
    public abstract class Consideration
    {
        protected Consideration(string name, ResponseCurve curve = null)
        {
            Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
            Curve = curve ?? ResponseCurve.Linear;
        }

        public string Name { get; }

        /// <summary>Shapes the raw input into a final score. Defaults to a straight line.</summary>
        public ResponseCurve Curve { get; set; }

        /// <summary>How much this consideration matters to a given archetype.</summary>
        public abstract float WeightIn(FencingArchetype archetype);

        /// <summary>Raw 0..1 measurement, before the response curve.</summary>
        protected abstract float EvaluateInput(in FencingContext context);

        public float Score(in FencingContext context)
        {
            return Curve.Evaluate(EvaluateInput(context));
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Shared maths for the default considerations.</summary>
    public static class ConsiderationMath
    {
        /// <summary>
        /// How much a defensive outcome is worth. Reading these as fencing: a clean parry is complete,
        /// a deflection keeps you alive but out of line, a bind is a stalemate, and a hit is a failure.
        /// </summary>
        public static float DefensiveValue(CombatOutcome outcome)
        {
            switch (outcome)
            {
                case CombatOutcome.Parry: return 1.0f;
                case CombatOutcome.Deflect: return 0.75f;
                case CombatOutcome.Bind: return 0.45f;
                case CombatOutcome.HitBody: return 0.0f;
                default: return 0.1f;
            }
        }

        /// <summary>
        /// The distance band this combatant wants right now. A wounded enemy widens the band outward,
        /// which is how "disengage and reset when hurt" falls out of the same model rather than
        /// needing a separate flee state.
        /// </summary>
        public static void PreferredRange(in FencingContext context, out float min, out float max)
        {
            var archetype = context.Archetype;
            min = archetype.PreferredMinDistance;
            max = archetype.PreferredMaxDistance;

            if (archetype.WoundedHealthFraction <= 0f ||
                context.SelfHealthFraction > archetype.WoundedHealthFraction)
            {
                return;
            }

            min = max;
            max *= 1.7f;
        }

        /// <summary>How far outside its preferred band the combatant is, in metres. Zero means happy.</summary>
        public static float DistanceError(float distance, float min, float max)
        {
            if (distance < min)
                return min - distance;

            return distance > max ? distance - max : 0f;
        }

        /// <summary>
        /// The attack lines the opponent could currently come along. Uses their live transition when
        /// they are committed to one, and otherwise everything their guard can lead to.
        /// </summary>
        public static void OpponentThreatDirections(in FencingContext context, System.Collections.Generic.List<TransitionDirection> results)
        {
            results.Clear();

            var opponent = context.Opponent;
            if (opponent.InTransition)
            {
                results.Add(opponent.Transition.Direction);
                return;
            }

            var table = context.TransitionTable;
            if (table == null || opponent.Guard == GuardId.None)
            {
                results.Add(TransitionDirection.Neutral);
                return;
            }

            var profiles = table.Profiles;
            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                if (profile.From != opponent.Guard || !profile.HasDamageWindow)
                    continue;

                if (!results.Contains(profile.Direction))
                    results.Add(profile.Direction);
            }

            if (results.Count == 0)
                results.Add(TransitionDirection.Neutral);
        }
    }
}

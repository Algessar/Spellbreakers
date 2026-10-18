namespace Spellbreakers.Combat.Ai
{
    /// <summary>What the brain can choose to do. Movement is part of the action, not a side channel.</summary>
    public enum FencingActionKind
    {
        /// <summary>Hold the guard and do nothing. A real option - not every beat is an attack.</summary>
        Wait = 0,

        /// <summary>Start the transition into another guard. This is the attack.</summary>
        ChangeGuard = 1,

        /// <summary>Move along the line: positive is towards the opponent.</summary>
        Step = 2,

        /// <summary>Move away from the opponent to restore measure.</summary>
        Retreat = 3,

        /// <summary>
        /// Break the line across it: the doc's avoidance. Distinct from <see cref="Retreat"/> because
        /// opening the measure and leaving the attack's line are different answers - retreating from a
        /// committed lunge still loses, whereas stepping off the line does not.
        /// </summary>
        Evade = 4,
    }

    /// <summary>
    /// One candidate behaviour. Scoring happens over a list of these, which is what makes the AI's
    /// choice inspectable in the debug overlay instead of opaque.
    /// </summary>
    public readonly struct FencingAction
    {
        private FencingAction(
            FencingActionKind kind,
            GuardId targetGuard,
            float stepDistance,
            float lateralDistance,
            string name)
        {
            Kind = kind;
            TargetGuard = targetGuard;
            StepDistance = stepDistance;
            LateralDistance = lateralDistance;
            Name = name;
        }

        public FencingActionKind Kind { get; }

        /// <summary>Only meaningful for <see cref="FencingActionKind.ChangeGuard"/>.</summary>
        public GuardId TargetGuard { get; }

        /// <summary>
        /// Metres along the line, signed so that positive means *closer*. A step in therefore has a
        /// positive value; a retreat is negative. An evade does not change the line distance, so it is 0.
        /// </summary>
        public float StepDistance { get; }

        /// <summary>
        /// Metres across the line, signed. Positive and negative are the two directions of the slip;
        /// which way is "left" is the locomotion layer's business, not the brain's.
        /// </summary>
        public float LateralDistance { get; }

        public string Name { get; }

        public static FencingAction Wait { get; } =
            new FencingAction(FencingActionKind.Wait, GuardId.None, 0f, 0f, "Wait");

        public static FencingAction ChangeGuard(GuardId guard)
        {
            return new FencingAction(
                FencingActionKind.ChangeGuard,
                guard,
                0f,
                0f,
                "Change to " + GuardIds.ToDisplayName(guard));
        }

        public static FencingAction Step(float metres)
        {
            var distance = FencingMath.Max(0f, metres);
            return distance <= 0f
                ? Wait
                : new FencingAction(FencingActionKind.Step, GuardId.None, distance, 0f, $"Step in {distance:0.0}m");
        }

        public static FencingAction Retreat(float metres)
        {
            var distance = FencingMath.Max(0f, metres);
            return distance <= 0f
                ? Wait
                : new FencingAction(FencingActionKind.Retreat, GuardId.None, -distance, 0f, $"Retreat {distance:0.0}m");
        }

        /// <summary>
        /// A lateral slip out of the attack's line. The sign picks which way; the magnitude is how far.
        /// </summary>
        public static FencingAction Evade(float lateralMetres)
        {
            var distance = FencingMath.Max(0f, lateralMetres);
            return distance <= 0f
                ? Wait
                : new FencingAction(FencingActionKind.Evade, GuardId.None, 0f, distance, $"Evade {distance:0.0}m");
        }

        /// <summary>Any action that moves the body. Used where a check cares about commitment, not direction.</summary>
        public bool IsMovement =>
            Kind == FencingActionKind.Step ||
            Kind == FencingActionKind.Retreat ||
            Kind == FencingActionKind.Evade;

        /// <summary>Moves along the line, and therefore changes the measure.</summary>
        public bool IsLineMovement =>
            Kind == FencingActionKind.Step || Kind == FencingActionKind.Retreat;

        /// <summary>Stays in the line, and therefore ignores the attack's direction.</summary>
        public bool IsInLine =>
            Kind != FencingActionKind.Evade;

        public override string ToString()
        {
            return Name;
        }
    }
}

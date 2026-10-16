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
    }

    /// <summary>
    /// One candidate behaviour. Scoring happens over a list of these, which is what makes the AI's
    /// choice inspectable in the debug overlay instead of opaque.
    /// </summary>
    public readonly struct FencingAction
    {
        private FencingAction(FencingActionKind kind, GuardId targetGuard, float stepDistance, string name)
        {
            Kind = kind;
            TargetGuard = targetGuard;
            StepDistance = stepDistance;
            Name = name;
        }

        public FencingActionKind Kind { get; }

        /// <summary>Only meaningful for <see cref="FencingActionKind.ChangeGuard"/>.</summary>
        public GuardId TargetGuard { get; }

        /// <summary>
        /// Metres along the line, signed so that positive means *closer*. A step in therefore has a
        /// positive value; a retreat is negative.
        /// </summary>
        public float StepDistance { get; }

        public string Name { get; }

        public static FencingAction Wait { get; } = new FencingAction(FencingActionKind.Wait, GuardId.None, 0f, "Wait");

        public static FencingAction ChangeGuard(GuardId guard)
        {
            return new FencingAction(
                FencingActionKind.ChangeGuard,
                guard,
                0f,
                "Change to " + GuardIds.ToDisplayName(guard));
        }

        public static FencingAction Step(float metres)
        {
            var distance = FencingMath.Max(0f, metres);
            return distance <= 0f
                ? Wait
                : new FencingAction(FencingActionKind.Step, GuardId.None, distance, $"Step in {distance:0.0}m");
        }

        public static FencingAction Retreat(float metres)
        {
            var distance = FencingMath.Max(0f, metres);
            return distance <= 0f
                ? Wait
                : new FencingAction(FencingActionKind.Retreat, GuardId.None, -distance, $"Retreat {distance:0.0}m");
        }

        public bool IsMovement => Kind == FencingActionKind.Step || Kind == FencingActionKind.Retreat;

        public override string ToString()
        {
            return Name;
        }
    }
}

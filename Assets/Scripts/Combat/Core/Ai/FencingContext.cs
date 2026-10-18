namespace Spellbreakers.Combat.Ai
{
    /// <summary>
    /// What the AI knows about its opponent. Produced by perception (Gameplay) and consumed by the
    /// decision layer (pure), so a brain can be tested from a hand-written snapshot with no scene.
    /// </summary>
    public readonly struct OpponentSnapshot
    {
        public OpponentSnapshot(
            GuardId guard,
            GuardTransitionState transition,
            float progress01,
            bool bladeLive,
            bool inParryWindow,
            float distance,
            Measure measure,
            float healthFraction,
            bool isAlive,
            float secondsSinceGuardChange)
        {
            Guard = guard;
            Transition = transition;
            Progress01 = progress01;
            BladeLive = bladeLive;
            InParryWindow = inParryWindow;
            Distance = distance;
            Measure = measure;
            HealthFraction = healthFraction;
            IsAlive = isAlive;
            SecondsSinceGuardChange = secondsSinceGuardChange;
        }

        public GuardId Guard { get; }

        public GuardTransitionState Transition { get; }

        public bool InTransition => Transition.IsValid;

        public float Progress01 { get; }

        /// <summary>The opponent's blade is inside its damage window right now.</summary>
        public bool BladeLive { get; }

        public bool InParryWindow { get; }

        public float Distance { get; }

        public Measure Measure { get; }

        public float HealthFraction { get; }

        public bool IsAlive { get; }

        public float SecondsSinceGuardChange { get; }

        /// <summary>The opponent is past its commitment point and can no longer change its mind.</summary>
        public bool IsCommitted => InTransition && Transition.IsCommitted(Progress01);

        public bool HasOpponent => Guard != GuardId.None && IsAlive;

        public static OpponentSnapshot None =>
            new OpponentSnapshot(
                GuardId.None,
                default,
                0f,
                false,
                false,
                float.PositiveInfinity,
                Measure.OutOfRange,
                1f,
                false,
                0f);
    }

    /// <summary>
    /// Everything a consideration is allowed to look at, including the candidate being scored. Bundled
    /// so that adding a new consideration never changes any existing signature.
    /// </summary>
    public readonly struct FencingContext
    {
        /// <summary>
        /// Backward-compatible overload. Defaults the combatant to being able to both parry and avoid,
        /// which is the behaviour every caller had before defence modes existed.
        /// </summary>
        public FencingContext(
            GuardId selfGuard,
            GuardTransitionState selfTransition,
            float selfProgress01,
            float selfHealthFraction,
            ReactionState selfReaction,
            bool selfVor,
            float selfTipReach,
            float elapsedSeconds,
            OpponentSnapshot opponent,
            FencingAction action,
            TransitionTable transitionTable,
            ParryTable parryTable,
            FencingArchetype archetype,
            GuardId lastRequestedGuard,
            float random01)
            : this(
                selfGuard,
                selfTransition,
                selfProgress01,
                selfHealthFraction,
                selfReaction,
                selfVor,
                selfTipReach,
                elapsedSeconds,
                opponent,
                action,
                transitionTable,
                parryTable,
                archetype,
                lastRequestedGuard,
                random01,
                DefenceMode.Both)
        {
        }

        public FencingContext(
            GuardId selfGuard,
            GuardTransitionState selfTransition,
            float selfProgress01,
            float selfHealthFraction,
            ReactionState selfReaction,
            bool selfVor,
            float selfTipReach,
            float elapsedSeconds,
            OpponentSnapshot opponent,
            FencingAction action,
            TransitionTable transitionTable,
            ParryTable parryTable,
            FencingArchetype archetype,
            GuardId lastRequestedGuard,
            float random01,
            DefenceMode selfDefenceMode)
        {
            SelfGuard = selfGuard;
            SelfTransition = selfTransition;
            SelfProgress01 = selfProgress01;
            SelfHealthFraction = selfHealthFraction;
            SelfReaction = selfReaction;
            SelfVor = selfVor;
            SelfTipReach = selfTipReach;
            ElapsedSeconds = elapsedSeconds;
            Opponent = opponent;
            Action = action;
            TransitionTable = transitionTable;
            ParryTable = parryTable;
            Archetype = archetype;
            LastRequestedGuard = lastRequestedGuard;
            Random01 = random01;
            SelfDefenceMode = selfDefenceMode;
        }

        /// <summary>
        /// Which defensive answers this combatant actually has. A creature with no hard appendage cannot
        /// parry and must avoid instead; that choice belongs to the creature, not to the player's rules.
        /// </summary>
        public DefenceMode SelfDefenceMode { get; }

        public bool CanParry => (SelfDefenceMode & DefenceMode.Parry) != 0;

        public bool CanAvoid => (SelfDefenceMode & DefenceMode.Avoid) != 0;

        public GuardId SelfGuard { get; }

        public GuardTransitionState SelfTransition { get; }

        public bool SelfInTransition => SelfTransition.IsValid;

        public float SelfProgress01 { get; }

        public float SelfHealthFraction { get; }

        public ReactionState SelfReaction { get; }

        public bool SelfVor { get; }

        public float SelfTipReach { get; }

        public float ElapsedSeconds { get; }

        public OpponentSnapshot Opponent { get; }

        public FencingAction Action { get; }

        public TransitionTable TransitionTable { get; }

        public ParryTable ParryTable { get; }

        public FencingArchetype Archetype { get; }

        public GuardId LastRequestedGuard { get; }

        /// <summary>Fresh 0..1 noise for this decision, so considerations cannot accidentally share it.</summary>
        public float Random01 { get; }

        /// <summary>
        /// Distance after the candidate movement, which is what spacing decisions care about.
        ///
        /// <see cref="FencingAction.StepDistance"/> is signed along the line with positive meaning
        /// *closer*, so it is subtracted. Getting this backwards makes every distance-based
        /// consideration prefer walking away from the opponent, which is exactly the kind of inversion
        /// that reads as "the AI is broken" rather than "the sign is wrong".
        /// </summary>
        public float ProjectedDistance
        {
            get
            {
                if (Action.IsMovement)
                    return FencingMath.Max(0.1f, Opponent.Distance - Action.StepDistance);

                return Opponent.Distance;
            }
        }

        /// <summary>The guard the candidate would leave the combatant in.</summary>
        public GuardId ProjectedGuard =>
            Action.Kind == FencingActionKind.ChangeGuard ? Action.TargetGuard : SelfGuard;

        /// <summary>
        /// The same knowledge with one candidate swapped in. The brain scores every option against an
        /// identical world, so a candidate can never win because it was handed fresher information.
        /// </summary>
        public FencingContext WithAction(FencingAction action, float random01)
        {
            return new FencingContext(
                SelfGuard,
                SelfTransition,
                SelfProgress01,
                SelfHealthFraction,
                SelfReaction,
                SelfVor,
                SelfTipReach,
                ElapsedSeconds,
                Opponent,
                action,
                TransitionTable,
                ParryTable,
                Archetype,
                LastRequestedGuard,
                random01,
                SelfDefenceMode);
        }
    }
}

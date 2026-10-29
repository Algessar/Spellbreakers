using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The combat state machine shared by the player and every enemy: which guard a combatant is in,
    /// which transition is running, how far through it is, and when its blade is live or its guard can
    /// parry.
    ///
    /// Deliberately plain C# with no UnityEngine dependency and no Animator. The design doc requires
    /// that combat decisions determine movement rather than the animation secretly deciding combat, so
    /// the core owns the clock and the presentation layer is scrubbed to match. That also makes every
    /// rule below directly testable.
    ///
    /// Spatial facts are not stored here. Callers pass distance in, so the same rules can be applied to
    /// a live duel, a recorded replay, or a unit test.
    /// </summary>
    public sealed class FencingAgent
    {
        private readonly TransitionTable _table;

        private float _elapsed;
        private float _duration;
        private float _feintRemaining = -1f;
        private float _reactionRemaining;
        private float _lastKnownDistance = float.PositiveInfinity;
        private Measure _lastKnownMeasure = Measure.OutOfRange;
        private GuardId _queuedGuard = GuardId.None;

        /// <summary>Which side the queued request asked for, so a side switch survives the queue.</summary>
        private GuardSide _queuedGuardSide = GuardSide.Right;
        private ReactionState _reaction;

        public FencingAgent(TransitionTable table, GuardId initialGuard)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            Current = initialGuard;
            Previous = initialGuard;

            var guardProfile = _table.GuardOr(initialGuard);
            TipReach = guardProfile.TipReach;

            // A freshly spawned combatant gets the same arrival grace it would have had by transitioning
            // into the guard. Without this, a fighter that has never moved is defenceless from frame one.
            ParryWindowRemaining = guardProfile.ParryGraceSeconds;
        }

        /// <summary>
        /// The rules this combatant resolves against. Each combatant gets its own clone, so an archetype
        /// can tighten distance gates for an enemy without changing the player's guard freedom.
        /// </summary>
        public TransitionTable Table => _table;

        /// <summary>The guard the combatant is in, or is transitioning towards completion of.</summary>
        public GuardId Current { get; private set; }

        /// <summary>Which side the current guard is held on (design doc 4). Right unless authored otherwise.</summary>
        public GuardSide CurrentSide { get; private set; } = GuardSide.Right;

        /// <summary>The guard held before the last completed transition.</summary>
        public GuardId Previous { get; private set; }

        /// <summary>Which side the previous guard was held on.</summary>
        public GuardSide PreviousSide { get; private set; } = GuardSide.Right;

        /// <summary>The current guard including its side.</summary>
        public GuardKey CurrentKey => new GuardKey(Current, CurrentSide);

        /// <summary>
        /// Which defensive answers this combatant actually has. Defaults to both, which is what every
        /// combatant had before defence modes existed, so nothing changes unless something sets it.
        ///
        /// This lives on the agent rather than on the AI archetype because it is a property of the body,
        /// not of the personality: a creature with no hard appendage cannot parry whatever its mood, and
        /// the hit test needs to know that at the moment of contact - long after any decision was made.
        /// </summary>
        public DefenceMode DefenceMode { get; set; } = DefenceMode.Both;

        /// <summary>True when this combatant can meet an attack with its own body or weapon.</summary>
        public bool CanParry => (DefenceMode & DefenceMode.Parry) != 0;

        /// <summary>True when this combatant can leave the attack's line instead of meeting it.</summary>
        public bool CanAvoid => (DefenceMode & DefenceMode.Avoid) != 0;

        /// <summary>The previous guard including its side.</summary>
        public GuardKey PreviousKey => new GuardKey(Previous, PreviousSide);

        public GuardTransitionState ActiveTransition { get; private set; }

        public bool IsTransitioning { get; private set; }

        /// <summary>0..1 through the active transition. This is the authoritative combat clock.</summary>
        public float Progress01 { get; private set; }

        public float Duration => _duration;

        public float Elapsed => _elapsed;

        public PassPhase Phase { get; private set; }

        /// <summary>Initiative (the doc's Vor). Set by whoever owns the tempo, usually the AI brain.</summary>
        public bool Vor { get; set; }

        /// <summary>How long the current guard has been held. Drives "am I still allowed to parry?".</summary>
        public float SecondsInGuard { get; private set; }

        public float ParryWindowRemaining { get; private set; }

        public ReactionState Reaction => _reaction;

        public bool IsAlive => _reaction != ReactionState.Dead;

        /// <summary>Point reach of the current guard, used for measure classification.</summary>
        public float TipReach { get; private set; }

        public bool IsCommitted => IsTransitioning && ActiveTransition.IsCommitted(Progress01);

        public bool IsInDamageWindow => IsTransitioning && ActiveTransition.IsInDamageWindow(Progress01);

        /// <summary>
        /// A defender can parry either because it just arrived in a guard, or because its own transition
        /// is inside its parry window - meeting an attack with an attack is a real fencing answer.
        /// </summary>
        public bool IsInParryWindow
        {
            get
            {
                if (IsTransitioning)
                    return ActiveTransition.IsInParryWindow(Progress01);

                return ParryWindowRemaining > 0f;
            }
        }

        private float FeintDurationSeconds => 0.2f;

        public event Action<PassPhase> PhaseChanged;
        public event Action<GuardId> GuardChanged;
        public event Action<GuardTransitionState> TransitionStarted;
        public event Action<GuardTransitionState> TransitionCompleted;
        public event Action<GuardTransitionState, string> TransitionFailed;
        public event Action<ReactionState> ReactionChanged;

        /// <summary>Bands a distance using the current guard's reach.</summary>
        public Measure ClassifyMeasure(float distance)
        {
            return _table.GuardOr(Current).Classify(distance);
        }

        /// <summary>
        /// Asks to move to another guard on the side it is currently held on. Convenience overload; the
        /// side-aware form below is the real entry point.
        /// </summary>
        public RequestResult RequestGuard(GuardId target, float distanceToOpponent)
        {
            return RequestGuard(target, CurrentSide, distanceToOpponent);
        }

        /// <summary>
        /// Asks to move to another guard, on a given side. This is the single entry point for both player
        /// intent and AI intent, so the two can never disagree about what is legal.
        ///
        /// A single-slot queue holds one request made mid-transition. Mashing three guards therefore
        /// keeps only the last one - deliberate, and documented, rather than accidental.
        /// </summary>
        public RequestResult RequestGuard(GuardId target, GuardSide side, float distanceToOpponent)
        {
            _lastKnownDistance = distanceToOpponent;
            _lastKnownMeasure = ClassifyMeasure(distanceToOpponent);

            if (target == GuardId.None || !IsAlive)
                return RequestResult.Rejected;

            if (_reaction != ReactionState.None)
                return RequestResult.Rejected;

            if (IsTransitioning)
            {
                // Cancelling before commitment is allowed; after it, the pass must finish.
                if (IsCommitted)
                {
                    _queuedGuard = target;
                    _queuedGuardSide = side;
                    return RequestResult.Queued;
                }

                CancelTransition("Superseded before commitment.");
            }

            // A request for the guard and side already held is a no-op, not a self-transition: the
            // self-transition is reached through the table, and its ToSide decides whether it is a
            // same-side re-attack or a side switch.
            // The table is descriptive rather than a permission list (doc 3.1), so an unauthored pair
            // still resolves - to a non-damaging traversal. Only a genuine no-op or a side the pairing
            // was never authored for is refused.
            if (!_table.TryResolve(CurrentKey, new GuardKey(target, side), out var profile) || profile == null)
                return RequestResult.Rejected;

            var result = RequestResult.Accepted;
            if (profile.EnforceDistanceGate)
            {
                if (distanceToOpponent < profile.MinDistance)
                    return RequestResult.Rejected;

                if (distanceToOpponent > profile.MaxDistance)
                {
                    if (!profile.FeintWhenOutOfRange)
                        return RequestResult.Rejected;

                    result = RequestResult.Feinted;
                }
            }

            BeginTransition(profile, result == RequestResult.Feinted);
            return result;
        }

        /// <summary>
        /// Advances the clock. Reactions freeze it entirely, which is what turns a parry or a guard
        /// break into a genuine opening instead of a cosmetic flourish.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            if (_reaction != ReactionState.None)
            {
                if (_reaction == ReactionState.Dead)
                    return;

                _reactionRemaining -= deltaTime;
                if (_reactionRemaining <= 0f)
                {
                    _reaction = ReactionState.None;
                    ReactionChanged?.Invoke(ReactionState.None);
                }

                return;
            }

            SecondsInGuard += deltaTime;
            if (ParryWindowRemaining > 0f)
                ParryWindowRemaining = FencingMath.Max(0f, ParryWindowRemaining - deltaTime);

            if (!IsTransitioning)
                return;

            _elapsed += deltaTime;

            if (_feintRemaining >= 0f)
            {
                _feintRemaining -= deltaTime;
                if (_feintRemaining <= 0f)
                {
                    CancelTransition("Feint: out of measure.");
                    return;
                }
            }

            Progress01 = _duration > 0f ? FencingMath.Clamp01(_elapsed / _duration) : 1f;
            UpdatePhase();

            if (Progress01 >= 1f)
                CompleteTransition();
        }

        /// <summary>Aborts a transition and returns to the guard it started from.</summary>
        public void CancelTransition(string reason)
        {
            if (!IsTransitioning)
                return;

            var aborted = ActiveTransition;
            IsTransitioning = false;
            ActiveTransition = default;
            Progress01 = 0f;
            _elapsed = 0f;
            _duration = 0f;
            _feintRemaining = -1f;
            SetPhase(PassPhase.None);
            TransitionFailed?.Invoke(aborted, reason);
        }

        /// <summary>
        /// Sets the guard without playing a transition. Recovery path for a failed presentation, and for
        /// spawning a combatant directly into a pose.
        /// </summary>
        /// <summary>Sets the guard directly on its default (right) side, without playing a transition.</summary>
        public void ForceGuard(GuardId guard)
        {
            ForceGuard(new GuardKey(guard, GuardSide.Right));
        }

        /// <summary>Sets the guard and the side it is held on, without playing a transition.</summary>
        public void ForceGuard(GuardKey guard)
        {
            if (!guard.IsValid)
                return;

            IsTransitioning = false;
            ActiveTransition = default;
            Progress01 = 0f;
            _elapsed = 0f;
            _duration = 0f;
            _feintRemaining = -1f;
            _queuedGuard = GuardId.None;
            Previous = Current;
            PreviousSide = CurrentSide;
            Current = guard.Id;
            CurrentSide = guard.Side;
            SecondsInGuard = 0f;
            ParryWindowRemaining = _table.GuardOr(guard.Id).ParryGraceSeconds;
            TipReach = _table.GuardOr(guard.Id).TipReach;
            SetPhase(PassPhase.None);
            GuardChanged?.Invoke(Current);
        }

        /// <summary>
        /// Brings a dead combatant back, optionally posed in <paramref name="guard"/>.
        ///
        /// This is deliberately its own entry point rather than a special case of <see cref="SetReaction"/>:
        /// that method refuses to leave <see cref="ReactionState.Dead"/>, because "death never expires" is
        /// the rule that makes a kill mean something. Coming back is a different decision, made by whoever
        /// owns respawning.
        /// </summary>
        public void Revive(GuardId guard)
        {
            if (_reaction != ReactionState.Dead)
                return;

            var restored = guard != GuardId.None ? guard : (Current != GuardId.None ? Current : Previous);

            IsTransitioning = false;
            ActiveTransition = default;
            Progress01 = 0f;
            _elapsed = 0f;
            _duration = 0f;
            _feintRemaining = -1f;
            _queuedGuard = GuardId.None;
            _reaction = ReactionState.None;
            _reactionRemaining = 0f;

            Previous = restored;
            PreviousSide = CurrentSide;
            Current = restored;

            // Reviving restores the guard the fighter was holding, and that includes the side it was held
            // on: forcing the right side here would silently mirror a left-guard fighter back on respawn.
            SecondsInGuard = 0f;

            var guardProfile = _table.GuardOr(restored);
            ParryWindowRemaining = guardProfile.ParryGraceSeconds;
            TipReach = guardProfile.TipReach;

            SetPhase(PassPhase.None);
            ReactionChanged?.Invoke(ReactionState.None);
            GuardChanged?.Invoke(Current);
        }

        /// <summary>
        /// Puts the combatant into a non-fencing state. <see cref="ReactionState.Dead"/> never expires.
        /// </summary>
        public void SetReaction(ReactionState state, float seconds = 0f)
        {
            if (_reaction == ReactionState.Dead)
                return;

            _reaction = state;
            _reactionRemaining = state == ReactionState.Dead
                ? float.PositiveInfinity
                : FencingMath.Max(0f, seconds);
            ReactionChanged?.Invoke(state);
        }

        /// <summary>Convenience for the common "the blade landed, flinch for a moment" case.</summary>
        public void ApplyHitReaction(float flinchSeconds = 0.25f)
        {
            SetReaction(ReactionState.Hit, flinchSeconds);
        }

        /// <summary>
        /// Restarts the arrival grace of the current guard. Used when a defence actually worked, so a
        /// successful parry leaves the defender briefly safe again rather than immediately exposed.
        /// </summary>
        public void RefreshParryWindow(Measure measure)
        {
            SecondsInGuard = 0f;
            ParryWindowRemaining = _table.GuardOr(Current).ResolveParryGrace(measure);
        }

        private void BeginTransition(TransitionProfile profile, bool isFeint)
        {
            ActiveTransition = new GuardTransitionState(profile);
            IsTransitioning = true;
            _elapsed = 0f;
            _duration = profile.ResolveDuration(_lastKnownMeasure, Vor);
            Progress01 = 0f;
            _feintRemaining = isFeint ? profile.FeintDurationSeconds : -1f;
            SetPhase(PassPhase.ToPass);
            TransitionStarted?.Invoke(ActiveTransition);
        }

        private void CompleteTransition()
        {
            var finished = ActiveTransition;

            Previous = Current;
            PreviousSide = CurrentSide;
            Current = finished.To;
            CurrentSide = finished.Profile != null ? finished.Profile.ToSide : CurrentSide;
            IsTransitioning = false;
            ActiveTransition = default;
            Progress01 = 0f;
            _elapsed = 0f;
            _duration = 0f;
            _feintRemaining = -1f;
            SecondsInGuard = 0f;

            var guardProfile = _table.GuardOr(Current);
            ParryWindowRemaining = guardProfile.ResolveParryGrace(_lastKnownMeasure);
            TipReach = guardProfile.TipReach;
            SetPhase(PassPhase.None);

            GuardChanged?.Invoke(Current);
            TransitionCompleted?.Invoke(finished);

            var queued = _queuedGuard;
            var queuedSide = _queuedGuardSide;
            _queuedGuard = GuardId.None;
            if (queued != GuardId.None)
                RequestGuard(queued, queuedSide, _lastKnownDistance);
        }

        /// <summary>
        /// Phases come from the authored damage window, so "the blade is live" always means the same
        /// thing to hit detection, to IK and to an opponent reading the attack.
        /// </summary>
        private void UpdatePhase()
        {
            var profile = ActiveTransition.Profile;
            if (profile == null)
            {
                SetPhase(PassPhase.None);
                return;
            }

            PassPhase next;
            if (!profile.HasDamageWindow || Progress01 < profile.DamageWindowStart)
                next = PassPhase.ToPass;
            else if (Progress01 < profile.DamageWindowEnd)
                next = PassPhase.Strike;
            else
                next = PassPhase.Settle;

            SetPhase(next);
        }

        private void SetPhase(PassPhase phase)
        {
            if (Phase == phase)
                return;

            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }
    }
}

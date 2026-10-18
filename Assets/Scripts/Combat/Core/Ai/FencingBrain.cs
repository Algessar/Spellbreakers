using System;
using System.Collections.Generic;
using Spellbreakers.Combat.Ai.Considerations;

namespace Spellbreakers.Combat.Ai
{
    /// <summary>
    /// Utility-AI decision maker for a fencing duel.
    ///
    /// Every candidate action - wait, step, retreat, or a specific guard change - is scored on the same
    /// axes, and the best one wins. Two properties matter more than the arithmetic:
    ///
    /// 1. The enemy's choice is explainable. The breakdown is kept, so "why did it just stand there?"
    ///    has an answer in the debug overlay rather than requiring a guess.
    /// 2. The enemy is beatable by timing, not by numbers. Reaction lag is a real delay before it
    ///    responds to a change, and the decisions themselves are pure fences rules, so a player who
    ///    reads and times the enemy wins without needing better stats.
    ///
    /// The brain is plain C# with no engine dependency: a whole duel can be replayed in a unit test
    /// from manufactured snapshots.
    /// </summary>
    public sealed class FencingBrain
    {
        private readonly TransitionTable _table;
        private readonly ParryTable _parryTable;
        private readonly FencingArchetype _archetype;
        private readonly List<Consideration> _considerations = new List<Consideration>();
        private readonly List<FencingAction> _actions = new List<FencingAction>();
        private readonly List<ScoredAction> _scores = new List<ScoredAction>();
        private readonly List<TransitionProfile> _transitionBuffer = new List<TransitionProfile>();

        private DeterministicRandom _random;
        private float _decisionTimer;
        private float _reactUntil;
        private bool _hasObservedOpponent;
        private GuardTransitionState _lastObservedOpponentTransition;
        private GuardId _lastRequestedGuard = GuardId.None;
        private ScoredAction _choice;

        public FencingBrain(TransitionTable table, ParryTable parryTable, FencingArchetype archetype)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _parryTable = parryTable ?? throw new ArgumentNullException(nameof(parryTable));
            _archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            _random = new DeterministicRandom(archetype.RandomSeed);

            _considerations.Add(new MeasureFitConsideration());
            _considerations.Add(new GuardMatchupConsideration());
            _considerations.Add(new OpeningReadConsideration());
            _considerations.Add(new InitiativeConsideration());
            _considerations.Add(new CommitRiskConsideration());
            _considerations.Add(new SpacingConsideration());
            _considerations.Add(new PredictabilityConsideration());
            _considerations.Add(new EvasionConsideration());
            _considerations.Add(new BaselineConsideration());
        }

        /// <summary>The current best action. Never null after the first decision.</summary>
        public ScoredAction Choice => _choice;

        /// <summary>Every candidate considered in the most recent decision, best first is not guaranteed.</summary>
        public IReadOnlyList<ScoredAction> Scores => _scores;

        public IReadOnlyList<Consideration> Considerations => _considerations;

        public FencingArchetype Archetype => _archetype;

        /// <summary>The last guard this brain asked for, used to avoid repeating itself.</summary>
        public GuardId LastRequestedGuard => _lastRequestedGuard;

        /// <summary>True while the reaction delay is suppressing decisions.</summary>
        public bool IsReacting(float elapsedSeconds) => elapsedSeconds < _reactUntil;

        /// <summary>
        /// Advances the decision clock. Returns true when a new choice was made this frame, which the
        /// debug layer uses to log only real decisions rather than every tick.
        /// </summary>
        public bool Tick(float deltaTime, in FencingContext context)
        {
            ObserveOpponent(context);

            _decisionTimer -= deltaTime;
            if (deltaTime < 0f)
                _decisionTimer = 0f;

            if (context.ElapsedSeconds < _reactUntil || _decisionTimer > 0f)
                return false;

            _decisionTimer = FencingMath.Max(0.02f, _archetype.DecisionInterval);
            Decide(context);
            return true;
        }

        /// <summary>Forces an immediate re-decision, for example after being parried.</summary>
        public void InvalidateDecision()
        {
            _decisionTimer = 0f;
            _reactUntil = 0f;
        }

        private void ObserveOpponent(in FencingContext context)
        {
            var observed = context.Opponent.Transition;

            if (!_hasObservedOpponent || !ReferenceEquals(observed.Profile, _lastObservedOpponentTransition.Profile))
            {
                _hasObservedOpponent = true;
                _lastObservedOpponentTransition = observed;

                // A new action by the opponent costs this combatant its reaction time before it can answer.
                _reactUntil = context.ElapsedSeconds + _archetype.ReactionSeconds;
            }
        }

        private void Decide(in FencingContext context)
        {
            BuildActions(context);

            _scores.Clear();
            ScoredAction best = null;

            for (var i = 0; i < _actions.Count; i++)
            {
                var action = _actions[i];
                var actionContext = context.WithAction(action, _random.Next01());
                var scored = ScoreAction(actionContext);

                _scores.Add(scored);

                if (best == null || scored.Score > best.Score)
                    best = scored;
            }

            _choice = best;

            if (best != null && best.Action.Kind == FencingActionKind.ChangeGuard)
                _lastRequestedGuard = best.Action.TargetGuard;
        }

        /// <summary>
        /// The candidate list: hold, close, open, or commit to any transition the archetype is willing
        /// to use. Bounded by the transition table, which is what keeps the action space small enough
        /// for the enemy's behaviour to stay readable.
        /// </summary>
        private void BuildActions(in FencingContext context)
        {
            _actions.Clear();
            _actions.Add(FencingAction.Wait);

            if (context.Opponent.HasOpponent)
            {
                var step = FencingMath.Max(0.1f, _archetype.StepDistance);
                _actions.Add(FencingAction.Step(step));
                _actions.Add(FencingAction.Retreat(step));

                // Avoidance is only an option for a combatant that can actually leave the line.
                if (context.CanAvoid)
                    _actions.Add(FencingAction.Evade(FencingMath.Max(0.1f, _archetype.EvadeDistance)));
            }

            if (context.SelfInTransition)
            {
                // Mid-transition the only meaningful intents are movement; the guard is already chosen.
                return;
            }

            _table.CollectFrom(context.SelfGuard, _transitionBuffer);
            for (var i = 0; i < _transitionBuffer.Count; i++)
            {
                var profile = _transitionBuffer[i];
                if (!_archetype.IsGuardAllowed(profile.To))
                    continue;

                _actions.Add(FencingAction.ChangeGuard(profile.To));
            }
        }

        private ScoredAction ScoreAction(in FencingContext context)
        {
            var breakdown = new ConsiderationScore[_considerations.Count];
            var totalWeight = 0f;
            var weighted = 0f;

            for (var i = 0; i < _considerations.Count; i++)
            {
                var consideration = _considerations[i];
                var weight = FencingMath.Max(0f, consideration.WeightIn(_archetype));
                var score = consideration.Score(context);

                breakdown[i] = new ConsiderationScore(consideration.Name, weight, score);
                weighted += weight * score;
                totalWeight += weight;
            }

            var normalised = totalWeight > 0.0001f ? weighted / totalWeight : 0f;
            normalised += (_archetype.Aggression - 0.5f) * 0.2f;
            normalised += (_archetype.Jitter > 0f ? (_random.Next01() - 0.5f) * _archetype.Jitter : 0f);

            return new ScoredAction(context.Action, FencingMath.Clamp01(normalised), breakdown);
        }
    }
}

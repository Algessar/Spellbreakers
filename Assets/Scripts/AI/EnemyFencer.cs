using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace Spellbreakers.Ai
{
    /// <summary>
    /// Drives one enemy: it feeds perception into the decision layer and turns the chosen action into a
    /// guard request or a change of footwork.
    ///
    /// The point of the split is that the enemy fights through exactly the same entry points as the
    /// player. Its attacks are guard requests on a <see cref="CombatHandler"/>, so they go through the
    /// same transition legality, the same measure gates, the same damage windows and the same parry
    /// table. There is no separate enemy combat implementation that could drift out of step.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyFencer : MonoBehaviour
    {
        [Tooltip("Fighting personality. Falls back to a default fencer when left empty.")]
        [SerializeField] private FencingArchetypeAsset _archetype;

        [SerializeField] private CombatHandler _combat;
        [SerializeField] private FencingPerception _perception;
        [SerializeField] private EnemyLocomotion _locomotion;
        [SerializeField] private Damageable _damageable;

        [Tooltip("Applies Zufechten/Krieg distance rules to this enemy's own transitions.")]
        [SerializeField] private bool _enforceMeasureGate = true;

        [Tooltip("Temporary: logs each decision with its full score breakdown.")]
        [SerializeField] private bool _logDecisions;

        private TransitionTable _table;
        private float _elapsedSeconds;
        private GUIStyle _debugStyle;

        /// <summary>The decision maker, exposed for the debug overlay.</summary>
        public FencingBrain Brain { get; private set; }

        public FencingArchetype Archetype { get; private set; }

        public FencingAgent Agent => _combat != null ? _combat.Agent : null;

        public FencingPerception Perception => _perception;

        public EnemyLocomotion Locomotion => _locomotion;

        /// <summary>True once the enemy has closed to a distance worth duelling at.</summary>
        public bool InDuel { get; private set; }

        public OpponentSnapshot Snapshot { get; private set; }

        private void Awake()
        {
            if (_combat == null)
                _combat = GetComponent<CombatHandler>() ?? transform.root.GetComponentInChildren<CombatHandler>(true);

            if (_perception == null)
                _perception = GetComponent<FencingPerception>() ?? GetComponentInChildren<FencingPerception>(true);

            if (_locomotion == null)
                _locomotion = GetComponent<EnemyLocomotion>() ?? GetComponentInChildren<EnemyLocomotion>(true);

            if (_damageable == null)
                _damageable = GetComponent<Damageable>() ?? transform.root.GetComponentInChildren<Damageable>(true);
        }

        /// <summary>
        /// Wiring happens in Start so the <see cref="CombatHandler"/> has already built its agent.
        /// Component start order is not something to build a combat system on.
        /// </summary>
        private void Start()
        {
            if (_combat == null || _combat.Agent == null)
            {
                Debug.LogError("EnemyFencer needs a CombatHandler with a live agent on this character.", this);
                enabled = false;
                return;
            }

            Archetype = _archetype != null ? _archetype.Build() : FencingArchetype.CreateFencer();
            _table = _combat.Agent.Table;

            if (_enforceMeasureGate)
                _table.SetDistanceGateForAll(true);

            Brain = new FencingBrain(_table, CombatRules.Parry, Archetype);

            if (_locomotion != null)
                _locomotion.SetDesiredBand(Archetype.PreferredMinDistance, Archetype.PreferredMaxDistance);

            if (_damageable != null)
                _damageable.Died += OnDied;

            _combat.ContactResolved += OnContactResolved;
        }

        private void OnDestroy()
        {
            if (_damageable != null)
                _damageable.Died -= OnDied;

            if (_combat != null)
                _combat.ContactResolved -= OnContactResolved;
        }

        private void OnDisable()
        {
            _locomotion?.Stop();
        }

        private void Update()
        {
            if (Brain == null || Agent == null || !Agent.IsAlive)
                return;

            var deltaTime = Time.deltaTime;
            _elapsedSeconds += deltaTime;

            _perception.Tick(deltaTime);

            Snapshot = _perception.BuildSnapshot();

            var target = _perception.Target;
            var opponentTransform = target != null ? target.transform : null;

            // With nobody to fight there is nothing to decide. Without this the brain would happily
            // cycle guards at an empty courtyard because a transition is always "legal".
            if (!Snapshot.HasOpponent)
            {
                InDuel = false;
                _locomotion.Tick(deltaTime, null, false);
                return;
            }

            InDuel = Snapshot.Measure == Measure.Krieg || Snapshot.Measure == Measure.Zufechten;

            // Keep the measure gate honest for transitions that opt into it.
            _combat.SetOpponentDistance(Snapshot.Distance);

            Agent.Vor = InDuel && !Agent.IsTransitioning && !Snapshot.BladeLive && !Snapshot.InParryWindow;

            if (Brain.Tick(deltaTime, BuildContext(Snapshot)))
            {
                Execute(Brain.Choice);

                if (_logDecisions && Brain.Choice != null)
                    DebugLog.Info(true, $"[AI] {name} {Brain.Choice.Explain()}", this);
            }

            _locomotion.Tick(deltaTime, opponentTransform, InDuel);
        }

        private FencingContext BuildContext(in OpponentSnapshot snapshot)
        {
            var healthFraction = _damageable != null && _damageable.MaxHealth > 0f
                ? _damageable.CurrentHealth / _damageable.MaxHealth
                : 1f;

            return new FencingContext(
                Agent.Current,
                Agent.ActiveTransition,
                Agent.Progress01,
                healthFraction,
                Agent.Reaction,
                Agent.Vor,
                Agent.TipReach,
                _elapsedSeconds,
                snapshot,
                FencingAction.Wait,
                _table,
                CombatRules.Parry,
                Archetype,
                Brain.LastRequestedGuard,
                0f);
        }

        /// <summary>
        /// Turns a decision into intent. The brain never touches the Animator or the transform itself -
        /// it produces fences decisions, and the movement and combat layers realise them.
        /// </summary>
        private void Execute(ScoredAction choice)
        {
            if (choice == null)
                return;

            var action = choice.Action;

            switch (action.Kind)
            {
                case FencingActionKind.ChangeGuard:
                    _combat.RequestGuard(action.TargetGuard);
                    break;

                case FencingActionKind.Step:
                case FencingActionKind.Retreat:
                    // Positive StepDistance closes, so the desired distance moves the opposite way.
                    _locomotion?.AdjustDesiredDistance(-action.StepDistance);
                    break;

                case FencingActionKind.Wait:
                default:
                    break;
            }
        }

        private void OnContactResolved(CombatContact contact)
        {
            // A contact changes the situation, so the next decision must not be based on stale reasoning.
            Brain?.InvalidateDecision();
        }

        private void OnDied(Damageable damageable)
        {
            // The brain is kept rather than discarded: tearing it down here left a respawned enemy
            // permanently inert, because nothing rebuilds it. The agent's Dead reaction is what stops
            // it acting, and that is now something a respawn can clear.
            Agent?.SetReaction(ReactionState.Dead);
            _locomotion?.Stop();
        }

        /// <summary>
        /// A compact on-screen readout of what this enemy is thinking. Small, but it turns "why is it
        /// doing that?" from a guess into a glance.
        /// </summary>
        private void OnGUI()
        {
            if (!_logDecisions || Brain == null || Agent == null)
                return;

            if (_debugStyle == null && Event.current != null)
            {
                _debugStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            }

            var screen = Camera.main != null ? Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.1f) : Vector3.zero;
            if (screen.z < 0f)
                return;

            var y = Screen.height - screen.y;
            var lines = new[]
            {
                $"{name}: {GuardIds.ToDisplayName(Agent.Current)} {Agent.Phase} {(Agent.IsTransitioning ? Agent.Progress01.ToString("0.00") : string.Empty)}",
                $"measure {Snapshot.Measure} d={Snapshot.Distance:0.00} band={Locomotion?.DesiredDistance:0.00}",
                Brain.IsReacting(_elapsedSeconds) ? "reacting..." : (Brain.Choice != null ? Brain.Choice.Explain() : "no choice"),
            };

            for (var i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(screen.x - 120f, y + i * 13f, 420f, 14f), lines[i], _debugStyle);
        }
    }
}

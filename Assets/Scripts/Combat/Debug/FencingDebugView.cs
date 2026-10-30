using System.Collections.Generic;
using Spellbreakers.Ai;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Draws what the combat system is actually thinking.
    ///
    /// Enemy AI is impossible to tune from the outside: "it just stood there" has a dozen possible causes
    /// and no visible symptom. This surfaces the whole chain in one place - the guard each fighter is in,
    /// how far through a transition they are, when a blade is live, when a guard can still parry, the
    /// distance band each fighter wants, and the score breakdown behind every enemy decision.
    ///
    /// Gizmos cover the spatial facts and the on-screen panel covers the state. Toggle with F1.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FencingDebugView : MonoBehaviour
    {
        public static FencingDebugView Instance { get; private set; }

        [Tooltip("Key that shows and hides the readout.")]
        [SerializeField] private Key _toggleKey = Key.F1;

        [Tooltip("Draw measure rings and blades for every combatant, not just the player's target.")]
        [SerializeField] private bool _showAllCombatants = true;

        [Tooltip("Show the per-consideration breakdown for each enemy's last decision.")]
        [SerializeField] private bool _showDecisionBreakdown = true;

        private readonly List<Damageable> _combatants = new List<Damageable>();

        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private bool _visible = true;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (_toggleKey != Key.None && keyboard[_toggleKey].wasPressedThisFrame)
                _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            EnsureStyles();

            var origin = transform.position;
            var playerCombat = GetComponent<CombatHandler>();
            var playerAgent = playerCombat != null ? playerCombat.Agent : null;

            var y = 10f;
            GUI.Label(new Rect(10f, y, 700f, 18f), "SPELLBREAKERS COMBAT  [F1 to hide]", _headerStyle);
            y += 20f;

            if (playerAgent != null)
            {
                var playerLines = new[]
                {
                    $"player   guard {GuardIds.ToDisplayName(playerAgent.CurrentKey)}  phase {playerAgent.Phase}" +
                    (playerAgent.IsTransitioning ? $"  {playerAgent.Progress01:0.00}" : string.Empty) +
                    $"  reaction {playerAgent.Reaction}  vor {playerAgent.Vor}",
                    $"         blade live {(playerAgent.IsInDamageWindow ? "YES" : "no")}   " +
                    $"can parry {(playerAgent.IsInParryWindow ? $"YES ({playerAgent.ParryWindowRemaining:0.00}s)" : "no")}   " +
                    $"reach {playerAgent.TipReach:0.00}m",
                };

                for (var i = 0; i < playerLines.Length; i++)
                {
                    GUI.Label(new Rect(10f, y, 900f, 18f), playerLines[i], _panelStyle);
                    y += 15f;
                }
            }

            y += 6f;
            CollectCombatants();

            for (var i = 0; i < _combatants.Count; i++)
            {
                var combatant = _combatants[i];
                if (combatant == null)
                    continue;

                var combat = CombatantRegistry.FindOwnerComponent<CombatHandler>(combatant);
                if (combat == null || combat.Agent == null)
                    continue;

                var agent = combat.Agent;
                var distance = playerAgent != null
                    ? Vector3.Distance(transform.position, combatant.AimPosition)
                    : 0f;

                var measure = playerAgent != null ? playerAgent.ClassifyMeasure(distance) : Measure.OutOfRange;

                GUI.Label(
                    new Rect(10f, y, 900f, 18f),
                    $"{combatant.name,-18} {combatant.Team,-7} {GuardIds.ToDisplayName(agent.CurrentKey),-16} " +
                    $"{agent.Phase,-6} {(agent.IsTransitioning ? agent.Progress01.ToString("0.00") : "  - ")} " +
                    $"d={distance:0.00} {measure} hp={combatant.CurrentHealth:0}/{combatant.MaxHealth:0}",
                    _panelStyle);
                y += 15f;

                var enemy = combatant.GetComponent<EnemyFencer>() ?? combatant.GetComponentInChildren<EnemyFencer>();
                if (!_showDecisionBreakdown || enemy == null || enemy.Brain == null)
                    continue;

                var brain = enemy.Brain;
                var choice = brain.Choice;

                GUI.Label(
                    new Rect(28f, y, 900f, 18f),
                    $"  wants {enemy.Locomotion?.DesiredDistance ?? 0f:0.00}m  " +
                    $"{(brain.IsReacting(Time.timeSinceLevelLoad) ? "REACTING" : choice != null ? choice.Explain() : "no decision yet")}",
                    _panelStyle);
                y += 15f;

                if (choice == null || choice.Breakdown == null)
                    continue;

                for (var b = 0; b < choice.Breakdown.Length; b++)
                {
                    var entry = choice.Breakdown[b];
                    GUI.Label(
                        new Rect(46f, y, 900f, 18f),
                        $"    {entry.Name,-14} {entry.Score:0.000}  x{entry.Weight:0.00}  = {entry.Contribution:0.000}",
                        _panelStyle);
                    y += 13f;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!_visible || !Application.isPlaying)
                return;

            CollectCombatants();

            for (var i = 0; i < _combatants.Count; i++)
            {
                var combatant = _combatants[i];
                if (combatant == null)
                    continue;

                if (!_showAllCombatants && combatant.transform.root != transform.root)
                    continue;

                var combat = CombatantRegistry.FindOwnerComponent<CombatHandler>(combatant);
                var agent = combat != null ? combat.Agent : null;
                if (agent == null)
                    continue;

                var centre = combatant.AimPosition;

                // Measure bands, read from the same guard profile the rules use.
                var guardProfile = agent.Table.GuardOr(agent.Current);
                Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
                DrawRing(centre, guardProfile.TipReach);

                Gizmos.color = new Color(0.9f, 0.8f, 0.2f, 0.25f);
                DrawRing(centre, guardProfile.TipReach * 2f);

                // The blade, so a live window and a visible sweep can be correlated by eye.
                var blade = combatant.GetComponentInChildren<Blade>(true);
                if (blade != null && blade.IsValid)
                {
                    Gizmos.color = agent.IsInDamageWindow
                        ? new Color(1f, 0.2f, 0.1f, 1f)
                        : new Color(0.8f, 0.8f, 0.8f, 0.6f);
                    Gizmos.DrawLine(blade.BasePosition, blade.TipPosition);
                    Gizmos.DrawWireSphere(blade.TipPosition, blade.Radius);
                }
            }
        }

        private void CollectCombatants()
        {
            _combatants.Clear();
            foreach (var candidate in Damageable.All)
            {
                if (candidate != null && candidate.IsAlive)
                    _combatants.Add(candidate);
            }
        }

        private static void DrawRing(Vector3 centre, float radius)
        {
            const int segments = 48;
            var previous = centre + new Vector3(radius, 0f, 0f);

            for (var i = 1; i <= segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                var point = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false };
            _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        }
    }
}

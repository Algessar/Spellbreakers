using System.Collections.Generic;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;
using UnityEngine;

namespace Spellbreakers.Ai
{
    /// <summary>
    /// Turns the world into the opponent snapshot the decision layer reasons about.
    ///
    /// This is the only place an enemy reads the world, and it deliberately reports the same facts the
    /// player can see: which guard the opponent is in, whether a transition is running and how far
    /// through it is, whether the opponent's blade is live, and whether the opponent could still parry.
    /// An enemy therefore cannot cheat with information a player would not have.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FencingPerception : MonoBehaviour
    {
        [Header("Range")]
        [SerializeField, Min(1f)] private float _sightRange = 25f;

        [Tooltip("Total cone, in degrees, in which a target can be acquired.")]
        [SerializeField, Range(10f, 360f)] private float _sightAngle = 240f;

        [Tooltip("Seconds the enemy keeps chasing after losing sight of its target.")]
        [SerializeField, Min(0f)] private float _loseTargetGrace = 3f;

        [SerializeField, Min(0.02f)] private float _refreshInterval = 0.2f;

        [Header("Line of sight")]
        [SerializeField] private bool _requireLineOfSight;

        [SerializeField] private LayerMask _obstructionMask = ~0;

        [SerializeField, Min(0.1f)] private float _eyeHeight = 1.6f;

        private readonly List<Damageable> _buffer = new List<Damageable>();

        private Damageable _self;
        private CombatHandler _selfCombat;
        private Transform _selfTransform;
        private float _nextRefreshTime;
        private float _lastSeenTime = float.NegativeInfinity;

        /// <summary>The opponent this enemy is currently engaging, or null.</summary>
        public Damageable Target { get; private set; }

        /// <summary>The target's combat handler, cached because perception needs it every snapshot.</summary>
        public CombatHandler TargetCombat { get; private set; }

        public bool HasTarget => Target != null && Target.IsAlive;

        public Vector3 LastKnownPosition { get; private set; }

        public float TimeSinceTargetSeen => Time.time - _lastSeenTime;

        private void Awake()
        {
            _selfTransform = transform;
            _self = GetComponent<Damageable>() ?? transform.root.GetComponentInChildren<Damageable>(true);
            _selfCombat = GetComponent<CombatHandler>() ?? transform.root.GetComponentInChildren<CombatHandler>(true);
        }

        /// <summary>Ticked by the owning brain so perception and decision share one frame order.</summary>
        public void Tick(float deltaTime)
        {
            if (Time.time < _nextRefreshTime && HasTarget && IsTargetStillValid())
                return;

            _nextRefreshTime = Time.time + _refreshInterval;
            AcquireOrRefresh();
        }

        public void ForgetTarget()
        {
            Target = null;
            TargetCombat = null;
        }

        private bool IsTargetStillValid()
        {
            if (Target == null || !Target.IsAlive)
                return false;

            var distance = Vector3.Distance(Origin, Target.AimPosition);
            if (distance > _sightRange)
                return false;

            return !_requireLineOfSight || HasLineOfSight(Target);
        }

        private void AcquireOrRefresh()
        {
            var origin = Origin;
            var selfTeam = _self != null ? _self.Team : CombatTeam.Hostile;

            var candidate = CombatantRegistry.FindBestHostile(
                origin,
                _selfTransform.forward,
                selfTeam,
                _sightRange,
                _sightAngle,
                _buffer);

            if (candidate != null && _requireLineOfSight && !HasLineOfSight(candidate))
                candidate = null;

            if (candidate != null)
            {
                Target = candidate;
                TargetCombat = CombatantRegistry.FindOwnerComponent<CombatHandler>(candidate);
                LastKnownPosition = candidate.AimPosition;
                _lastSeenTime = Time.time;
                return;
            }

            // Keep the last known position for a grace period so losing sight is not instant amnesia.
            if (_lastSeenTime > float.NegativeInfinity && TimeSinceTargetSeen <= _loseTargetGrace)
                return;

            Target = null;
            TargetCombat = null;
        }

        /// <summary>
        /// The opponent as the brain sees it. Everything here is information the player also has: guard,
        /// transition progress, whether the blade is live, and whether the guard can still parry.
        /// </summary>
        public OpponentSnapshot BuildSnapshot()
        {
            var ownAgent = _selfCombat != null ? _selfCombat.Agent : null;

            if (!HasTarget)
            {
                var distanceToLastKnown = ownAgent != null
                    ? Vector3.Distance(Origin, LastKnownPosition)
                    : float.PositiveInfinity;

                return new OpponentSnapshot(
                    GuardId.None,
                    default,
                    0f,
                    false,
                    false,
                    distanceToLastKnown,
                    Measure.OutOfRange,
                    1f,
                    false,
                    0f);
            }

            var agent = TargetCombat != null ? TargetCombat.Agent : null;
            var distance = Vector3.Distance(Origin, Target.AimPosition);
            var measure = ownAgent != null ? ownAgent.ClassifyMeasure(distance) : Measure.OutOfRange;

            var healthFraction = Target.MaxHealth > 0f ? Target.CurrentHealth / Target.MaxHealth : 1f;

            return new OpponentSnapshot(
                agent != null ? agent.Current : GuardId.None,
                agent != null ? agent.ActiveTransition : default,
                agent != null ? agent.Progress01 : 0f,
                agent != null && agent.IsInDamageWindow,
                agent != null && agent.IsInParryWindow,
                distance,
                measure,
                healthFraction,
                Target.IsAlive,
                agent != null ? agent.SecondsInGuard : 0f);
        }

        private Vector3 Origin => _self != null ? _self.AimPosition : _selfTransform.position;

        private bool HasLineOfSight(Damageable candidate)
        {
            var origin = _selfTransform.position + Vector3.up * _eyeHeight;
            var destination = candidate.AimPosition;
            var direction = destination - origin;
            var distance = direction.magnitude;

            if (distance <= 0.01f)
                return true;

            if (!Physics.Raycast(origin, direction / distance, out var hit, distance, _obstructionMask,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            // Whatever we hit first must belong to the candidate, otherwise something is in the way.
            return hit.collider.GetComponentInParent<Damageable>() == candidate;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, _sightRange);

            if (Target != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position + Vector3.up * _eyeHeight, Target.AimPosition);
            }
        }
    }
}

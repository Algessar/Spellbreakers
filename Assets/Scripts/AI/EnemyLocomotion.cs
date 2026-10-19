using Player;
using UnityEngine;
using UnityEngine.AI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace Spellbreakers.Ai
{
    /// <summary>
    /// Enemy footwork: how an enemy walks, closes and holds measure.
    ///
    /// Two modes, chosen by distance. Far away, a NavMesh path handles getting around world geometry -
    /// which is what lets a fight happen in a gateway or a ruin rather than only in an open arena. Close
    /// in, the duel takes over and steering is purely about distance along the line and circling, because
    /// a navmesh tells you how to reach a point but nothing about how to fence from it.
    ///
    /// Steering is expressed as a <see cref="LocomotionIntent"/>, so an enemy moves with exactly the
    /// player's locomotion rules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyLocomotion : MonoBehaviour
    {
        [Header("Speeds")]
        [Tooltip("Speed while holding measure in a duel.")]
        [SerializeField, Min(0.1f)] private float _duelSpeed = 2.2f;

        [Tooltip("Speed while closing from outside measure.")]
        [SerializeField, Min(0.1f)] private float _approachSpeed = 3.4f;

        [Tooltip("Reference speed that maps onto the Animator's run threshold.")]
        [SerializeField, Min(0.1f)] private float _runSpeedReference = 3.4f;

        [Header("Duel steering")]
        [Tooltip("0 moves straight in and out of measure; 1 is pure circling.")]
        [SerializeField, Range(0f, 1f)] private float _circleBias = 0.55f;

        [Tooltip("Seconds between changing the direction of the circle.")]
        [SerializeField, Min(0.5f)] private float _circleFlipSeconds = 2.5f;

        [Tooltip("Distance error inside which the enemy stops adjusting and simply holds.")]
        [SerializeField, Min(0.01f)] private float _distanceDeadband = 0.14f;

        [Tooltip("Distance error that produces full-speed movement.")]
        [SerializeField, Min(0.05f)] private float _distanceErrorForFullSpeed = 0.7f;

        [Header("Navigation")]
        [Tooltip("Beyond this distance the enemy navigates with a NavMesh path instead of duelling.")]
        [SerializeField, Min(1f)] private float _navMeshEngageDistance = 6f;

        [Header("Obstacle avoidance")]
        [SerializeField, Min(0.1f)] private float _probeDistance = 0.9f;
        [SerializeField, Min(0.05f)] private float _probeRadius = 0.35f;
        [SerializeField] private LayerMask _obstacleMask = ~0;
        [SerializeField, Min(0.1f)] private float _probeHeight = 0.9f;

        private Movement _movement;
        private NavMeshAgent _navMeshAgent;
        private NavMeshNavigator _navigator;

        private float _desiredDistance = 1.6f;
        private float _bandMin = 1.1f;
        private float _bandMax = 2.1f;
        private float _circleSign = 1f;
        private float _nextCircleFlipTime;

        [Header("Evade")]
        [SerializeField, Min(0.5f)] private float _evadeSpeed = 5f;

        [Tooltip("Fallback recovery when the caller supplies none.")]
        [SerializeField, Min(0f)] private float _evadeRecovery = 0.35f;

        private float _evadeRemaining;
        private float _recoveryRemaining;
        private float _evadeSign = 1f;
        private Vector3 _evadeDirection;
        private Vector3 _lastLineDirection;

        /// <summary>True while the body is actually slipping across the line.</summary>
        public bool IsEvading => _evadeRemaining > 0f;

        /// <summary>
        /// True while recovering from an evade. This is the cost that keeps avoidance from strictly
        /// dominating parrying: the slip gets you out of the way, the recovery is the opening that follows.
        /// </summary>
        public bool IsRecoveringFromEvade => _recoveryRemaining > 0f;

        /// <summary>Whether the combatant is committed to an evade and cannot act normally.</summary>
        public bool IsCommitted => IsEvading || IsRecoveringFromEvade;

        /// <summary>The distance the enemy is currently trying to hold.</summary>
        public float DesiredDistance => _desiredDistance;

        public bool IsNavigating { get; private set; }

        private void Awake()
        {
            _movement = GetComponent<Movement>() ?? transform.root.GetComponentInChildren<Movement>(true);
            _navMeshAgent = GetComponent<NavMeshAgent>() ?? transform.root.GetComponentInChildren<NavMeshAgent>(true);
            _navigator = new NavMeshNavigator(_navMeshAgent);

            _nextCircleFlipTime = Time.time + _circleFlipSeconds;
        }

        /// <summary>Sets the distance band this enemy wants to fight in, from its archetype.</summary>
        public void SetDesiredBand(float min, float max)
        {
            _bandMin = Mathf.Max(0.3f, Mathf.Min(min, max));
            _bandMax = Mathf.Max(_bandMin, Mathf.Max(min, max));
            _desiredDistance = Mathf.Clamp(_desiredDistance, _bandMin, _bandMax);
        }

        /// <summary>Applies a step or retreat. Positive closes, negative opens.</summary>
        public void AdjustDesiredDistance(float delta)
        {
            _desiredDistance = Mathf.Clamp(_desiredDistance + delta, _bandMin, _bandMax);
        }

        public void Stop()
        {
            _movement?.ClearExternalIntent();
            _navigator?.Reset();
            IsNavigating = false;
        }

        /// <summary>
        /// Commits to a lateral slip across the attack's line: the doc's avoidance, performed.
        ///
        /// The side is chosen by alternating, because which way is "away from the attack" depends on the
        /// incoming line and there is no guarantee the brain knows it. Alternating at least stops the
        /// creature shivering into the same side repeatedly, and the direction is genuinely sideways -
        /// an evade that merely backs off would lose to a committed lunge on distance.
        /// </summary>
        public void ExecuteEvade(float lateralMetres, float recoverySeconds = -1f)
        {
            if (_movement == null || _lastLineDirection.sqrMagnitude < 0.0001f)
                return;

            _evadeSign = -_evadeSign;
            _evadeDirection = Vector3.Cross(Vector3.up, _lastLineDirection).normalized * _evadeSign;

            var metres = Mathf.Max(0.1f, lateralMetres);
            _evadeRemaining = Mathf.Clamp(metres / Mathf.Max(0.5f, _evadeSpeed), 0.08f, 1f);
            _recoveryRemaining = recoverySeconds >= 0f ? recoverySeconds : _evadeRecovery;
            IsNavigating = false;
            _navigator?.Reset();
        }

        /// <summary>Cancels any evade in progress, e.g. on death or on losing the target.</summary>
        public void CancelEvade()
        {
            _evadeRemaining = 0f;
            _recoveryRemaining = 0f;
        }

        /// <summary>
        /// Steers for one frame. <paramref name="duel"/> is false while the enemy is still approaching
        /// from outside measure, which is when pathfinding is worth its cost.
        /// </summary>
        public void Tick(float deltaTime, Transform target, bool duel)
        {
            if (_movement == null)
                return;

            if (target == null)
            {
                Stop();
                return;
            }

            _movement.SetTargeting(true, target);

            var position = transform.position;
            var toTarget = Flatten(target.position - position);
            var distance = toTarget.magnitude;

            if (distance < 0.01f)
            {
                Stop();
                return;
            }

            var toTargetDirection = toTarget / distance;
            _lastLineDirection = toTargetDirection;

            // A committed evade owns the body until it and its recovery are done: nothing else may
            // interrupt it. That commitment is what stops avoidance from being free.
            if (_evadeRemaining > 0f)
            {
                _evadeRemaining -= deltaTime;
                _movement.SetIntent(ToIntent(_evadeDirection, _evadeSpeed));
                return;
            }

            if (_recoveryRemaining > 0f)
            {
                // The opening after a slip: the body is still, so a fighter who read the evade punishes it.
                _recoveryRemaining -= deltaTime;
                _movement.SetIntent(Vector2.zero, false);
                return;
            }

            if (!duel && distance > _navMeshEngageDistance &&
                _navigator.TryGetApproachDirection(position, target.position, out var pathDirection))
            {
                IsNavigating = true;
                _movement.SetIntent(ToIntent(pathDirection, _approachSpeed));

                // Refresh the desired distance as we close, so the enemy arrives at measure rather than
                // running into the opponent and having to back out.
                if (distance < _bandMax * 3f)
                    _desiredDistance = Mathf.Clamp(distance, _bandMin, _bandMax);

                return;
            }

            IsNavigating = false;

            if (!duel && distance > _navMeshEngageDistance)
            {
                // No navmesh available: walk straight in, which is the best that can be done.
                _movement.SetIntent(ToIntent(toTargetDirection, _approachSpeed));
                return;
            }

            _navigator.Reset();
            TickDuelSteering(deltaTime, toTargetDirection, distance);
        }

        private void TickDuelSteering(float deltaTime, Vector3 toTargetDirection, float distance)
        {
            if (Time.time >= _nextCircleFlipTime)
            {
                _nextCircleFlipTime = Time.time + _circleFlipSeconds;
                _circleSign = -_circleSign;
            }

            var error = distance - _desiredDistance;
            var radial = Mathf.Abs(error) <= _distanceDeadband
                ? 0f
                : Mathf.Sign(error) * Mathf.Clamp01(Mathf.Abs(error) / _distanceErrorForFullSpeed);

            var right = Vector3.Cross(Vector3.up, toTargetDirection);
            var tangent = right * (_circleSign * _circleBias * (1f - Mathf.Abs(radial) * 0.5f));

            var steer = toTargetDirection * radial + tangent;
            steer.y = 0f;

            if (steer.sqrMagnitude < 0.0001f)
            {
                // Standing still inside the band is a real choice: it stops the enemy shivering on the spot.
                _movement.SetIntent(Vector2.zero, false);
                return;
            }

            steer.Normalize();
            steer = AvoidObstacles(steer);

            var speed = Mathf.Abs(radial) > 0.01f ? _duelSpeed : _duelSpeed * 0.55f;
            _movement.SetIntent(ToIntent(steer, speed));
        }

        /// <summary>Slides the steering direction along whatever is directly ahead.</summary>
        private Vector3 AvoidObstacles(Vector3 direction)
        {
            var origin = transform.position + Vector3.up * _probeHeight;

            if (!Physics.SphereCast(origin, _probeRadius, direction, out var hit, _probeDistance, _obstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                return direction;
            }

            var slide = Vector3.ProjectOnPlane(direction, hit.normal);
            slide.y = 0f;

            if (slide.sqrMagnitude > 0.0001f)
                return slide.normalized;

            var around = Vector3.Cross(Vector3.up, hit.normal);
            return around.sqrMagnitude > 0.0001f ? around.normalized * _circleSign : direction;
        }

        /// <summary>
        /// Converts the enemy's world-space steering vector into the character's local input space
        /// (x = strafe right, y = forward). This ensures CharacterAnimatorDriver correctly picks forward,
        /// backward, or strafe animations relative to where the character is facing.
        /// </summary>
        private LocomotionIntent ToIntent(Vector3 worldDirection, float speed)
        {
            var flattened = Flatten(worldDirection);
            if (flattened.sqrMagnitude < 0.0001f)
                return LocomotionIntent.Idle;

            var throttle = Mathf.Clamp01(speed / Mathf.Max(0.1f, _runSpeedReference));

            // Transform world direction into character local horizontal space:
            // Vector3.forward in local space -> (0, 1) in Vector2 intent (forward)
            // Vector3.back in local space -> (0, -1) in Vector2 intent (backward)
            // Vector3.right in local space -> (1, 0) in Vector2 intent (strafe right)
            // Vector3.left in local space -> (-1, 0) in Vector2 intent (strafe left)
            var localDir = transform.InverseTransformDirection(flattened.normalized) * throttle;

            return new LocomotionIntent(new Vector2(localDir.x, localDir.z), throttle, true, true);
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, _desiredDistance);

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, _navMeshEngageDistance);
        }
    }
}

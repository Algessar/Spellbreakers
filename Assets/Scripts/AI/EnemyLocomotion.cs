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

        private Movement_V3 _movement;
        private NavMeshAgent _navMeshAgent;
        private NavMeshNavigator _navigator;

        private float _desiredDistance = 1.6f;
        private float _bandMin = 1.1f;
        private float _bandMax = 2.1f;
        private float _circleSign = 1f;
        private float _nextCircleFlipTime;

        /// <summary>The distance the enemy is currently trying to hold.</summary>
        public float DesiredDistance => _desiredDistance;

        public bool IsNavigating { get; private set; }

        private void Awake()
        {
            _movement = GetComponent<Movement_V3>() ?? transform.root.GetComponentInChildren<Movement_V3>(true);
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
        /// Movement without a camera treats the intent as world-space, so the enemy's steering vector maps
        /// straight across. Magnitude is normalised against the run reference so the Animator still picks
        /// walk or run the way it does for the player.
        /// </summary>
        private LocomotionIntent ToIntent(Vector3 worldDirection, float speed)
        {
            var flattened = Flatten(worldDirection);
            if (flattened.sqrMagnitude < 0.0001f)
                return LocomotionIntent.Idle;

            var throttle = Mathf.Clamp01(speed / Mathf.Max(0.1f, _runSpeedReference));
            var direction = flattened.normalized * throttle;

            return new LocomotionIntent(new Vector2(direction.x, direction.z), throttle, true, true);
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

using UnityEngine;
using UnityEngine.AI;

namespace Spellbreakers.Ai
{
    /// <summary>
    /// Uses a <see cref="NavMeshAgent"/> purely as a pathfinder.
    ///
    /// The agent never moves or rotates the character: the character controller stays the single owner of
    /// the transform, so navigation and the duel's footwork use identical collision and grounding rules.
    /// Letting a NavMeshAgent drive the transform directly is the usual way these two systems end up
    /// fighting each other.
    /// </summary>
    public sealed class NavMeshNavigator
    {
        private readonly NavMeshAgent _agent;

        public NavMeshNavigator(NavMeshAgent agent)
        {
            _agent = agent;

            if (_agent == null)
                return;

            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.autoBraking = true;
        }

        public bool IsAvailable => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        /// <summary>
        /// A world-space direction towards <paramref name="destination"/>. Returns false when there is no
        /// usable path, which tells the caller to fall back to direct steering.
        /// </summary>
        public bool TryGetApproachDirection(Vector3 currentPosition, Vector3 destination, out Vector3 direction)
        {
            direction = Vector3.zero;

            if (!IsAvailable)
                return false;

            // Feed the true position back in so the agent's own notion of where it is stays honest.
            _agent.nextPosition = currentPosition;

            if (!_agent.SetDestination(destination))
                return false;

            var velocity = _agent.desiredVelocity;
            velocity.y = 0f;

            if (velocity.sqrMagnitude < 0.0001f)
                return _agent.remainingDistance > _agent.stoppingDistance;

            direction = velocity.normalized;
            return true;
        }

        /// <summary>Stops pathing. Called when the fight closes to duel range.</summary>
        public void Reset()
        {
            if (!IsAvailable)
                return;

            _agent.ResetPath();
        }

        public void SetStoppingDistance(float distance)
        {
            if (_agent != null)
                _agent.stoppingDistance = Mathf.Max(0f, distance);
        }
    }
}

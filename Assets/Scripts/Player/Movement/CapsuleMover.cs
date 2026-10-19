using UnityEngine;

namespace Player
{
    /// <summary>
    /// Capsule collision and grounding, extracted from the character controller so it is a single piece
    /// of geometry rather than something entangled with Animator state.
    ///
    /// The previous version used one layer mask for both grounding and world collision. That made a
    /// walkable surface and a wall indistinguishable, and it meant a blade or a prop on the ground layer
    /// could be mistaken for floor. Grounding and collision are now separate masks.
    /// </summary>
    public sealed class CapsuleMover
    {
        private const float SkinWidth = 0.001f;

        private readonly CapsuleCollider _capsule;
        private readonly Transform _transform;

        private float _radius;
        private float _height;
        private Vector3 _center;

        public CapsuleMover(CapsuleCollider capsule, Transform owner)
        {
            _capsule = capsule;
            _transform = owner;
            RefreshDimensions();
        }

        public float Gravity { get; set; } = -15f;

        public float GroundCheckDistance { get; set; } = 0.1f;

        public float TerminalVelocity { get; set; } = -30f;

        public LayerMask GroundMask { get; set; } = ~0;

        /// <summary>Everything solid, minus the ground mask and the mover's own layer.</summary>
        public LayerMask CollisionMask { get; set; } = ~0;

        public bool IsGrounded { get; private set; }

        public float VerticalVelocity { get; private set; }

        /// <summary>Set by a jump or a knockback; the mover integrates it until the ground stops it.</summary>
        public void AddVerticalVelocity(float velocity)
        {
            VerticalVelocity += velocity;
        }

        public void RefreshDimensions()
        {
            _radius = _capsule.radius;
            _height = _capsule.height;
            _center = _capsule.center;
        }

        public Vector3 CapsuleBottom =>
            _transform.position + _transform.up * (_center.y - _height / 2f + _radius);

        public Vector3 Center =>
            _transform.position + _transform.up * _center.y;

        /// <summary>Snaps to the ground and integrates gravity.</summary>
        public void ApplyGravityAndGrounding()
        {
            var capsuleBottom = CapsuleBottom;
            var startPoint = capsuleBottom + Vector3.up * _radius;

            // Look at least as far down as this step's fall will travel, so a fast fall cannot tunnel
            // through the ground between checks.
            var fallStep = Mathf.Max(0f, -VerticalVelocity) * Time.fixedDeltaTime;
            var maxDistance = _radius + GroundCheckDistance + 0.05f + fallStep;

            if (Physics.SphereCast(startPoint, _radius, Vector3.down, out var hit, maxDistance, GroundMask))
            {
                var desiredBottomY = hit.point.y + _radius;
                var correction = desiredBottomY - capsuleBottom.y;
                if (Mathf.Abs(correction) > 0.001f)
                    _transform.position += Vector3.up * correction;

                if (VerticalVelocity <= 0f)
                {
                    IsGrounded = true;
                    VerticalVelocity = 0f;
                }
            }
            else
            {
                IsGrounded = false;
            }

            if (!IsGrounded)
                VerticalVelocity += Gravity * Time.fixedDeltaTime;

            if (VerticalVelocity < TerminalVelocity)
                VerticalVelocity = TerminalVelocity;
        }

        /// <summary>
        /// Moves from <paramref name="origin"/> by <paramref name="movement"/>, sliding along whatever it
        /// hits. The movement's vertical component rides on the integrated velocity, not on the sweep.
        /// </summary>
        public Vector3 Resolve(Vector3 origin, Vector3 movement)
        {
            if (movement.magnitude < 0.0001f)
                return origin;

            var mask = EffectiveCollisionMask();
            var finalPosition = origin;
            var remaining = movement;

            GetSweepPoints(origin, out var point1, out var point2);

            if (Physics.CapsuleCast(point1, point2, _radius, remaining.normalized, out var hit,
                    remaining.magnitude, mask))
            {
                var distanceToHit = Mathf.Max(0f, hit.distance - SkinWidth);
                finalPosition += remaining.normalized * distanceToHit;

                var remainingDistance = remaining.magnitude - distanceToHit;
                if (remainingDistance <= 0.01f)
                    return finalPosition;

                var slideDirection = Vector3.ProjectOnPlane(remaining.normalized, hit.normal).normalized;
                if (slideDirection.sqrMagnitude < 0.01f || Vector3.Dot(slideDirection, remaining.normalized) < 0f)
                    return finalPosition;

                var slideVector = slideDirection * remainingDistance;
                GetSweepPoints(finalPosition, out point1, out point2);

                if (Physics.CapsuleCast(point1, point2, _radius, slideVector.normalized, out var slideHit,
                        slideVector.magnitude, mask))
                {
                    finalPosition += slideVector.normalized * Mathf.Max(0f, slideHit.distance - SkinWidth);
                }
                else
                {
                    finalPosition += slideVector;
                }
            }
            else
            {
                finalPosition += remaining;
            }

            return finalPosition;
        }

        /// <summary>
        /// Removes the mover's own layer from the collision mask. Without this the sweep starts inside
        /// the character's own collider and reports a hit at distance zero.
        /// </summary>
        public LayerMask EffectiveCollisionMask()
        {
            var mask = CollisionMask;
            var ownLayer = _transform.gameObject.layer;
            if (ownLayer >= 0)
                mask &= ~(1 << ownLayer);

            return mask;
        }

        private void GetSweepPoints(Vector3 origin, out Vector3 top, out Vector3 bottom)
        {
            var halfHeight = (_height / 2f) - _radius;
            var centerOffset = origin + _transform.up * _center.y;

            bottom = centerOffset - _transform.up * halfHeight;
            top = centerOffset + _transform.up * halfHeight;
        }

        public void DrawGizmos()
        {
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(CapsuleBottom + Vector3.down * GroundCheckDistance, _radius);
        }
    }
}

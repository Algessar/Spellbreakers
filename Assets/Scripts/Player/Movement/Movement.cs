using UnityEngine;
using UnityEngine.InputSystem;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace Player
{
    /// <summary>
    /// Character locomotion for both the player and enemies.
    ///
    /// It used to require an <see cref="InputHandler"/> and a <see cref="PlayerCamera"/> and to read the
    /// camera directly, which made it impossible to reuse for an enemy. Intent now arrives either from
    /// input or from <see cref="SetIntent"/>, so an enemy walks with the player's exact movement rules -
    /// which matters, because a duel is only fair if both fighters are constrained the same way.
    ///
    /// The physics and the Animator state selection live in <see cref="CapsuleMover"/> and
    /// <see cref="CharacterAnimatorDriver"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public class Movement : MonoBehaviour
    {
        [Header("Direction")]
        [SerializeField] private PlayerCamera _camera;

        [SerializeField] private float _moveAmount;
        [SerializeField] private bool _isMoving;

        [SerializeField] private float _rotationSpeed = 5f;

        [Header("Target Lock Facing")]
        [SerializeField, Min(0f)] private float _targetFacingDelay = 0.12f;
        [SerializeField, Min(1f)] private float _targetFacingTurnSpeed = 240f;

        [Header("Target Lock Animation")]
        [Tooltip("Base Layer state used while locked on and stationary. Its lower-body pose can sit beneath the Passes guard overlay.")]
        [SerializeField] private string _targetingIdleStateName = "InGuard-Idle";

        [Header("CrossFade")]
        [SerializeField] private float _crossFadeDuration = 0.1f;
        [SerializeField] private float _walkThreshold = 0.1f;
        [SerializeField] private float _runThreshold = 0.7f;

        [Header("Gravity & Ground")]
        [SerializeField] private float _gravity = -15f;
        [SerializeField] private float _groundCheckDistance = 0.1f;

        [Tooltip("Layers treated as walkable ground. Kept separate from the collision mask.")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Tooltip("Layers the capsule sweeps against. Leave as Everything: the character's own layer is removed at runtime.")]
        [SerializeField] private LayerMask _collisionMask = ~0;

        [SerializeField] private float _verticalVelocity;
        [SerializeField] private bool _isGrounded;

        [Header("Diagnostics")]
        [Tooltip("Temporary: logs the inputs and computed facing used by UpdateMovementDirection.")]
        [SerializeField] private bool _logMovementDirection;

        private Animator _animator;
        private CapsuleCollider _capsule;
        private Transform _transform;
        private CapsuleMover _mover;
        private CharacterAnimatorDriver _animation;

        private Vector2 _direction;
        private bool _isTargeting;
        private Transform _targetingTarget;
        private float _targetFacingStartTime;

        /// <summary>True once something other than the input handler owns this character's intent.</summary>
        public bool HasExternalIntent { get; private set; }

        public bool IsGrounded => _mover != null && _mover.IsGrounded;

        private void Start()
        {
            _transform = transform;

            if (!_animator)
                _animator = GetComponent<Animator>();

            if (!_capsule)
                _capsule = GetComponent<CapsuleCollider>();

            if (_camera == null)
                _camera = GetComponent<PlayerCamera>();

            if (_animator == null)
            {
                Debug.LogError("Movement_V3 requires an Animator on this object.", this);
                enabled = false;
                return;
            }

            if (_capsule == null)
            {
                Debug.LogError("Movement_V3 requires a CapsuleCollider on this object.", this);
                enabled = false;
                return;
            }

            // Root motion is left enabled so deltaPosition is populated; the motor consumes it explicitly
            // rather than letting Unity apply it a second time.
            _animator.applyRootMotion = true;

            _mover = new CapsuleMover(_capsule, _transform)
            {
                Gravity = _gravity,
                GroundCheckDistance = _groundCheckDistance,
                GroundMask = _groundLayer,
                CollisionMask = _collisionMask,
            };

            _animation = new CharacterAnimatorDriver(
                _animator,
                _targetingIdleStateName,
                _crossFadeDuration,
                _walkThreshold,
                _runThreshold);

            _animation.PlayInitialIdle();
        }

        private void Update()
        {
            if (_animation == null)
                return;

            _animation.Play(BuildIntent());
        }

        private void LateUpdate()
        {
            if (!_isTargeting || _targetingTarget == null || Time.time < _targetFacingStartTime)
                return;

            var toTarget = Vector3.ProjectOnPlane(_targetingTarget.position - _transform.position, Vector3.up);
            if (toTarget.sqrMagnitude <= 0.0001f)
                return;

            var targetFacingRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            _transform.rotation = Quaternion.RotateTowards(
                _transform.rotation,
                targetFacingRotation,
                _targetFacingTurnSpeed * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (_mover == null)
                return;

            // Keep the tunables live so the inspector still behaves as a tuning surface.
            _mover.Gravity = _gravity;
            _mover.GroundCheckDistance = _groundCheckDistance;
            _mover.GroundMask = _groundLayer;
            _mover.CollisionMask = _collisionMask;

            _mover.ApplyGravityAndGrounding();
            _verticalVelocity = _mover.VerticalVelocity;
            _isGrounded = _mover.IsGrounded;

            UpdateMovementDirection();
            ApplyRootMotionWithCollision();
        }

        /// <summary>
        /// Hands control of this character's legs to a non-input source, such as enemy duel steering.
        /// Once set, input no longer drives movement until <see cref="ClearExternalIntent"/>.
        /// </summary>
        public void SetIntent(Vector2 direction, bool isMoving)
        {
            HasExternalIntent = true;
            _direction = direction;
            _isMoving = isMoving;
            _moveAmount = Mathf.Clamp01(Mathf.Abs(direction.x) + Mathf.Abs(direction.y));
        }

        public void SetIntent(in LocomotionIntent intent)
        {
            SetIntent(intent.Direction, intent.IsMoving);
        }

        public void ClearExternalIntent()
        {
            HasExternalIntent = false;
            _direction = Vector2.zero;
            _isMoving = false;
            _moveAmount = 0f;
        }

        public void SetTargeting(bool targetingEnabled, Transform target)
        {
            if (targetingEnabled && (!_isTargeting || target != _targetingTarget))
                _targetFacingStartTime = Time.time + _targetFacingDelay;

            _isTargeting = targetingEnabled;
            _targetingTarget = targetingEnabled ? target : null;
        }

        public void OnMove(InputAction.CallbackContext ctx)
        {
            if (HasExternalIntent)
                return;

            _direction = ctx.ReadValue<Vector2>();
            _isMoving = _direction.sqrMagnitude > 0.0001f;
            _moveAmount = Mathf.Clamp01(Mathf.Abs(_direction.x) + Mathf.Abs(_direction.y));
        }

        private LocomotionIntent BuildIntent()
        {
            return new LocomotionIntent(
                _direction,
                _moveAmount,
                _isMoving,
                _isTargeting && _targetingTarget != null);
        }

        private void UpdateMovementDirection()
        {
            if (_isTargeting && _targetingTarget != null)
                return;

            if (!_isMoving || _direction == Vector2.zero)
                return;

            var worldMoveDir = GetMoveDirection(_direction);
            var targetRotation = Quaternion.LookRotation(worldMoveDir, Vector3.up);

            if (_logMovementDirection)
            {
                Debug.Log(
                    $"[MoveDir] cam='{(_camera == null ? "none (world-space)" : _camera.name)}'" +
                    $" input=({_direction.x:F2},{_direction.y:F2})" +
                    $" worldMoveDir={worldMoveDir.ToString("F3")}" +
                    $" targetYaw={targetRotation.eulerAngles.y:F1}" +
                    $" currentYaw={_transform.eulerAngles.y:F1}",
                    this);
            }

            _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRotation, _rotationSpeed * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Camera-relative for the player, world-space for anything without a camera. Keeping the fallback
        /// here is what lets an enemy reuse this component unchanged.
        /// </summary>
        private Vector3 GetMoveDirection(Vector2 input)
        {
            if (_camera == null || _camera.CameraTransform == null)
                return new Vector3(input.x, 0f, input.y).normalized;

            return _camera.GetCameraRelativeMoveDirection(input);
        }

        private void ApplyRootMotionWithCollision()
        {
            var deltaRootMotion = _animator.deltaPosition;
            var horizontalMovement = new Vector3(deltaRootMotion.x, 0f, deltaRootMotion.z);
            var verticalMovement = Vector3.up * (_mover.VerticalVelocity * Time.fixedDeltaTime);
            var desiredMovement = horizontalMovement + verticalMovement;

            if (desiredMovement.sqrMagnitude < 0.0001f)
                return;

            _transform.position = _mover.Resolve(_transform.position, desiredMovement);
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || _mover == null)
                return;

            _mover.DrawGizmos();
        }
    }
}

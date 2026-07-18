using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace Player
{
    public class Movement_V3 : MonoBehaviour
    {
        private Animator _animator;
        [SerializeField] private AnimatorController _controller;
        private CapsuleCollider _capsule;
        private Transform _transform;

        [Header("Direction")] 
        private PlayerCamera _camera;

        // --- State names (hashed for speed) ---
        private int _idleHash;
        private int _walkHash;
        private int _runHash;
        private int _currentStateHash;   // track current state to avoid repeated CrossFade calls

        private Vector2 _direction;
        [SerializeField] private float _vertical = 0f;
        [SerializeField] private float _horizontal = 0f;
        [SerializeField] private float _moveAmount = 0f;
        [SerializeField] private bool _isMoving = false;

        [SerializeField] private float _rotationSpeed = 5f;

        // --- CrossFade settings ---
        [SerializeField] private float _crossFadeDuration = 0.1f;   // seconds for transition blend
        [SerializeField] private float _walkThreshold = 0.1f;       // moveAmount above 0 to start walking
        [SerializeField] private float _runThreshold = 0.7f;        // moveAmount above this switches to run

        [Header("Gravity & Ground")] 
        [SerializeField] private float _gravity = -15f;
        [SerializeField] private float _jumpHeight = 3f;
        [SerializeField] private float _groundCheckDistance = 0.1f;
        [SerializeField] private LayerMask _groundLayer = ~0;

        [SerializeField] private float _verticalVelocity = 0f;
        [SerializeField] private bool _isGrounded = false;

        // ---- COLLISION SETTINGS (Auto-detected from CapsuleCollider) ----
        private float _capsuleRadius;
        private float _capsuleHeight;
        private Vector3 _capsuleCenter;

        void Start()
        {
            _transform = transform;
            _animator = GetComponent<Animator>();
            _capsule = GetComponent<CapsuleCollider>();
            _camera = GetComponent<PlayerCamera>();
            _animator.runtimeAnimatorController = _controller;

            _animator.applyRootMotion = true;   // DON'T TOUCH THIS

            // Hash the state names once
            _idleHash = Animator.StringToHash("Idle");
            _walkHash = Animator.StringToHash("Walk");
            _runHash  = Animator.StringToHash("Run");

            // Start in Idle
            _currentStateHash = _idleHash;
            _animator.CrossFade(_idleHash, 0f);   // instant start

            _capsuleRadius = _capsule.radius;
            _capsuleHeight = _capsule.height;
            _capsuleCenter = _capsule.center;
        }

        void Update()
        {
            // --- Replace the old SetFloat calls with CrossFade logic ---
            UpdateAnimationState();
        }

        private void FixedUpdate()
        {
            HandleGravityAndGrounding();
            UpdateMovementDirection();
            ApplyRootMotionWithCollision();
        }

        /// <summary>
        /// Choose Idle / Walk / Run based on moveAmount and CrossFade to the corresponding state.
        /// </summary>
        private void UpdateAnimationState()
        {
            int targetHash;

            if (!_isMoving || _moveAmount < _walkThreshold)
            {
                targetHash = _idleHash;
            }
            else if (_moveAmount < _runThreshold)
            {
                targetHash = _walkHash;
            }
            else
            {
                targetHash = _runHash;
            }

            // Only call CrossFade if the state actually changes
            if (targetHash != _currentStateHash)
            {
                _animator.CrossFade(targetHash, _crossFadeDuration);
                _currentStateHash = targetHash;
            }
        }

        private void UpdateMovementDirection()
        {
            if (!_isMoving || _direction == Vector2.zero) return;

            Vector3 worldMoveDir = _camera.GetCameraRelativeMoveDirection(_direction);

            Quaternion targetRotation = Quaternion.LookRotation(worldMoveDir, Vector3.up);
            _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRotation, _rotationSpeed * Time.fixedDeltaTime);

            // No need to feed blend tree parameters anymore;
            // the direction is only used for rotation, root motion handles forward movement.
        }

        private void HandleGravityAndGrounding()
        {
            Vector3 capsuleBottom = GetCapsuleBottom();

            Vector3 startPoint = capsuleBottom + Vector3.up * _capsuleRadius;
            float maxDistance = _capsuleRadius + _groundCheckDistance + 0.05f;

            if (Physics.SphereCast(startPoint, _capsuleRadius, Vector3.down, out RaycastHit hit, maxDistance, _groundLayer))
            {
                float desiredBottomY = hit.point.y + _capsuleRadius;
                float currentBottomY = capsuleBottom.y;
                float correction = desiredBottomY - currentBottomY;

                if (Mathf.Abs(correction) > 0.001f)
                {
                    _transform.position += Vector3.up * correction;
                }

                if (_verticalVelocity <= 0)
                {
                    _isGrounded = true;
                    _verticalVelocity = 0;
                }
            }
            else
            {
                _isGrounded = false;
            }

            if (!_isGrounded)
            {
                _verticalVelocity += _gravity * Time.fixedDeltaTime;
            }

            if (_verticalVelocity < -30f) _verticalVelocity = -30f;
        }

        private void ApplyRootMotionWithCollision()
        {
            Vector3 deltaRootMotion = _animator.deltaPosition;
            Vector3 horizontalMovement = new Vector3(deltaRootMotion.x, 0, deltaRootMotion.z);
            Vector3 verticalMovement = Vector3.up * (_verticalVelocity * Time.fixedDeltaTime);
            Vector3 desiredMovement = horizontalMovement + verticalMovement;

            if (desiredMovement.sqrMagnitude < 0.0001f)
                return;

            Vector3 finalPosition = ResolveCapsuleCollision(_transform.position, desiredMovement);
            _transform.position = finalPosition;
        }

        private Vector3 ResolveCapsuleCollision(Vector3 origin, Vector3 movement)
        {
            if (movement.magnitude < 0.0001f) return origin;

            Vector3 finalPos = origin;
            Vector3 remainingMovement = movement;

            GetCapsuleSweepPoints(out Vector3 point1, out Vector3 point2);

            if (Physics.CapsuleCast(point1, point2, _capsuleRadius, remainingMovement.normalized, out RaycastHit hit,
                    remainingMovement.magnitude, _groundLayer))
            {
                float distanceToHit = Mathf.Max(0, hit.distance - 0.001f);
                finalPos += remainingMovement.normalized * distanceToHit;

                float remainingDistance = remainingMovement.magnitude - distanceToHit;
                if (remainingDistance <= 0.01f) return finalPos;

                Vector3 hitNormal = hit.normal;
                Vector3 newDirection = Vector3.ProjectOnPlane(remainingMovement.normalized, hitNormal).normalized;

                if (newDirection.sqrMagnitude < 0.01f || Vector3.Dot(newDirection, remainingMovement.normalized) < 0f)
                    return finalPos;

                float slideDistance = remainingDistance;
                Vector3 slideVector = newDirection * slideDistance;

                Vector3 newOrigin = finalPos;
                GetCapsuleSweepPoints(out point1, out point2, newOrigin);

                if (Physics.CapsuleCast(point1, point2, _capsuleRadius, slideVector.normalized, out RaycastHit slideHit,
                        slideVector.magnitude, _groundLayer))
                {
                    float slideDistToHit = Mathf.Max(0, slideHit.distance - 0.001f);
                    finalPos += slideVector.normalized * slideDistToHit;
                }
                else
                {
                    finalPos += slideVector;
                }
            }
            else
            {
                finalPos += remainingMovement;
            }

            return finalPos;
        }

        private void GetCapsuleSweepPoints(out Vector3 top, out Vector3 bottom)
        {
            GetCapsuleSweepPoints(out top, out bottom, _transform.position);
        }

        private void GetCapsuleSweepPoints(out Vector3 top, out Vector3 bottom, Vector3 origin)
        {
            float halfHeight = (_capsuleHeight / 2f) - _capsuleRadius;
            Vector3 centerOffset = origin + _transform.up * _capsuleCenter.y;

            bottom = centerOffset - _transform.up * halfHeight;
            top = centerOffset + _transform.up * halfHeight;
        }

        private Vector3 GetCapsuleBottom()
        {
            return _transform.position + _transform.up * (_capsuleCenter.y - _capsuleHeight / 2f + _capsuleRadius);
        }

        public void OnMove(InputAction.CallbackContext ctx)
        {
            _direction = ctx.ReadValue<Vector2>();
            _isMoving = !(_direction == Vector2.zero);

            MoveInput(_direction.x, _direction.y);
        }

        private void MoveInput(float x, float y)
        {
            _vertical = y;
            _horizontal = x;
            _moveAmount = Mathf.Clamp01(Mathf.Abs(x) + Mathf.Abs(y));
        }


        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(GetCapsuleBottom() + Vector3.down * _groundCheckDistance, _capsuleRadius);
        }
    }
}
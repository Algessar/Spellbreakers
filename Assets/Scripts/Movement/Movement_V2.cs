using System;
using System.Numerics;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class Movement_V2 : MonoBehaviour
{
    private Animator _animator;
    [SerializeField] private AnimatorController _controller;
    private CapsuleCollider _capsule;
    private Transform _transform;


    [Header("Direction")]
    private PlayerCamera _camera;
    
    private int _verticalHash;
    private int _horizontalHash;
    private int _speedHash; //NOTE: I don't think this one's needed

    private Vector2 _direction;
    [SerializeField] private float _vertical = 0f;
    [SerializeField] private float _horizontal = 0f;
    [SerializeField] private float _moveAmount = 0f;
    [SerializeField] private bool _isMoving = false;


    [Header("Gravity & Ground")] [SerializeField]
    private float _gravity = -15f; // Stronger gravity for snappy feel

    [SerializeField] private float _jumpHeight = 3f;
    [SerializeField] private float _groundCheckDistance = 0.1f;
    [SerializeField] private LayerMask _groundLayer = ~0;

    [SerializeField] private float _verticalVelocity = 0f;
    [SerializeField] private bool _isGrounded = false;

    // ---- COLLISION SETTINGS (Auto-detects from CapsuleCollider) ----
    private float _capsuleRadius;
    private float _capsuleHeight;
    private Vector3 _capsuleCenter;

    void Start()
    {
        _transform = transform;
        _animator = GetComponent<Animator>();
        _capsule = GetComponent<CapsuleCollider>();
        _animator.runtimeAnimatorController = _controller;

        _animator.applyRootMotion = true;

        _verticalHash = Animator.StringToHash("Vertical");
        _horizontalHash = Animator.StringToHash("Horizontal");
        _speedHash =
            Animator.StringToHash("Speed"); //NOTE: Gonna keep this around for a bit, might be useful at some point.

        _capsuleRadius = _capsule.radius;
        _capsuleHeight = _capsule.height;
        _capsuleCenter = _capsule.center;
    }

    void Update()
    {
        _animator.SetFloat(_verticalHash, _vertical);
        _animator.SetFloat(_horizontalHash, _horizontal);
        _animator.SetFloat(_speedHash, _moveAmount);
    }

    private void FixedUpdate()
    {
        HandleGravityAndGrounding();
        ApplyRootMotionWithCollision();
    }

    private void HandleGravityAndGrounding()
    {
        // Detect if we are on the ground by casting straight down
        Vector3 capsuleBottom = GetCapsuleBottom();
        float castDistance = _groundCheckDistance + 0.05f;

        if (Physics.SphereCast(capsuleBottom, _capsuleRadius, Vector3.down, out RaycastHit hit, castDistance, _groundLayer))
        {
            // Snap to ground to prevent floating
            float distanceToGround = hit.distance - _groundCheckDistance;
            if (distanceToGround > 0.001f)
            {
                _transform.position -= Vector3.up * distanceToGround;
            }

            // If we are moving downward, reset velocity
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

        // Apply gravity (even if grounded, we keep it 0, but if jumping, we handle it)
        if (!_isGrounded)
        {
            _verticalVelocity += _gravity * Time.fixedDeltaTime;
        }
        // Clamp terminal velocity so you don't go mach 5
        if (_verticalVelocity < -30f) _verticalVelocity = -30f;
    }

    private void ApplyRootMotionWithCollision()
    {
        // 1. Get the root motion deltas from the animation (this is the "Horizontal" movement)
        Vector3 deltaRootMotion = _animator.deltaPosition;

        // Strip out any vertical movement the animation tries to do (we own the Y axis)
        Vector3 horizontalMovement = new Vector3(deltaRootMotion.x, 0, deltaRootMotion.z);

        // 2. Build our final movement vector
        Vector3 verticalMovement = Vector3.up * (_verticalVelocity * Time.fixedDeltaTime);
        Vector3 desiredMovement = horizontalMovement + verticalMovement;

        // 3. If there is virtually no movement, skip collision to save performance
        if (desiredMovement.sqrMagnitude < 0.0001f)
            return;

        // 4. --- MANUAL COLLISION RESOLUTION (CapsuleCast) ---
        // We move the capsule, check for hits, and slide along surfaces.
        Vector3 finalPosition = ResolveCapsuleCollision(_transform.position, desiredMovement);

        // 5. Apply the final safe position
        _transform.position = finalPosition;

        // 6. Apply root rotation separately (so turning feels smooth)
        _transform.rotation *= _animator.deltaRotation;
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
            {
                return finalPos;
            }

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
        // The sweep points are the centers of the two hemispheres of the capsule
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
        _moveAmount = Mathf.Clamp01(Mathf.Abs(_vertical) + Mathf.Abs(_horizontal));
    }
    
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(GetCapsuleBottom() + Vector3.down * _groundCheckDistance, _capsuleRadius);
    }
}
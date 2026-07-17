using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using AnimatorController = UnityEditor.Animations.AnimatorController;

public class PlayerMovement : MonoBehaviour
{
    private Animator _animator;
    [SerializeField] private AnimatorController _controller;

    private Vector2 _direction;
    [SerializeField] private float _vertical = 0f;
    [SerializeField] private float _horizontal = 0f;
    [SerializeField] private float _moveAmount = 0f;
    [SerializeField] private bool _isMoving = false;

    [Header("Ground matching")] 
    public float _gravity = 1f;

    public float sphereRadius = 1;
    public float maxDistance;
    public LayerMask GroundLayer;
    
    private void Start()
    {
        _vertical = 0f;
        _horizontal = 0f;
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = true;
        _animator.runtimeAnimatorController = _controller;

        _horizontal = Animator.StringToHash("Horizontal");
        _vertical = Animator.StringToHash("Vertical");

    }

    private void Update()
    {
        UpdateMoveAnim();
        UpdateStrafeAnim();
    }

    private void FixedUpdate()
    {
        // Gravity();
    }

    private void Gravity()
    {
        transform.position = new Vector3(0, -_gravity, 0);
    }

    private void UpdateMoveAnim()
    {
            _animator.SetFloat("Vertical", _vertical);
    }

    private void UpdateStrafeAnim()
    {
        //Crossfade strafe
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

    private void GroundCheck()
    {
        var ray = new Ray();
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            
        }
    }
    
    private bool IsGroundedCheck(out RaycastHit hit)
    {
        bool hitGround = Physics.SphereCast(transform.position, sphereRadius, Vector3.down, out hit, maxDistance, GroundLayer);
        return hitGround;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        
        // Gizmos.DrawRay(transform.position, Vector3.down);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * maxDistance);
    }
}

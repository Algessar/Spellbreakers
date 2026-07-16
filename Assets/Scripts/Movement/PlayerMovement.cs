using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using AnimatorController = UnityEditor.Animations.AnimatorController;

public class PlayerMovement : MonoBehaviour
{
    private Animator _animator;
    [SerializeField] private AnimatorController _controller;

    private Vector2 _direction;
    [SerializeField] private float _vertical;
    [SerializeField] private float _horizontal;
    [SerializeField] private float _moveAmount;
    [SerializeField] private bool _isMoving;


    [SerializeField] private List<AnimationClip> _clips;
    private AnimationClip _idleClip;
    private AnimationClip _walkClip;
    private AnimationClip _runClip;

    
    private void Start()
    {
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
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class Input : MonoBehaviour
{
    private Actions _actions;

    private PlayerMovement _movement;
    private PlayerCamera _camera;


    private void Start()
    {

        _actions = new Actions();

        _movement = GetComponent<PlayerMovement>();
        _camera = GetComponent<PlayerCamera>();

        _actions.Player.Enable();

        _actions.Player.Move.performed += _movement.OnMove;
        _actions.Player.Move.canceled  += _movement.OnMove;

        _actions.Player.Look.performed += _camera.OnLook;
        _actions.Player.Look.canceled  += _camera.OnLook;
        

    }

    private void OnDisable()
    {
        _actions.Player.Move.performed += _movement.OnMove;
        _actions.Player.Move.canceled  += _movement.OnMove;

        _actions.Player.Look.performed += _camera.OnLook;
        _actions.Player.Look.canceled  += _camera.OnLook;
        
        _actions.Disable();

    }





}

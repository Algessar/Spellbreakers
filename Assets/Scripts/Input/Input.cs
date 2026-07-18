using UnityEngine;


namespace Player
{


    /// <summary>
    /// Player Input class. Only used to register input actions.
    /// </summary>
    public class Input : MonoBehaviour
    {
        private Actions _actions;

        private PlayerMovement _movement;
        private Movement_V2 _move2;
        private Movement_V3 _move3;
        private PlayerCamera _camera;

        private void Start()
        {
            _actions = new Actions();

            _movement = GetComponent<PlayerMovement>();
            // _move2 = GetComponent<Movement_V2>();
            _move3 = GetComponent<Movement_V3>();

            _camera = GetComponent<PlayerCamera>();

            _actions.Player.Enable();

            // _actions.Player.Move.performed += _movement.OnMove;
            // _actions.Player.Move.canceled  += _movement.OnMove;

            // _actions.Player.Move.performed += _move2.OnMove;
            // _actions.Player.Move.canceled  += _move2.OnMove;
            
            _actions.Player.Move.performed += _move3.OnMove;
            _actions.Player.Move.canceled  += _move3.OnMove;


            _actions.Player.Look.performed += _camera.OnLook;
            _actions.Player.Look.canceled  += _camera.OnLook;
        }

        private void OnDisable()
        {
            // _actions.Player.Move.performed -= _movement.OnMove;
            // _actions.Player.Move.canceled  -= _movement.OnMove;

            // _actions.Player.Move.performed -= _move2.OnMove;
            // _actions.Player.Move.canceled  -= _move2.OnMove;
            
            _actions.Player.Move.performed -= _move3.OnMove;
            _actions.Player.Move.canceled  -= _move3.OnMove;

            _actions.Player.Look.performed -= _camera.OnLook;
            _actions.Player.Look.canceled  -= _camera.OnLook;

            _actions.Disable();

        }

    }
}
using UnityEngine;


namespace Player
{
    /// <summary>
    /// Player Input class. Only used to register input actions.
    /// </summary>
    
    [DisallowMultipleComponent]
    public class InputHandler : MonoBehaviour
    {
        private Actions _actions;

        // private PlayerMovement _movement;
        // private Movement_V2 _move2;
        private Movement_V3 _move3;
        private PlayerCamera _camera;

        private CombatHandler _combat;
        private TargetingSystem _targeting;

        private void OnEnable()
        {
            _actions = new Actions();
            _actions.Player.Enable();
            // _movement = GetComponent<PlayerMovement>();
            // _move2 = GetComponent<Movement_V2>();
            _move3 = GetComponent<Movement_V3>();

            _camera = GetComponent<PlayerCamera>();

            _combat = GetComponent<CombatHandler>();

            // Animator IK callbacks run on the Animator's GameObject.
            var animator = GetComponentInChildren<Animator>(true);
            _targeting = TargetingSystem.GetOrAddForAnimator(animator);
            
            if (_move3 != null)
            {
                _actions.Player.Move.performed += _move3.OnMove;
                _actions.Player.Move.canceled += _move3.OnMove;
            }
  
            if (_camera != null)
            {
                _actions.Player.Look.performed += _camera.OnLook;
                _actions.Player.Look.canceled += _camera.OnLook;
            }
            
            //Combat stuff

            if (_combat != null)
            {
                _actions.Player.VomTag.started += _combat.OnVomTag;
                _actions.Player.Pflug.started += _combat.OnPflug;
                _actions.Player.Alber.started += _combat.OnAlber;
                _actions.Player.Ochs.started += _combat.OnOchs;
                _actions.Player.IronGate.started += _combat.OnIronGate;
            }
            if (_targeting != null)
                _actions.Player.SwitchTarget.started += _targeting.ToggleTargeting;
        }

        private void OnDisable()
        {
            if (_actions == null)
                return;

            if (_move3 != null)
            {
                _actions.Player.Move.performed -= _move3.OnMove;
                _actions.Player.Move.canceled -= _move3.OnMove;
            }
            if (_camera != null)
            {
                _actions.Player.Look.performed -= _camera.OnLook;
                _actions.Player.Look.canceled -= _camera.OnLook;
            }
            
            if (_combat != null)
            {
                _actions.Player.VomTag.started -= _combat.OnVomTag;
                _actions.Player.Pflug.started -= _combat.OnPflug;
                _actions.Player.Alber.started -= _combat.OnAlber;
                _actions.Player.Ochs.started -= _combat.OnOchs;
                _actions.Player.IronGate.started -= _combat.OnIronGate;
            }
            if (_targeting != null)
                _actions.Player.SwitchTarget.started -= _targeting.ToggleTargeting;
            _targeting?.StopTargeting();


            _actions.Disable();
            _actions.Dispose();
            _actions = null;
        }
    }
}

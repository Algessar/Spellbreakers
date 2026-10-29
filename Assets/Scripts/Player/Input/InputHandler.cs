using UnityEngine;

namespace Player
{
    /// <summary>
    /// Routes the player's input asset into the character's systems. It holds no gameplay rules of its
    /// own: every combat input becomes a guard request on <see cref="CombatHandler"/>, which is the same
    /// entry point an enemy brain uses.
    ///
    /// Subscriptions are set up in OnEnable and torn down in OnDisable, and the action asset is disposed
    /// on destroy. The previous version subscribed only in OnEnable but assumed Start had already run,
    /// never disposed the asset, and would silently stay dead after a disable/enable cycle.
    /// </summary>
    [DisallowMultipleComponent]
    public class InputHandler : MonoBehaviour
    {
        private Actions _actions;
        private Movement _movement;
        private PlayerCamera _camera;
        private CombatHandler _combat;
        private TargetingSystem _targeting;

        private void Awake()
        {
            _movement = GetComponent<Movement>();
            _camera = GetComponent<PlayerCamera>();
            _combat = GetComponent<CombatHandler>();

            // Animator IK callbacks run on the Animator's GameObject, so targeting resolves from there.
            var animator = GetComponentInChildren<Animator>(true);
            _targeting = TargetingSystem.GetOrAddForAnimator(animator);

            // Created here, after targeting, so both exist before Start resolves their references. Combat
            // being a range-driven state rather than a permanent stance is what keeps the guard inputs
            // free outside a fight.
            CombatEngagement.GetOrAdd(gameObject);

            _actions = new Actions();

            WarnIfMissing(_movement, nameof(Movement));
            WarnIfMissing(_camera, nameof(PlayerCamera));
            WarnIfMissing(_combat, nameof(CombatHandler));
        }

        private void OnEnable()
        {
            if (_actions == null)
                return;

            _actions.Player.Enable();

            var player = _actions.Player;

            if (_movement != null)
            {
                player.Move.performed += _movement.OnMove;
                player.Move.canceled += _movement.OnMove;
            }

            if (_camera != null)
            {
                player.Look.performed += _camera.OnLook;
                player.Look.canceled += _camera.OnLook;
            }

            if (_combat != null)
            {
                player.VomTag.started += _combat.OnVomTag;
                player.Pflug.started += _combat.OnPflug;
                player.Alber.started += _combat.OnAlber;
                player.Ochs.started += _combat.OnOchs;
                player.IronGate.started += _combat.OnIronGate;
                player.SwitchMirroredGuard.started += _combat.OnSwitchMirroredGuard;
            }

            if (_targeting != null)
                player.SwitchTarget.started += _targeting.ToggleTargeting;
        }

        private void OnDisable()
        {
            if (_actions == null)
                return;

            var player = _actions.Player;

            if (_movement != null)
            {
                player.Move.performed -= _movement.OnMove;
                player.Move.canceled -= _movement.OnMove;
            }

            if (_camera != null)
            {
                player.Look.performed -= _camera.OnLook;
                player.Look.canceled -= _camera.OnLook;
            }

            if (_combat != null)
            {
                player.VomTag.started -= _combat.OnVomTag;
                player.Pflug.started -= _combat.OnPflug;
                player.Alber.started -= _combat.OnAlber;
                player.Ochs.started -= _combat.OnOchs;
                player.IronGate.started -= _combat.OnIronGate;
                player.SwitchMirroredGuard.started -= _combat.OnSwitchMirroredGuard;
            }

            if (_targeting != null)
            {
                player.SwitchTarget.started -= _targeting.ToggleTargeting;
                _targeting.StopTargeting();
            }

            player.Disable();
        }

        private void OnDestroy()
        {
            if (_actions == null)
                return;

            _actions.Disable();
            _actions.Dispose();
            _actions = null;
        }

        /// <summary>Misconfigured prefabs should fail loudly and early rather than with a null reference.</summary>
        private void WarnIfMissing(Object component, string typeName)
        {
            if (component == null)
                Debug.LogWarning($"{name}: InputHandler found no {typeName} on this object.", this);
        }
    }
}

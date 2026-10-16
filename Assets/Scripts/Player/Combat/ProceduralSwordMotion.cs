using Spellbreakers.Combat;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Procedurally enhances guard transitions with cutting arc dynamics and torso follow-through:
    /// - Adds pitch, yaw, and roll to the character's spine/chest based on the active attack line (Oberhau, Mittelhau, Unterhau).
    /// - Smoothly damps these additive rotations so cuts feel grounded, weighty, and physical without requiring bespoke full-body mocap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralSwordMotion : MonoBehaviour
    {
        [Header("Dynamics Intensity")]
        [Tooltip("Max degrees of forward torso pitch on downward cuts (Oberhau).")]
        [SerializeField, Range(0f, 25f)] private float _oberhauPitch = 12f;

        [Tooltip("Max degrees of rotational yaw follow-through on horizontal cuts (Mittelhau).")]
        [SerializeField, Range(0f, 35f)] private float _mittelhauYaw = 18f;

        [Tooltip("Max degrees of torso extension on upward cuts (Unterhau).")]
        [SerializeField, Range(0f, 20f)] private float _unterhauPitch = 8f;

        [Tooltip("Speed at which procedural spine adjustments damp back toward rest.")]
        [SerializeField, Min(1f)] private float _dampingSpeed = 12f;

        [Header("References")]
        [SerializeField] private CombatHandler _combatHandler;
        [SerializeField] private Animator _animator;

        private Transform _spine;
        private Transform _chest;
        private Quaternion _currentOffset = Quaternion.identity;

        private void Awake()
        {
            if (_combatHandler == null)
                _combatHandler = GetComponent<CombatHandler>() ?? GetComponentInParent<CombatHandler>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);

            if (_animator != null && _animator.isHuman)
            {
                _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                _chest = _animator.GetBoneTransform(HumanBodyBones.Chest) ?? _spine;
            }
        }

        private void LateUpdate()
        {
            if (!_chest || !_combatHandler)
                return;

            Quaternion targetOffset = Quaternion.identity;

            if (_combatHandler.IsTransitioning && _combatHandler.Agent != null)
            {
                var transition = _combatHandler.Agent.ActiveTransition;
                var progress = _combatHandler.Agent.Progress01;
                // Parabolic intensity curve peaking in the strike window (around progress 0.45 - 0.55)
                var intensity = 4f * progress * (1f - progress);

                switch (transition.Direction)
                {
                    case TransitionDirection.Oberhau:
                        // Torso bends forward and slightly rotates into downward cut
                        targetOffset = Quaternion.Euler(_oberhauPitch * intensity, 4f * intensity, 0f);
                        break;

                    case TransitionDirection.Mittelhau:
                        // Torso rotates with the cut direction
                        targetOffset = Quaternion.Euler(2f * intensity, _mittelhauYaw * intensity, -3f * intensity);
                        break;

                    case TransitionDirection.Unterhau:
                        // Torso lifts upward and opens slightly
                        targetOffset = Quaternion.Euler(-_unterhauPitch * intensity, -6f * intensity, 0f);
                        break;

                    case TransitionDirection.Thrust:
                        // Linear forward lean during point thrust
                        targetOffset = Quaternion.Euler(5f * intensity, 0f, 0f);
                        break;
                }
            }

            _currentOffset = Quaternion.Slerp(_currentOffset, targetOffset, _dampingSpeed * Time.deltaTime);

            if (Quaternion.Angle(_currentOffset, Quaternion.identity) > 0.01f)
            {
                _chest.localRotation = _chest.localRotation * _currentOffset;
            }
        }
    }
}

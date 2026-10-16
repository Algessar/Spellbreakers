using UnityEngine;

namespace Player
{
    /// <summary>Forwards Animator IK callbacks to a TargetingSystem stored on the player root.</summary>
    [DisallowMultipleComponent]
    public sealed class TargetingIKRelay : MonoBehaviour
    {
        private TargetingSystem _targetingSystem;

        private void Awake()
        {
            _targetingSystem = GetComponentInParent<TargetingSystem>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_targetingSystem == null)
                _targetingSystem = GetComponentInParent<TargetingSystem>();

            _targetingSystem?.ProcessAnimatorIK(layerIndex);
        }
    }
}

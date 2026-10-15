using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>Locks onto a nearby enemy and blends the Animator's upper body toward it.</summary>
    [DisallowMultipleComponent]
    public sealed class TargetingSystem : MonoBehaviour
    {
        [SerializeField] private LayerMask _enemyLayers = 1 << 8;
        [SerializeField, Min(1f)] private float _acquisitionRange = 25f;
        [SerializeField, Range(1f, 180f)] private float _acquisitionAngle = 100f;
        [SerializeField, Min(0.05f)] private float _targetRefreshInterval = 0.25f;
        [SerializeField, Range(0f, 1f)] private float _lookAtBodyWeight = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _lookAtHeadWeight = 0.85f;
        [SerializeField, Range(0f, 1f)] private float _lookAtEyesWeight = 0.2f;
        [SerializeField, Min(0.1f)] private float _lookAtBlendSpeed = 4f;
        [SerializeField, Range(0f, 1f)] private float _swordAimWeight = 0.85f;
        [SerializeField, Min(0.1f)] private float _swordAimBlendSpeed = 5f;
        [SerializeField] private Transform _swordTip;
        [SerializeField] private Transform _weaponAimBone;
        [SerializeField] private string _weaponAimBoneName = "DEF-WEAPON_MAIN";
        [SerializeField] private Camera _viewCamera;

        private readonly HashSet<Transform> _consideredTargets = new HashSet<Transform>();
        private Movement_V3 _movement;
        private Animator _animator;
        private Transform _playerRoot;
        private Transform _currentTarget;
      
        private Damageable _currentDamageable;
        private Vector3 _currentAimPoint;
        private float _nextTargetRefreshTime;
        private float _lookAtWeight;
        private float _swordAimWeightCurrent;
        private Transform _rightHand;
        private Transform _leftHand;
        private WeaponDamage _weaponDamage;
        private bool _guardPassAimActive;
        private CombatHandler _combatHandler;

        public bool IsTargeting { get; private set; }
        [field: SerializeField] public Transform CurrentTarget => _currentTarget;
        public Transform TargetCurrent;

        public static TargetingSystem GetOrAddForAnimator(Animator animator)
        {
            if (animator == null)
                return null;

            var targeting = animator.GetComponentInParent<TargetingSystem>();
            if (targeting == null)
                targeting = animator.gameObject.AddComponent<TargetingSystem>();

            if (targeting.gameObject != animator.gameObject &&
                animator.GetComponent<TargetingIKRelay>() == null)
            {
                animator.gameObject.AddComponent<TargetingIKRelay>();
            }

            return targeting;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);

            _movement = GetComponentInParent<Movement_V3>();
            if (_movement == null)
                _movement = GetComponentInChildren<Movement_V3>(true);

            _playerRoot = _movement != null ? _movement.transform : transform.root;

            if (_animator != null && _animator.isHuman)
            {
                _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
            if (_playerRoot != null)
            {
                if (_swordTip == null)
                    _swordTip = FindChildByName(_playerRoot, "Sword Tip");
                if (_weaponAimBone == null && !string.IsNullOrWhiteSpace(_weaponAimBoneName))
                    _weaponAimBone = FindChildByName(_playerRoot, _weaponAimBoneName);
            }
            // if (_swordTip == null && _playerRoot != null)
                _weaponDamage = _playerRoot.GetComponentInChildren<WeaponDamage>(true);

            if (_viewCamera == null)
                _viewCamera = GetComponentInChildren<Camera>(true);
            if (_viewCamera == null)
                _viewCamera = Camera.main;

            _combatHandler = GetComponentInParent<CombatHandler>();
        }

        private void OnEnable()
        {
            if (_combatHandler == null)
                _combatHandler = GetComponentInParent<CombatHandler>();
            if (_combatHandler != null)
                _combatHandler.PhaseChanged += HandlePassPhaseChanged;
        }

        private void Update()
        {
            if (!IsTargeting)
            {
                _lookAtWeight = Mathf.MoveTowards(_lookAtWeight, 0f, _lookAtBlendSpeed * Time.deltaTime);
                _swordAimWeightCurrent = Mathf.MoveTowards(_swordAimWeightCurrent, 0f, _swordAimBlendSpeed * Time.deltaTime);
                return;
            }

            if (Time.time >= _nextTargetRefreshTime)
            {
                _nextTargetRefreshTime = Time.time + _targetRefreshInterval;
                if (!IsCurrentTargetValid())
                    AcquireTarget();
            }

            _lookAtWeight = Mathf.MoveTowards(
                _lookAtWeight,
                _currentTarget ? 1f : 0f,
                _lookAtBlendSpeed * Time.deltaTime);
            _swordAimWeightCurrent = Mathf.MoveTowards(
                _swordAimWeightCurrent,
                _currentTarget ? _swordAimWeight : 0f,
                _swordAimBlendSpeed * Time.deltaTime);

            if(_currentTarget) TargetCurrent = _currentTarget;
        }

        private void OnDisable()
        {
            if (_combatHandler != null)
                _combatHandler.PhaseChanged -= HandlePassPhaseChanged;
            IsTargeting = false;
            _guardPassAimActive = false;
            ClearTarget();
            _lookAtWeight = 0f;
            _swordAimWeightCurrent = 0f;
            _movement?.SetTargeting(false, null);
        }

        public void ToggleTargeting(InputAction.CallbackContext context)
        {
            if (!context.started)
                return;

            IsTargeting = !IsTargeting;
            if (IsTargeting)
            {
                _nextTargetRefreshTime = 0f;
                AcquireTarget();
            }
            else
            {
                ClearTarget();
            }

            _movement?.SetTargeting(IsTargeting, _currentTarget);
        }

        public void StopTargeting()
        {
            IsTargeting = false;
            _guardPassAimActive = false;
            ClearTarget();
            _lookAtWeight = 0f;
            _swordAimWeightCurrent = 0f;
            _movement?.SetTargeting(false, null);
        }

        private void AcquireTarget()
        {
            ClearTarget();
            _consideredTargets.Clear();

            var center = _playerRoot.position + Vector3.up * 1.2f;
            var colliders = Physics.OverlapSphere(
                center,
                _acquisitionRange,
                _enemyLayers,
                QueryTriggerInteraction.Ignore);

            Transform bestTarget = null;
            Damageable bestDamageable = null;
            Vector3 bestAimPoint = default;
            var bestScore = float.PositiveInfinity;

            foreach (var targetCollider in colliders)
            {
                var damageable = targetCollider.GetComponentInParent<Damageable>();
                var target = damageable != null
                    ? damageable.transform
                    : targetCollider.attachedRigidbody != null
                        ? targetCollider.attachedRigidbody.transform
                        : targetCollider.transform.root;

                ConsiderTarget(target, damageable, center, ref bestTarget, ref bestDamageable, ref bestAimPoint, ref bestScore);
            }

            // Damageable props such as the training dummy can be targetable without using the Enemy layer.
            var damageableTargets = FindObjectsByType<Damageable>(FindObjectsInactive.Exclude);
            foreach (var damageable in damageableTargets)
            {
                if (damageable && !damageable.IsDead)
                    ConsiderTarget(damageable.transform, damageable, center, ref bestTarget, ref bestDamageable, ref bestAimPoint, ref bestScore);
            }

            _currentTarget = bestTarget;
            _currentDamageable = bestDamageable;
            _currentAimPoint = bestAimPoint;
            _movement?.SetTargeting(IsTargeting, _currentTarget);
        }

        private void ConsiderTarget(
            Transform target,
            Damageable damageable,
            Vector3 origin,
            ref Transform bestTarget,
            ref Damageable bestDamageable,
            ref Vector3 bestAimPoint,
            ref float bestScore)
        {
            if (!target || target == _playerRoot || target.IsChildOf(_playerRoot) ||
                !_consideredTargets.Add(target) || (damageable != null && damageable.IsDead))
            {
                return;
            }

            var aimPoint = GetAimPoint(target);
            var offset = aimPoint - origin;
            var distance = offset.magnitude;
            if (distance <= 0.01f || distance > _acquisitionRange)
                return;

            var viewForward = _viewCamera ? _viewCamera.transform.forward : _playerRoot.forward;
            var angle = Vector3.Angle(viewForward, offset);
            if (angle > _acquisitionAngle * 0.5f)
                return;

            // Prefer a target close to the view center, with a small bias toward nearer targets.
            var score = angle + distance / _acquisitionRange * 15f;
            if (score >= bestScore)
                return;

            bestScore = score;
            bestTarget = target;
            bestDamageable = damageable;
            bestAimPoint = aimPoint;
        }

        private static Vector3 GetAimPoint(Transform target)
        {
            
            
            var colliders = target.GetComponentsInChildren<Collider>(true);
            var hasBounds = false;
            var bounds = default(Bounds);

            foreach (var targetCollider in colliders)
            {
                if (targetCollider == null || !targetCollider.enabled || targetCollider.isTrigger)
                    continue;

                if (hasBounds)
                    bounds.Encapsulate(targetCollider.bounds);
                else
                {
                    bounds = targetCollider.bounds;
                    hasBounds = true;
                }
            }

            return hasBounds ? bounds.center : target.position + Vector3.up * 1.2f;
        }

        private bool IsCurrentTargetValid()
        {
            if (!_currentTarget)
                return false;

            if (_currentDamageable && _currentDamageable.IsDead)
                return false;

            return Vector3.Distance(_playerRoot.position, _currentTarget.position) <= _acquisitionRange;
        }

        private void ClearTarget()
        {
            _currentTarget = null;
            _currentDamageable = null;
            _currentAimPoint = default;
        }

        private void HandlePassPhaseChanged(PassPhase phase)
        {
            _guardPassAimActive = phase == PassPhase.Strike;
        }

        // The Passes layer has IK Pass enabled so the pose correction is applied after its animation.
        private void OnAnimatorIK(int layerIndex)
        {
            ProcessAnimatorIK(layerIndex);
        }

        internal void ProcessAnimatorIK(int layerIndex)
        {
            if (_animator == null)
                return;

            var passesLayer = _animator.GetLayerIndex("Passes");
            if (passesLayer < 0 || layerIndex != passesLayer)
                return;

            if (!_guardPassAimActive || !IsTargeting || _lookAtWeight <= 0f || _currentTarget == null)
            {
                ClearIKWeights();
                return;
            }

            _animator.SetLookAtWeight(
                _lookAtWeight,
                _lookAtBodyWeight,
                _lookAtHeadWeight,
                _lookAtEyesWeight,
                0.5f);
            _currentAimPoint = GetAimPoint(_currentTarget);
            _animator.SetLookAtPosition(_currentAimPoint);

            ApplySwordAim();
        }

        private void ApplySwordAim()
        {
            if (_swordAimWeightCurrent <= 0f || _rightHand == null || !_rightHand ||
                _currentTarget == null || !_animator.isHuman)
            {
                ClearHandIKWeights();
                return;
            }

            var handPosition = _rightHand.position;
            var handRotation = _rightHand.rotation;
            var canAimWeaponBone = _weaponAimBone != null;
            var leftHandLocalPosition = canAimWeaponBone && _leftHand != null
                ? _weaponAimBone.InverseTransformPoint(_leftHand.position)
                : Vector3.zero;
            var leftHandLocalRotation = canAimWeaponBone && _leftHand != null
                ? Quaternion.Inverse(_weaponAimBone.rotation) * _leftHand.rotation
                : Quaternion.identity;
            if (!TryGetSwordTip(handPosition, out var tipPosition))
            {
                // Prefer the weapon bone's forward axis if no blade marker or collider is available.
                var fallbackRotation = canAimWeaponBone ? _weaponAimBone.rotation : handRotation;
                tipPosition = handPosition + fallbackRotation * Vector3.forward;
            }

            var bladeDirection = tipPosition - handPosition;
            var targetDirection = _currentAimPoint - handPosition;
            if (bladeDirection.sqrMagnitude < 0.0001f || targetDirection.sqrMagnitude < 0.0001f)
            {
                ClearHandIKWeights();
                return;
            }

            var aimDelta = Quaternion.FromToRotation(bladeDirection, targetDirection);
            var weightedAimDelta = Quaternion.Slerp(Quaternion.identity, aimDelta, _swordAimWeightCurrent);

            // DEF-WEAPON_MAIN is an extra (non-Humanoid) transform, so steer it directly as well as
            // asking the Humanoid solver to bring the right hand along with the authored grip.
            if (_weaponAimBone != null)
            {
                var weaponDirection = tipPosition - _weaponAimBone.position;
                var weaponTargetDirection = _currentAimPoint - _weaponAimBone.position;
                if (weaponDirection.sqrMagnitude > 0.0001f && weaponTargetDirection.sqrMagnitude > 0.0001f)
                {
                    var weaponDelta = Quaternion.FromToRotation(weaponDirection, weaponTargetDirection);
                    var weightedWeaponDelta = Quaternion.Slerp(Quaternion.identity, weaponDelta, _swordAimWeightCurrent);
                    _weaponAimBone.rotation = weightedWeaponDelta * _weaponAimBone.rotation;

                    // Carry the authored off-hand grip point with the weapon bone, then let Humanoid IK
                    // pull the left arm back to it after the weapon has been steered.
                    if (_leftHand != null)
                    {
                        _animator.SetIKPosition(AvatarIKGoal.LeftHand, _weaponAimBone.TransformPoint(leftHandLocalPosition));
                        _animator.SetIKRotation(AvatarIKGoal.LeftHand, _weaponAimBone.rotation * leftHandLocalRotation);
                        _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _swordAimWeightCurrent);
                        _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, _swordAimWeightCurrent);
                    }
                }
                else
                {
                    _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                    _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                }
            }
            else
            {
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            }

            _animator.SetIKPosition(AvatarIKGoal.RightHand, handPosition);
            _animator.SetIKRotation(AvatarIKGoal.RightHand, weightedAimDelta * handRotation);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _swordAimWeightCurrent);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _swordAimWeightCurrent);
        }

        private void ClearIKWeights()
        {
            _animator.SetLookAtWeight(0f);
            ClearHandIKWeights();
        }

        private void ClearHandIKWeights()
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root.name == childName)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChildByName(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private bool TryGetSwordTip(Vector3 handPosition, out Vector3 tipPosition)
        {
            if (_swordTip != null)
            {
                tipPosition = _swordTip.position;
                return true;
            }

            if (_weaponDamage == null || !_weaponDamage.TryGetComponent<BoxCollider>(out var box))
            {
                tipPosition = default;
                return false;
            }

            // The weapon's box collider follows the blade. Pick the end furthest from the hand as the tip.
            var size = box.size;
            var axis = size.x >= size.y && size.x >= size.z ? Vector3.right :
                size.y >= size.z ? Vector3.up : Vector3.forward;
            var halfLength = Vector3.Scale(size, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))).magnitude * 0.5f;
            var localCenter = box.center;
            var endA = box.transform.TransformPoint(localCenter - axis * halfLength);
            var endB = box.transform.TransformPoint(localCenter + axis * halfLength);
            tipPosition = (endA - handPosition).sqrMagnitude > (endB - handPosition).sqrMagnitude ? endA : endB;
            return true;
        }
    }
}

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

        public bool IsTargeting { get; private set; }
        public Transform CurrentTarget => _currentTarget;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);

            _movement = GetComponentInParent<Movement_V3>();
            if (_movement == null)
                _movement = GetComponentInChildren<Movement_V3>(true);

            _playerRoot = _movement != null ? _movement.transform : transform.root;

            if (_viewCamera == null)
                _viewCamera = GetComponentInChildren<Camera>(true);
            if (_viewCamera == null)
                _viewCamera = Camera.main;
        }

        private void Update()
        {
            if (!IsTargeting)
            {
                _lookAtWeight = Mathf.MoveTowards(_lookAtWeight, 0f, _lookAtBlendSpeed * Time.deltaTime);
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
        }

        private void OnDisable()
        {
            IsTargeting = false;
            ClearTarget();
            _lookAtWeight = 0f;
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
            ClearTarget();
            _lookAtWeight = 0f;
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
            if (target == null || target == _playerRoot || target.IsChildOf(_playerRoot) ||
                !_consideredTargets.Add(target) || (damageable != null && damageable.IsDead))
            {
                return;
            }

            var aimPoint = GetAimPoint(target);
            var offset = aimPoint - origin;
            var distance = offset.magnitude;
            if (distance <= 0.01f || distance > _acquisitionRange)
                return;

            var viewForward = _viewCamera != null ? _viewCamera.transform.forward : _playerRoot.forward;
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

        // Unity invokes this callback on the GameObject that owns the Animator.
        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || _animator == null)
                return;

            if (_lookAtWeight <= 0f || _currentTarget == null)
            {
                _animator.SetLookAtWeight(0f);
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
        }
    }
}

using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace Player
{
    /// <summary>
    /// What a character wants its legs to do this frame, independent of where that intent came from.
    /// The player fills it from the input asset; an enemy fills it from duel steering. Because both feed
    /// the same struct into the same driver, an enemy walks with exactly the player's locomotion rules.
    /// </summary>
    public readonly struct LocomotionIntent
    {
        public LocomotionIntent(Vector2 direction, float moveAmount, bool isMoving, bool targetLocked)
        {
            Direction = direction;
            MoveAmount = moveAmount;
            IsMoving = isMoving;
            TargetLocked = targetLocked;
        }

        public Vector2 Direction { get; }

        /// <summary>0..1 throttle, derived from the raw input magnitude rather than the normalised direction.</summary>
        public float MoveAmount { get; }

        public bool IsMoving { get; }

        /// <summary>Whether facing should follow a target instead of the movement direction.</summary>
        public bool TargetLocked { get; }

        public static LocomotionIntent Idle => new LocomotionIntent(Vector2.zero, 0f, false, false);

        /// <summary>Builds an intent from an analogue stick or a steering vector.</summary>
        public static LocomotionIntent FromDirection(Vector2 direction, bool targetLocked)
        {
            var magnitude = Mathf.Clamp01(Mathf.Abs(direction.x) + Mathf.Abs(direction.y));
            return new LocomotionIntent(direction, magnitude, direction.sqrMagnitude > 0.0001f, targetLocked);
        }

        public LocomotionIntent WithTargetLock(bool targetLocked)
        {
            return new LocomotionIntent(Direction, MoveAmount, IsMoving, targetLocked);
        }
    }

    /// <summary>
    /// Chooses locomotion Animator states from a <see cref="LocomotionIntent"/>.
    ///
    /// Extracted from the character controller so the state-hash comparison, the target-lock variants and
    /// the "another system changed layer 0" reconciliation live in one place instead of being tangled
    /// with physics.
    /// </summary>
    public sealed class CharacterAnimatorDriver
    {
        private static readonly int InGuardWalkForwardHash = Animator.StringToHash("InGuard-WalkForward");
        private static readonly int InGuardStrafeLeftHash = Animator.StringToHash("InGuard-Strafe-Left");
        private static readonly int InGuardStrafeRightHash = Animator.StringToHash("InGuard-Strafe-Right");
        private static readonly int InGuardWalkBackwardHash = Animator.StringToHash("InGuard-WalkBackward");

        private const int BaseLayer = 0;

        private readonly Animator _animator;
        private readonly int _idleHash = Animator.StringToHash("Idle");
        private readonly int _walkHash = Animator.StringToHash("Walk");
        private readonly int _runHash = Animator.StringToHash("Run");
        private readonly int _targetingIdleHash;

        private int _currentStateHash;

        public CharacterAnimatorDriver(
            Animator animator,
            string targetingIdleStateName,
            float crossFadeDuration,
            float walkThreshold,
            float runThreshold)
        {
            _animator = animator;
            _targetingIdleHash = Animator.StringToHash(targetingIdleStateName);
            CrossFadeDuration = crossFadeDuration;
            WalkThreshold = walkThreshold;
            RunThreshold = runThreshold;
        }

        public float CrossFadeDuration { get; set; }

        public float WalkThreshold { get; set; }

        public float RunThreshold { get; set; }

        public int CurrentStateHash => _currentStateHash;

        /// <summary>Starts the character in Idle without a blend.</summary>
        public void PlayInitialIdle()
        {
            _currentStateHash = _idleHash;
            _animator.CrossFade(_idleHash, 0f, BaseLayer);
        }

        public void Play(in LocomotionIntent intent)
        {
            var targetHash = ResolveState(intent);

            // Bail out while a transition is already heading where we want to go. Re-issuing the
            // CrossFade on top of an in-flight one produces a visible pop for no benefit.
            if (_animator.IsInTransition(BaseLayer) &&
                _animator.GetNextAnimatorStateInfo(BaseLayer).shortNameHash == targetHash)
            {
                _currentStateHash = targetHash;
                return;
            }

            var currentState = _animator.GetCurrentAnimatorStateInfo(BaseLayer);

            // Also detect states changed by another system, so a one-off pose is not immediately undone.
            if (targetHash != _currentStateHash ||
                (!_animator.IsInTransition(BaseLayer) && currentState.shortNameHash != targetHash))
            {
                _animator.CrossFade(targetHash, CrossFadeDuration, BaseLayer);
                _currentStateHash = targetHash;
            }
        }

        private int ResolveState(in LocomotionIntent intent)
        {
            if (intent.TargetLocked)
                return ResolveTargetLockedState(intent);

            if (!intent.IsMoving || intent.MoveAmount < WalkThreshold)
                return _idleHash;

            return intent.MoveAmount < RunThreshold ? _walkHash : _runHash;
        }

        private int ResolveTargetLockedState(in LocomotionIntent intent)
        {
            const float inputThreshold = 0.1f;

            if (!intent.IsMoving || intent.MoveAmount < inputThreshold)
                return _animator.HasState(BaseLayer, _targetingIdleHash) ? _targetingIdleHash : _idleHash;

            var direction = intent.Direction;

            if (direction.x < -inputThreshold && Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return InGuardStrafeLeftHash;

            if (direction.x > inputThreshold && Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return InGuardStrafeRightHash;

            // Fixed a long-standing inversion: a stick within the deadzone on the forward axis used to
            // fall through to the backward clip. Only genuinely backward input uses it now.
            if (direction.y < -inputThreshold)
                return InGuardWalkBackwardHash;

            return InGuardWalkForwardHash;
        }
    }
}

using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Decides whether a character is in combat, from nothing but the distance to the nearest hostile.
    ///
    /// The design doc (Stance.md) wants combat to start when an enemy comes into relevant range rather
    /// than when the player presses a "draw the sword" button, and it wants leaving combat to be just as
    /// automatic. Two radii rather than one is what makes that bearable: entering at the edge would
    /// otherwise flicker the guard on and off every time a fighter stepped over the line.
    ///
    /// It is deliberately engine-free and clock-free - the caller supplies delta time - so the hysteresis
    /// can be tested directly instead of by driving a MonoBehaviour and watching an Animator.
    /// </summary>
    public sealed class CombatAwareness
    {
        /// <summary>Distance at which combat starts when nothing else is specified.</summary>
        public const float DefaultEngageRadius = 15f;

        /// <summary>Distance that has to be exceeded for combat to end, by default.</summary>
        public const float DefaultDisengageRadius = 20f;

        /// <summary>Seconds beyond the disengage radius before combat ends, by default.</summary>
        public const float DefaultDisengageDelay = 2.5f;

        private float _engageRadius;
        private float _disengageRadius;
        private float _disengageDelay;
        private float _timeOutOfRange;

        public CombatAwareness(
            float engageRadius = DefaultEngageRadius,
            float disengageRadius = DefaultDisengageRadius,
            float disengageDelay = DefaultDisengageDelay)
        {
            Retune(engageRadius, disengageRadius, disengageDelay);
        }

        /// <summary>True once a hostile has come within the engage radius.</summary>
        public bool IsEngaged { get; private set; }

        /// <summary>Distance at which combat starts.</summary>
        public float EngageRadius => _engageRadius;

        /// <summary>Distance that has to be exceeded before combat can end. Never below the engage radius.</summary>
        public float DisengageRadius => _disengageRadius;

        /// <summary>How long the nearest hostile must stay beyond the disengage radius.</summary>
        public float DisengageDelay => _disengageDelay;

        /// <summary>How long the character has already been beyond the disengage radius.</summary>
        public float TimeOutOfRange => _timeOutOfRange;

        /// <summary>
        /// Changes the radii without resetting the current state, so a live tuning change does not make a
        /// fighting character suddenly sheathe its sword. A disengage radius below the engage radius is
        /// raised to match: the alternative is a band where combat both should and should not be running.
        /// </summary>
        public void Retune(float engageRadius, float disengageRadius, float disengageDelay)
        {
            _engageRadius = Math.Max(0f, engageRadius);
            _disengageRadius = Math.Max(_engageRadius, disengageRadius);
            _disengageDelay = Math.Max(0f, disengageDelay);
        }

        /// <summary>
        /// Advances the state. <paramref name="nearestHostileDistance"/> should be
        /// <see cref="float.PositiveInfinity"/> when there is nobody to fight.
        /// </summary>
        /// <returns>True when the state flipped, so the caller only reacts to real transitions.</returns>
        public bool Tick(float nearestHostileDistance, float deltaTime)
        {
            if (float.IsNaN(nearestHostileDistance))
                nearestHostileDistance = float.PositiveInfinity;

            if (!IsEngaged)
            {
                if (nearestHostileDistance > _engageRadius)
                    return false;

                IsEngaged = true;
                _timeOutOfRange = 0f;
                return true;
            }

            if (nearestHostileDistance <= _disengageRadius)
            {
                // Still inside the band: the countdown restarts from zero every frame we are back in.
                _timeOutOfRange = 0f;
                return false;
            }

            if (deltaTime <= 0f)
                return false;

            _timeOutOfRange += deltaTime;
            if (_timeOutOfRange < _disengageDelay)
                return false;

            IsEngaged = false;
            _timeOutOfRange = 0f;
            return true;
        }

        /// <summary>Forces combat to start. Used when the player deliberately engages.</summary>
        /// <returns>True when the state flipped.</returns>
        public bool Engage()
        {
            if (IsEngaged)
                return false;

            IsEngaged = true;
            _timeOutOfRange = 0f;
            return true;
        }

        /// <summary>Forces combat to end, for example on death or a scene change.</summary>
        /// <returns>True when the state flipped.</returns>
        public bool Disengage()
        {
            if (!IsEngaged)
                return false;

            IsEngaged = false;
            _timeOutOfRange = 0f;
            return true;
        }
    }
}

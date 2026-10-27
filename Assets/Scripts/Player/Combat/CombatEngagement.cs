using System;
using System.Collections.Generic;
using Spellbreakers.Combat;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Puts the player into and out of combat automatically, from the distance to the nearest hostile.
    ///
    /// Before this existed the player was permanently in a guard: the Guards layer sat at weight one
    /// from the moment the scene loaded, so exploration ran on the in-guard locomotion states and five
    /// gamepad buttons were spent on a stance nobody had chosen. Combat is now a state the world puts
    /// you in, and the guard buttons only mean something while it lasts.
    ///
    /// The decision itself lives in <see cref="CombatAwareness"/> - engine-free and unit-tested. This
    /// component only supplies the distance, applies the result to <see cref="CombatHandler"/> and
    /// publishes it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatEngagement : MonoBehaviour
    {
        
        [Header("Engagement")]
        [Tooltip("A hostile inside this radius starts combat. Deliberately a little inside the enemies' " +
                 "own sight range, so the player is armed well before anyone is close enough to swing.")]
        [SerializeField, Min(0f)] private float _engageRadius = 18f;

        [Tooltip("Combat only ends once the nearest hostile is beyond this radius. Keeping it larger " +
                 "than the engage radius is what stops a fight flickering on and off at the boundary.")]
        [SerializeField, Min(0f)] private float _disengageRadius = 26f;

        [Tooltip("Seconds the nearest hostile must stay beyond the disengage radius before combat ends.")]
        [SerializeField, Min(0f)] private float _disengageDelay = 2.5f;

        [Header("Scanning")]
        [Tooltip("How often the hostile search runs. Targeting and perception use similar intervals; a " +
                 "registry walk every frame would be wasted work.")]
        [SerializeField, Min(0.02f)] private float _scanInterval = 0.15f;

        [Header("Diagnostics")]
        [SerializeField] private bool _logCombatState;

        private readonly List<Damageable> _hostiles = new List<Damageable>();

        private CombatAwareness _awareness;
        private CombatHandler _combat;
        private Damageable _self;
        private float _nextScanTime;
        private float _nearestHostileDistance = float.PositiveInfinity;

        /// <summary>True while this character is fighting.</summary>
        public bool IsInCombat => _awareness != null && _awareness.IsEngaged;

        /// <summary>Distance to the nearest hostile, or infinity when there is none in range.</summary>
        public float NearestHostileDistance => _nearestHostileDistance;

        /// <summary>Raised after the combat state actually changed.</summary>
        public event Action<bool> CombatStateChanged;

        /// <summary>
        /// Finds the engagement controller on this character, adding one if the prefab predates it. The
        /// component is created by <see cref="InputHandler"/> rather than by an editor tool, so a player
        /// prefab needs no re-wiring and an existing scene keeps working.
        /// </summary>
        public static CombatEngagement GetOrAdd(GameObject owner)
        {
            if (owner == null)
                return null;

            var engagement = owner.GetComponent<CombatEngagement>();
            if (engagement == null)
                engagement = owner.AddComponent<CombatEngagement>();

            return engagement;
        }

        private void Awake()
        {
            _awareness = new CombatAwareness(_engageRadius, _disengageRadius, _disengageDelay);
            ResolveCombatHandler();
            _self = GetComponent<Damageable>();
        }

        private void OnEnable()
        {
            ResolveCombatHandler();
            SubscribeToDeath();
        }

        private void OnDisable()
        {
            if (_self != null)
                _self.Died -= HandleDied;
        }

        private void Start()
        {
            // Start, not Awake: the CombatHandler has to have finished building its Animator layers
            // before we ask it to hide the guard, and component Awake order is not a promise.
            _combat?.SetCombatActive(IsInCombat, instant: true);
            _nextScanTime = 0f;
        }

        private void Update()
        {
            if (_awareness == null)
                return;

            _awareness.Retune(_engageRadius, _disengageRadius, _disengageDelay);

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + _scanInterval;
                ScanForHostiles();
            }

            if (!_awareness.Tick(_nearestHostileDistance, Time.deltaTime))
                return;

            Apply(_awareness.IsEngaged);
        }

        /// <summary>
        /// Starts a fight deliberately, for the case where the player presses target while an enemy is
        /// already visible. Returns false when there is nobody close enough, so the caller can leave
        /// targeting alone rather than locking onto empty air.
        /// </summary>
        public bool TryEngage()
        {
            if (IsInCombat)
                return true;

            if (_awareness == null)
                return false;

            ScanForHostiles();
            if (_nearestHostileDistance > _awareness.EngageRadius)
                return false;

            _awareness.Engage();
            Apply(true);
            return true;
        }

        /// <summary>Ends combat unconditionally, for example on death or a respawn.</summary>
        public void ForceDisengage()
        {
            if (_awareness == null || !_awareness.Disengage())
                return;

            Apply(false);
        }

        private void Apply(bool inCombat)
        {
            ResolveCombatHandler();
            _combat?.SetCombatActive(inCombat);

            if (_logCombatState)
            {
                var nearest = float.IsPositiveInfinity(_nearestHostileDistance)
                    ? "none"
                    : _nearestHostileDistance.ToString("0.0");
                Debug.Log($"[Combat] {(inCombat ? "engaged" : "disengaged")} (nearest = {nearest})", this);
            }

            CombatStateChanged?.Invoke(inCombat);
        }

        private void ResolveCombatHandler()
        {
            if (_combat == null)
                _combat = GetComponent<CombatHandler>();

            if (_combat == null)
                _combat = GetComponentInParent<CombatHandler>();
        }

        private void SubscribeToDeath()
        {
            if (_self == null)
                return;

            _self.Died -= HandleDied;
            _self.Died += HandleDied;
        }

        /// <summary>
        /// A corpse has no guard to hold. Combat resumes by proximity once the respawn puts the player
        /// back in the world, so nothing has to be restored by hand.
        /// </summary>
        private void HandleDied(Damageable damageable)
        {
            ForceDisengage();
        }

        /// <summary>
        /// Walks the registry for the nearest hostile. Neutral props - a training dummy - are deliberately
        /// not enemies, matching <see cref="CombatantRegistry"/> and the AI's own perception.
        /// </summary>
        private void ScanForHostiles()
        {
            var team = _self != null ? _self.Team : CombatTeam.Player;
            var origin = _self != null ? _self.AimPosition : transform.position + Vector3.up * 1.2f;

            var count = CombatantRegistry.CollectHostiles(
                origin,
                team,
                _awareness.DisengageRadius,
                _hostiles);

            var nearest = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var distance = Vector3.Distance(origin, _hostiles[i].AimPosition);
                if (distance < nearest)
                    nearest = distance;
            }

            _nearestHostileDistance = nearest;
        }
    }
}

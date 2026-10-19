using Spellbreakers.Combat;
using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// One creature's combat identity beyond its guard table.
    ///
    /// The design doc asks that deflection work on the physical relationship between attack and defender
    /// "rather than on the attacker's anatomy", and expects creatures to defend differently: a wolf with
    /// nothing hard to meet a blade with has to avoid, while a creature with a heavy carapace can turn a
    /// sword aside. That difference is not an AI personality trait, so it does not belong on the
    /// archetype asset - it is a fact about the body, and it has to be readable at the moment of contact.
    ///
    /// Note there is deliberately no per-creature parry table. The earlier plan was one table per
    /// species, but the doc's direction is a continuous gradient driven by timing, angle and speed, with
    /// the shared table acting only as a prior. Duplicating that prior per species would have hardened a
    /// model the doc is moving away from.
    /// </summary>
    [CreateAssetMenu(menuName = "Spellbreakers/Combat Profile", fileName = "Profile_")]
    public sealed class CombatProfileAsset : ScriptableObject
    {
        [Header("Defence")]
        [Tooltip("Parry: can meet an attack with its own weapon or hard body and deflect it.\n" +
                 "Avoid: can leave the attack's line entirely.\n" +
                 "Both: may do either. None: cannot defend at all.")]
        [SerializeField] private DefenceMode _defenceMode = DefenceMode.Both;

        [Tooltip("Mass relative to a human swordsman. Heavier creatures absorb more force; reserved for " +
                 "the momentum model and currently informational.")]
        [SerializeField, Min(0.1f)] private float _mass = 1f;

        [Tooltip("Whether the passive Avoid action is available to this creature at all.")]
        [SerializeField] private bool _canEvade = true;

        [Tooltip("Seconds of recovery after a committed evade, so avoidance has a real cost.")]
        [SerializeField, Min(0f)] private float _evadeRecoverySeconds = 0.35f;

        public DefenceMode DefenceMode => _defenceMode;

        public float Mass => _mass;

        public bool CanEvade => _canEvade && (_defenceMode & DefenceMode.Avoid) != 0;

        public float EvadeRecoverySeconds => _evadeRecoverySeconds;

        /// <summary>Pushes this profile onto a combatant. Safe to call repeatedly.</summary>
        public void ApplyTo(CombatHandler combat)
        {
            if (combat == null || combat.Agent == null)
                return;

            combat.Agent.DefenceMode = _defenceMode;
        }

        /// <summary>Builds a transient profile at runtime, for scene building and tests.</summary>
        public static CombatProfileAsset CreateTransient(
            DefenceMode defenceMode,
            string assetName,
            float mass = 1f,
            bool canEvade = true,
            float evadeRecoverySeconds = 0.35f)
        {
            var asset = CreateInstance<CombatProfileAsset>();
            asset.name = assetName;
            asset._defenceMode = defenceMode;
            asset._mass = mass;
            asset._canEvade = canEvade;
            asset._evadeRecoverySeconds = evadeRecoverySeconds;
            return asset;
        }

        private void OnValidate()
        {
            if (_mass < 0.1f)
                _mass = 0.1f;

            // A creature that cannot avoid and cannot parry simply has no defence; that is legal but
            // almost certainly a mistake worth surfacing here rather than in playtest.
            if (_defenceMode == DefenceMode.None)
            {
                Debug.LogWarning($"[CombatProfile] '{name}' cannot parry and cannot avoid, so it has no " +
                                 "defence at all. Intended for a purely decorative or harmless target.", this);
            }
        }
    }
}

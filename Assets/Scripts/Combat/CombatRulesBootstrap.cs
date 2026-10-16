using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Applies an authored <see cref="CombatRulesAsset"/> at startup.
    ///
    /// Place one on a bootstrap object in a scene to move combat tuning out of code. With no asset
    /// assigned the code-built defaults stay in force, so a scene without a bootstrap behaves exactly
    /// as before.
    ///
    /// Applied before any other component reads the rules by using a very early execution order, because
    /// <see cref="CombatHandler"/> builds its agent in Awake.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class CombatRulesBootstrap : MonoBehaviour
    {
        [SerializeField] private CombatRulesAsset _rules;

        [Tooltip("Restore the code-built defaults when this object is disabled again. Useful in tests and " +
                 "for editor tooling that swaps rules temporarily.")]
        [SerializeField] private bool _restoreDefaultsOnDisable;

        private void Awake()
        {
            Apply();
        }

        private void OnDisable()
        {
            if (_restoreDefaultsOnDisable)
                CombatRules.ResetToDefaults();
        }

        /// <summary>Applies the assigned rules, or the defaults when none are assigned.</summary>
        public void Apply()
        {
            if (_rules == null)
                return;

            _rules.Build(out var transitions, out var parryTable);
            CombatRules.Override(transitions, parryTable);

            Debug.Log($"[CombatRules] Applied '{_rules.name}': {transitions.Count} transitions, " +
                      $"{parryTable.GuardCellCount} guard parry cells, " +
                      $"{parryTable.TransitionCellCount} transition parry cells.", this);
        }
    }
}

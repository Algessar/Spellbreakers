using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Loads the authored combat rules without anyone having to remember to wire something up.
    ///
    /// The previous design required a <see cref="CombatRulesBootstrap"/> component in the scene, and in
    /// practice nothing referenced it - so the asset existed, compiled, and silently did nothing while
    /// every system fell back to the code-built defaults. A tuning surface that is inert by default is
    /// worse than no tuning surface, because it looks like it is working.
    ///
    /// So this loads automatically from a conventional path. Missing asset is fine and quiet (the code
    /// defaults are a complete rule set); a *present* asset that is malformed is loud, because that
    /// means someone tried to configure combat and it did not take.
    /// </summary>
    public static class CombatRulesLoader
    {
        /// <summary>Conventional location: Assets/Resources/CombatRules.asset.</summary>
        public const string ResourcesPath = "CombatRules";

        private static bool _reportedMissing;

        /// <summary>
        /// Runs before the first scene loads, which is before any <see cref="CombatHandler"/> builds its
        /// agent in Awake.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoad()
        {
            // Reset the report flag so a domain reload re-checks rather than remembering a stale answer.
            _reportedMissing = false;
            ApplyFromResources();
        }

        /// <summary>Loads and applies the rules from <see cref="ResourcesPath"/>. Returns false if absent.</summary>
        public static bool ApplyFromResources()
        {
            var asset = Resources.Load<CombatRulesAsset>(ResourcesPath);

            if (asset == null)
            {
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    Debug.Log($"[CombatRules] No '{ResourcesPath}' asset in a Resources folder; using the " +
                              "code-built rules. This is a valid configuration - create the asset only " +
                              "when you want to tune combat as data.");
                }

                return false;
            }

            return Apply(asset, "Resources/" + ResourcesPath);
        }

        /// <summary>Applies a specific asset, validating it before it can replace the live rules.</summary>
        public static bool Apply(CombatRulesAsset asset, string source)
        {
            if (asset == null)
                return false;

            asset.Build(out var transitions, out var parryTable);

            if (transitions == null || transitions.Count == 0)
            {
                Debug.LogError($"[CombatRules] '{source}' produced no transitions, so it has been ignored " +
                               "and the previous rules remain in force.");
                return false;
            }

            if (parryTable == null)
            {
                Debug.LogError($"[CombatRules] '{source}' produced no parry table, so it has been ignored " +
                               "and the previous rules remain in force.");
                return false;
            }

            CombatRules.Override(transitions, parryTable);

            Debug.Log($"[CombatRules] Applied '{source}': {transitions.Count} transitions, " +
                      $"{parryTable.GuardCellCount}/{ParryTable.ExpectedGuardCells} guard parry cells, " +
                      $"{parryTable.TransitionCellCount}/{ParryTable.ExpectedTransitionCells} transition parry cells.");

            return true;
        }
    }

    /// <summary>
    /// Optional explicit override of the combat rules, for tests and for scenes that want a deliberate
    /// variant. Prefer <see cref="CombatRulesLoader"/>, which needs no wiring at all.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class CombatRulesBootstrap : MonoBehaviour
    {
        [SerializeField] private CombatRulesAsset _rules;

        [Tooltip("Restore the code-built defaults when this object is disabled, so a temporary swap does " +
                 "not leak into the next scene or the next test.")]
        [SerializeField] private bool _restoreDefaultsOnDisable;

        /// <summary>True when this object actually replaced the rules.</summary>
        public bool Applied { get; private set; }

        private void Awake()
        {
            Apply();
        }

        private void OnDisable()
        {
            if (_restoreDefaultsOnDisable && Applied)
            {
                CombatRules.ResetToDefaults();
                Applied = false;
            }
        }

        /// <summary>Applies the assigned rules. Warns rather than doing nothing when none is assigned.</summary>
        public void Apply()
        {
            if (_rules == null)
            {
                Debug.LogWarning($"[CombatRules] '{name}' has no CombatRulesAsset assigned, so it did " +
                                 "nothing. Use the auto-loaded Resources asset, or assign one here.");
                return;
            }

            Applied = CombatRulesLoader.Apply(_rules, _rules.name);
        }
    }
}

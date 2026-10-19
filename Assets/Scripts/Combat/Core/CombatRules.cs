using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The shared combat rules: which transitions exist and which defensive answer beats which attack.
    ///
    /// Everything falls back to code-built defaults, so the game runs correctly with no assets wired up
    /// at all. Authored ScriptableObject assets can replace them at startup when tuning needs to move
    /// out of code, and tests can replace them with a deliberately distorted table.
    /// </summary>
    public static class CombatRules
    {
        private static TransitionTable _transitions;
        private static ParryTable _parryTable;

        /// <summary>Every legal guard transition. Shared by the player and every enemy.</summary>
        public static TransitionTable Transitions => _transitions ?? (_transitions = TransitionTable.CreateDefault());

        /// <summary>Which defence beats which attack.</summary>
        public static ParryTable Parry => _parryTable ?? (_parryTable = ParryTable.CreateDefault());

        /// <summary>Raised when the rules are replaced, so live agents can be rebuilt.</summary>
        public static event Action RulesChanged;

        /// <summary>Replaces the rules. Passing null restores the code-built defaults.</summary>
        public static void Override(TransitionTable transitions, ParryTable parryTable)
        {
            _transitions = transitions;
            _parryTable = parryTable;
            RulesChanged?.Invoke();
        }

        /// <summary>Restores the defaults. Used by tests to leave no residue between cases.</summary>
        public static void ResetToDefaults()
        {
            _transitions = null;
            _parryTable = null;
            RulesChanged?.Invoke();
        }

        /// <summary>
        /// A fresh copy for one combatant. Archetypes change distance gates and windows, so they must
        /// never mutate the shared table the player is using.
        /// </summary>
        public static TransitionTable CreateTransitionsForCombatant()
        {
            return Transitions.Clone();
        }
    }
}

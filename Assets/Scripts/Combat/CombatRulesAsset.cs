using System;
using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Authoring surface for the combat rules.
    ///
    /// Everything still works with no asset assigned at all: an empty entry list falls back to the
    /// code-built defaults, so a fresh clone of the project runs correctly and this asset only becomes
    /// interesting once tuning needs to move out of code and into version-controlled data.
    ///
    /// Rows are explicit rather than nested dictionaries because Unity cannot serialize a dictionary,
    /// and an explicit row list is also what a designer can read and diff.
    /// </summary>
    [CreateAssetMenu(menuName = "Spellbreakers/Combat Rules", fileName = "CombatRules")]
    public sealed class CombatRulesAsset : ScriptableObject
    {
        [Serializable]
        public struct GuardProfileEntry
        {
            public GuardId Guard;

            [Tooltip("Distance at which this guard's point can reach, in metres.")]
            public float TipReach;

            [Tooltip("Seconds after arriving in this guard during which a contact parries instead of hitting.")]
            public float ParryGraceSeconds;
        }

        [Serializable]
        public struct TransitionProfileEntry
        {
            public GuardId From;
            public GuardId To;
            public TransitionDirection Direction;

            [Tooltip("State name on the Animator's Passes layer that realises this transition.")]
            public string PassStateName;

            public float Duration;
            public float KriegDuration;
            public float MinDistance;
            public float MaxDistance;

            [Tooltip("When off, distance never gates this transition. Leave off for the player.")]
            public bool EnforceDistanceGate;

            public bool FeintWhenOutOfRange;

            [Tooltip("Normalised progress at which the blade becomes live, and stops being live.")]
            public float DamageWindowStart;
            public float DamageWindowEnd;

            public float ParryWindowStart;
            public float ParryWindowEnd;
            public float Commitment;
            public float LungeDistance;

            [Tooltip("The extreme pose the attack reaches through. Langort is the general case at Zufechten.")]
            public GuardId TransitionPose;

            [Tooltip("Transition pose used in Krieg instead. None means 'same as TransitionPose'.")]
            public GuardId TransitionPoseInKrieg;

            [Tooltip("Display-only technique name, e.g. Zornhau or Stichen. Carries no mechanics.")]
            public string TechniqueName;
        }

        [Serializable]
        public struct ParryGuardCell
        {
            public GuardId DefenderGuard;
            public TransitionDirection Attack;
            public CombatOutcome Outcome;
        }

        [Serializable]
        public struct ParryTransitionCell
        {
            public TransitionDirection Defence;
            public TransitionDirection Attack;
            public CombatOutcome Outcome;
        }

        [Header("Guards")]
        [Tooltip("Leave empty to use the code-built defaults.")]
        [SerializeField] private GuardProfileEntry[] _guards = Array.Empty<GuardProfileEntry>();

        [Header("Transitions")]
        [Tooltip("Leave empty to use the code-built defaults.")]
        [SerializeField] private TransitionProfileEntry[] _transitions = Array.Empty<TransitionProfileEntry>();

        [Header("Parry relations")]
        [Tooltip("Which guard beats which attack line. Leave empty to use the code-built defaults.")]
        [SerializeField] private ParryGuardCell[] _guardParries = Array.Empty<ParryGuardCell>();

        [Tooltip("Which transition beats which attack line. Leave empty to use the code-built defaults.")]
        [SerializeField] private ParryTransitionCell[] _transitionParries = Array.Empty<ParryTransitionCell>();

        [Tooltip("Warn when a lookup has no authored cell. Completeness is checked by an EditMode test.")]
        [SerializeField] private bool _logIncompleteCells = true;

        /// <summary>Builds the runtime rules, falling back to the defaults for anything left unauthored.</summary>
        public void Build(out TransitionTable transitions, out ParryTable parryTable)
        {
            transitions = BuildTransitions();
            parryTable = BuildParryTable();
        }

        private TransitionTable BuildTransitions()
        {
            if (_transitions == null || _transitions.Length == 0)
                return TransitionTable.CreateDefault();

            var table = new TransitionTable();

            if (_guards != null)
            {
                for (var i = 0; i < _guards.Length; i++)
                {
                    var entry = _guards[i];
                    if (entry.Guard == GuardId.None)
                        continue;

                    table.AddGuard(new GuardProfile(entry.Guard, entry.TipReach, entry.ParryGraceSeconds));
                }
            }

            for (var i = 0; i < _transitions.Length; i++)
            {
                var entry = _transitions[i];
                if (entry.From == GuardId.None || entry.To == GuardId.None || entry.From == entry.To)
                {
                    Debug.LogWarning($"[CombatRules] '{name}' transition row {i} is not a valid guard change " +
                                     $"({entry.From} -> {entry.To}); skipping it.", this);
                    continue;
                }

                var profile = new TransitionProfile(
                    entry.From,
                    entry.To,
                    entry.Direction,
                    entry.Duration,
                    entry.DamageWindowStart,
                    entry.DamageWindowEnd)
                {
                    KriegDuration = entry.KriegDuration,
                    MinDistance = entry.MinDistance,
                    MaxDistance = entry.MaxDistance,
                    EnforceDistanceGate = entry.EnforceDistanceGate,
                    FeintWhenOutOfRange = entry.FeintWhenOutOfRange,
                    ParryWindowStart = entry.ParryWindowStart,
                    ParryWindowEnd = entry.ParryWindowEnd,
                    Commitment = entry.Commitment,
                    LungeDistance = entry.LungeDistance,
                };

                if (!string.IsNullOrEmpty(entry.PassStateName))
                    profile.PassStateName = entry.PassStateName;

                if (entry.TransitionPose != GuardId.None)
                    profile.TransitionPose = entry.TransitionPose;

                profile.TransitionPoseInKrieg = entry.TransitionPoseInKrieg;
                profile.TechniqueName = entry.TechniqueName;

                if (!table.Add(profile))
                {
                    Debug.LogWarning($"[CombatRules] '{name}' authors {entry.From} -> {entry.To} more than " +
                                     "once; the first row wins.", this);
                }
            }

            return table;
        }

        private ParryTable BuildParryTable()
        {
            var guardCells = _guardParries != null ? _guardParries.Length : 0;
            var transitionCells = _transitionParries != null ? _transitionParries.Length : 0;

            if (guardCells == 0 && transitionCells == 0)
                return ParryTable.CreateDefault();

            var table = new ParryTable();

            for (var i = 0; i < guardCells; i++)
            {
                var cell = _guardParries[i];
                table.SetGuardCell(cell.DefenderGuard, cell.Attack, cell.Outcome);
            }

            for (var i = 0; i < transitionCells; i++)
            {
                var cell = _transitionParries[i];
                table.SetTransitionCell(cell.Defence, cell.Attack, cell.Outcome);
            }

            if (_logIncompleteCells)
                WarnAboutIncompleteCells(table);

            return table;
        }

        /// <summary>
        /// A partial table is legitimate while tuning, but an unfilled cell silently reads as a hit, so it
        /// is worth naming the gaps rather than discovering them in play.
        /// </summary>
        private void WarnAboutIncompleteCells(ParryTable table)
        {
            var missing = 0;
            string firstMissing = null;

            foreach (var cell in ParryTable.EnumerateGuardCells())
            {
                if (table.ResolveGuard(cell.Guard, cell.Attack) != CombatOutcome.Undefined)
                    continue;

                missing++;
                firstMissing ??= $"{cell.Guard} vs {cell.Attack}";
            }

            foreach (var cell in ParryTable.EnumerateTransitionCells())
            {
                if (table.ResolveTransition(cell.Defence, cell.Attack) != CombatOutcome.Undefined)
                    continue;

                missing++;
                firstMissing ??= $"{cell.Defence} vs {cell.Attack}";
            }

            if (missing > 0)
            {
                Debug.LogWarning(
                    $"[CombatRules] '{name}' leaves {missing} parry cell(s) unauthored (first: {firstMissing}). " +
                    "An unfilled cell resolves as a hit.", this);
            }
        }

        private void OnValidate()
        {
            if (_guards != null)
            {
                for (var i = 0; i < _guards.Length; i++)
                {
                    if (_guards[i].TipReach <= 0f)
                        _guards[i].TipReach = GuardProfile.DefaultTipReach;
                }
            }

            if (_transitions == null)
                return;

            for (var i = 0; i < _transitions.Length; i++)
            {
                var entry = _transitions[i];

                if (entry.Duration < 0.01f)
                    entry.Duration = 0.01f;

                if (entry.KriegDuration <= 0f || entry.KriegDuration > entry.Duration)
                    entry.KriegDuration = entry.Duration * 0.8f;

                if (entry.MaxDistance < entry.MinDistance)
                    entry.MaxDistance = entry.MinDistance;

                // A damage window that ends before it starts means "no live blade", which is how a
                // recovery is expressed. Anything else is a typo worth normalising.
                if (entry.DamageWindowEnd < entry.DamageWindowStart)
                    entry.DamageWindowEnd = entry.DamageWindowStart;

                _transitions[i] = entry;
            }
        }
    }
}

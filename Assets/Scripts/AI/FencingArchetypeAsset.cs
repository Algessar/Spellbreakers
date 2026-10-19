using System;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;
using UnityEngine;

namespace Spellbreakers.Ai
{
    /// <summary>
    /// ScriptableObject wrapper so an enemy's fighting personality can be authored and diffed as an
    /// asset instead of living in code. Difficulty is tuned here, not by inflating health.
    /// </summary>
    [CreateAssetMenu(menuName = "Spellbreakers/Fencing Archetype", fileName = "Archetype_")]
    public sealed class FencingArchetypeAsset : ScriptableObject
    {
        [Header("Tempo")]
        [Tooltip("Delay before this enemy answers a change in what the opponent is doing.")]
        [SerializeField, Min(0f)] private float _reactionSeconds = 0.28f;

        [Tooltip("How often the brain re-evaluates its options.")]
        [SerializeField, Min(0.02f)] private float _decisionInterval = 0.1f;

        [SerializeField, Range(0f, 1f)] private float _aggression = 0.55f;

        [Header("Measure")]
        [SerializeField, Min(0.2f)] private float _stepDistance = 0.6f;
        [SerializeField, Min(0.2f)] private float _preferredMinDistance = 1.1f;
        [SerializeField, Min(0.2f)] private float _preferredMaxDistance = 2.1f;
        [SerializeField, Range(0f, 1f)] private float _measureDiscipline = 0.65f;

        [Header("Character")]
        [SerializeField, Range(0f, 1f)] private float _jitter = 0.1f;
        [SerializeField] private int _randomSeed = 202;
        [SerializeField, Range(0f, 1f)] private float _woundedHealthFraction = 0.3f;

        [Header("Reactions")]
        [SerializeField, Min(0f)] private float _hitFlinchSeconds = 0.28f;
        [SerializeField, Min(0f)] private float _staggerSeconds = 0.55f;
        [SerializeField, Min(0f)] private float _guardBreakSeconds = 0.9f;

        [Header("Consideration weights")]
        [SerializeField] private float _measureFitWeight = 1f;
        [SerializeField] private float _guardMatchupWeight = 1.2f;
        [SerializeField] private float _openingReadWeight = 1f;
        [SerializeField] private float _initiativeWeight = 0.7f;
        [SerializeField] private float _commitRiskWeight = 0.9f;
        [SerializeField] private float _spacingWeight = 0.8f;
        [SerializeField] private float _predictabilityWeight = 0.5f;
        [SerializeField] private float _baselineWeight = 0.25f;

        [Header("Guard preferences")]
        [Tooltip("Guards this enemy will transition into. Empty means every guard in the table.")]
        [SerializeField] private GuardId[] _allowedTargetGuards =
        {
            GuardId.VomTag, GuardId.Pflug, GuardId.Alber, GuardId.Ochs, GuardId.Langort,
        };

        /// <summary>A fresh, independent archetype. Assets must never be mutated by a running agent.</summary>
        public FencingArchetype Build()
        {
            return new FencingArchetype
            {
                Name = name,
                ReactionSeconds = _reactionSeconds,
                DecisionInterval = _decisionInterval,
                Aggression = _aggression,
                StepDistance = _stepDistance,
                PreferredMinDistance = Mathf.Min(_preferredMinDistance, _preferredMaxDistance),
                PreferredMaxDistance = Mathf.Max(_preferredMinDistance, _preferredMaxDistance),
                MeasureDiscipline = _measureDiscipline,
                Jitter = _jitter,
                RandomSeed = _randomSeed,
                WoundedHealthFraction = _woundedHealthFraction,
                HitFlinchSeconds = _hitFlinchSeconds,
                StaggerSeconds = _staggerSeconds,
                GuardBreakSeconds = _guardBreakSeconds,
                MeasureFitWeight = _measureFitWeight,
                GuardMatchupWeight = _guardMatchupWeight,
                OpeningReadWeight = _openingReadWeight,
                InitiativeWeight = _initiativeWeight,
                CommitRiskWeight = _commitRiskWeight,
                SpacingWeight = _spacingWeight,
                PredictabilityWeight = _predictabilityWeight,
                BaselineWeight = _baselineWeight,
                AllowedTargetGuards = _allowedTargetGuards == null || _allowedTargetGuards.Length == 0
                    ? null
                    : (GuardId[])_allowedTargetGuards.Clone(),
            };
        }

        /// <summary>Creates an asset at runtime, for tests and for the scene builder.</summary>
        public static FencingArchetypeAsset CreateTransient(FencingArchetype archetype, string assetName)
        {
            var asset = CreateInstance<FencingArchetypeAsset>();
            asset.name = assetName;
            asset._reactionSeconds = archetype.ReactionSeconds;
            asset._decisionInterval = archetype.DecisionInterval;
            asset._aggression = archetype.Aggression;
            asset._stepDistance = archetype.StepDistance;
            asset._preferredMinDistance = archetype.PreferredMinDistance;
            asset._preferredMaxDistance = archetype.PreferredMaxDistance;
            asset._measureDiscipline = archetype.MeasureDiscipline;
            asset._jitter = archetype.Jitter;
            asset._randomSeed = archetype.RandomSeed;
            asset._woundedHealthFraction = archetype.WoundedHealthFraction;
            asset._hitFlinchSeconds = archetype.HitFlinchSeconds;
            asset._staggerSeconds = archetype.StaggerSeconds;
            asset._guardBreakSeconds = archetype.GuardBreakSeconds;
            asset._measureFitWeight = archetype.MeasureFitWeight;
            asset._guardMatchupWeight = archetype.GuardMatchupWeight;
            asset._openingReadWeight = archetype.OpeningReadWeight;
            asset._initiativeWeight = archetype.InitiativeWeight;
            asset._commitRiskWeight = archetype.CommitRiskWeight;
            asset._spacingWeight = archetype.SpacingWeight;
            asset._predictabilityWeight = archetype.PredictabilityWeight;
            asset._baselineWeight = archetype.BaselineWeight;
            asset._allowedTargetGuards = archetype.AllowedTargetGuards;
            return asset;
        }

        private void OnValidate()
        {
            if (_preferredMaxDistance < _preferredMinDistance)
                _preferredMaxDistance = _preferredMinDistance;
        }
    }
}

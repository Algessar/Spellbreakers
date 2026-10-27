using System;
using System.Collections.Generic;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// One element's tree: its talents, in tier then column order, plus the rule for when a tier unlocks.
    ///
    /// Like <see cref="SpiritTalent"/> this is plain data. The tree is validated when it is built, so a
    /// typo in a prerequisite id fails loudly at construction rather than quietly producing a node that
    /// can never be reached.
    /// </summary>
    public sealed class SpiritTree
    {
        /// <summary>
        /// Points that must already be spent in this element before the next tier opens, following the
        /// Classic WoW convention the brief asked to start from. Tier 1 is always open; tier 2 needs five
        /// points in the tree, tier 3 ten, and so on.
        /// </summary>
        public const int PointsPerTier = 5;

        private readonly Dictionary<string, SpiritTalent> _byId;

        public SpiritTree(SpiritElement element, IReadOnlyList<SpiritTalent> talents)
        {
            if (talents == null)
                throw new ArgumentNullException(nameof(talents));

            if (talents.Count == 0)
                throw new ArgumentException($"The {SpiritElements.ToDisplayName(element)} tree has no talents.", nameof(talents));

            Element = element;
            _byId = new Dictionary<string, SpiritTalent>(talents.Count, StringComparer.Ordinal);

            var ordered = new List<SpiritTalent>(talents);
            ordered.Sort(CompareForLayout);

            var maxTier = 0;
            foreach (var talent in ordered)
            {
                if (talent == null)
                    throw new ArgumentException($"The {SpiritElements.ToDisplayName(element)} tree contains a null talent.", nameof(talents));

                if (talent.Element != element)
                {
                    throw new ArgumentException(
                        $"Talent '{talent.Id}' is a {SpiritElements.ToDisplayName(talent.Element)} talent " +
                        $"but sits in the {SpiritElements.ToDisplayName(element)} tree.", nameof(talents));
                }

                if (_byId.ContainsKey(talent.Id))
                    throw new ArgumentException($"The {SpiritElements.ToDisplayName(element)} tree has two talents with id '{talent.Id}'.", nameof(talents));

                _byId.Add(talent.Id, talent);
                if (talent.Tier > maxTier)
                    maxTier = talent.Tier;
            }

            Talents = ordered;
            MaxTier = maxTier;

            foreach (var talent in ordered)
            {                if (!talent.HasPrerequisite)
                    continue;

                if (!_byId.TryGetValue(talent.RequiresId, out var prerequisite))
                {
                    throw new ArgumentException(
                        $"Talent '{talent.Id}' requires '{talent.RequiresId}', which is not in the " +
                        $"{SpiritElements.ToDisplayName(element)} tree.", nameof(talents));
                }

                if (prerequisite.Tier > talent.Tier)
                {
                    throw new ArgumentException(
                        $"Talent '{talent.Id}' on tier {talent.Tier} requires '{prerequisite.Id}' on tier " +
                        $"{prerequisite.Tier}; a prerequisite can never sit below its dependant.", nameof(talents));
                }
            }
        }

        public SpiritElement Element { get; }

        /// <summary>All talents, ordered by tier then column.</summary>
        public IReadOnlyList<SpiritTalent> Talents { get; }

        public int MaxTier { get; }

        /// <summary>Points that must be spent in this tree before <paramref name="tier"/> opens.</summary>
        public static int PointsRequiredForTier(int tier)
        {
            return tier <= 1 ? 0 : (tier - 1) * PointsPerTier;
        }

        public SpiritTalent Find(string talentId)
        {
            if (string.IsNullOrEmpty(talentId))
                return null;

            return _byId.TryGetValue(talentId, out var talent) ? talent : null;
        }

        public bool Contains(string talentId)
        {
            return !string.IsNullOrEmpty(talentId) && _byId.ContainsKey(talentId);
        }

        /// <summary>Tier is 1-based; empty list when the tier does not exist.</summary>
        public IReadOnlyList<SpiritTalent> TalentsOnTier(int tier)
        {
            var result = new List<SpiritTalent>();
            foreach (var talent in Talents)
            {
                if (talent.Tier == tier)
                    result.Add(talent);
            }

            return result;
        }

        private static int CompareForLayout(SpiritTalent a, SpiritTalent b)
        {
            if (ReferenceEquals(a, b))
                return 0;

            if (a == null)
                return -1;

            if (b == null)
                return 1;

            var tier = a.Tier.CompareTo(b.Tier);
            return tier != 0 ? tier : a.Column.CompareTo(b.Column);
        }
    }
}

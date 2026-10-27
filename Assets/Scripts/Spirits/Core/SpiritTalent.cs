using System;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// One node in an element's tree: a rankable upgrade with a cost, a position, and optionally a
    /// prerequisite and a boon it eventually turns on.
    ///
    /// This mirrors the Classic WoW talent model the design brief asked for - a handful of ranks each,
    /// a tier you must invest enough points to reach, and prerequisite nodes that have to be taken
    /// first. It is deliberately a plain data object: no ScriptableObject, no engine types, so a tree can
    /// be built and validated in a unit test.
    /// </summary>
    public sealed class SpiritTalent
    {
        /// <summary>
        /// What an upgrade actually grants is still an open question in Spirits.md, so a talent names the
        /// boon it is *meant* to drive and leaves the numbers to whatever consumes it later. An empty id
        /// means "no effect wired yet", not "no effect planned".
        /// </summary>
        public const string NoBoon = "";

        public SpiritTalent(
            string id,
            SpiritElement element,
            int tier,
            int column,
            string name,
            string description,
            int maxRank,
            int costPerRank = 1,
            string requiresId = null,
            int requiresRank = 0,
            string boonId = NoBoon)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A talent needs a stable id.", nameof(id));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException($"Talent '{id}' needs a display name.", nameof(name));

            if (tier < 1)
                throw new ArgumentOutOfRangeException(nameof(tier), $"Talent '{id}' must sit on tier 1 or above.");

            if (column < 0)
                throw new ArgumentOutOfRangeException(nameof(column), $"Talent '{id}' must have a non-negative column.");

            if (maxRank < 1)
                throw new ArgumentOutOfRangeException(nameof(maxRank), $"Talent '{id}' must be rankable.");

            if (costPerRank < 1)
                throw new ArgumentOutOfRangeException(nameof(costPerRank), $"Talent '{id}' must cost at least one point.");

            if (requiresRank < 0)
                throw new ArgumentOutOfRangeException(nameof(requiresRank), $"Talent '{id}' has a negative prerequisite rank.");

            Id = id;
            Element = element;
            Tier = tier;
            Column = column;
            Name = name;
            Description = description ?? string.Empty;
            MaxRank = maxRank;
            CostPerRank = costPerRank;
            RequiresId = string.IsNullOrWhiteSpace(requiresId) ? null : requiresId;
            RequiresRank = RequiresId == null ? 0 : requiresRank;
            BoonId = string.IsNullOrWhiteSpace(boonId) ? NoBoon : boonId;
        }

        /// <summary>Stable identifier, also the key in save data. Never change one without a migration.</summary>
        public string Id { get; }

        public SpiritElement Element { get; }

        /// <summary>Row in the tree, 1-based. Every tier above the first costs tree investment to reach.</summary>
        public int Tier { get; }

        /// <summary>Horizontal slot in the tier, 0-based, used only for layout.</summary>
        public int Column { get; }

        public string Name { get; }

        public string Description { get; }

        public int MaxRank { get; }

        public int CostPerRank { get; }

        /// <summary>The talent that must be ranked first, or null.</summary>
        public string RequiresId { get; }

        /// <summary>How many ranks of <see cref="RequiresId"/> are needed.</summary>
        public int RequiresRank { get; }

        /// <summary>The boon this talent is intended to feed, or <see cref="NoBoon"/>.</summary>
        public string BoonId { get; }

        public bool HasPrerequisite => RequiresId != null;

        public int TotalCost => MaxRank * CostPerRank;
    }
}

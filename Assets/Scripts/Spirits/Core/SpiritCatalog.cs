using System;
using System.Collections.Generic;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// The four element trees, and the lookup the rest of the game uses to find one.
    ///
    /// Following the pattern already established by <c>CombatRules</c>, the content lives in code so the
    /// system runs with no assets wired up, and can be replaced wholesale for a test or for tuning that
    /// later moves into authored data.
    ///
    /// The numbers are first-pass placeholders. Spirits.md is explicit that what an upgrade *represents*
    /// mechanically is still an open question, so each talent names the boon it is intended to drive and
    /// stops there; nothing in the game reads those boons yet.
    /// </summary>
    public static class SpiritCatalog
    {
        private static IReadOnlyList<SpiritTree> _trees;
        private static Dictionary<SpiritElement, SpiritTree> _byElement;

        /// <summary>Every tree, in tab order.</summary>
        public static IReadOnlyList<SpiritTree> Trees => _trees ?? (_trees = BuildDefault());

        public static SpiritTree Tree(SpiritElement element)
        {
            EnsureIndex();
            return _byElement.TryGetValue(element, out var tree) ? tree : null;
        }

        /// <summary>The tree that owns a talent id, or null.</summary>
        public static SpiritTree TreeOf(string talentId)
        {
            if (string.IsNullOrEmpty(talentId))
                return null;

            foreach (var tree in Trees)
            {
                if (tree.Contains(talentId))
                    return tree;
            }

            return null;
        }

        /// <summary>Finds a talent by id anywhere in the catalog, or null.</summary>
        public static SpiritTalent FindTalent(string talentId)
        {
            return TreeOf(talentId)?.Find(talentId);
        }

        /// <summary>Replaces the content. Passing null restores the code-built default trees.</summary>
        public static void Override(IReadOnlyList<SpiritTree> trees)
        {
            _trees = trees;
            _byElement = null;
        }

        /// <summary>Restores the defaults. Used by tests so cases leave no residue behind them.</summary>
        public static void ResetToDefaults()
        {
            Override(null);
        }

        private static void EnsureIndex()
        {
            if (_byElement != null)
                return;

            var index = new Dictionary<SpiritElement, SpiritTree>();
            foreach (var tree in Trees)
                index[tree.Element] = tree;

            _byElement = index;
        }

        private static IReadOnlyList<SpiritTree> BuildDefault()
        {
            return new[]
            {
                Fire(),
                Wind(),
                Earth(),
                Water(),
            };
        }

        private static SpiritTree Fire()
        {
            const SpiritElement element = SpiritElement.Fire;

            return new SpiritTree(element, new[]
            {
                new SpiritTalent(
                    "fire.ember", element, tier: 1, column: 0,
                    "Ember",
                    "The Spirit's regard warms your strikes. Each rank raises the direct damage of fire " +
                    "boons the Spirits grant you.",
                    maxRank: 5, boonId: "fire.ember"),

                new SpiritTalent(
                    "fire.cinder", element, tier: 1, column: 2,
                    "Cinder",
                    "Fire lingers. Each rank strengthens the damage over time left behind by a fire boon.",
                    maxRank: 4, boonId: "fire.cinder"),

                new SpiritTalent(
                    "fire.wildfire", element, tier: 2, column: 0,
                    "Wildfire",
                    "Fire spreads along the blade's line. Each rank widens the area a fire boon covers.",
                    maxRank: 2, requiresId: "fire.ember", requiresRank: 3, boonId: "fire.wildfire"),

                new SpiritTalent(
                    "fire.brand", element, tier: 2, column: 2,
                    "Burning Brand",
                    "A well-timed defence is rewarded with heat. Each rank makes a perfect parry more " +
                    "likely to scorch the attacker.",
                    maxRank: 3, requiresId: "fire.cinder", requiresRank: 1, boonId: "fire.brand"),

                new SpiritTalent(
                    "fire.pyre", element, tier: 3, column: 1,
                    "Pyre",
                    "The Spirit answers a flawless exchange with a burst of flame around you.",
                    maxRank: 1, requiresId: "fire.wildfire", requiresRank: 1, boonId: "fire.pyre"),

                new SpiritTalent(
                    "fire.sunforged", element, tier: 4, column: 1,
                    "Sunforged",
                    "A capstone. Fire boons burn longer and harder, and no longer need to be earned twice.",
                    maxRank: 1, requiresId: "fire.pyre", requiresRank: 1, boonId: "fire.sunforged"),
            });
        }

        private static SpiritTree Wind()
        {
            const SpiritElement element = SpiritElement.Wind;

            return new SpiritTree(element, new[]
            {
                new SpiritTalent(
                    "wind.gust", element, tier: 1, column: 0,
                    "Gust",
                    "The Spirit lends you its pace. Each rank raises the movement speed a wind boon grants.",
                    maxRank: 5, boonId: "wind.gust"),

                new SpiritTalent(
                    "wind.windward", element, tier: 1, column: 2,
                    "Windward",
                    "Wind turns the blade aside. Each rank lengthens the parry window a wind boon opens.",
                    maxRank: 4, boonId: "wind.windward"),

                new SpiritTalent(
                    "wind.tempest", element, tier: 2, column: 0,
                    "Tempest",
                    "A parried blow is answered with a shove. Each rank increases the pushback a wind boon " +
                    "applies.",
                    maxRank: 2, requiresId: "wind.gust", requiresRank: 3, boonId: "wind.tempest"),

                new SpiritTalent(
                    "wind.slipstream", element, tier: 2, column: 2,
                    "Slipstream",
                    "Move as if unarmoured. Each rank reduces the cost of circling and holding measure " +
                    "while a wind boon is active.",
                    maxRank: 3, requiresId: "wind.windward", requiresRank: 1, boonId: "wind.slipstream"),

                new SpiritTalent(
                    "wind.gale", element, tier: 3, column: 1,
                    "Gale Ward",
                    "The Spirit stands between you and the next blow, once.",
                    maxRank: 1, requiresId: "wind.tempest", requiresRank: 1, boonId: "wind.gale"),

                new SpiritTalent(
                    "wind.eye", element, tier: 4, column: 1,
                    "Eye of the Storm",
                    "A capstone. Wind boons last longer and can be earned more often.",
                    maxRank: 1, requiresId: "wind.gale", requiresRank: 1, boonId: "wind.eye"),
            });
        }

        private static SpiritTree Earth()
        {
            const SpiritElement element = SpiritElement.Earth;

            return new SpiritTree(element, new[]
            {
                new SpiritTalent(
                    "earth.bedrock", element, tier: 1, column: 0,
                    "Bedrock",
                    "Stand as ground stands. Each rank reduces the damage an earth boon absorbs.",
                    maxRank: 5, boonId: "earth.bedrock"),

                new SpiritTalent(
                    "earth.pillar", element, tier: 1, column: 2,
                    "Pillar",
                    "A guard that does not give. Each rank makes a guard break less likely while an earth " +
                    "boon holds.",
                    maxRank: 4, boonId: "earth.pillar"),

                new SpiritTalent(
                    "earth.shard", element, tier: 2, column: 0,
                    "Shard",
                    "Meeting steel with stone. Each rank improves the deflection an earth boon produces.",
                    maxRank: 2, requiresId: "earth.bedrock", requiresRank: 3, boonId: "earth.shard"),

                new SpiritTalent(
                    "earth.tremor", element, tier: 2, column: 2,
                    "Tremor",
                    "The ground answers a heavy step. Each rank widens the stagger an earth boon applies.",
                    maxRank: 3, requiresId: "earth.pillar", requiresRank: 1, boonId: "earth.tremor"),

                new SpiritTalent(
                    "earth.monolith", element, tier: 3, column: 1,
                    "Monolith",
                    "For a moment you cannot be moved at all.",
                    maxRank: 1, requiresId: "earth.shard", requiresRank: 1, boonId: "earth.monolith"),

                new SpiritTalent(
                    "earth.unmoved", element, tier: 4, column: 1,
                    "Unmoved",
                    "A capstone. Earth boons no longer expire while you hold a guard.",
                    maxRank: 1, requiresId: "earth.monolith", requiresRank: 1, boonId: "earth.unmoved"),
            });
        }

        private static SpiritTree Water()
        {
            const SpiritElement element = SpiritElement.Water;

            return new SpiritTree(element, new[]
            {
                new SpiritTalent(
                    "water.tide", element, tier: 1, column: 0,
                    "Tide",
                    "Water finds the seam. Each rank raises the healing a water boon grants.",
                    maxRank: 5, boonId: "water.tide"),

                new SpiritTalent(
                    "water.stillpool", element, tier: 1, column: 2,
                    "Still Pool",
                    "Calm after a clean exchange. Each rank lengthens a water boon's recovery.",
                    maxRank: 4, boonId: "water.stillpool"),

                new SpiritTalent(
                    "water.flow", element, tier: 2, column: 0,
                    "Flow",
                    "A bind becomes an opening. Each rank improves the reward for catching a bind and " +
                    "following it up.",
                    maxRank: 2, requiresId: "water.tide", requiresRank: 3, boonId: "water.flow"),

                new SpiritTalent(
                    "water.ripcurrent", element, tier: 2, column: 2,
                    "Rip Current",
                    "A parried blade is pulled off its line. Each rank increases the opening a water boon " +
                    "creates.",
                    maxRank: 3, requiresId: "water.stillpool", requiresRank: 1, boonId: "water.ripcurrent"),

                new SpiritTalent(
                    "water.reflect", element, tier: 3, column: 1,
                    "Reflect",
                    "For a heartbeat the water shows the attacker their own blow.",
                    maxRank: 1, requiresId: "water.flow", requiresRank: 1, boonId: "water.reflect"),

                new SpiritTalent(
                    "water.deepwell", element, tier: 4, column: 1,
                    "Deep Well",
                    "A capstone. Water boons can stack twice before they fade.",
                    maxRank: 1, requiresId: "water.reflect", requiresRank: 1, boonId: "water.deepwell"),
            });
        }
    }
}

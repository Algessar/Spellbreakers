using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spellbreakers.Spirits;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The tree rules are the part of the Spirits system that has to be right before any of it is fun:
    /// five points per tier, prerequisites, one rank at a time, and a respec that gives everything back.
    /// A deliberately small test tree keeps these tests about the rules rather than about content.
    /// </summary>
    [TestFixture]
    public sealed class SpiritProgressionTests
    {
        private static SpiritTree TestFireTree()
        {
            const SpiritElement fire = SpiritElement.Fire;

            return new SpiritTree(fire, new[]
            {
                new SpiritTalent("fire.root", fire, tier: 1, column: 0, "Root", "First.", maxRank: 5, boonId: "fire.root"),
                new SpiritTalent("fire.second", fire, tier: 1, column: 1, "Second", "Needs root.", maxRank: 3,
                    requiresId: "fire.root", requiresRank: 2),
                new SpiritTalent("fire.third", fire, tier: 2, column: 0, "Third", "Tier two.", maxRank: 2,
                    requiresId: "fire.root", requiresRank: 3),
                new SpiritTalent("fire.fourth", fire, tier: 2, column: 1, "Fourth", "Costs two.", maxRank: 1,
                    costPerRank: 2),
            });
        }

        private static SpiritTree TestWindTree()
        {
            const SpiritElement wind = SpiritElement.Wind;

            return new SpiritTree(wind, new[]
            {
                new SpiritTalent("wind.root", wind, tier: 1, column: 0, "Wind Root", "First.", maxRank: 5),
                new SpiritTalent("wind.third", wind, tier: 2, column: 0, "Wind Third", "Tier two.", maxRank: 1),
            });
        }

        private static IReadOnlyList<SpiritTree> TestTrees()
        {
            return new[] { TestFireTree(), TestWindTree() };
        }

        private static SpiritProgression Progression(int points = 20)
        {
            return new SpiritProgression(TestTrees(), points);
        }

        [Test]
        public void NewProgressionHasEarnedButUnspentPoints()
        {
            var progression = Progression(10);

            Assert.That(progression.TotalPoints, Is.EqualTo(10));
            Assert.That(progression.SpentPoints, Is.EqualTo(0));
            Assert.That(progression.AvailablePoints, Is.EqualTo(10));
            Assert.That(progression.RankOf("fire.root"), Is.EqualTo(0));
        }

        [Test]
        public void RankUpSpendsAPointAndRaisesTheRank()
        {
            var progression = Progression(3);

            Assert.That(progression.RankUp("fire.root"), Is.True);
            Assert.That(progression.RankOf("fire.root"), Is.EqualTo(1));
            Assert.That(progression.SpentPoints, Is.EqualTo(1));
            Assert.That(progression.AvailablePoints, Is.EqualTo(2));
        }

        [Test]
        public void RankUpRefusesWithoutAPoint()
        {
            var progression = Progression(0);

            Assert.That(progression.RankUp("fire.root"), Is.False);
            Assert.That(progression.CanRankUp("fire.root", out var reason), Is.False);
            Assert.That(reason, Does.Contain("spirit point"));
        }

        [Test]
        public void RankUpStopsAtMaxRank()
        {
            var progression = Progression(20);

            for (var i = 0; i < 5; i++)
                Assert.That(progression.RankUp("fire.root"), Is.True);

            Assert.That(progression.IsMaxed("fire.root"), Is.True);
            Assert.That(progression.RankUp("fire.root"), Is.False);
            Assert.That(progression.StateOf("fire.root"), Is.EqualTo(SpiritTalentState.Maxed));
        }

        [Test]
        public void CostPerRankIsChargedPerRank()
        {
            var progression = Progression(10);

            for (var i = 0; i < 5; i++)
                progression.RankUp("fire.root");

            Assert.That(progression.RankUp("fire.fourth"), Is.True);
            Assert.That(progression.SpentPoints, Is.EqualTo(7));
            Assert.That(progression.AvailablePoints, Is.EqualTo(3));
        }

        [Test]
        public void SecondTierIsLockedUntilFivePointsAreSpentInTheTree()
        {
            var progression = Progression(20);

            // Tier two opens on tree investment, so it must be closed before any points go in.
            Assert.That(progression.StateOf("fire.third"), Is.EqualTo(SpiritTalentState.Locked));

            for (var i = 0; i < 4; i++)
                progression.RankUp("fire.root");

            Assert.That(progression.StateOf("fire.third"), Is.EqualTo(SpiritTalentState.Locked));

            progression.RankUp("fire.root"); // five spent
            Assert.That(progression.StateOf("fire.third"), Is.EqualTo(SpiritTalentState.Available));
        }

        [Test]
        public void PointsInAnotherTreeDoNotOpenATier()
        {
            var progression = Progression(20);

            for (var i = 0; i < 5; i++)
                progression.RankUp("wind.root");

            Assert.That(progression.SpentIn(SpiritElement.Wind), Is.EqualTo(5));
            Assert.That(progression.SpentIn(SpiritElement.Fire), Is.EqualTo(0));
            Assert.That(progression.RankUp("fire.third"), Is.False);
            Assert.That(progression.CanRankUp("fire.third", out var reason), Is.False);
            Assert.That(reason, Does.Contain("Fire"));
        }

        [Test]
        public void PrerequisiteRankIsRequired()
        {
            var progression = Progression(20);

            Assert.That(progression.RankUp("fire.second"), Is.False);

            progression.RankUp("fire.root");
            Assert.That(progression.RankUp("fire.second"), Is.False);

            progression.RankUp("fire.root");
            Assert.That(progression.RankUp("fire.second"), Is.True);
        }

        [Test]
        public void RefundReturnsTheLastRankOnly()
        {
            var progression = Progression(10);
            progression.RankUp("fire.root");
            progression.RankUp("fire.root");

            Assert.That(progression.Refund("fire.root"), Is.True);
            Assert.That(progression.RankOf("fire.root"), Is.EqualTo(1));
            Assert.That(progression.AvailablePoints, Is.EqualTo(9));

            Assert.That(progression.Refund("fire.root"), Is.True);
            Assert.That(progression.RankOf("fire.root"), Is.EqualTo(0));
            Assert.That(progression.Refund("fire.root"), Is.False);
        }

        [Test]
        public void RefundingAPrerequisiteReLocksItsDependants()
        {
            var progression = Progression(20);

            for (var i = 0; i < 2; i++)
                progression.RankUp("fire.root");

            progression.RankUp("fire.second");
            Assert.That(progression.RankOf("fire.second"), Is.EqualTo(1));

            // Dropping the root back below its requirement locks the dependant again. The rank stays; it is
            // the rules that refuse to go further, which matches how the reference trees behave.
            progression.Refund("fire.root");

            Assert.That(progression.RankUp("fire.second"), Is.False);
            Assert.That(progression.StateOf("fire.second"), Is.EqualTo(SpiritTalentState.Locked));
        }

        [Test]
        public void ResetElementOnlyRefundsThatTree()
        {
            var progression = Progression(20);
            progression.RankUp("fire.root");
            progression.RankUp("wind.root");

            var refunded = progression.ResetElement(SpiritElement.Fire);

            Assert.That(refunded, Is.EqualTo(1));
            Assert.That(progression.SpentIn(SpiritElement.Fire), Is.EqualTo(0));
            Assert.That(progression.SpentIn(SpiritElement.Wind), Is.EqualTo(1));
            Assert.That(progression.AvailablePoints, Is.EqualTo(19));
        }

        [Test]
        public void ResetAllRefundsEverything()
        {
            var progression = Progression(20);

            for (var i = 0; i < 5; i++)
                progression.RankUp("fire.root"); // 5 points, and enough investment to open tier two

            progression.RankUp("fire.fourth"); // costs 2
            progression.RankUp("wind.root");   // 1

            Assert.That(progression.SpentPoints, Is.EqualTo(8));
            Assert.That(progression.ResetAll(), Is.EqualTo(8));
            Assert.That(progression.SpentPoints, Is.EqualTo(0));
            Assert.That(progression.AvailablePoints, Is.EqualTo(20));
            Assert.That(progression.ResetAll(), Is.EqualTo(0));
        }

        [Test]
        public void ChangedFiresOnEveryRealChangeOnly()
        {
            var progression = Progression(5);
            var changes = 0;
            progression.Changed += () => changes++;

            progression.RankUp("fire.root");
            progression.RankUp("fire.root");
            progression.Refund("fire.root");
            progression.ResetAll();

            Assert.That(changes, Is.EqualTo(4));

            // A refused action is not a change.
            progression.RankUp("fire.second");
            Assert.That(changes, Is.EqualTo(4));
        }

        [Test]
        public void ActiveBoonIdsFollowPurchasedRanks()
        {
            var progression = Progression(5);

            Assert.That(progression.ActiveBoonIds, Is.Empty);

            progression.RankUp("fire.root");
            Assert.That(progression.ActiveBoonIds, Is.EquivalentTo(new[] { "fire.root" }));

            progression.Refund("fire.root");
            Assert.That(progression.ActiveBoonIds, Is.Empty);
        }

        [Test]
        public void RestoreRankIgnoresUnknownTalentsAndClampsToMaxRank()
        {
            var progression = Progression(0);

            Assert.That(progression.RestoreRank("nope", 2), Is.False);
            Assert.That(progression.RestoreRank("fire.root", 99), Is.True);
            Assert.That(progression.RankOf("fire.root"), Is.EqualTo(5));
        }

        [Test]
        public void SpentPointsSurviveATotalThatIsLowerThanTheRanksBought()
        {
            // A save written against a bigger budget must not go negative when the budget is reloaded.
            var progression = Progression(0);
            progression.RestoreRank("fire.root", 3);

            Assert.That(progression.SpentPoints, Is.EqualTo(3));
            Assert.That(progression.AvailablePoints, Is.EqualTo(-3));
        }
    }

    /// <summary>
    /// Sanity checks on the shipped content, so a typo in a boon row cannot ship a tree with an
    /// unreachable node or a duplicated id.
    /// </summary>
    [TestFixture]
    public sealed class SpiritCatalogTests
    {
        [Test]
        public void CatalogHasOneTreePerElement()
        {
            Assert.That(SpiritCatalog.Trees.Count, Is.EqualTo(SpiritElements.All.Length));

            foreach (var element in SpiritElements.All)
                Assert.That(SpiritCatalog.Tree(element), Is.Not.Null, $"{element} tree missing");
        }

        [Test]
        public void EveryTalentIdIsUniqueAcrossTheCatalog()
        {
            var ids = SpiritCatalog.Trees.SelectMany(tree => tree.Talents).Select(talent => talent.Id).ToList();

            Assert.That(ids, Is.Unique);
            Assert.That(ids.Count, Is.GreaterThan(0));
        }

        [Test]
        public void EveryPrerequisiteResolvesInsideItsOwnTree()
        {
            foreach (var tree in SpiritCatalog.Trees)
            {
                foreach (var talent in tree.Talents)
                {
                    if (!talent.HasPrerequisite)
                        continue;

                    Assert.That(tree.Find(talent.RequiresId), Is.Not.Null,
                        $"{talent.Id} requires missing talent {talent.RequiresId}");
                    Assert.That(talent.RequiresRank, Is.GreaterThan(0));
                }
            }
        }

        [Test]
        public void EveryTreeCanBeFullyRankedWithinItsOwnBudget()
        {
            // The classic trap: a tree whose own prerequisites make its last tier unreachable. Walking it
            // greedy-cheapest proves the content is at least self-consistent.
            foreach (var tree in SpiritCatalog.Trees)
            {
                var progression = new SpiritProgression(SpiritCatalog.Trees, 999);
                var bought = 0;

                foreach (var talent in tree.Talents)
                {
                    var rank = 0;
                    while (rank < talent.MaxRank && progression.CanRankUp(talent.Id, out _))
                    {
                        Assert.That(progression.RankUp(talent.Id), Is.True);
                        rank++;
                        bought++;
                    }
                }

                Assert.That(bought, Is.EqualTo(tree.Talents.Sum(talent => talent.MaxRank)),
                    $"{tree.Element} could not be fully ranked");
            }
        }

        [Test]
        public void DefaultTreesUseFivePointsPerTier()
        {
            Assert.That(SpiritTree.PointsRequiredForTier(1), Is.EqualTo(0));
            Assert.That(SpiritTree.PointsRequiredForTier(2), Is.EqualTo(5));
            Assert.That(SpiritTree.PointsRequiredForTier(3), Is.EqualTo(10));
        }

        [Test]
        public void OverrideReplacesTheCatalogAndResetRestoresIt()
        {
            try
            {
                SpiritCatalog.Override(new[]
                {
                    new SpiritTree(SpiritElement.Fire, new[]
                    {
                        new SpiritTalent("only.fire", SpiritElement.Fire, 1, 0, "Only", "Just one.", 1),
                    }),
                });

                Assert.That(SpiritCatalog.Tree(SpiritElement.Fire).Talents.Count, Is.EqualTo(1));
                Assert.That(SpiritCatalog.Tree(SpiritElement.Wind), Is.Null);
                Assert.That(SpiritCatalog.FindTalent("fire.ember"), Is.Null);
            }
            finally
            {
                SpiritCatalog.ResetToDefaults();
            }

            // Defaults are rebuilt from code, so identity is not preserved - only the content is.
            Assert.That(SpiritCatalog.Tree(SpiritElement.Fire), Is.Not.Null);
            Assert.That(SpiritCatalog.FindTalent("fire.ember"), Is.Not.Null);
            Assert.That(SpiritCatalog.Tree(SpiritElement.Wind), Is.Not.Null);
        }
    }
}

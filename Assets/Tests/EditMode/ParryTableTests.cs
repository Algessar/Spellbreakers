using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The parry table is authored data with no safe default: an unset cell means "nobody decided this
    /// answer yet". These tests are therefore mostly completeness tests, because a hole in the table
    /// would otherwise surface in play as an unexplained free hit.
    /// </summary>
    [TestFixture]
    public sealed class ParryTableTests
    {
        private const int ExpectedGuardCellTotal = 30;
        private const int ExpectedTransitionCellTotal = 25;

        [Test]
        public void ExpectedCellCounts_AreDerivedFromTheGuardAndDirectionSets()
        {
            Assert.That(ParryTable.ExpectedGuardCells, Is.EqualTo(GuardIds.All.Length * GuardIds.Directions.Length));
            Assert.That(ParryTable.ExpectedGuardCells, Is.EqualTo(ExpectedGuardCellTotal));
            Assert.That(ParryTable.ExpectedTransitionCells,
                Is.EqualTo(GuardIds.Directions.Length * GuardIds.Directions.Length));
            Assert.That(ParryTable.ExpectedTransitionCells, Is.EqualTo(ExpectedTransitionCellTotal));
        }

        [Test]
        public void CreateDefault_FillsEveryCellTheCompletenessGuardsEnumerate()
        {
            var table = ParryTable.CreateDefault();

            Assert.That(table.GuardCellCount, Is.EqualTo(ParryTable.ExpectedGuardCells));
            Assert.That(table.TransitionCellCount, Is.EqualTo(ParryTable.ExpectedTransitionCells));
        }

        /// <summary>
        /// The completeness guard. Every (guard, attack) pair the rest of the game can ask about must
        /// have an authored answer, so this iterates the same enumeration the table is built from.
        /// </summary>
        [Test]
        public void EveryGuardCellResolvesToARealOutcome()
        {
            var table = ParryTable.CreateDefault();

            foreach (var (guard, attack) in ParryTable.EnumerateGuardCells())
            {
                Assert.That(table.ResolveGuard(guard, attack), Is.Not.EqualTo(CombatOutcome.Undefined),
                    $"{guard} vs {attack}");
            }
        }

        [Test]
        public void EveryTransitionCellResolvesToARealOutcome()
        {
            var table = ParryTable.CreateDefault();

            foreach (var (defence, attack) in ParryTable.EnumerateTransitionCells())
            {
                Assert.That(table.ResolveTransition(defence, attack), Is.Not.EqualTo(CombatOutcome.Undefined),
                    $"{defence} meeting {attack}");
            }
        }

        [Test]
        public void EnumerateGuardCells_YieldsEveryPairExactlyOnce()
        {
            var seen = new System.Collections.Generic.HashSet<(GuardId, TransitionDirection)>();

            foreach (var cell in ParryTable.EnumerateGuardCells())
                Assert.That(seen.Add(cell), Is.True, $"{cell.Guard} vs {cell.Attack}");

            Assert.That(seen.Count, Is.EqualTo(ParryTable.ExpectedGuardCells));
        }

        [Test]
        public void EnumerateTransitionCells_YieldsEveryPairExactlyOnce()
        {
            var seen = new System.Collections.Generic.HashSet<(TransitionDirection, TransitionDirection)>();

            foreach (var cell in ParryTable.EnumerateTransitionCells())
                Assert.That(seen.Add(cell), Is.True, $"{cell.Defence} meeting {cell.Attack}");

            Assert.That(seen.Count, Is.EqualTo(ParryTable.ExpectedTransitionCells));
        }

        [Test]
        public void EmptyTable_ReportsUndefinedForEveryLookup()
        {
            var table = new ParryTable();

            Assert.That(table.GuardCellCount, Is.EqualTo(0));
            Assert.That(table.TransitionCellCount, Is.EqualTo(0));
            Assert.That(table.ResolveGuard(GuardId.Ochs, TransitionDirection.Oberhau), Is.EqualTo(CombatOutcome.Undefined));
            Assert.That(table.ResolveTransition(TransitionDirection.Oberhau, TransitionDirection.Thrust),
                Is.EqualTo(CombatOutcome.Undefined));
        }

        [Test]
        public void SetCells_AreReadBack()
        {
            var table = new ParryTable();

            table.SetGuardCell(GuardId.Alber, TransitionDirection.Oberhau, CombatOutcome.Deflect);
            table.SetTransitionCell(TransitionDirection.Unterhau, TransitionDirection.Mittelhau, CombatOutcome.Parry);

            Assert.That(table.ResolveGuard(GuardId.Alber, TransitionDirection.Oberhau), Is.EqualTo(CombatOutcome.Deflect));
            Assert.That(table.ResolveTransition(TransitionDirection.Unterhau, TransitionDirection.Mittelhau),
                Is.EqualTo(CombatOutcome.Parry));
            Assert.That(table.GuardCellCount, Is.EqualTo(1));
            Assert.That(table.TransitionCellCount, Is.EqualTo(1));
        }

        [Test]
        public void SetGuardCell_OverwritesRatherThanCountingASecondCell()
        {
            var table = new ParryTable();

            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Thrust, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Thrust, CombatOutcome.HitBody);

            Assert.That(table.GuardCellCount, Is.EqualTo(1));
            Assert.That(table.ResolveGuard(GuardId.Ochs, TransitionDirection.Thrust), Is.EqualTo(CombatOutcome.HitBody));
        }

        /// <summary>A clone is what an archetype or a test distortion mutates, so writing to it must not
        /// rewrite the shared authored relations.</summary>
        [Test]
        public void Clone_IsIndependentOfTheOriginal()
        {
            var original = ParryTable.CreateDefault();
            var clone = original.Clone();

            Assert.That(clone.GuardCellCount, Is.EqualTo(original.GuardCellCount));
            Assert.That(clone.TransitionCellCount, Is.EqualTo(original.TransitionCellCount));

            var authored = original.ResolveGuard(GuardId.VomTag, TransitionDirection.Oberhau);
            clone.SetGuardCell(GuardId.VomTag, TransitionDirection.Oberhau, CombatOutcome.HitBody);
            clone.SetTransitionCell(TransitionDirection.Oberhau, TransitionDirection.Oberhau, CombatOutcome.Deflect);

            Assert.That(original.ResolveGuard(GuardId.VomTag, TransitionDirection.Oberhau), Is.EqualTo(authored));
            Assert.That(clone.ResolveGuard(GuardId.VomTag, TransitionDirection.Oberhau), Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(original.ResolveTransition(TransitionDirection.Oberhau, TransitionDirection.Oberhau),
                Is.EqualTo(CombatOutcome.Bind));
        }

        /// <summary>
        /// Spot-checks that the authored relations still read as fencing rather than merely as filled
        /// cells: each guard has a line it loses to, which is what keeps the matchup interesting.
        /// </summary>
        [TestCase(GuardId.VomTag, TransitionDirection.Unterhau, CombatOutcome.HitBody)]
        [TestCase(GuardId.Alber, TransitionDirection.Oberhau, CombatOutcome.HitBody)]
        [TestCase(GuardId.IronGate, TransitionDirection.Unterhau, CombatOutcome.HitBody)]
        [TestCase(GuardId.Ochs, TransitionDirection.Oberhau, CombatOutcome.Parry)]
        [TestCase(GuardId.Langort, TransitionDirection.Oberhau, CombatOutcome.Bind)]
        public void CreateDefault_AuthorsTheExpectedMatchup(
            GuardId guard,
            TransitionDirection attack,
            CombatOutcome expected)
        {
            Assert.That(ParryTable.CreateDefault().ResolveGuard(guard, attack), Is.EqualTo(expected));
        }
    }
}

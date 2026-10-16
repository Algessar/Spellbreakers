using System.Collections.Generic;
using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The transition table is the authored vocabulary of the combat language: which guard changes exist
    /// at all. Lookup has to be exact, because a missing entry is what keeps the AI's action space small
    /// and a spurious one would let an illegal guard change through.
    /// </summary>
    [TestFixture]
    public sealed class TransitionTableTests
    {
        // 18 attacks plus the 4 Langort re-guarding recoveries. Langort -> Alber is the Oberhau cut from
        // the long point, so it is an attack and the recovery to Alber is not authored.
        private const int ExpectedDefaultProfileCount = 22;
        private const int ExpectedDefaultGuardCount = 6;
        private const int ExpectedRecoveryCount = 4;

        [Test]
        public void CreateDefault_AuthorsSixGuardsAndTheFullTransitionSet()
        {
            var table = TransitionTable.CreateDefault();

            Assert.That(table.Count, Is.EqualTo(ExpectedDefaultProfileCount));
            Assert.That(table.Profiles.Count, Is.EqualTo(ExpectedDefaultProfileCount));
            Assert.That(table.GuardOr(GuardId.VomTag).TipReach, Is.GreaterThan(0f));
            Assert.That(table.GuardOr(GuardId.IronGate).Guard, Is.EqualTo(GuardId.IronGate));

            var guarded = new HashSet<GuardId>();
            foreach (var profile in table.Profiles)
            {
                guarded.Add(profile.From);
                guarded.Add(profile.To);
            }

            Assert.That(guarded.Count, Is.EqualTo(ExpectedDefaultGuardCount));
        }

        [Test]
        public void CreateDefault_NeverAuthorsATransitionOntoItsOwnGuard()
        {
            var table = TransitionTable.CreateDefault();

            foreach (var profile in table.Profiles)
                Assert.That(profile.From, Is.Not.EqualTo(profile.To), profile.DisplayName);
        }

        /// <summary>
        /// Recoveries are the punish window: settling back out of Langort carries no live blade. The
        /// invariant is therefore "no damage window implies a Neutral line", not "everything out of
        /// Langort is a recovery" - Langort also throws the cut down into Alber.
        /// </summary>
        [Test]
        public void CreateDefault_TransitionsWithoutADamageWindowAreExactlyTheRecoveries()
        {
            var table = TransitionTable.CreateDefault();
            var recoveries = 0;
            var attacks = 0;

            foreach (var profile in table.Profiles)
            {
                if (!profile.HasDamageWindow)
                {
                    recoveries++;
                    Assert.That(profile.Direction, Is.EqualTo(TransitionDirection.Neutral), profile.DisplayName);
                    Assert.That(profile.From, Is.EqualTo(GuardId.Langort), profile.DisplayName);
                }
                else
                {
                    attacks++;
                }
            }

            Assert.That(recoveries, Is.EqualTo(ExpectedRecoveryCount));
            Assert.That(attacks, Is.EqualTo(ExpectedDefaultProfileCount - ExpectedRecoveryCount));
            Assert.That(attacks + recoveries, Is.EqualTo(ExpectedDefaultProfileCount));
        }

        /// <summary>
        /// Every profile out of Langort except the cut into Alber is a re-guarding recovery, and that cut
        /// is a real attack. This is the shape that caught the duplicated Langort pair.
        /// </summary>
        [Test]
        public void CreateDefault_LangortThrowsOneCutAndOtherwiseRecovers()
        {
            var table = TransitionTable.CreateDefault();

            Assert.That(table.TryGet(GuardId.Langort, GuardId.Alber, out var cut), Is.True);
            Assert.That(cut.HasDamageWindow, Is.True, "the cut down from the long point is an attack");
            Assert.That(cut.Direction, Is.EqualTo(TransitionDirection.Oberhau));

            foreach (var profile in table.Profiles)
            {
                if (profile.From != GuardId.Langort || profile.To == GuardId.Alber)
                    continue;

                Assert.That(profile.HasDamageWindow, Is.False, profile.DisplayName);
                Assert.That(profile.Direction, Is.EqualTo(TransitionDirection.Neutral), profile.DisplayName);
            }
        }

        [Test]
        public void EveryAuthoredPairIsRetrievableByLookup()
        {
            var table = TransitionTable.CreateDefault();

            foreach (var profile in table.Profiles)
            {
                var found = table.TryGet(profile.From, profile.To, out var retrieved);

                Assert.That(found, Is.True, profile.DisplayName);
                Assert.That(retrieved, Is.SameAs(profile));
            }
        }

        [TestCase(GuardId.None, GuardId.Alber)]
        [TestCase(GuardId.VomTag, GuardId.None)]
        [TestCase(GuardId.None, GuardId.None)]
        [TestCase(GuardId.Ochs, GuardId.Ochs)]
        public void TryGet_RefusesNonsensePairs(GuardId from, GuardId to)
        {
            var table = TransitionTable.CreateDefault();

            Assert.That(table.TryGet(from, to, out var profile), Is.False);
            Assert.That(profile, Is.Null);
        }

        [Test]
        public void Add_RefusesADuplicatePairButKeepsTheFirst()
        {
            var table = TransitionTable.CreateDefault();
            var existing = table.Profiles[0];
            var duplicate = new TransitionProfile(
                existing.From,
                existing.To,
                TransitionDirection.Unterhau,
                1f,
                0.1f,
                0.2f);

            Assert.That(table.Add(duplicate), Is.False);
            Assert.That(table.Count, Is.EqualTo(ExpectedDefaultProfileCount));
            Assert.That(table.TryGet(existing.From, existing.To, out var stored), Is.True);
            Assert.That(stored, Is.SameAs(existing));
        }

        [Test]
        public void Add_AcceptsANewPairAndMakesItReachable()
        {
            var table = new TransitionTable();
            var profile = new TransitionProfile(
                GuardId.IronGate,
                GuardId.Ochs,
                TransitionDirection.Mittelhau,
                0.4f,
                0.3f,
                0.6f);

            Assert.That(table.Add(profile), Is.True);
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.TryGet(GuardId.IronGate, GuardId.Ochs, out var found), Is.True);
            Assert.That(found, Is.SameAs(profile));
        }

        [Test]
        public void CollectFrom_ReturnsOnlyThatGuardAndClearsTheListFirst()
        {
            var table = TransitionTable.CreateDefault();
            var results = new List<TransitionProfile> { null, null, null };

            table.CollectFrom(GuardId.Pflug, results);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results, Has.None.Null);
            foreach (var profile in results)
                Assert.That(profile.From, Is.EqualTo(GuardId.Pflug));

            // A second call must not append to the previous contents. The expected size is derived from
            // the table itself so this test pins the clearing behaviour, not the authored counts.
            var pflugCount = results.Count;
            Assert.That(pflugCount, Is.GreaterThan(0), "Pflug authors real transitions");

            var expectedLangort = 0;
            foreach (var profile in table.Profiles)
            {
                if (profile.From == GuardId.Langort)
                    expectedLangort++;
            }

            table.CollectFrom(GuardId.Langort, results);

            Assert.That(expectedLangort, Is.GreaterThan(0));
            Assert.That(results.Count, Is.EqualTo(expectedLangort), "the previous contents were replaced, not appended to");
            foreach (var profile in results)
                Assert.That(profile.From, Is.EqualTo(GuardId.Langort));
        }

        [Test]
        public void CollectFrom_LeavesAnEmptyListForAGuardWithNoTransitions()
        {
            var table = new TransitionTable();
            var results = new List<TransitionProfile> { null };

            table.CollectFrom(GuardId.Ochs, results);

            Assert.That(results, Is.Empty);
        }

        [Test]
        public void GuardOr_FallsBackToADefaultProfileRatherThanReturningNull()
        {
            var table = new TransitionTable();

            var fallback = table.GuardOr(GuardId.Ochs);

            Assert.That(fallback, Is.Not.Null);
            Assert.That(fallback.Guard, Is.EqualTo(GuardId.Ochs));
            Assert.That(fallback.TipReach, Is.GreaterThan(0f));
        }

        [Test]
        public void ForceGuard_SeesTheAuthoredGuardWhenOneExists()
        {
            var table = TransitionTable.CreateDefault();
            var authored = table.GuardOr(GuardId.Langort);

            Assert.That(authored.ParryGraceSeconds, Is.EqualTo(0.22f).Within(1e-5f));
        }

        [Test]
        public void SetDistanceGateForAll_FlipsEveryProfileInBothDirections()
        {
            var table = TransitionTable.CreateDefault();

            table.SetDistanceGateForAll(true);
            foreach (var profile in table.Profiles)
                Assert.That(profile.EnforceDistanceGate, Is.True, profile.DisplayName);

            table.SetDistanceGateForAll(false);
            foreach (var profile in table.Profiles)
                Assert.That(profile.EnforceDistanceGate, Is.False, profile.DisplayName);
        }

        /// <summary>
        /// An archetype turns the gate on for its own copy only. If Clone shared profile objects, giving
        /// one enemy measure discipline would silently take free guard switching away from the player.
        /// </summary>
        [Test]
        public void Clone_IsDeepSoGateChangesDoNotLeakBack()
        {
            var original = TransitionTable.CreateDefault();
            var clone = original.Clone();

            clone.SetDistanceGateForAll(true);

            foreach (var profile in clone.Profiles)
                Assert.That(profile.EnforceDistanceGate, Is.True, profile.DisplayName);
            foreach (var profile in original.Profiles)
                Assert.That(profile.EnforceDistanceGate, Is.False, profile.DisplayName);

            Assert.That(clone.Count, Is.EqualTo(original.Count));
        }

        [Test]
        public void Clone_KeepsTheSameAuthoredPairsReachable()
        {
            var original = TransitionTable.CreateDefault();
            var clone = original.Clone();

            foreach (var profile in clone.Profiles)
                Assert.That(clone.TryGet(profile.From, profile.To, out _), Is.True, profile.DisplayName);

            Assert.That(clone.Profiles[0], Is.Not.SameAs(original.Profiles[0]));
        }
    }

    /// <summary>
    /// The AI, perception and parry resolution all pass this value type around instead of a profile, so
    /// its "nothing is happening" default has to be inert rather than throwing or reading as valid.
    /// </summary>
    [TestFixture]
    public sealed class GuardTransitionStateTests
    {
        [Test]
        public void DefaultState_ReportsItselfAsInvalidAndInert()
        {
            var state = default(GuardTransitionState);

            Assert.That(state.IsValid, Is.False);
            Assert.That(state.Profile, Is.Null);
            Assert.That(state.From, Is.EqualTo(GuardId.None));
            Assert.That(state.To, Is.EqualTo(GuardId.None));
            Assert.That(state.Direction, Is.EqualTo(TransitionDirection.Neutral));
            Assert.That(state.LungeDistance, Is.EqualTo(0f));
            Assert.That(state.IsInDamageWindow(0.5f), Is.False);
            Assert.That(state.IsInParryWindow(0.5f), Is.False);
        }

        /// <summary>A missing profile must read as fully committed, so callers cannot cancel nothing.</summary>
        [Test]
        public void DefaultState_IsTreatedAsCommitted()
        {
            Assert.That(default(GuardTransitionState).IsCommitted(0f), Is.True);
        }

        [Test]
        public void ConstructedState_MirrorsItsProfile()
        {
            var profile = new TransitionProfile(
                GuardId.Alber,
                GuardId.VomTag,
                TransitionDirection.Unterhau,
                0.44f,
                0.34f,
                0.62f)
            {
                LungeDistance = 0.9f,
            };

            var state = new GuardTransitionState(profile);

            Assert.That(state.IsValid, Is.True);
            Assert.That(state.Profile, Is.SameAs(profile));
            Assert.That(state.From, Is.EqualTo(GuardId.Alber));
            Assert.That(state.To, Is.EqualTo(GuardId.VomTag));
            Assert.That(state.Direction, Is.EqualTo(TransitionDirection.Unterhau));
            Assert.That(state.PassThrough, Is.EqualTo(GuardId.Langort));
            Assert.That(state.LungeDistance, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(state.IsInDamageWindow(0.4f), Is.True);
            Assert.That(state.IsInDamageWindow(0.1f), Is.False);
        }

        [Test]
        public void IsCommitted_TurnsTrueExactlyAtTheCommitmentPoint()
        {
            var profile = new TransitionProfile(
                GuardId.Ochs,
                GuardId.Alber,
                TransitionDirection.Mittelhau,
                0.42f,
                0.3f,
                0.62f);
            var state = new GuardTransitionState(profile);

            Assert.That(state.IsCommitted(profile.Commitment - 0.01f), Is.False);
            Assert.That(state.IsCommitted(profile.Commitment), Is.True);
        }
    }
}

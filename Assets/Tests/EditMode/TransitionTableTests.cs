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
        // Counts are deliberately NOT hard-coded: this table is live authored data (the dev doc marks it
        // "under review"), so pinning an exact total means every added transition breaks the suite for
        // no reason. What matters are the shape invariants below, which the table must keep no matter
        // how many rows it grows to.

        [Test]
        public void CreateDefault_AuthorsEveryDestinationGuard()
        {
            var table = TransitionTable.CreateDefault();

            Assert.That(table.Count, Is.GreaterThan(0));
            Assert.That(table.Profiles.Count, Is.EqualTo(table.Count), "Count and Profiles must agree");
            Assert.That(table.GuardOr(GuardId.VomTag).TipReach, Is.GreaterThan(0f));
            Assert.That(table.GuardOr(GuardId.IronGate).Guard, Is.EqualTo(GuardId.IronGate));

            var guarded = new HashSet<GuardId>();
            foreach (var profile in table.Profiles)
            {
                guarded.Add(profile.From);
                guarded.Add(profile.To);
            }

            // Langort is pass-only, so the authored endpoints are exactly the guards a transition may
            // legitimately end in - one fewer than every guard the build can represent.
            Assert.That(guarded.Count, Is.EqualTo(GuardIds.Destinations.Length),
                "every destination guard must appear as an endpoint, and no one else");
            foreach (var destination in GuardIds.Destinations)
                Assert.That(guarded, Does.Contain(destination), $"{destination} is never an endpoint");
        }

        /// <summary>
        /// Langort is a pose a cut passes *through*, never a guard a fighter holds (dev doc 5). An
        /// authored row that starts or ends there would make the pass state into a real stance.
        /// </summary>
        [Test]
        public void CreateDefault_NeverUsesLangortAsAnEndpoint()
        {
            var table = TransitionTable.CreateDefault();

            foreach (var profile in table.Profiles)
            {
                Assert.That(profile.From, Is.Not.EqualTo(GuardId.Langort),
                    $"{profile.DisplayName}: Langort is pass-only per dev doc 5, never a source guard");
                Assert.That(profile.To, Is.Not.EqualTo(GuardId.Langort),
                    $"{profile.DisplayName}: Langort is pass-only per dev doc 5, never a destination guard");
            }
        }

        /// <summary>
        /// Design doc 3.2: a self-transition is legal - "Vom Tag -> Vom Tag = Zornhau". The invariant is
        /// therefore not "from must differ from to", but "a self-transition must be a real attack": it
        /// has to carry a live blade, otherwise it is a no-op that could be requested forever.
        /// </summary>
        [Test]
        public void CreateDefault_SelfTransitionsAreLegalButMustBeAttacks()
        {
            var table = TransitionTable.CreateDefault();
            var selfTransitions = 0;

            foreach (var profile in table.Profiles)
            {
                if (profile.From != profile.To)
                    continue;

                selfTransitions++;
                Assert.That(profile.HasDamageWindow, Is.True,
                    $"{profile.DisplayName} is a self-transition with no live blade, so it does nothing");
                Assert.That(profile.IsSelfTransition, Is.True);
            }

            Assert.That(selfTransitions, Is.GreaterThan(0),
                "doc 3.2 requires the concept to be supported, and the default table should demonstrate it");
        }

        /// <summary>The one self-transition the dev doc names: the Zornhau thrown from Vom Tag returns
        /// to Vom Tag. The other three self-pairs are deliberately not authored.</summary>
        [TestCase(GuardId.VomTag, "Zornhau")]
        public void CreateDefault_AuthorsTheDocumentedSelfTransitions(GuardId guard, string technique)
        {
            var table = TransitionTable.CreateDefault();

            Assert.That(table.TryGet(guard, guard, out var profile), Is.True, $"{guard} -> {guard}");
            Assert.That(profile.IsSelfTransition, Is.True);
            Assert.That(profile.TechniqueName, Is.EqualTo(technique));
            Assert.That(profile.HasDamageWindow, Is.True);
        }

        /// <summary>
        /// Every authored transition is an attack: the blade is live through its damage window, and the
        /// beat to punish is the Settle phase that follows. A row with no damage window - or a Neutral
        /// line - would be the retired "recovery" concept sneaking back in.
        /// </summary>
        [Test]
        public void CreateDefault_AuthorsEveryTransitionAsAnAttack()
        {
            var table = TransitionTable.CreateDefault();

            Assert.That(table.Count, Is.GreaterThan(0));

            foreach (var profile in table.Profiles)
            {
                Assert.That(profile.HasDamageWindow, Is.True,
                    $"{profile.DisplayName} has no live blade, so it is not an attack");
                Assert.That(profile.Direction, Is.Not.EqualTo(TransitionDirection.Neutral),
                    $"{profile.DisplayName} names no cut, so it is not an attack");
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
            var countBefore = table.Count;
            var existing = table.Profiles[0];
            var duplicate = new TransitionProfile(
                existing.From,
                existing.To,
                TransitionDirection.Unterhau,
                1f,
                0.1f,
                0.2f);

            Assert.That(table.Add(duplicate), Is.False);
            Assert.That(table.Count, Is.EqualTo(countBefore), "a rejected duplicate must not change the table");
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

            table.CollectFrom(GuardId.VomTag, results);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results, Has.None.Null);
            foreach (var profile in results)
                Assert.That(profile.From, Is.EqualTo(GuardId.VomTag));

            // A second call must not append to the previous contents. The expected size is derived from
            // the table itself so this test pins the clearing behaviour, not the authored counts.
            Assert.That(results.Count, Is.GreaterThan(0), "Vom Tag authors real transitions");

            var expected = 0;
            foreach (var profile in table.Profiles)
            {
                if (profile.From == GuardId.Pflug)
                    expected++;
            }

            table.CollectFrom(GuardId.Pflug, results);

            Assert.That(expected, Is.GreaterThan(0), "Pflug authors real transitions");
            Assert.That(results.Count, Is.EqualTo(expected), "the previous contents were replaced, not appended to");
            foreach (var profile in results)
                Assert.That(profile.From, Is.EqualTo(GuardId.Pflug));
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
            Assert.That(state.TransitionPose, Is.EqualTo(GuardId.Langort));
            Assert.That(state.LungeDistance, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(state.IsInDamageWindow(0.4f), Is.True);
            Assert.That(state.IsInDamageWindow(0.1f), Is.False);
        }

        /// <summary>
        /// The state is what perception and parry resolution receive, so it has to forward the pose
        /// members - including the Krieg-specific one - rather than exposing only the normal pose.
        /// </summary>
        [Test]
        public void ConstructedState_MirrorsThePoseTheAttackReachesThrough()
        {
            var profile = new TransitionProfile(
                GuardId.Alber,
                GuardId.VomTag,
                TransitionDirection.Unterhau,
                0.44f,
                0.34f,
                0.62f)
            {
                TransitionPose = GuardId.Pflug,
                TransitionPoseInKrieg = GuardId.Langort,
            };

            var state = new GuardTransitionState(profile);

            Assert.That(state.TransitionPose, Is.EqualTo(GuardId.Pflug));
            Assert.That(state.TransitionPoseInKrieg, Is.EqualTo(GuardId.Langort));
            Assert.That(state.ResolveTransitionPose(Measure.Zufechten), Is.EqualTo(GuardId.Pflug));
            Assert.That(state.ResolveTransitionPose(Measure.Krieg), Is.EqualTo(GuardId.Langort));
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

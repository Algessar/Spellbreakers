using System.Collections.Generic;
using NUnit.Framework;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The brain is the part of the AI players actually experience, so what matters is that it is
    /// reproducible, bounded and explainable: the same seeds and the same snapshots must produce the
    /// same decision, and every decision must carry a full breakdown.
    /// </summary>
    [TestFixture]
    public sealed class FencingBrainTests
    {
        private const float Tolerance = 1e-4f;
        private const float DecisionDelta = 0.1f;
        private const float ComfortableDistance = 1.6f;
        private const float WoundedFraction = 0.1f;
        private const float HealthyFraction = 1f;
        private const int SweepSteps = 60;

        [SetUp]
        public void SetUp()
        {
            CombatRules.ResetToDefaults();
        }

        // --- Fixtures -------------------------------------------------------------------

        private static FencingBrain NewBrain(
            FencingArchetype archetype = null,
            TransitionTable table = null,
            ParryTable parryTable = null)
        {
            return new FencingBrain(
                table ?? TransitionTable.CreateDefault(),
                parryTable ?? ParryTable.CreateDefault(),
                archetype ?? FencingArchetype.CreateFencer());
        }

        /// <summary>Looks a consideration up by name, so a test can assert on one axis in isolation.</summary>
        private static Consideration FindConsideration(FencingBrain brain, string name)
        {
            for (var i = 0; i < brain.Considerations.Count; i++)
            {
                if (brain.Considerations[i].Name == name)
                    return brain.Considerations[i];
            }

            return null;
        }

        /// <summary>A predictable archetype: fixed cadence, no reaction hold, no jitter.</summary>
        private static FencingArchetype ObserverArchetype()
        {
            return new FencingArchetype
            {
                Name = "Observer",
                ReactionSeconds = 0f,
                DecisionInterval = 0.05f,
                Aggression = 0.5f,
                Jitter = 0f,
                MeasureDiscipline = 0.65f,
                RandomSeed = 424242,
            };
        }

        private static OpponentSnapshot Opponent(
            GuardId guard = GuardId.Ochs,
            float distance = ComfortableDistance,
            GuardTransitionState transition = default,
            float progress01 = 0f,
            bool bladeLive = false,
            bool inParryWindow = false,
            bool isAlive = true)
        {
            return new OpponentSnapshot(
                guard,
                transition,
                progress01,
                bladeLive,
                inParryWindow,
                distance,
                Measure.Zufechten,
                HealthyFraction,
                isAlive,
                1f);
        }

        private static FencingContext Context(
            GuardId selfGuard = GuardId.VomTag,
            OpponentSnapshot opponent = default,
            FencingAction action = default,
            float elapsedSeconds = 0f,
            GuardTransitionState selfTransition = default,
            float selfProgress01 = 0f,
            float selfHealthFraction = HealthyFraction,
            ReactionState selfReaction = ReactionState.None,
            bool selfVor = false,
            GuardId lastRequestedGuard = GuardId.None,
            TransitionTable table = null,
            ParryTable parryTable = null,
            FencingArchetype archetype = null)
        {
            var resolvedTable = table ?? TransitionTable.CreateDefault();
            return new FencingContext(
                selfGuard,
                selfTransition,
                selfProgress01,
                selfHealthFraction,
                selfReaction,
                selfVor,
                resolvedTable.GuardOr(selfGuard).TipReach,
                elapsedSeconds,
                opponent,
                action,
                resolvedTable,
                parryTable ?? ParryTable.CreateDefault(),
                archetype ?? FencingArchetype.CreateFencer(),
                lastRequestedGuard,
                0.5f);
        }

        /// <summary>
        /// Ticks a brain on a fixed cadence until it has chosen, so tests state "the brain decides" rather
        /// than encoding the reaction-delay arithmetic in every setup.
        /// </summary>
        private static int DriveUntilChosen(FencingBrain brain, FencingContext context, int maxTicks = 20)
        {
            for (var i = 0; i < maxTicks; i++)
            {
                var ticked = context.WithAction(context.Action, 0.5f);
                if (brain.Tick(DecisionDelta, ticked) && brain.Choice != null)
                    return i + 1;
            }

            return -1;
        }

        // --- Candidate generation -------------------------------------------------------

        [Test]
        public void Tick_ProducesAChoiceWithScoresAndAFullBreakdown()
        {
            var brain = NewBrain();
            var elapsed = 0f;
            var choseAt = -1;

            for (var i = 0; i < 10 && choseAt < 0; i++)
            {
                if (brain.Tick(DecisionDelta, Context(elapsedSeconds: elapsed)))
                    choseAt = i;

                elapsed += DecisionDelta;
            }

            Assert.That(choseAt, Is.GreaterThanOrEqualTo(0), "the brain must decide within a few ticks");
            Assert.That(brain.Choice, Is.Not.Null);
            Assert.That(brain.Choice.Action.Name, Is.Not.Null.And.Not.Empty);
            Assert.That(brain.Scores, Is.Not.Empty);
            Assert.That(brain.Considerations, Is.Not.Empty);

            foreach (var scored in brain.Scores)
            {
                Assert.That(scored.Score, Is.GreaterThanOrEqualTo(0f));
                Assert.That(scored.Score, Is.LessThanOrEqualTo(1f));
                Assert.That(scored.Breakdown.Length, Is.EqualTo(brain.Considerations.Count),
                    "every candidate must be judged on every axis");
            }
        }

        [Test]
        public void Decisions_AreRateLimitedByTheDecisionInterval()
        {
            var brain = NewBrain(ObserverArchetype());
            var decisions = 0;
            var elapsed = 0f;

            for (var i = 0; i < 10; i++)
            {
                if (brain.Tick(0.02f, Context(elapsedSeconds: elapsed)))
                    decisions++;

                elapsed += 0.02f;
            }

            // One decision per DecisionInterval over the sampled span, with slack for the reaction hold.
            Assert.That(decisions, Is.GreaterThan(0));
            Assert.That(decisions, Is.LessThanOrEqualTo(4));
        }

        /// <summary>Mid-transition the guard is already chosen, so offering another guard change would
        /// make the brain able to cancel its own commitment.</summary>
        [Test]
        public void WhileSelfInTransition_NoGuardChangeCandidatesAreOffered()
        {
            var table = TransitionTable.CreateDefault();
            table.TryGet(GuardId.VomTag, GuardId.Alber, out var profile);
            var active = new GuardTransitionState(profile);

            var brain = NewBrain(ObserverArchetype(), table);
            var elapsed = 0f;

            for (var i = 0; i < 20; i++)
            {
                brain.Tick(DecisionDelta, Context(
                    GuardId.VomTag,
                    Opponent(),
                    FencingAction.Wait,
                    elapsed,
                    selfTransition: active,
                    selfProgress01: 0.5f,
                    table: table));

                elapsed += DecisionDelta;
            }

            Assert.That(brain.Scores, Is.Not.Empty, "movement is still worth deciding about");
            foreach (var scored in brain.Scores)
                Assert.That(scored.Action.Kind, Is.Not.EqualTo(FencingActionKind.ChangeGuard), scored.Action.Name);
        }

        [Test]
        public void MovementCandidates_RequireAnOpponentToMoveRelativeTo()
        {
            var brain = NewBrain(ObserverArchetype());
            var elapsed = 0f;

            for (var i = 0; i < 10; i++)
            {
                brain.Tick(DecisionDelta, Context(
                    GuardId.VomTag,
                    OpponentSnapshot.None,
                    FencingAction.Wait,
                    elapsed));

                elapsed += DecisionDelta;
            }

            foreach (var scored in brain.Scores)
            {
                Assert.That(scored.Action.Kind, Is.Not.EqualTo(FencingActionKind.Step), scored.Action.Name);
                Assert.That(scored.Action.Kind, Is.Not.EqualTo(FencingActionKind.Retreat), scored.Action.Name);
            }
        }

        [Test]
        public void GuardFiltering_OffersOnlyTheGuardsTheArchetypeWillUse()
        {
            var table = TransitionTable.CreateDefault();
            var archetype = ObserverArchetype();
            archetype.AllowedTargetGuards = new[] { GuardId.Langort };

            var brain = NewBrain(archetype, table);
            var elapsed = 0f;

            for (var i = 0; i < 10; i++)
            {
                brain.Tick(DecisionDelta, Context(GuardId.VomTag, Opponent(), FencingAction.Wait, elapsed));
                elapsed += DecisionDelta;
            }

            Assert.That(brain.Scores, Is.Not.Empty);
            foreach (var scored in brain.Scores)
            {
                if (scored.Action.Kind == FencingActionKind.ChangeGuard)
                    Assert.That(scored.Action.TargetGuard, Is.EqualTo(GuardId.Langort));
            }
        }

        [Test]
        public void GuardCandidates_ComeFromTheTransitionTableNotAHandWrittenList()
        {
            var table = TransitionTable.CreateDefault();
            var archetype = ObserverArchetype();
            archetype.AllowedTargetGuards = null;

            var brain = NewBrain(archetype, table);
            var elapsed = 0f;

            for (var i = 0; i < 10; i++)
            {
                brain.Tick(DecisionDelta, Context(GuardId.VomTag, Opponent(), FencingAction.Wait, elapsed));
                elapsed += DecisionDelta;
            }

            var expected = new List<GuardId>();
            var buffer = new List<TransitionProfile>();
            table.CollectFrom(GuardId.VomTag, buffer);
            foreach (var profile in buffer)
                expected.Add(profile.To);

            var offered = new List<GuardId>();
            foreach (var scored in brain.Scores)
            {
                if (scored.Action.Kind == FencingActionKind.ChangeGuard)
                    offered.Add(scored.Action.TargetGuard);
            }

            CollectionAssert.AreEquivalent(expected, offered);
        }

        // --- Determinism ----------------------------------------------------------------

        /// <summary>
        /// Two enemies built from the same archetype must fight identically. This is what makes a duel
        /// replayable and a balance change measurable rather than anecdotal.
        /// </summary>
        [Test]
        public void IdenticalArchetypesAndSchedules_MakeIdenticalChoices()
        {
            var schedule = BuildSchedule();
            var first = NewBrain(ObserverArchetype());
            var second = NewBrain(ObserverArchetype());

            for (var i = 0; i < schedule.Count; i++)
            {
                var decisionA = first.Tick(DecisionDelta, schedule[i]);
                var decisionB = second.Tick(DecisionDelta, schedule[i]);

                Assert.That(decisionB, Is.EqualTo(decisionA), $"tick {i}");
            }

            Assert.That(first.Choice, Is.Not.Null);
            Assert.That(second.Choice, Is.Not.Null);
            Assert.That(second.Choice.Action.Name, Is.EqualTo(first.Choice.Action.Name));
            Assert.That(second.Choice.Score, Is.EqualTo(first.Choice.Score).Within(Tolerance));
            Assert.That(second.Scores.Count, Is.EqualTo(first.Scores.Count));

            for (var i = 0; i < first.Scores.Count; i++)
                Assert.That(second.Scores[i].Score, Is.EqualTo(first.Scores[i].Score).Within(Tolerance), $"candidate {i}");
        }

        /// <summary>
        /// A schedule with a distinct opponent transition per step, so the reaction hold is exercised
        /// rather than bypassed by an unchanging snapshot.
        /// </summary>
        private static List<FencingContext> BuildSchedule()
        {
            var table = TransitionTable.CreateDefault();
            table.TryGet(GuardId.VomTag, GuardId.Alber, out var firstProfile);
            table.TryGet(GuardId.VomTag, GuardId.Pflug, out var secondProfile);

            var schedule = new List<FencingContext>();
            for (var i = 0; i < 12; i++)
            {
                var profile = i % 2 == 0 ? firstProfile : secondProfile;
                var snapshot = Opponent(GuardId.Ochs, transition: new GuardTransitionState(profile), progress01: 0.4f);
                schedule.Add(Context(GuardId.VomTag, snapshot, FencingAction.Wait, i * DecisionDelta, table: table));
            }

            return schedule;
        }

        // --- Reaction lag ---------------------------------------------------------------

        /// <summary>
        /// Reaction lag is what makes the enemy beatable by timing: a new opponent action buys the
        /// player a window in which the enemy is still answering the previous situation.
        /// </summary>
        [Test]
        public void ReactionLag_HoldsDecisionsUntilTheReactionTimeHasElapsed()
        {
            var table = TransitionTable.CreateDefault();
            table.TryGet(GuardId.VomTag, GuardId.Alber, out var firstProfile);
            table.TryGet(GuardId.VomTag, GuardId.Pflug, out var secondProfile);

            var archetype = ObserverArchetype();
            archetype.ReactionSeconds = 0.30f;
            archetype.DecisionInterval = 0.05f;

            var brain = NewBrain(archetype, table);

            // Tick on a fixed cadence, optionally swapping the opponent's transition at a given elapsed time.
            bool Step(float elapsed, bool swapOpponent)
            {
                var profile = swapOpponent ? secondProfile : firstProfile;
                var snapshot = Opponent(
                    GuardId.Ochs,
                    transition: new GuardTransitionState(profile),
                    progress01: 0.4f);

                return brain.Tick(DecisionDelta, Context(
                    GuardId.VomTag,
                    snapshot,
                    FencingAction.Wait,
                    elapsed,
                    table: table));
            }

            // Settle into a cadence against a stable opponent action. The reaction hold is charged once
            // for the first observation, so decisions only begin after 0.30s.
            var lastDecisionAt = -1f;
            for (var step = 0; step < 6; step++)
            {
                var elapsed = step * DecisionDelta;
                if (Step(elapsed, swapOpponent: false))
                    lastDecisionAt = elapsed;
            }

            Assert.That(lastDecisionAt, Is.GreaterThanOrEqualTo(0f), "the brain must be deciding by now");

            // The opponent changes action at 0.60s. That must hold the brain until 0.90s.
            const float ChangeAt = 6 * DecisionDelta;
            var reactionEnd = ChangeAt + archetype.ReactionSeconds;

            Step(ChangeAt, swapOpponent: true);

            Assert.That(brain.IsReacting(ChangeAt), Is.True, "the change must start the reaction hold");

            // Strictly inside the window both the hold and the suppression must hold.
            for (var step = 7; step <= 8; step++)
            {
                var elapsed = step * DecisionDelta;

                Assert.That(brain.IsReacting(elapsed), Is.True, $"elapsed {elapsed}");
                Assert.That(Step(elapsed, swapOpponent: true), Is.False, $"no decision at {elapsed}");
            }

            // Past the window the brain is free to answer the new situation again.
            Assert.That(brain.IsReacting(reactionEnd), Is.False);
            Assert.That(Step(reactionEnd, swapOpponent: true), Is.True, "the brain must resume deciding");
        }

        [Test]
        public void InvalidateDecision_ForcesAnImmediateReDecision()
        {
            var brain = NewBrain(ObserverArchetype());

            brain.Tick(DecisionDelta, Context());
            brain.InvalidateDecision();

            var decided = brain.Tick(0f, Context(elapsedSeconds: DecisionDelta));

            Assert.That(decided, Is.True);
        }

        // --- Behavioural consequences ---------------------------------------------------

        /// <summary>
        /// The orderings below are the reason the weights exist at all. If a consideration could not
        /// change a candidate's score in the direction its weight implies, the archetype would be
        /// tuning nothing.
        /// </summary>
        [Test]
        public void MeasureFit_ScoresAnUnreachableGuardChangeLowerThanOneInRange()
        {
            var table = TransitionTable.CreateDefault();
            table.TryGet(GuardId.VomTag, GuardId.Alber, out var profile);

            var measureFit = FindConsideration(NewBrain(), "Measure fit");

            Assert.That(measureFit, Is.Not.Null);

            var inRange = measureFit.Score(Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, profile.MaxDistance - 0.4f),
                FencingAction.ChangeGuard(GuardId.Alber),
                table: table));

            var outOfRange = measureFit.Score(Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, profile.MaxDistance + 0.4f),
                FencingAction.ChangeGuard(GuardId.Alber),
                table: table));

            Assert.That(outOfRange, Is.LessThan(inRange));
        }

        [Test]
        public void MeasureFit_RespondsToBeingWounded()
        {
            var archetype = ObserverArchetype();
            var measureFit = new Spellbreakers.Combat.Ai.Considerations.MeasureFitConsideration();

            var healthy = measureFit.Score(Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, 1.5f),
                FencingAction.Wait,
                selfHealthFraction: HealthyFraction,
                archetype: archetype));

            var wounded = measureFit.Score(Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, 1.5f),
                FencingAction.Wait,
                selfHealthFraction: WoundedFraction,
                archetype: archetype));

            Assert.That(wounded, Is.Not.EqualTo(healthy).Within(Tolerance));
        }

        [Test]
        public void Choice_IsAlwaysOneOfTheScoredCandidates()
        {
            var brain = NewBrain(ObserverArchetype());

            brain.Tick(DecisionDelta, Context());

            Assert.That(brain.Choice, Is.Not.Null);
            var names = new List<string>();
            foreach (var scored in brain.Scores)
                names.Add(scored.Action.Name);

            Assert.That(names, Does.Contain(brain.Choice.Action.Name));
        }

        [Test]
        public void Scores_ExplainTheirOwnArithmetic()
        {
            var brain = NewBrain(ObserverArchetype());
            brain.Tick(DecisionDelta, Context());

            foreach (var scored in brain.Scores)
            {
                var explanation = scored.Explain();

                Assert.That(explanation, Does.Contain(scored.Action.Name));
                foreach (var breakdown in scored.Breakdown)
                {
                    Assert.That(explanation, Does.Contain(breakdown.Name));
                    Assert.That(breakdown.Contribution,
                        Is.EqualTo(breakdown.Weight * breakdown.Score).Within(Tolerance));
                }
            }
        }

        [Test]
        public void LastRequestedGuard_RemembersAGuardChangeSoItCanAvoidRepeatingIt()
        {
            var table = TransitionTable.CreateDefault();
            var archetype = ObserverArchetype();
            archetype.PredictabilityWeight = 1000f;
            archetype.Jitter = 0f;

            var brain = NewBrain(archetype, table);
            var elapsed = 0f;

            for (var i = 0; i < 6; i++)
            {
                brain.Tick(DecisionDelta, Context(GuardId.VomTag, Opponent(), FencingAction.Wait, elapsed));
                elapsed += DecisionDelta;
            }

            Assert.That(brain.Choice, Is.Not.Null);
            if (brain.Choice.Action.Kind == FencingActionKind.ChangeGuard)
                Assert.That(brain.LastRequestedGuard, Is.EqualTo(brain.Choice.Action.TargetGuard));
        }

        [Test]
        public void Constructor_RefusesMissingRules()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new FencingBrain(null, ParryTable.CreateDefault(), ObserverArchetype()));
            Assert.Throws<System.ArgumentNullException>(() =>
                new FencingBrain(TransitionTable.CreateDefault(), null, ObserverArchetype()));
            Assert.Throws<System.ArgumentNullException>(() =>
                new FencingBrain(TransitionTable.CreateDefault(), ParryTable.CreateDefault(), null));
        }

        [Test]
        public void Archetype_IsExposedForTuningAndIntrospection()
        {
            var archetype = ObserverArchetype();
            var brain = NewBrain(archetype);

            Assert.That(brain.Archetype, Is.SameAs(archetype));
            Assert.That(brain.Considerations.Count, Is.EqualTo(8));
        }

        /// <summary>
        /// A brain that could pick an unlocked candidate would let the AI request a guard change the
        /// player's own rules would refuse.
        /// </summary>
        [Test]
        public void SafeSweep_KeepsEveryChoiceInsideTheUnitRange()
        {
            var brain = NewBrain(ObserverArchetype());
            var elapsed = 0f;

            for (var i = 0; i < SweepSteps; i++)
            {
                var distance = 0.2f + (5.8f * (i / (float)SweepSteps));
                brain.Tick(DecisionDelta, Context(
                    GuardId.VomTag,
                    Opponent(GuardId.Ochs, distance, isAlive: i % 2 == 0),
                    FencingAction.Wait,
                    elapsed));

                elapsed += DecisionDelta;

                if (brain.Choice == null)
                    continue;

                Assert.That(brain.Choice.Score, Is.GreaterThanOrEqualTo(0f));
                Assert.That(brain.Choice.Score, Is.LessThanOrEqualTo(1f));
            }

            Assert.That(brain.Choice, Is.Not.Null);
        }
    }
}

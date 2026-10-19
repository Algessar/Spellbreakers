using NUnit.Framework;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;
using Spellbreakers.Combat.Ai.Considerations;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// Avoidance is the defensive option for a combatant that cannot parry - a creature with nothing hard
    /// enough to turn a blade. It has to be a genuine alternative to meeting the attack, which means the
    /// brain must only propose it where the creature can actually leave the line, and must weigh it
    /// against the real threat rather than at random.
    ///
    /// The evade is a lateral slip, so it deliberately does *not* change the measure: retreating from a
    /// committed attack loses on distance, whereas stepping off the line does not.
    /// </summary>
    [TestFixture]
    public sealed class EvasionTests
    {
        private const float Tolerance = 1e-5f;
        private const float ReferenceDistance = 1.6f;

        /// <summary>An elapsed time comfortably past any archetype's reaction lag.</summary>
        private const float SettledElapsedSeconds = 5f;

        private static readonly TransitionTable Table = TransitionTable.CreateDefault();
        private static readonly ParryTable Parry = ParryTable.CreateDefault();

        private static OpponentSnapshot Opponent(
            bool bladeLive = false,
            float progress01 = 0f,
            GuardId guard = GuardId.Ochs,
            bool inTransition = true)
        {
            GuardTransitionState transition = default;
            if (inTransition && Table.TryGet(guard, GuardId.Alber, out var profile))
                transition = new GuardTransitionState(profile);

            return new OpponentSnapshot(
                guard,
                transition,
                progress01,
                bladeLive,
                false,
                ReferenceDistance,
                Measure.Krieg,
                1f,
                true,
                0f);
        }

        private static FencingContext Context(
            FencingAction action,
            DefenceMode defenceMode,
            OpponentSnapshot opponent,
            FencingArchetype archetype = null,
            float elapsedSeconds = SettledElapsedSeconds)
        {
            return new FencingContext(
                GuardId.VomTag,
                default,
                0f,
                1f,
                ReactionState.None,
                true,
                1.4f,
                elapsedSeconds,
                opponent,
                action,
                Table,
                Parry,
                archetype ?? FencingArchetype.CreateFencer(),
                GuardId.None,
                0.5f,
                defenceMode);
        }

        /// <summary>
        /// Runs a brain to a real decision. The first tick only establishes the opponent and starts the
        /// reaction clock, so a single call never produces a choice - which is the intended behaviour.
        /// </summary>
        private static void Decide(FencingBrain brain, DefenceMode defenceMode, OpponentSnapshot opponent)
        {
            brain.Tick(0.1f, Context(FencingAction.Wait, defenceMode, opponent));
            brain.Tick(0.1f, Context(FencingAction.Wait, defenceMode, opponent, null, SettledElapsedSeconds + 1f));
        }

        // --- The action itself -----------------------------------------------------------

        [Test]
        public void Evade_NeedsADistanceToBeMeaningful()
        {
            Assert.That(FencingAction.Evade(0f).Kind, Is.EqualTo(FencingActionKind.Wait));
            Assert.That(FencingAction.Evade(-2f).Kind, Is.EqualTo(FencingActionKind.Wait));
        }

        [Test]
        public void Evade_MovesAcrossTheLineAndNotAlongIt()
        {
            var evade = FencingAction.Evade(0.9f);

            Assert.That(evade.Kind, Is.EqualTo(FencingActionKind.Evade));
            Assert.That(evade.LateralDistance, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(evade.StepDistance, Is.EqualTo(0f), "an evade must not change the measure");
            Assert.That(evade.IsMovement, Is.True);
            Assert.That(evade.IsLineMovement, Is.False);
            Assert.That(evade.IsInLine, Is.False);
        }

        [Test]
        public void Evade_LeavesTheProjectedDistanceUntouched()
        {
            var opponent = Opponent();
            var evade = Context(FencingAction.Evade(0.9f), DefenceMode.Avoid, opponent);
            var wait = Context(FencingAction.Wait, DefenceMode.Avoid, opponent);

            Assert.That(evade.ProjectedDistance, Is.EqualTo(wait.ProjectedDistance).Within(Tolerance));
        }

        [Test]
        public void SteppingAndRetreatingStillChangeTheProjectedDistance()
        {
            var opponent = Opponent();
            var step = Context(FencingAction.Step(0.6f), DefenceMode.Both, opponent);
            var retreat = Context(FencingAction.Retreat(0.6f), DefenceMode.Both, opponent);

            Assert.That(step.ProjectedDistance, Is.LessThan(ReferenceDistance));
            Assert.That(retreat.ProjectedDistance, Is.GreaterThan(ReferenceDistance));
        }

        // --- The consideration -----------------------------------------------------------

        [Test]
        public void EvasionScoresNothingForACreatureThatCannotAvoid()
        {
            var consideration = new EvasionConsideration();
            var context = Context(FencingAction.Evade(0.9f), DefenceMode.Parry, Opponent(bladeLive: true));

            Assert.That(consideration.Score(context), Is.EqualTo(0f));
        }

        [Test]
        public void ThreatIsMaximalWhileTheBladeIsLive()
        {
            var live = Context(FencingAction.Evade(0.9f), DefenceMode.Avoid, Opponent(bladeLive: true));

            Assert.That(EvasionConsideration.Threat(live), Is.EqualTo(1f).Within(Tolerance));
        }

        /// <summary>Reading the telegraph has to pay, so threat must rise before the blade goes live.</summary>
        [Test]
        public void ThreatRisesAsTheBladeApproachesGoingLive()
        {
            var early = Context(FencingAction.Evade(0.9f), DefenceMode.Avoid, Opponent(progress01: 0.05f));
            var late = Context(FencingAction.Evade(0.9f), DefenceMode.Avoid, Opponent(progress01: 0.24f));

            Assert.That(EvasionConsideration.Threat(late), Is.GreaterThan(EvasionConsideration.Threat(early)));
            Assert.That(EvasionConsideration.Threat(early), Is.GreaterThan(0f));
        }

        [Test]
        public void EvadingBeatsStayingInTheLineWhenAThreatIsLive()
        {
            var consideration = new EvasionConsideration();
            var opponent = Opponent(bladeLive: true);

            var evade = consideration.Score(Context(FencingAction.Evade(0.9f), DefenceMode.Avoid, opponent));
            var wait = consideration.Score(Context(FencingAction.Wait, DefenceMode.Avoid, opponent));

            Assert.That(evade, Is.GreaterThan(wait));
        }

        /// <summary>
        /// A creature that can also parry already has an answer, so it should be pulled towards slipping
        /// less strongly than one that cannot - otherwise avoidance crowds out parrying entirely.
        /// </summary>
        [Test]
        public void EvasionIsWeightedLowerForACombatantThatCanAlsoParry()
        {
            var consideration = new EvasionConsideration();
            var opponent = Opponent(bladeLive: true);

            var avoidOnly = consideration.Score(Context(FencingAction.Evade(0.9f), DefenceMode.Avoid, opponent));
            var both = consideration.Score(Context(FencingAction.Evade(0.9f), DefenceMode.Both, opponent));

            Assert.That(both, Is.LessThan(avoidOnly));
        }

        // --- Wiring into the brain -------------------------------------------------------

        [Test]
        public void TheBrainOnlyProposesAnEvadeWhenTheCombatantCanAvoid()
        {
            var opponent = Opponent(bladeLive: true);
            var canAvoid = new FencingBrain(Table, Parry, FencingArchetype.CreateFencer());

            Decide(canAvoid, DefenceMode.Avoid, opponent);

            var proposedEvade = false;
            foreach (var scored in canAvoid.Scores)
            {
                if (scored.Action.Kind == FencingActionKind.Evade)
                    proposedEvade = true;
            }

            Assert.That(proposedEvade, Is.True, "an avoiding creature must be able to choose a slip");

            var cannotAvoid = new FencingBrain(Table, Parry, FencingArchetype.CreateFencer());
            Decide(cannotAvoid, DefenceMode.Parry, opponent);

            foreach (var scored in cannotAvoid.Scores)
                Assert.That(scored.Action.Kind, Is.Not.EqualTo(FencingActionKind.Evade));
        }

        [Test]
        public void EveryCandidateIsScoredOnEveryAxisIncludingEvasion()
        {
            var brain = new FencingBrain(Table, Parry, FencingArchetype.CreateFencer());

            Decide(brain, DefenceMode.Avoid, Opponent(bladeLive: true));

            Assert.That(brain.Scores.Count, Is.GreaterThan(0));
            foreach (var scored in brain.Scores)
                Assert.That(scored.Breakdown.Length, Is.EqualTo(brain.Considerations.Count), scored.Action.Name);
        }
    }
}

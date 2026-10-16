using System.Collections.Generic;
using NUnit.Framework;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;
using Spellbreakers.Combat.Ai.Considerations;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// A consideration is the only place a behavioural judgement is allowed to live. Every one must be
    /// total: it has to return a sane 0..1 score for any world the game can present, including a
    /// missing opponent, a live blade in its face and a combatant at death's door.
    /// </summary>
    [TestFixture]
    public sealed class ConsiderationTests
    {
        private const float Tolerance = 1e-5f;
        private const float NearDistance = 0.2f;
        private const float FarDistance = 6f;
        private const float WoundedFraction = 0.1f;
        private const float HealthyFraction = 1f;

        private static TransitionTable Table()
        {
            return TransitionTable.CreateDefault();
        }

        private static FencingArchetype Archetype()
        {
            return FencingArchetype.CreateFencer();
        }

        private static OpponentSnapshot Opponent(
            GuardId guard = GuardId.Ochs,
            float distance = 1.6f,
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
            TransitionTable table = null,
            ParryTable parryTable = null,
            FencingArchetype archetype = null,
            GuardTransitionState selfTransition = default,
            float selfProgress01 = 0f,
            float selfHealthFraction = HealthyFraction,
            ReactionState selfReaction = ReactionState.None,
            bool selfVor = false,
            GuardId lastRequestedGuard = GuardId.None)
        {
            var resolvedTable = table ?? Table();
            return new FencingContext(
                selfGuard,
                selfTransition,
                selfProgress01,
                selfHealthFraction,
                selfReaction,
                selfVor,
                resolvedTable.GuardOr(selfGuard).TipReach,
                0f,
                opponent,
                action,
                resolvedTable,
                parryTable ?? ParryTable.CreateDefault(),
                archetype ?? Archetype(),
                lastRequestedGuard,
                0.37f);
        }

        private static Consideration[] AllConsiderations()
        {
            return new Consideration[]
            {
                new MeasureFitConsideration(),
                new GuardMatchupConsideration(),
                new OpeningReadConsideration(),
                new InitiativeConsideration(),
                new CommitRiskConsideration(),
                new SpacingConsideration(),
                new PredictabilityConsideration(),
                new BaselineConsideration(),
            };
        }

        private static FencingAction[] AllActions()
        {
            return new FencingAction[]
            {
                FencingAction.Wait,
                FencingAction.Step(0.6f),
                FencingAction.Retreat(0.6f),
                FencingAction.ChangeGuard(GuardId.Alber),
            };
        }

        /// <summary>
        /// The sweep the brain depends on: a consideration that could return NaN or leave 0..1 would
        /// poison the weighted average and make the comparison between candidates meaningless.
        /// </summary>
        [Test]
        public void EveryConsideration_StaysInsideTheUnitRangeAcrossABroadSweep()
        {
            var considerations = AllConsiderations();
            var table = Table();
            var archetype = Archetype();
            var checked_ = 0;

            for (var step = 0; step <= 29; step++)
            {
                var distance = NearDistance + (FarDistance - NearDistance) * (step / 29f);

                foreach (var selfGuard in GuardIds.All)
                {
                    foreach (var opponentGuard in GuardIds.All)
                    {
                        foreach (var action in AllActions())
                        {
                            foreach (var health in new[] { WoundedFraction, HealthyFraction })
                            {
                                var context = Context(
                                    selfGuard,
                                    Opponent(opponentGuard, distance),
                                    action,
                                    table,
                                    ParryTable.CreateDefault(),
                                    archetype,
                                    selfHealthFraction: health);

                                foreach (var consideration in considerations)
                                {
                                    var score = consideration.Score(context);

                                    Assert.That(score, Is.GreaterThanOrEqualTo(0f),
                                        $"{consideration.Name} at {distance} from {selfGuard} vs {opponentGuard}");
                                    Assert.That(score, Is.LessThanOrEqualTo(1f),
                                        $"{consideration.Name} at {distance} from {selfGuard} vs {opponentGuard}");
                                    checked_++;
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(checked_, Is.GreaterThan(1000));
        }

        [Test]
        public void EveryConsideration_ReturnsASaneScoreWithNoOpponent()
        {
            var context = Context(opponent: OpponentSnapshot.None);

            foreach (var consideration in AllConsiderations())
            {
                var score = consideration.Score(context);

                Assert.That(score, Is.GreaterThanOrEqualTo(0f), consideration.Name);
                Assert.That(score, Is.LessThanOrEqualTo(1f), consideration.Name);
            }
        }

        [Test]
        public void EveryConsideration_HasANameAndAWeightForItsArchetype()
        {
            var archetype = Archetype();

            foreach (var consideration in AllConsiderations())
            {
                Assert.That(consideration.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(consideration.WeightIn(archetype), Is.GreaterThanOrEqualTo(0f), consideration.Name);
                Assert.That(consideration.Curve, Is.Not.Null, consideration.Name);
            }
        }

        [Test]
        public void WeightIn_ComesFromTheArchetypeSoDifficultyLivesInData()
        {
            var archetype = Archetype();
            archetype.SpacingWeight = 0f;
            archetype.MeasureFitWeight = 2.5f;

            var considerations = AllConsiderations();
            var spacing = System.Array.Find(considerations, c => c is SpacingConsideration);
            var measureFit = System.Array.Find(considerations, c => c is MeasureFitConsideration);

            Assert.That(spacing.WeightIn(archetype), Is.EqualTo(0f));
            Assert.That(measureFit.WeightIn(archetype), Is.EqualTo(2.5f).Within(Tolerance));
        }

        /// <summary>
        /// The brain should prefer closing over committing when the candidate's own transition cannot
        /// reach: the same swing becomes a feint out of measure, which is the doc's Zufechten rule.
        /// </summary>
        [Test]
        public void MeasureFit_PenalisesAGuardChangeTheProfileCannotReach()
        {
            var table = Table();
            var contextWithWait = Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, 2.8f),
                FencingAction.Wait,
                table);

            // Vom Tag -> Alber is a real profile; 2.8m is beyond its authored 2.6m reach.
            table.TryGet(GuardId.VomTag, GuardId.Alber, out var profile);
            var outOfRange = Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, profile.MaxDistance + 0.2f),
                FencingAction.ChangeGuard(GuardId.Alber),
                table);

            var consideration = new MeasureFitConsideration();
            var waitScore = consideration.Score(contextWithWait);
            var outOfRangeScore = consideration.Score(outOfRange);

            Assert.That(profile.MaxDistance, Is.EqualTo(2.6f).Within(Tolerance));
            Assert.That(outOfRangeScore, Is.LessThan(waitScore));
        }

        [Test]
        public void MeasureFit_RewardsTheCandidateThatFixesTheDistance()
        {
            var table = Table();
            var measureFit = new MeasureFitConsideration();

            // Far outside the preferred band: stepping in must score better than waiting or retreating.
            var wait = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 4.5f), FencingAction.Wait, table);
            var step = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 4.5f), FencingAction.Step(0.6f), table);
            var retreat = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 4.5f), FencingAction.Retreat(0.6f), table);

            Assert.That(measureFit.Score(step), Is.GreaterThan(measureFit.Score(wait)));
            Assert.That(measureFit.Score(step), Is.GreaterThan(measureFit.Score(retreat)));
        }

        [Test]
        public void Spacing_PrefersTheMovementThatClosesTheGap()
        {
            var table = Table();
            var spacing = new SpacingConsideration();

            var step = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 4.5f), FencingAction.Step(0.6f), table);
            var retreat = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 4.5f), FencingAction.Retreat(0.6f), table);

            Assert.That(spacing.Score(step), Is.GreaterThan(spacing.Score(retreat)));
        }

        [Test]
        public void Spacing_PrefersRetreatingWhenTooClose()
        {
            var table = Table();
            var spacing = new SpacingConsideration();

            // 0.3m is inside the preferred minimum of 1.1m, so opening up is the right correction.
            var step = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 0.3f), FencingAction.Step(0.6f), table);
            var retreat = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 0.3f), FencingAction.Retreat(0.6f), table);

            Assert.That(spacing.Score(retreat), Is.GreaterThan(spacing.Score(step)));
        }

        /// <summary>The doc's disengage-when-hurt behaviour falls out of the preferred band widening,
        /// not out of a separate flee state.</summary>
        [Test]
        public void MeasureFit_BehavesDifferentlyWhenWounded()
        {
            var table = Table();
            var archetype = Archetype();
            var measureFit = new MeasureFitConsideration();

            // 1.5m sits inside the healthy preferred band but outside the wounded one.
            var healthy = Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, 1.5f),
                FencingAction.Wait,
                table,
                archetype: archetype,
                selfHealthFraction: HealthyFraction);

            var wounded = Context(
                GuardId.VomTag,
                Opponent(GuardId.Ochs, 1.5f),
                FencingAction.Wait,
                table,
                archetype: archetype,
                selfHealthFraction: WoundedFraction);

            Assert.That(archetype.PreferredMinDistance, Is.EqualTo(1.1f).Within(Tolerance));
            Assert.That(archetype.PreferredMaxDistance, Is.EqualTo(2.1f).Within(Tolerance));
            Assert.That(measureFit.Score(healthy), Is.Not.EqualTo(measureFit.Score(wounded)).Within(Tolerance));
        }

        [Test]
        public void Predictability_PunishesRepeatingTheSameGuard()
        {
            var table = Table();
            var predictability = new PredictabilityConsideration();
            var action = FencingAction.ChangeGuard(GuardId.Alber);

            var fresh = Context(
                GuardId.VomTag,
                Opponent(),
                action,
                table,
                lastRequestedGuard: GuardId.Pflug);
            var repeated = Context(
                GuardId.VomTag,
                Opponent(),
                action,
                table,
                lastRequestedGuard: GuardId.Alber);

            Assert.That(predictability.Score(repeated), Is.LessThan(predictability.Score(fresh)));
        }

        [Test]
        public void Predictability_IsNeutralForEverythingThatIsNotAGuardChange()
        {
            var table = Table();
            var predictability = new PredictabilityConsideration();

            Assert.That(predictability.Score(Context(action: FencingAction.Wait, table: table)),
                Is.EqualTo(predictability.Score(Context(action: FencingAction.Step(0.6f), table: table))).Within(Tolerance));
        }

        [Test]
        public void Baseline_IsAFlatOptionSoWaitingRemainsGenuine()
        {
            var baseline = new BaselineConsideration();
            var table = Table();

            var wait = baseline.Score(Context(GuardId.VomTag, Opponent(), FencingAction.Wait, table));
            var attack = baseline.Score(Context(
                GuardId.VomTag,
                Opponent(),
                FencingAction.ChangeGuard(GuardId.Alber),
                table));

            Assert.That(wait, Is.EqualTo(attack).Within(Tolerance));
            Assert.That(wait, Is.GreaterThan(0f));
        }

        [Test]
        public void CommitRisk_FavoursWaitingWhileTheOpponentsBladeIsLive()
        {
            var table = Table();
            var commitRisk = new CommitRiskConsideration();

            var liveBlade = Opponent(GuardId.Ochs, bladeLive: true);
            var wait = commitRisk.Score(Context(GuardId.VomTag, liveBlade, FencingAction.Wait, table));
            var attack = commitRisk.Score(Context(
                GuardId.VomTag,
                liveBlade,
                FencingAction.ChangeGuard(GuardId.Alber),
                table));

            Assert.That(wait, Is.GreaterThan(attack));
        }

        [Test]
        public void OpeningRead_SeesRecoveryAsTheBestWindow()
        {
            var table = Table();
            var opening = new OpeningReadConsideration();

            table.TryGet(GuardId.Ochs, GuardId.Langort, out var recoverable);
            var recovery = new GuardTransitionState(recoverable);

            // Past the damage window end, an opponent in transition is showing a punishable opening.
            var recovering = Opponent(
                GuardId.Ochs,
                transition: recovery,
                progress01: recoverable.DamageWindowEnd + 0.05f,
                bladeLive: false);
            var live = Opponent(GuardId.Ochs, transition: recovery, progress01: 0.45f, bladeLive: true);

            var attackingDuringRecovery = opening.Score(Context(
                GuardId.VomTag,
                recovering,
                FencingAction.ChangeGuard(GuardId.Alber),
                table));
            var attackingIntoALiveBlade = opening.Score(Context(
                GuardId.VomTag,
                live,
                FencingAction.ChangeGuard(GuardId.Alber),
                table));

            Assert.That(attackingDuringRecovery, Is.GreaterThan(attackingIntoALiveBlade));
        }

        [Test]
        public void GuardMatchup_ScoresTheGuardTheCandidateWouldLeaveUsIn()
        {
            var table = Table();
            var parryTable = ParryTable.CreateDefault();
            var matchup = new GuardMatchupConsideration();
            var opponent = Opponent(GuardId.Ochs);

            var intoOchs = matchup.Score(Context(
                GuardId.VomTag,
                opponent,
                FencingAction.ChangeGuard(GuardId.Ochs),
                table,
                parryTable));
            var intoAlber = matchup.Score(Context(
                GuardId.VomTag,
                opponent,
                FencingAction.ChangeGuard(GuardId.Alber),
                table,
                parryTable));

            Assert.That(intoOchs, Is.Not.EqualTo(intoAlber).Within(Tolerance));
        }

        [Test]
        public void Initiative_RewardsActingWhileHoldingVor()
        {
            var table = Table();
            var initiative = new InitiativeConsideration();
            var action = FencingAction.Step(0.6f);

            var withoutVor = Context(GuardId.VomTag, Opponent(), action, table, selfVor: false);
            var withVor = Context(GuardId.VomTag, Opponent(), action, table, selfVor: true);

            Assert.That(initiative.Score(withVor), Is.GreaterThan(initiative.Score(withoutVor)));
        }

        [Test]
        public void ConsiderationMath_DefensiveValueRanksAParryAboveABindAboveAHit()
        {
            Assert.That(ConsiderationMath.DefensiveValue(CombatOutcome.Parry),
                Is.GreaterThan(ConsiderationMath.DefensiveValue(CombatOutcome.Deflect)));
            Assert.That(ConsiderationMath.DefensiveValue(CombatOutcome.Deflect),
                Is.GreaterThan(ConsiderationMath.DefensiveValue(CombatOutcome.Bind)));
            Assert.That(ConsiderationMath.DefensiveValue(CombatOutcome.Bind),
                Is.GreaterThan(ConsiderationMath.DefensiveValue(CombatOutcome.HitBody)));
        }

        /// <summary>An unknown defensive answer is not the same as a known bad one, but it must not be
        /// scored as safety either.</summary>
        [Test]
        public void ConsiderationMath_UnknownOutcomeScoresLowButNotZero()
        {
            var undefined = ConsiderationMath.DefensiveValue(CombatOutcome.Undefined);

            Assert.That(undefined, Is.GreaterThan(ConsiderationMath.DefensiveValue(CombatOutcome.HitBody)));
            Assert.That(undefined, Is.LessThan(ConsiderationMath.DefensiveValue(CombatOutcome.Bind)));
        }

        [Test]
        public void ConsiderationMath_DistanceErrorIsZeroOnlyInsideTheBand()
        {
            Assert.That(ConsiderationMath.DistanceError(1.5f, 1f, 2f), Is.EqualTo(0f));
            Assert.That(ConsiderationMath.DistanceError(0.5f, 1f, 2f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(ConsiderationMath.DistanceError(3f, 1f, 2f), Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void ConsiderationMath_ThreatDirectionsReadTheOpponentsLiveAttackFirst()
        {
            var table = Table();
            table.TryGet(GuardId.Ochs, GuardId.Langort, out var thrust);
            var live = new GuardTransitionState(thrust);

            var directions = new List<TransitionDirection>();
            ConsiderationMath.OpponentThreatDirections(
                Context(GuardId.VomTag, Opponent(GuardId.Ochs, transition: live), table: table),
                directions);

            Assert.That(directions, Is.EqualTo(new[] { TransitionDirection.Thrust }));
        }

        [Test]
        public void ConsiderationMath_ThreatDirectionsFallBackToTheOpponentsGuard()
        {
            var table = Table();
            var directions = new List<TransitionDirection>();

            ConsiderationMath.OpponentThreatDirections(
                Context(GuardId.VomTag, Opponent(GuardId.VomTag), table: table),
                directions);

            // Vom Tag attacks along Oberhau, Mittelhau and Thrust; Neutral is not a threat.
            Assert.That(directions, Is.Not.Empty);
            Assert.That(directions, Has.No.Member(TransitionDirection.Neutral));
            Assert.That(directions, Does.Contain(TransitionDirection.Oberhau));
        }

        /// <summary>An archetype is shared data, so cloning it must not let a running enemy's tuning
        /// rewrite the asset it came from.</summary>
        [Test]
        public void Archetype_CloneIsIndependentAndReportsItsTotalWeight()
        {
            var original = FencingArchetype.CreateVeteran();
            var clone = original.Clone();

            clone.AllowedTargetGuards[0] = GuardId.IronGate;
            clone.Aggression = 0f;

            Assert.That(original.AllowedTargetGuards[0], Is.Not.EqualTo(GuardId.IronGate));
            Assert.That(original.Aggression, Is.Not.EqualTo(0f));
            Assert.That(original.TotalWeight, Is.EqualTo(clone.TotalWeight).Within(Tolerance));
        }

        [Test]
        public void Archetype_EmptyOrNullGuardListAllowsEverything()
        {
            var archetype = FencingArchetype.CreateRecruit();

            archetype.AllowedTargetGuards = null;
            Assert.That(archetype.IsGuardAllowed(GuardId.IronGate), Is.True);

            archetype.AllowedTargetGuards = new GuardId[0];
            Assert.That(archetype.IsGuardAllowed(GuardId.IronGate), Is.True);

            // No guard is always refused, whatever the allow-list says.
            Assert.That(archetype.IsGuardAllowed(GuardId.None), Is.False);
        }

        [Test]
        public void Archetype_AllowedGuardListIsHonoured()
        {
            var archetype = FencingArchetype.CreateRecruit();
            archetype.AllowedTargetGuards = new[] { GuardId.Ochs };

            Assert.That(archetype.IsGuardAllowed(GuardId.Ochs), Is.True);
            Assert.That(archetype.IsGuardAllowed(GuardId.IronGate), Is.False);
        }

        [Test]
        public void Action_StepAndRetreatCarryTheirSignConvention()
        {
            Assert.That(FencingAction.Step(0.6f).StepDistance, Is.EqualTo(0.6f).Within(Tolerance));
            Assert.That(FencingAction.Step(0.6f).IsMovement, Is.True);
            Assert.That(FencingAction.Retreat(0.6f).StepDistance, Is.EqualTo(-0.6f).Within(Tolerance));
            Assert.That(FencingAction.Retreat(0.6f).IsMovement, Is.True);
            Assert.That(FencingAction.Wait.IsMovement, Is.False);
        }

        [Test]
        public void Action_ZeroMovementCollapsesToWaiting()
        {
            Assert.That(FencingAction.Step(0f).Kind, Is.EqualTo(FencingActionKind.Wait));
            Assert.That(FencingAction.Retreat(-1f).Kind, Is.EqualTo(FencingActionKind.Wait));
        }

        [Test]
        public void Context_ProjectsDistanceAndGuardForTheCandidate()
        {
            var table = Table();
            var step = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 2f), FencingAction.Step(0.6f), table);
            var retreat = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 2f), FencingAction.Retreat(0.6f), table);

            Assert.That(step.ProjectedDistance, Is.EqualTo(1.4f).Within(Tolerance));
            Assert.That(retreat.ProjectedDistance, Is.EqualTo(2.6f).Within(Tolerance));
            Assert.That(step.ProjectedGuard, Is.EqualTo(GuardId.VomTag));

            var change = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 2f), FencingAction.ChangeGuard(GuardId.Alber), table);
            Assert.That(change.ProjectedGuard, Is.EqualTo(GuardId.Alber));
        }

        /// <summary>Movement cannot close past contact, so the projected distance is floored rather than
        /// allowed to go negative and invert every distance comparison downstream.</summary>
        [Test]
        public void Context_ProjectedDistanceNeverGoesNegative()
        {
            var table = Table();
            var context = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 0.2f), FencingAction.Step(2f), table);

            Assert.That(context.ProjectedDistance, Is.GreaterThan(0f));
        }

        [Test]
        public void Context_WithActionKeepsEveryOtherFactIdentical()
        {
            var table = Table();
            var original = Context(GuardId.VomTag, Opponent(GuardId.Ochs, 2f), FencingAction.Wait, table);

            var swapped = original.WithAction(FencingAction.Step(0.6f), 0.9f);

            Assert.That(swapped.SelfGuard, Is.EqualTo(original.SelfGuard));
            Assert.That(swapped.Opponent.Guard, Is.EqualTo(original.Opponent.Guard));
            Assert.That(swapped.Action.Kind, Is.EqualTo(FencingActionKind.Step));
            Assert.That(swapped.Random01, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(swapped.TransitionTable, Is.SameAs(original.TransitionTable));
        }

        [Test]
        public void OpponentSnapshot_NoneReportsNoOpponent()
        {
            var none = OpponentSnapshot.None;

            Assert.That(none.HasOpponent, Is.False);
            Assert.That(none.InTransition, Is.False);
            Assert.That(none.IsCommitted, Is.False);
            Assert.That(none.Guard, Is.EqualTo(GuardId.None));
        }

        [Test]
        public void OpponentSnapshot_CommitmentFollowsTheObservedTransition()
        {
            var table = Table();
            table.TryGet(GuardId.Ochs, GuardId.Langort, out var profile);
            var transition = new GuardTransitionState(profile);

            var early = Opponent(GuardId.Ochs, transition: transition, progress01: profile.Commitment - 0.05f);
            var late = Opponent(GuardId.Ochs, transition: transition, progress01: profile.Commitment + 0.05f);

            Assert.That(early.IsCommitted, Is.False);
            Assert.That(late.IsCommitted, Is.True);
        }
    }
}

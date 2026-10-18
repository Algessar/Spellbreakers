using System.Collections.Generic;
using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The agent is the combat state machine both the player and every enemy run on. These tests drive it
    /// the way the game does - tiny ticks, explicit distances in - so the rules they pin (commitment,
    /// feints, reactions, phase timing) are the same rules the duel actually experiences.
    /// </summary>
    [TestFixture]
    public sealed class FencingAgentTests
    {
        private const float Tolerance = 1e-5f;

        // The frame the game would use, and enough ticks to finish the longest authored pass.
        private const float TickStep = 0.02f;
        private const float GenerousDistance = 1.2f;
        private const int MaxTicksForOneTransition = 40;

        private const GuardId StartGuard = GuardId.VomTag;
        private const GuardId AttackTarget = GuardId.Alber;
        private const GuardId SecondTarget = GuardId.Pflug;
        private const GuardId ThirdTarget = GuardId.Langort;

        [SetUp]
        public void SetUp()
        {
            CombatRules.ResetToDefaults();
        }

        // --- Construction ---------------------------------------------------------------

        private static FencingAgent NewAgent(GuardId guard = StartGuard)
        {
            return new FencingAgent(TransitionTable.CreateDefault(), guard);
        }

        private static FencingAgent NewAgentWithDistanceGate(GuardId from, GuardId to, out TransitionProfile profile)
        {
            var table = TransitionTable.CreateDefault();
            table.TryGet(from, to, out profile);
            profile.EnforceDistanceGate = true;

            return new FencingAgent(table, from);
        }

        /// <summary>
        /// A table with two guard profiles and no authored transitions, so a request is guaranteed to
        /// exercise synthesis. The default table cannot serve: 21 of its 25 non-Langort pairs are
        /// authored, and the four unauthored self-pairs are refused as no-ops.
        /// </summary>
        private static FencingAgent NewAgentWithoutAuthoredTransitions(GuardId from, GuardId to)
        {
            var table = new TransitionTable();
            table.AddGuard(new GuardProfile(from));
            table.AddGuard(new GuardProfile(to));

            return new FencingAgent(table, from);
        }

        /// <summary>Ticks until the transition finishes, so tests do not encode a magic tick count.</summary>
        private static int DriveToCompletion(FencingAgent agent, int maxTicks = MaxTicksForOneTransition)
        {
            var ticks = 0;
            while (agent.IsTransitioning && ticks < maxTicks)
            {
                agent.Tick(TickStep);
                ticks++;
            }

            return ticks;
        }

        [Test]
        public void NewAgent_StartsIdleInItsInitialGuard()
        {
            var agent = NewAgent();

            Assert.That(agent.Current, Is.EqualTo(StartGuard));
            Assert.That(agent.Previous, Is.EqualTo(StartGuard));
            Assert.That(agent.IsTransitioning, Is.False);
            Assert.That(agent.ActiveTransition.IsValid, Is.False);
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.None));
            Assert.That(agent.Progress01, Is.EqualTo(0f));
            Assert.That(agent.TipReach, Is.EqualTo(agent.Table.GuardOr(StartGuard).TipReach).Within(Tolerance));
            Assert.That(agent.IsAlive, Is.True);
        }

        /// <summary>A live duel is many small ticks. A clock that moved on zero or negative frames would
        /// stall or run backwards depending on the editor's paused frame timing.</summary>
        [Test]
        public void Tick_WithNonPositiveDeltaTime_DoesNothing()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);

            agent.Tick(0f);
            agent.Tick(-1f);

            Assert.That(agent.Elapsed, Is.EqualTo(0f));
            Assert.That(agent.Progress01, Is.EqualTo(0f));
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.ToPass));
        }

        // --- Request guard --------------------------------------------------------------

        [Test]
        public void RequestGuard_AcceptedPairStartsTheTransitionAndRaisesStartedOnce()
        {
            var agent = NewAgent();
            var started = 0;
            agent.TransitionStarted += _ => started++;

            var result = agent.RequestGuard(AttackTarget, GenerousDistance);

            Assert.That(result, Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.IsTransitioning, Is.True);
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.ToPass));
            Assert.That(agent.ActiveTransition.IsValid, Is.True);
            Assert.That(agent.ActiveTransition.From, Is.EqualTo(StartGuard));
            Assert.That(agent.ActiveTransition.To, Is.EqualTo(AttackTarget));
            Assert.That(started, Is.EqualTo(1));
        }

        [TestCase(GuardId.None)]
        public void RequestGuard_RejectsNoGuard(GuardId target)
        {
            Assert.That(NewAgent().RequestGuard(target, GenerousDistance), Is.EqualTo(RequestResult.Rejected));
        }

        /// <summary>
        /// Requesting the guard already held is not automatically a no-op: if a self-transition is
        /// authored for it (doc 3.2), it is a real attack - the Zornhau thrown from Vom Tag returns to
        /// Vom Tag. When nothing is authored, it is a genuine no-op and must be refused rather than
        /// silently starting a traversal to where the fighter already is.
        /// </summary>
        [Test]
        public void RequestGuard_OfTheGuardAlreadyHeldRunsTheAuthoredSelfTransition()
        {
            var agent = NewAgent(GuardId.VomTag);

            Assert.That(agent.RequestGuard(GuardId.VomTag, GenerousDistance), Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.IsTransitioning, Is.True);
            Assert.That(agent.ActiveTransition.IsSelfTransition, Is.True);
            Assert.That(agent.ActiveTransition.Profile.HasDamageWindow, Is.True);
        }

        [Test]
        public void RequestGuard_OfTheGuardAlreadyHeldIsANoOpWhenNoSelfTransitionIsAuthored()
        {
            // Alber has no authored self-transition, so asking for it says nothing at all.
            var agent = NewAgent(GuardId.Alber);

            Assert.That(agent.RequestGuard(GuardId.Alber, GenerousDistance), Is.EqualTo(RequestResult.Rejected));
            Assert.That(agent.IsTransitioning, Is.False);
        }

        /// <summary>
        /// Design doc 3.1: the table describes how a transition is expressed, not which transitions are
        /// mechanically permitted, so an unauthored pair is still traversable. Because nothing authored
        /// how it attacks, it resolves as a plain guard change with no damage window - movement is never
        /// forbidden, but no free attack is invented either.
        /// </summary>
        [Test]
        public void RequestGuard_AllowsAnUnauthoredPairAsANonDamagingTraversal()
        {
            var agent = NewAgentWithoutAuthoredTransitions(GuardId.IronGate, GuardId.VomTag);

            Assert.That(agent.Table.TryGet(GuardId.IronGate, GuardId.VomTag, out _), Is.False,
                "this pair genuinely has no authored expression");

            Assert.That(agent.RequestGuard(GuardId.VomTag, GenerousDistance), Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.IsTransitioning, Is.True);
            Assert.That(agent.ActiveTransition.Profile.HasDamageWindow, Is.False,
                "an unauthored traversal must not act as an attack");
            Assert.That(agent.ActiveTransition.Direction, Is.EqualTo(TransitionDirection.Neutral));
        }

        /// <summary>A table can opt out of synthesis when a creature genuinely has a fixed move list.</summary>
        [Test]
        public void RequestGuard_RejectsAnUnauthoredPairWhenSynthesisIsDisabled()
        {
            var agent = NewAgentWithoutAuthoredTransitions(GuardId.IronGate, GuardId.VomTag);
            agent.Table.AllowSynthesis = false;

            Assert.That(agent.RequestGuard(GuardId.VomTag, GenerousDistance), Is.EqualTo(RequestResult.Rejected));
            Assert.That(agent.IsTransitioning, Is.False);
        }

        /// <summary>
        /// Design doc 3.2: a transition may begin and end in the same guard - "Vom Tag -> Vom Tag =
        /// Zornhau". This is the end-to-end check that such a pair can actually be authored, looked up
        /// and executed, not merely constructed.
        /// </summary>
        [Test]
        public void RequestGuard_ReachesAnAuthoredSelfTransition()
        {
            var table = new TransitionTable();
            table.AddGuard(new GuardProfile(GuardId.VomTag));
            table.Add(new TransitionProfile(
                GuardId.VomTag,
                GuardId.VomTag,
                TransitionDirection.Oberhau,
                0.42f,
                0.3f,
                0.6f));

            var agent = new FencingAgent(table, GuardId.VomTag);

            Assert.That(agent.RequestGuard(GuardId.VomTag, GenerousDistance), Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.ActiveTransition.IsSelfTransition, Is.True);
            Assert.That(agent.ActiveTransition.Profile.HasDamageWindow, Is.True, "a self-transition is still an attack");
        }

        /// <summary>
        /// Iron Gate is a standing guard with its own binding, so every guard the player can hold must
        /// author a way into it. Acceptance alone would not prove that: an unauthored pair is still
        /// traversable as a plain, attackless synthesis, so the authored edge is asserted explicitly.
        /// </summary>
        [TestCase(GuardId.VomTag)]
        [TestCase(GuardId.Pflug)]
        [TestCase(GuardId.Alber)]
        [TestCase(GuardId.Ochs)]
        public void RequestGuard_CanEnterIronGateFromEveryStandingGuard(GuardId from)
        {
            var agent = NewAgent(from);

            Assert.That(agent.Table.TryGet(from, GuardId.IronGate, out _), Is.True,
                $"{from} -> Iron Gate must be authored, not merely synthesised");

            Assert.That(agent.RequestGuard(GuardId.IronGate, GenerousDistance), Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.IsTransitioning, Is.True);

            DriveToCompletion(agent);
            Assert.That(agent.Current, Is.EqualTo(GuardId.IronGate));
        }

        /// <summary>The player's guard switching is deliberately unrestricted; only an opt-in profile
        /// applies the measure gate, so the default must accept a request from any distance.</summary>
        [Test]
        public void RequestGuard_WithTheGateOff_AcceptsEvenFarBeyondMaxDistance()
        {
            var agent = NewAgent();
            agent.Table.TryGet(StartGuard, AttackTarget, out var profile);

            var result = agent.RequestGuard(AttackTarget, profile.MaxDistance + 50f);

            Assert.That(profile.EnforceDistanceGate, Is.False);
            Assert.That(result, Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.IsTransitioning, Is.True);
        }

        [Test]
        public void RequestGuard_WithTheGateOn_RejectsInsideMinimumDistance()
        {
            var agent = NewAgentWithDistanceGate(StartGuard, AttackTarget, out var profile);
            profile.MinDistance = 0.5f;

            Assert.That(agent.RequestGuard(AttackTarget, 0.1f), Is.EqualTo(RequestResult.Rejected));
            Assert.That(agent.IsTransitioning, Is.False);
        }

        [Test]
        public void RequestGuard_WithTheGateOn_FeintsBeyondMaximumDistanceAndAbortsItself()
        {
            var agent = NewAgentWithDistanceGate(StartGuard, AttackTarget, out var profile);
            var failures = 0;
            agent.TransitionFailed += (_, _) => failures++;

            var result = agent.RequestGuard(AttackTarget, profile.MaxDistance + 1f);

            Assert.That(result, Is.EqualTo(RequestResult.Feinted));
            Assert.That(agent.IsTransitioning, Is.True, "the feint starts before it aborts");

            // The feint beat is 0.2s; ticking past it must abort rather than resolve.
            for (var i = 0; i < 12; i++)
                agent.Tick(TickStep);

            Assert.That(agent.IsTransitioning, Is.False);
            Assert.That(agent.Current, Is.EqualTo(StartGuard));
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.None));
            Assert.That(failures, Is.EqualTo(1));
        }

        [Test]
        public void RequestGuard_WithTheGateOnAndFeintingDisabled_RejectsOutOfRangeInstead()
        {
            var agent = NewAgentWithDistanceGate(StartGuard, AttackTarget, out var profile);
            profile.FeintWhenOutOfRange = false;

            Assert.That(agent.RequestGuard(AttackTarget, profile.MaxDistance + 1f), Is.EqualTo(RequestResult.Rejected));
            Assert.That(agent.IsTransitioning, Is.False);
        }

        [Test]
        public void RequestGuard_WithTheGateOnAndFeintingDisabled_StillAcceptsInsideTheGate()
        {
            var agent = NewAgentWithDistanceGate(StartGuard, AttackTarget, out var profile);
            profile.FeintWhenOutOfRange = false;

            Assert.That(agent.RequestGuard(AttackTarget, profile.MaxDistance - 0.1f), Is.EqualTo(RequestResult.Accepted));
        }

        // --- Phase progression ----------------------------------------------------------

        /// <summary>
        /// The phase ladder is derived from the authored damage window rather than from animation time,
        /// so this test walks the whole pass and checks the blade is live in exactly one band of it.
        /// </summary>
        [Test]
        public void Phases_WalkFromToPassThroughStrikeAndSettleToNone()
        {
            var agent = NewAgent();
            agent.Table.TryGet(StartGuard, AttackTarget, out var profile);

            var visited = new List<PassPhase>();
            agent.PhaseChanged += phase => visited.Add(phase);

            agent.RequestGuard(AttackTarget, GenerousDistance);

            var liveTicks = 0;
            var bladeLiveOnlyDuringStrike = true;

            while (agent.IsTransitioning)
            {
                agent.Tick(TickStep);

                if (agent.Phase == PassPhase.Strike)
                {
                    liveTicks++;
                    bladeLiveOnlyDuringStrike &= agent.IsInDamageWindow;
                }
                else if (agent.IsInDamageWindow)
                {
                    bladeLiveOnlyDuringStrike = false;
                }
            }

            Assert.That(profile.DamageWindowStart, Is.EqualTo(0.30f).Within(Tolerance));
            Assert.That(liveTicks, Is.GreaterThan(0));
            Assert.That(bladeLiveOnlyDuringStrike, Is.True, "the blade must be live exactly while Phase is Strike");
            Assert.That(visited, Does.Contain(PassPhase.ToPass));
            Assert.That(visited, Does.Contain(PassPhase.Strike));
            Assert.That(visited, Does.Contain(PassPhase.Settle));

            // The ladder must be visited in order, never skipping back.
            Assert.That(visited.IndexOf(PassPhase.ToPass), Is.LessThan(visited.IndexOf(PassPhase.Strike)));
            Assert.That(visited.IndexOf(PassPhase.Strike), Is.LessThan(visited.IndexOf(PassPhase.Settle)));
            Assert.That(visited[visited.Count - 1], Is.EqualTo(PassPhase.None));
        }

        /// <summary>PhaseChanged is an animation/IK trigger. Firing it every tick inside a band would
        /// restart the same presentation beat repeatedly.</summary>
        [Test]
        public void PhaseChanged_RaisesTheLiveBladeTransitionExactlyOnce()
        {
            var agent = NewAgent();
            var strikesEntered = 0;
            var previous = agent.Phase;
            agent.PhaseChanged += phase =>
            {
                if (phase == PassPhase.Strike && previous != PassPhase.Strike)
                    strikesEntered++;

                previous = phase;
            };

            agent.RequestGuard(AttackTarget, GenerousDistance);
            DriveToCompletion(agent);

            Assert.That(strikesEntered, Is.EqualTo(1));
        }

        [Test]
        public void Completion_ArrivesInTheDestinationGuardWithAFreshParryGrace()
        {
            var agent = NewAgent();
            var completions = new List<GuardTransitionState>();
            var guardChanges = new List<GuardId>();
            agent.TransitionCompleted += state => completions.Add(state);
            agent.GuardChanged += guard => guardChanges.Add(guard);

            var measure = agent.ClassifyMeasure(GenerousDistance);
            var expectedGrace = agent.Table.GuardOr(AttackTarget).ResolveParryGrace(measure);

            agent.RequestGuard(AttackTarget, GenerousDistance);
            DriveToCompletion(agent);

            Assert.That(agent.Current, Is.EqualTo(AttackTarget));
            Assert.That(agent.Previous, Is.EqualTo(StartGuard));
            Assert.That(agent.IsTransitioning, Is.False);
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.None));
            Assert.That(agent.SecondsInGuard, Is.EqualTo(0f));
            Assert.That(agent.ParryWindowRemaining, Is.EqualTo(expectedGrace).Within(Tolerance));
            Assert.That(agent.TipReach, Is.EqualTo(agent.Table.GuardOr(AttackTarget).TipReach).Within(Tolerance));
            Assert.That(completions, Has.Count.EqualTo(1));
            Assert.That(completions[0].To, Is.EqualTo(AttackTarget));
            Assert.That(guardChanges, Has.Count.EqualTo(1));
            Assert.That(guardChanges[0], Is.EqualTo(AttackTarget));
        }

        // --- Commitment, queueing and cancellation --------------------------------------

        [Test]
        public void Commitment_TurnsTrueOnceProgressPassesTheProfilePoint()
        {
            var agent = NewAgent();
            agent.Table.TryGet(StartGuard, AttackTarget, out var profile);
            agent.RequestGuard(AttackTarget, GenerousDistance);

            Assert.That(agent.IsCommitted, Is.False, "a fresh transition is still cancellable");

            while (agent.IsTransitioning && agent.Progress01 < profile.Commitment)
                agent.Tick(TickStep);

            Assert.That(agent.Progress01, Is.GreaterThanOrEqualTo(profile.Commitment));
            Assert.That(agent.IsCommitted, Is.True);
        }

        /// <summary>Single-slot queue, last wins: mashing three guards must not stack three passes.</summary>
        [Test]
        public void RequestGuard_AfterCommitment_QueuesAndAutoStartsOnCompletion()
        {
            var agent = NewAgent();
            agent.Table.TryGet(StartGuard, AttackTarget, out var profile);
            var landed = new List<GuardId>();
            agent.TransitionCompleted += state => landed.Add(state.To);
            agent.RequestGuard(AttackTarget, GenerousDistance);

            while (agent.IsTransitioning && agent.Progress01 < profile.Commitment)
                agent.Tick(TickStep);

            Assert.That(agent.RequestGuard(SecondTarget, GenerousDistance), Is.EqualTo(RequestResult.Queued));
            Assert.That(agent.RequestGuard(ThirdTarget, GenerousDistance), Is.EqualTo(RequestResult.Queued));

            // The pass in flight must be the original one, not the queued request.
            Assert.That(agent.ActiveTransition.To, Is.EqualTo(AttackTarget));

            // Drive only the first pass. Completion auto-starts the queued guard, so ticking "while
            // transitioning" would run straight through both passes.
            while (landed.Count == 0 && agent.IsTransitioning)
                agent.Tick(TickStep);

            Assert.That(landed, Is.EqualTo(new[] { AttackTarget }), "the committed pass lands first");
            Assert.That(agent.IsTransitioning, Is.True, "the queued request starts immediately afterwards");
            Assert.That(agent.ActiveTransition.From, Is.EqualTo(AttackTarget));
            Assert.That(agent.ActiveTransition.To, Is.EqualTo(ThirdTarget), "only the last queued guard survives");
        }

        [Test]
        public void RequestGuard_BeforeCommitment_CancelsTheOldPassAndReplacesIt()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            agent.Tick(TickStep);

            var failures = 0;
            GuardTransitionState failed = default;
            agent.TransitionFailed += (state, _) =>
            {
                failures++;
                failed = state;
            };

            var result = agent.RequestGuard(SecondTarget, GenerousDistance);

            Assert.That(result, Is.EqualTo(RequestResult.Accepted));
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(failed.To, Is.EqualTo(AttackTarget));
            Assert.That(agent.ActiveTransition.To, Is.EqualTo(SecondTarget));
            Assert.That(agent.Progress01, Is.EqualTo(0f));
        }

        [Test]
        public void CancelTransition_WhenIdleIsASafeNoOp()
        {
            var agent = NewAgent();
            var failures = 0;
            agent.TransitionFailed += (_, _) => failures++;

            Assert.DoesNotThrow(() => agent.CancelTransition("nothing to cancel"));

            Assert.That(failures, Is.EqualTo(0));
            Assert.That(agent.Current, Is.EqualTo(StartGuard));
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.None));
        }

        /// <summary>
        /// The failure mode this guards against is a wedge: a cancelled transition whose state was left
        /// half-set would make every later request look like a duplicate of the guard still in flight.
        /// </summary>
        [Test]
        public void CancelTransition_LeavesTheAgentAbleToStartAgain()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            agent.Tick(TickStep);

            agent.CancelTransition("test cancel");

            Assert.That(agent.IsTransitioning, Is.False);
            Assert.That(agent.Current, Is.EqualTo(StartGuard));
            Assert.That(agent.ActiveTransition.IsValid, Is.False);
            Assert.That(agent.RequestGuard(AttackTarget, GenerousDistance), Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.IsTransitioning, Is.True);
        }

        // --- Reactions ------------------------------------------------------------------

        /// <summary>
        /// A reaction is what makes a landed hit a real change in the situation: the combat clock stops,
        /// so the defender cannot simply carry on transitioning through it.
        /// </summary>
        [Test]
        public void Reaction_FreezesTheClockAndRefusesRequestsUntilItExpires()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            agent.Tick(TickStep);
            agent.Tick(TickStep);

            var progressBefore = agent.Progress01;
            // The reaction's seconds are consumed one tick at a time, so share the remaining time exactly
            // and assert the frozen window just inside the boundary rather than on it.
            const float ReactionSeconds = 0.06f;
            agent.SetReaction(ReactionState.Hit, ReactionSeconds);

            for (var i = 0; i < 2; i++)
                agent.Tick(TickStep);

            Assert.That(agent.Reaction, Is.EqualTo(ReactionState.Hit), "0.04s is not enough to clear a 0.06s reaction");
            Assert.That(agent.Progress01, Is.EqualTo(progressBefore).Within(Tolerance));
            Assert.That(agent.RequestGuard(SecondTarget, GenerousDistance), Is.EqualTo(RequestResult.Rejected));

            // Keep ticking clearly past the reaction, but not for so long that the pass completes and
            // resets the clock: 0.06s of reaction plus a few more ticks leaves the pass mid-flight.
            for (var i = 0; i < 10; i++)
                agent.Tick(TickStep);

            Assert.That(agent.Reaction, Is.EqualTo(ReactionState.None));
            Assert.That(agent.IsTransitioning, Is.True);
            Assert.That(agent.Progress01, Is.GreaterThan(progressBefore));
        }

        [Test]
        public void Reaction_ReportsTheReactionItWasGiven()
        {
            var agent = NewAgent();
            var observed = new List<ReactionState>();
            agent.ReactionChanged += state => observed.Add(state);

            agent.ApplyHitReaction();

            Assert.That(agent.Reaction, Is.EqualTo(ReactionState.Hit));
            Assert.That(observed, Is.EqualTo(new[] { ReactionState.Hit }));
        }

        [Test]
        public void Dead_NeverExpiresAndRefusesFurtherReactions()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            agent.Tick(TickStep);
            var progressBefore = agent.Progress01;

            agent.SetReaction(ReactionState.Dead);

            for (var i = 0; i < 60; i++)
                agent.Tick(TickStep);

            Assert.That(agent.Reaction, Is.EqualTo(ReactionState.Dead));
            Assert.That(agent.IsAlive, Is.False);
            Assert.That(agent.Progress01, Is.EqualTo(progressBefore).Within(Tolerance));

            agent.SetReaction(ReactionState.Hit, 1f);

            Assert.That(agent.Reaction, Is.EqualTo(ReactionState.Dead), "death is terminal");
            Assert.That(agent.RequestGuard(SecondTarget, GenerousDistance), Is.EqualTo(RequestResult.Rejected));
        }

        [Test]
        public void Dead_StopsTheClockBeforeAnyOtherWork()
        {
            var agent = NewAgent();
            agent.SetReaction(ReactionState.Dead);

            agent.Tick(1f);

            Assert.That(agent.SecondsInGuard, Is.EqualTo(0f));
            Assert.That(agent.IsAlive, Is.False);
        }

        // --- Tempo ----------------------------------------------------------------------

        /// <summary>Vor is the doc's initiative advantage; it must be observable as a shorter pass rather
        /// than as a hidden damage bonus.</summary>
        [Test]
        public void Vor_ShortensTheObservedPass()
        {
            var withoutVor = NewAgent();
            var withVor = NewAgent();
            withVor.Vor = true;

            withoutVor.RequestGuard(AttackTarget, GenerousDistance);
            withVor.RequestGuard(AttackTarget, GenerousDistance);

            // Read the resolved duration before completing: completion resets it to zero.
            var withoutVorDuration = withoutVor.Duration;
            var withVorDuration = withVor.Duration;

            var withoutVorTicks = DriveToCompletion(withoutVor);
            var withVorTicks = DriveToCompletion(withVor);

            Assert.That(withVorDuration, Is.LessThan(withoutVorDuration));
            Assert.That(withVorTicks, Is.LessThan(withoutVorTicks));
        }

        [Test]
        public void ResolvedDuration_UsesTheAuthoredKriegDurationInCloseMeasure()
        {
            var agent = NewAgent();
            agent.Table.TryGet(StartGuard, AttackTarget, out var profile);

            // Langort reaches 1.8m, so 1.0m bands as Krieg rather than Zufechten.
            agent.RequestGuard(AttackTarget, 1.0f);

            Assert.That(agent.ClassifyMeasure(1.0f), Is.EqualTo(Measure.Krieg));
            Assert.That(agent.Duration, Is.EqualTo(profile.KriegDuration).Within(Tolerance));
        }

        // --- Parry windows --------------------------------------------------------------

        [Test]
        public void ArrivalGrace_OpensTheParryWindowAndClosesItWhenItElapses()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            DriveToCompletion(agent);

            Assert.That(agent.IsInParryWindow, Is.True, "a fresh arrival can still parry");
            Assert.That(agent.ParryWindowRemaining, Is.GreaterThan(0f));

            for (var i = 0; i < 20; i++)
                agent.Tick(TickStep);

            Assert.That(agent.ParryWindowRemaining, Is.EqualTo(0f));
            Assert.That(agent.IsInParryWindow, Is.False, "holding a guard is not a permanent block");
        }

        /// <summary>Meeting an attack with an attack is a real fencing answer, so mid-transition the
        /// parry window comes from the profile's own window rather than from an arrival grace.</summary>
        [Test]
        public void ActiveTransition_TracksItsOwnParryWindowInsteadOfAClosedGrace()
        {
            var agent = NewAgent();
            agent.Table.TryGet(StartGuard, AttackTarget, out var profile);

            // A defender whose profile cannot parry past 40% must not be saved by the guard's grace.
            profile.ParryWindowEnd = 0.40f;
            agent.RequestGuard(AttackTarget, GenerousDistance);

            while (agent.IsTransitioning && agent.Progress01 < 0.5f)
                agent.Tick(TickStep);

            Assert.That(agent.IsTransitioning, Is.True);
            Assert.That(agent.IsInParryWindow, Is.False);

            var stillOpen = NewAgent();
            stillOpen.RequestGuard(AttackTarget, GenerousDistance);
            while (stillOpen.IsTransitioning && stillOpen.Progress01 < 0.5f)
                stillOpen.Tick(TickStep);

            Assert.That(stillOpen.IsInParryWindow, Is.True, "the default authored window is still open at half progress");
        }

        [Test]
        public void RefreshParryWindow_ResetsTheArrivalGraceAndTheTimeInGuard()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            DriveToCompletion(agent);

            for (var i = 0; i < 20; i++)
                agent.Tick(TickStep);

            Assert.That(agent.ParryWindowRemaining, Is.EqualTo(0f));

            var expected = agent.Table.GuardOr(agent.Current).ResolveParryGrace(Measure.Zufechten);
            agent.RefreshParryWindow(Measure.Zufechten);

            Assert.That(agent.SecondsInGuard, Is.EqualTo(0f));
            Assert.That(agent.ParryWindowRemaining, Is.EqualTo(expected).Within(Tolerance));
            Assert.That(agent.IsInParryWindow, Is.True);
        }

        // --- ForceGuard -----------------------------------------------------------------

        [Test]
        public void ForceGuard_SetsTheGuardWithoutPlayingOrCompletingAPass()
        {
            var agent = NewAgent();
            var completions = 0;
            var guardChanges = 0;
            agent.TransitionCompleted += _ => completions++;
            agent.GuardChanged += _ => guardChanges++;

            agent.ForceGuard(SecondTarget);

            Assert.That(agent.Current, Is.EqualTo(SecondTarget));
            Assert.That(agent.Previous, Is.EqualTo(StartGuard));
            Assert.That(agent.IsTransitioning, Is.False);
            Assert.That(agent.Phase, Is.EqualTo(PassPhase.None));
            Assert.That(agent.SecondsInGuard, Is.EqualTo(0f));
            Assert.That(completions, Is.EqualTo(0), "a forced pose is not a completed transition");
            Assert.That(guardChanges, Is.EqualTo(1));
            Assert.That(agent.ParryWindowRemaining,
                Is.EqualTo(agent.Table.GuardOr(SecondTarget).ParryGraceSeconds).Within(Tolerance));
        }

        [Test]
        public void ForceGuard_AbandonsAnyPassInFlight()
        {
            var agent = NewAgent();
            agent.RequestGuard(AttackTarget, GenerousDistance);
            agent.Tick(TickStep);

            agent.ForceGuard(SecondTarget);

            Assert.That(agent.Current, Is.EqualTo(SecondTarget));
            Assert.That(agent.IsTransitioning, Is.False);
            Assert.That(agent.ActiveTransition.IsValid, Is.False);
        }

        [Test]
        public void ForceGuard_IgnoresNoGuard()
        {
            var agent = NewAgent();

            agent.ForceGuard(GuardId.None);

            Assert.That(agent.Current, Is.EqualTo(StartGuard));
        }

        // --- Measure --------------------------------------------------------------------

        [Test]
        public void ClassifyMeasure_UsesTheCurrentGuardsReach()
        {
            var agent = NewAgent(GuardId.Langort);

            Assert.That(agent.ClassifyMeasure(0.5f), Is.EqualTo(Measure.Ringen));
            Assert.That(agent.ClassifyMeasure(1.2f), Is.EqualTo(Measure.Krieg));
            Assert.That(agent.ClassifyMeasure(3.0f), Is.EqualTo(Measure.Zufechten));
            Assert.That(agent.ClassifyMeasure(9f), Is.EqualTo(Measure.OutOfRange));
        }
    }
}

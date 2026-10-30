using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The mirror-switch intent: right-side guard in, same guard on the other side out.
    ///
    /// These are the tests for the one input a player presses *during* another transition, because guard
    /// changes take a third of a second and nobody waits for them to finish. Resolving the intent at press
    /// time reads the guard being left; resolving it when the transition lands reads the guard reached.
    /// Only the second is what the button says, and only the second is tested here.
    /// </summary>
    [TestFixture]
    public sealed class MirrorRequestTests
    {
        private const float TickStep = 0.02f;
        private const float GenerousDistance = 1.2f;
        private const int MaxTicks = 60;

        [SetUp]
        public void SetUp()
        {
            CombatRules.ResetToDefaults();
        }

        private static FencingAgent NewAgent(GuardId guard)
        {
            return new FencingAgent(TransitionTable.CreateDefault(), guard);
        }

        /// <summary>
        /// Ticks until nothing is transitioning. Because a landing mirror queues a follow-up, "while
        /// transitioning" alone would stop between the two and leave the second pass half-run.
        /// </summary>
        private static void DriveToSettled(FencingAgent agent)
        {
            for (var i = 0; i < MaxTicks && agent.IsTransitioning; i++)
                agent.Tick(TickStep);
        }

        [Test]
        public void RequestMirror_AtRest_FlipsTheSameGuardOntoTheOtherSide(
            [Values(GuardId.VomTag, GuardId.Pflug, GuardId.Alber, GuardId.Ochs, GuardId.IronGate)]
            GuardId guard)
        {
            var agent = NewAgent(guard);

            Assert.That(agent.RequestMirror(GenerousDistance), Is.EqualTo(RequestResult.Accepted));

            DriveToSettled(agent);

            Assert.That(agent.Current, Is.EqualTo(guard), "the guard itself must not change, only its side");
            Assert.That(agent.CurrentSide, Is.EqualTo(GuardSide.Left));
        }

        [Test]
        public void RequestMirror_Twice_ReturnsToTheRightSide()
        {
            var agent = NewAgent(GuardId.Alber);

            agent.RequestMirror(GenerousDistance);
            DriveToSettled(agent);
            agent.RequestMirror(GenerousDistance);
            DriveToSettled(agent);

            Assert.That(agent.Current, Is.EqualTo(GuardId.Alber));
            Assert.That(agent.CurrentSide, Is.EqualTo(GuardSide.Right));
        }

        /// <summary>
        /// The regression. Pressing the mirror while a guard change is still committed must mirror the guard
        /// that change is going *to*, not the one it is leaving - otherwise the fighter arrives in the wrong
        /// guard entirely, which is what made the button look bound to Vom Tag.
        /// </summary>
        [Test]
        public void RequestMirror_AfterCommitment_MirrorsTheGuardThatLands()
        {
            var agent = NewAgent(GuardId.VomTag);
            agent.Table.TryGet(GuardId.VomTag, GuardId.Alber, out var toAlber);

            agent.RequestGuard(GuardId.Alber, GenerousDistance);
            while (agent.IsTransitioning && agent.Progress01 < toAlber.Commitment)
                agent.Tick(TickStep);

            Assert.That(agent.IsCommitted, Is.True, "the guard change must be committed for this test");
            Assert.That(agent.Current, Is.EqualTo(GuardId.VomTag), "still leaving Vom Tag at press time");

            Assert.That(agent.RequestMirror(GenerousDistance), Is.EqualTo(RequestResult.Queued));

            // The queued intent starts its own transition when the committed one lands, so step once past
            // that hand-off and inspect what it became.
            var guardThatLanded = GuardId.None;
            agent.TransitionCompleted += state => { if (guardThatLanded == GuardId.None) guardThatLanded = state.To; };
            for (var i = 0; i < MaxTicks && guardThatLanded == GuardId.None; i++)
                agent.Tick(TickStep);

            Assert.That(guardThatLanded, Is.EqualTo(GuardId.Alber));

            DriveToSettled(agent);

            Assert.That(agent.Current, Is.EqualTo(GuardId.Alber),
                "the mirror must apply to Alber, the guard the change reached");
            Assert.That(agent.CurrentSide, Is.EqualTo(GuardSide.Left));
        }

        [Test]
        public void RequestMirror_BeforeCommitment_CancelsAndMirrorsTheGuardHeld()
        {
            var agent = NewAgent(GuardId.VomTag);
            agent.RequestGuard(GuardId.Alber, GenerousDistance);
            agent.Tick(TickStep);

            Assert.That(agent.IsCommitted, Is.False, "a fresh transition must still be cancellable");

            Assert.That(agent.RequestMirror(GenerousDistance), Is.EqualTo(RequestResult.Accepted));
            Assert.That(agent.ActiveTransition.From, Is.EqualTo(GuardId.VomTag));
            Assert.That(agent.ActiveTransition.To, Is.EqualTo(GuardId.VomTag));
            Assert.That(agent.ActiveTransition.IsSideSwitch, Is.True);

            DriveToSettled(agent);

            Assert.That(agent.CurrentKey, Is.EqualTo(GuardKey.Left(GuardId.VomTag)));
        }

        [Test]
        public void RequestMirror_WhileReacting_IsRejected()
        {
            var agent = NewAgent(GuardId.Ochs);
            agent.SetReaction(ReactionState.Staggered, 0.5f);

            Assert.That(agent.RequestMirror(GenerousDistance), Is.EqualTo(RequestResult.Rejected));
            Assert.That(agent.CurrentSide, Is.EqualTo(GuardSide.Right));
        }

        [Test]
        public void RequestMirror_WhenDead_IsRejected()
        {
            var agent = NewAgent(GuardId.Ochs);
            agent.SetReaction(ReactionState.Dead);

            Assert.That(agent.RequestMirror(GenerousDistance), Is.EqualTo(RequestResult.Rejected));
        }

        /// <summary>
        /// A queued intent must not outlive the transition it was queued for: leaving combat mid-change is
        /// exactly what cancels a transition, and the stale mirror would otherwise fire on the next
        /// unrelated completion.
        /// </summary>
        [Test]
        public void RequestMirror_CancelledTransition_DoesNotLeakTheQueuedIntent()
        {
            var agent = NewAgent(GuardId.VomTag);
            agent.Table.TryGet(GuardId.VomTag, GuardId.Alber, out var toAlber);

            agent.RequestGuard(GuardId.Alber, GenerousDistance);
            while (agent.IsTransitioning && agent.Progress01 < toAlber.Commitment)
                agent.Tick(TickStep);

            Assert.That(agent.RequestMirror(GenerousDistance), Is.EqualTo(RequestResult.Queued));

            agent.CancelTransition("combat ended mid-change");
            agent.RequestGuard(GuardId.Ochs, GenerousDistance);
            DriveToSettled(agent);

            Assert.That(agent.CurrentKey, Is.EqualTo(GuardKey.Right(GuardId.Ochs)),
                "the unrelated guard change must land on the right side");
        }
    }
}

using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// Combat is a range-driven state, so the two things that can go wrong are the ones pinned here: it
    /// starts when it should not, or it ends during the fight. The hysteresis band is the whole reason the
    /// class exists, so most of these are about the boundary rather than the middle of the range.
    /// </summary>
    [TestFixture]
    public sealed class CombatAwarenessTests
    {
        private const float Engage = 10f;
        private const float Disengage = 15f;
        private const float Delay = 2f;

        private static CombatAwareness Awareness()
        {
            return new CombatAwareness(Engage, Disengage, Delay);
        }

        [Test]
        public void StartsOutOfCombat()
        {
            Assert.That(Awareness().IsEngaged, Is.False);
        }

        [Test]
        public void StaysOutWhileTheNearestHostileIsBeyondTheEngageRadius()
        {
            var awareness = Awareness();

            Assert.That(awareness.Tick(Engage + 0.01f, 1f), Is.False);
            Assert.That(awareness.IsEngaged, Is.False);
        }

        [Test]
        public void EngagesWhenAHostileArrivesInsideTheEngageRadius()
        {
            var awareness = Awareness();

            Assert.That(awareness.Tick(Engage - 0.01f, 1f), Is.True);
            Assert.That(awareness.IsEngaged, Is.True);
        }

        [Test]
        public void StaysEngagedAcrossTheBandBetweenTheTwoRadii()
        {
            var awareness = Awareness();
            awareness.Tick(5f, 0.1f);

            // Well inside the band, and given far longer than the disengage delay: still fighting.
            for (var i = 0; i < 100; i++)
                Assert.That(awareness.Tick(Disengage - 0.01f, 0.1f), Is.False);

            Assert.That(awareness.IsEngaged, Is.True);
        }

        [Test]
        public void DoesNotEngageWhenAHostileSitsInsideTheBandFromOutsideCombat()
        {
            var awareness = Awareness();

            // The band is deliberately one-way: a hostile that never came close enough does not start a
            // fight merely by standing just beyond the engage radius.
            Assert.That(awareness.Tick((Engage + Disengage) * 0.5f, 1f), Is.False);
            Assert.That(awareness.IsEngaged, Is.False);
        }

        [Test]
        public void HoldsCombatUntilTheDisengageDelayHasElapsed()
        {
            var awareness = Awareness();
            awareness.Tick(5f, 0.1f);

            Assert.That(awareness.Tick(Disengage + 1f, Delay * 0.5f), Is.False);
            Assert.That(awareness.IsEngaged, Is.True);

            Assert.That(awareness.Tick(Disengage + 1f, Delay * 0.5f + 0.01f), Is.True);
            Assert.That(awareness.IsEngaged, Is.False);
        }

        [Test]
        public void ReenteringTheBandRestartsTheDisengageCountdown()
        {
            var awareness = Awareness();
            awareness.Tick(5f, 0.1f);

            awareness.Tick(Disengage + 1f, Delay * 0.75f);
            Assert.That(awareness.TimeOutOfRange, Is.GreaterThan(0f));

            // A hostile stepping back into the band zeroes the countdown rather than pausing it.
            awareness.Tick(Disengage - 1f, 0.1f);
            Assert.That(awareness.TimeOutOfRange, Is.EqualTo(0f).Within(1e-5f));

            Assert.That(awareness.Tick(Disengage + 1f, Delay * 0.75f), Is.False);
            Assert.That(awareness.IsEngaged, Is.True);
        }

        [Test]
        public void NobodyLeftToFightEndsCombatAfterTheDelay()
        {
            var awareness = Awareness();
            awareness.Tick(5f, 0.1f);

            Assert.That(awareness.Tick(float.PositiveInfinity, Delay * 0.5f), Is.False);
            Assert.That(awareness.Tick(float.PositiveInfinity, Delay * 0.5f + 0.01f), Is.True);
            Assert.That(awareness.IsEngaged, Is.False);
        }

        [Test]
        public void NonFiniteDistancesAreTreatedAsNobodyInRange()
        {
            var awareness = Awareness();
            awareness.Tick(5f, 0.1f);

            // A NaN would otherwise compare false against every boundary and leave the state undefined.
            Assert.That(awareness.Tick(float.NaN, Delay + 0.01f), Is.True);
            Assert.That(awareness.IsEngaged, Is.False);
        }

        [Test]
        public void EngageAndDisengageReportOnlyRealChanges()
        {
            var awareness = Awareness();

            Assert.That(awareness.Engage(), Is.True);
            Assert.That(awareness.Engage(), Is.False);
            Assert.That(awareness.Disengage(), Is.True);
            Assert.That(awareness.Disengage(), Is.False);
        }

        [Test]
        public void RetuneRaisesADisengageRadiusBelowTheEngageRadius()
        {
            var awareness = new CombatAwareness(10f, 2f, 1f);

            Assert.That(awareness.DisengageRadius, Is.EqualTo(10f).Within(1e-5f));
        }

        [Test]
        public void RetuneKeepsTheCurrentState()
        {
            var awareness = Awareness();
            awareness.Tick(5f, 0.1f);

            awareness.Retune(1f, 2f, 0f);

            Assert.That(awareness.IsEngaged, Is.True);
            Assert.That(awareness.EngageRadius, Is.EqualTo(1f).Within(1e-5f));
        }
    }
}

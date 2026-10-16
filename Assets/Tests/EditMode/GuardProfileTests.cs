using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// A guard profile is what makes distance mean something: its tip reach bands a raw metre value into
    /// a measure, and its parry grace decides how long an arrival can still stop a blade. Player, enemy
    /// and tests must all band distance the same way, so the boundaries are pinned here.
    /// </summary>
    [TestFixture]
    public sealed class GuardProfileTests
    {
        private const float Tolerance = 1e-5f;
        private const float TestTipReach = 2f;
        private const float TestParryGrace = 0.25f;

        /// <summary>Keeps the band boundaries readable: reach 2 means Ringen &lt;= 1.1, Krieg &lt;= 2, Zufechten &lt;= 4.</summary>
        private const float RingenCeiling = TestTipReach * 0.55f;
        private const float ZufechtenCeiling = TestTipReach * 2f;

        private static GuardProfile Guard()
        {
            return new GuardProfile(GuardId.Pflug, TestTipReach, TestParryGrace);
        }

        [Test]
        public void Constructor_ExposesTheGuardItDescribes()
        {
            var guard = Guard();

            Assert.That(guard.Guard, Is.EqualTo(GuardId.Pflug));
            Assert.That(guard.TipReach, Is.EqualTo(TestTipReach).Within(Tolerance));
            Assert.That(guard.ParryGraceSeconds, Is.EqualTo(TestParryGrace).Within(Tolerance));
        }

        [Test]
        public void Constructor_ClampsDegenerateReachAndGrace()
        {
            var guard = new GuardProfile(GuardId.Alber, -5f, -1f);

            Assert.That(guard.TipReach, Is.GreaterThan(0f));
            Assert.That(guard.ParryGraceSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void Classify_BoundariesAreInclusiveOnTheInnerBand()
        {
            var guard = Guard();

            Assert.That(guard.Classify(RingenCeiling), Is.EqualTo(Measure.Ringen));
            Assert.That(guard.Classify(TestTipReach), Is.EqualTo(Measure.Krieg));
            Assert.That(guard.Classify(ZufechtenCeiling), Is.EqualTo(Measure.Zufechten));
        }

        [Test]
        public void Classify_BandsMonotonicallyOutward()
        {
            var guard = Guard();

            Assert.That(guard.Classify(RingenCeiling - 0.01f), Is.EqualTo(Measure.Ringen));
            Assert.That(guard.Classify(RingenCeiling + 0.01f), Is.EqualTo(Measure.Krieg));
            Assert.That(guard.Classify(TestTipReach + 0.01f), Is.EqualTo(Measure.Zufechten));
            Assert.That(guard.Classify(ZufechtenCeiling + 0.01f), Is.EqualTo(Measure.OutOfRange));
        }

        [Test]
        public void Classify_ZeroDistanceIsGrapplingRange()
        {
            Assert.That(Guard().Classify(0f), Is.EqualTo(Measure.Ringen));
        }

        /// <summary>
        /// Krieg shortens the grace, because a parry window that stayed generous at grappling distance
        /// would make close measure safer than long measure - the opposite of the design intent.
        /// </summary>
        [Test]
        public void ResolveParryGrace_ScalesDownInKriegAndIsUnchangedElsewhere()
        {
            var guard = Guard();

            Assert.That(guard.ResolveParryGrace(Measure.Krieg), Is.EqualTo(TestParryGrace * 0.6f).Within(Tolerance));
            Assert.That(guard.ResolveParryGrace(Measure.Zufechten), Is.EqualTo(TestParryGrace).Within(Tolerance));
            Assert.That(guard.ResolveParryGrace(Measure.Ringen), Is.EqualTo(TestParryGrace).Within(Tolerance));
            Assert.That(guard.ResolveParryGrace(Measure.OutOfRange), Is.EqualTo(TestParryGrace).Within(Tolerance));
        }
    }
}

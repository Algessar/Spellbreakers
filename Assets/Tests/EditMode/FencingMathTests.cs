using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The core's numeric helpers are the floor everything else stands on: a clamp that lets a value
    /// through would corrupt every progress, measure and score derived from it. These tests pin the
    /// boundaries rather than the arithmetic, because the boundaries are where callers rely on it.
    /// </summary>
    [TestFixture]
    public sealed class FencingMathTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void Clamp01_PinsBothEndsAndPassesInteriorThrough()
        {
            Assert.That(FencingMath.Clamp01(-0.5f), Is.EqualTo(0f));
            Assert.That(FencingMath.Clamp01(0f), Is.EqualTo(0f));
            Assert.That(FencingMath.Clamp01(0.25f), Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(FencingMath.Clamp01(1f), Is.EqualTo(1f));
            Assert.That(FencingMath.Clamp01(4f), Is.EqualTo(1f));
        }

        [Test]
        public void Clamp_HonoursExplicitRange()
        {
            Assert.That(FencingMath.Clamp(-3f, -1f, 1f), Is.EqualTo(-1f));
            Assert.That(FencingMath.Clamp(0f, -1f, 1f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(FencingMath.Clamp(9f, -1f, 1f), Is.EqualTo(1f));
        }

        [Test]
        public void Lerp_ClampsT()
        {
            Assert.That(FencingMath.Lerp(10f, 20f, 0.5f), Is.EqualTo(15f).Within(Tolerance));
            Assert.That(FencingMath.Lerp(10f, 20f, -1f), Is.EqualTo(10f).Within(Tolerance));
            Assert.That(FencingMath.Lerp(10f, 20f, 2f), Is.EqualTo(20f).Within(Tolerance));
        }

        [Test]
        public void MaxAndMin_PickTheLargerAndSmallerOperand()
        {
            Assert.That(FencingMath.Max(2f, 5f), Is.EqualTo(5f));
            Assert.That(FencingMath.Max(5f, 2f), Is.EqualTo(5f));
            Assert.That(FencingMath.Min(2f, 5f), Is.EqualTo(2f));
            Assert.That(FencingMath.Min(5f, 2f), Is.EqualTo(2f));
        }

        [Test]
        public void InverseLerpClamped_MapsRangeAndClampsOutsideIt()
        {
            Assert.That(FencingMath.InverseLerpClamped(0f, 2f, 1f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(FencingMath.InverseLerpClamped(0f, 2f, -1f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(FencingMath.InverseLerpClamped(0f, 2f, 3f), Is.EqualTo(1f).Within(Tolerance));
        }

        /// <summary>
        /// A degenerate range has no scale to normalise against. Reporting "past the top" rather than
        /// dividing by zero keeps the AI's distance and measure maths finite.
        /// </summary>
        [Test]
        public void InverseLerpClamped_DegenerateRangeDoesNotDivideByZero()
        {
            Assert.That(FencingMath.InverseLerpClamped(4f, 4f, 4f), Is.EqualTo(1f));
            Assert.That(FencingMath.InverseLerpClamped(4f, 4f, 1f), Is.EqualTo(0f));
        }
    }
}

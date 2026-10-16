using System;
using NUnit.Framework;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// Response curves are what turn a raw measurement into a comparable 0..1 score. A curve that can
    /// return outside that range would break the weighted average the brain relies on, so the range
    /// guarantee is tested across every preset at the same sample points.
    /// </summary>
    [TestFixture]
    public sealed class ResponseCurveTests
    {
        private const float Tolerance = 1e-5f;

        private static readonly float[] SampleInputs = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        [Test]
        public void Linear_IsTheIdentity()
        {
            Assert.That(ResponseCurve.Linear.Evaluate(0.5f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(ResponseCurve.Linear.Evaluate(0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ResponseCurve.Linear.Evaluate(1f), Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Presets_HaveDistinctNamesSoTheDebugBreakdownIsReadable()
        {
            Assert.That(ResponseCurve.Linear.Name, Is.EqualTo("Linear"));
            Assert.That(ResponseCurve.EaseIn.Name, Is.EqualTo("EaseIn"));
            Assert.That(ResponseCurve.EaseOut.Name, Is.EqualTo("EaseOut"));
            Assert.That(ResponseCurve.Threshold.Name, Is.EqualTo("Threshold"));
            Assert.That(ResponseCurve.Banded.Name, Is.EqualTo("Banded"));
            Assert.That(ResponseCurve.Inverted.Name, Is.EqualTo("Inverted"));
        }

        [Test]
        public void EveryPreset_StaysInsideTheUnitRange()
        {
            var presets = new[]
            {
                ResponseCurve.Linear,
                ResponseCurve.EaseIn,
                ResponseCurve.EaseOut,
                ResponseCurve.Threshold,
                ResponseCurve.Banded,
                ResponseCurve.Inverted,
            };

            foreach (var preset in presets)
            {
                foreach (var input in SampleInputs)
                {
                    var value = preset.Evaluate(input);

                    Assert.That(value, Is.GreaterThanOrEqualTo(0f), $"{preset.Name} at {input}");
                    Assert.That(value, Is.LessThanOrEqualTo(1f), $"{preset.Name} at {input}");
                }
            }
        }

        /// <summary>Considerations can hand in unclamped measurements, so the curve must absorb them.</summary>
        [TestCase(-5f)]
        [TestCase(5f)]
        public void EveryPreset_ClampsInputsOutsideTheUnitRange(float input)
        {
            var presets = new[]
            {
                ResponseCurve.Linear,
                ResponseCurve.EaseIn,
                ResponseCurve.EaseOut,
                ResponseCurve.Threshold,
                ResponseCurve.Banded,
                ResponseCurve.Inverted,
            };

            foreach (var preset in presets)
            {
                var value = preset.Evaluate(input);

                Assert.That(value, Is.GreaterThanOrEqualTo(0f), $"{preset.Name} at {input}");
                Assert.That(value, Is.LessThanOrEqualTo(1f), $"{preset.Name} at {input}");
            }
        }

        [Test]
        public void EaseIn_StaysBelowLinearInTheFirstHalf()
        {
            Assert.That(ResponseCurve.EaseIn.Evaluate(0.5f), Is.LessThan(0.5f));
        }

        [Test]
        public void EaseOut_StaysAboveLinearInTheFirstHalf()
        {
            Assert.That(ResponseCurve.EaseOut.Evaluate(0.5f), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Threshold_StaysFlatUntilItIsNearlyCertain()
        {
            Assert.That(ResponseCurve.Threshold.Evaluate(0.5f), Is.EqualTo(0f));
            Assert.That(ResponseCurve.Threshold.Evaluate(0.9f), Is.EqualTo(1f));
        }

        [Test]
        public void Banded_PeaksInTheMiddle()
        {
            Assert.That(ResponseCurve.Banded.Evaluate(0.5f), Is.EqualTo(1f));
            Assert.That(ResponseCurve.Banded.Evaluate(0f), Is.EqualTo(0f));
            Assert.That(ResponseCurve.Banded.Evaluate(1f), Is.EqualTo(0f));
        }

        [Test]
        public void Inverted_IsOneMinusTheInput()
        {
            Assert.That(ResponseCurve.Inverted.Evaluate(0.25f), Is.EqualTo(0.75f).Within(Tolerance));
            Assert.That(ResponseCurve.Inverted.Evaluate(0f), Is.EqualTo(1f).Within(Tolerance));
            Assert.That(ResponseCurve.Inverted.Evaluate(1f), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void FromPoints_InterpolatesBetweenControlPoints()
        {
            var curve = ResponseCurve.FromPoints(new[] { 0f, 0.5f, 1f }, new[] { 0f, 0.8f, 1f }, "Custom");

            Assert.That(curve.Name, Is.EqualTo("Custom"));
            Assert.That(curve.Evaluate(0.25f), Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0.8f).Within(Tolerance));
        }

        [Test]
        public void FromPoints_RejectsMismatchedLengths()
        {
            Assert.Throws<ArgumentException>(() =>
                ResponseCurve.FromPoints(new[] { 0f, 0.5f, 1f }, new[] { 0f, 1f }, "Mismatched"));
        }

        [Test]
        public void FromPoints_RejectsTooFewPoints()
        {
            Assert.Throws<ArgumentException>(() => ResponseCurve.FromPoints(new[] { 0f }, new[] { 0f }, "Single"));
        }

        [Test]
        public void FromPoints_RejectsNullArrays()
        {
            Assert.Throws<ArgumentException>(() => ResponseCurve.FromPoints(null, new[] { 0f, 1f }, "NullX"));
            Assert.Throws<ArgumentException>(() => ResponseCurve.FromPoints(new[] { 0f, 1f }, null, "NullY"));
        }

        /// <summary>A non-ascending X set has no single value for a given input, so evaluating it would
        /// silently depend on iteration order.</summary>
        [Test]
        public void FromPoints_RejectsNonAscendingXValues()
        {
            Assert.Throws<ArgumentException>(() =>
                ResponseCurve.FromPoints(new[] { 0f, 0.5f, 0.4f }, new[] { 0f, 0.5f, 1f }, "Descending"));

            Assert.Throws<ArgumentException>(() =>
                ResponseCurve.FromPoints(new[] { 0f, 0.5f, 0.5f }, new[] { 0f, 0.5f, 1f }, "Duplicate"));
        }

        /// <summary>The caller's arrays may be reused buffers; the curve has to own its control points.</summary>
        [Test]
        public void FromPoints_CopiesTheControlPointsItIsGiven()
        {
            var xs = new[] { 0f, 1f };
            var ys = new[] { 0f, 1f };
            var curve = ResponseCurve.FromPoints(xs, ys, "Borrowed");

            xs[1] = 5f;
            ys[1] = 5f;

            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(curve.Evaluate(1f), Is.EqualTo(1f).Within(Tolerance));
        }
    }
}

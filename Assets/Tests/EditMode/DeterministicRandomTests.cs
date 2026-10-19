using NUnit.Framework;
using Spellbreakers.Combat.Ai;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The AI is only testable because its randomness is reproducible. A duel replayed from the same
    /// seeds must produce the same decisions, so the generator's determinism is a load-bearing property
    /// rather than a nicety.
    /// </summary>
    [TestFixture]
    public sealed class DeterministicRandomTests
    {
        private const float Tolerance = 1e-6f;
        private const int SequenceLength = 100;
        private const int SeedA = 20261016;
        private const int SeedB = 987654321;

        [Test]
        public void SameSeed_ProducesTheSameSequenceOfRawDraws()
        {
            var first = new DeterministicRandom(SeedA);
            var second = new DeterministicRandom(SeedA);

            for (var i = 0; i < SequenceLength; i++)
                Assert.That(second.NextUInt(), Is.EqualTo(first.NextUInt()), $"draw {i}");
        }

        [Test]
        public void SameSeed_ProducesTheSameSequenceOfUnitDraws()
        {
            var first = new DeterministicRandom(SeedA);
            var second = new DeterministicRandom(SeedA);

            for (var i = 0; i < SequenceLength; i++)
                Assert.That(second.Next01(), Is.EqualTo(first.Next01()).Within(Tolerance), $"draw {i}");
        }

        [Test]
        public void Next01_StaysInsideTheUnitInterval()
        {
            var random = new DeterministicRandom(SeedA);

            for (var i = 0; i < SequenceLength; i++)
            {
                var value = random.Next01();

                Assert.That(value, Is.GreaterThanOrEqualTo(0f), $"draw {i}");
                Assert.That(value, Is.LessThan(1f), $"draw {i}");
            }
        }

        /// <summary>Seed 0 is a fixed point of a plain LCG, so a generator that did not special-case it
        /// would emit zero forever and the enemy would lose all jitter.</summary>
        [Test]
        public void ZeroSeed_IsNotDegenerate()
        {
            var random = new DeterministicRandom(0);
            var distinct = new System.Collections.Generic.HashSet<uint>();

            for (var i = 0; i < 20; i++)
            {
                var value = random.NextUInt();

                Assert.That(value, Is.Not.EqualTo(0u), $"draw {i}");
                distinct.Add(value);
            }

            Assert.That(distinct.Count, Is.GreaterThan(1));
        }

        [Test]
        public void DifferentSeeds_Diverge()
        {
            var first = new DeterministicRandom(SeedA);
            var second = new DeterministicRandom(SeedB);
            var differences = 0;

            for (var i = 0; i < SequenceLength; i++)
            {
                if (first.NextUInt() != second.NextUInt())
                    differences++;
            }

            Assert.That(differences, Is.GreaterThan(SequenceLength / 2));
        }

        [Test]
        public void Range_FloatRespectsItsBounds()
        {
            var random = new DeterministicRandom(SeedA);
            const float min = -1.5f;
            const float max = 2.5f;

            for (var i = 0; i < SequenceLength; i++)
            {
                var value = random.Range(min, max);

                Assert.That(value, Is.GreaterThanOrEqualTo(min), $"draw {i}");
                Assert.That(value, Is.LessThanOrEqualTo(max), $"draw {i}");
            }
        }

        [Test]
        public void Range_IntRespectsItsExclusiveUpperBound()
        {
            var random = new DeterministicRandom(SeedA);
            const int min = 3;
            const int max = 9;

            for (var i = 0; i < SequenceLength; i++)
            {
                var value = random.Range(min, max);

                Assert.That(value, Is.GreaterThanOrEqualTo(min), $"draw {i}");
                Assert.That(value, Is.LessThan(max), $"draw {i}");
            }
        }

        [Test]
        public void Range_IntWithAnEmptyRangeReturnsTheMinimum()
        {
            var random = new DeterministicRandom(SeedA);

            Assert.That(random.Range(4, 4), Is.EqualTo(4));
            Assert.That(random.Range(4, 1), Is.EqualTo(4));
        }

        /// <summary>
        /// The struct is copied by value into the brain. Advancing one copy must not move the cursor of
        /// the other, or a decision would consume noise from a different candidate's stream.
        /// </summary>
        [Test]
        public void Copies_AdvanceIndependently()
        {
            var reference = new DeterministicRandom(SeedA);
            var firstDraw = reference.NextUInt();

            var original = new DeterministicRandom(SeedA);
            var copy = original;

            original.NextUInt();

            Assert.That(copy.NextUInt(), Is.EqualTo(firstDraw), "advancing the original must not consume from the copy");
        }
    }
}

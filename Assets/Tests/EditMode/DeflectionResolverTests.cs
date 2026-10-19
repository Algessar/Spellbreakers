using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The design doc asks for deflection to be a gradient driven by the physical relationship between
    /// attack and defender - good timing and angle yielding a clean deflection, degrading through partial
    /// deflection and knockback to a hit - rather than a binary lookup on the attacker's anatomy. These
    /// tests pin that gradient: each input must move the result in the direction the doc describes, and a
    /// defender that cannot parry must never reach it at all.
    /// </summary>
    [TestFixture]
    public sealed class DeflectionResolverTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>A comfortably fast attack, used where speed should not be the deciding factor.</summary>
        private const float ModerateSpeed = 4f;

        private const float ReferenceSpeed = 6f;

        private static DeflectionInput Input(
            float timing = 1f,
            float angle = 1f,
            float speed = ModerateSpeed,
            CombatOutcome baseline = CombatOutcome.Parry)
        {
            return new DeflectionInput(timing, angle, speed, ReferenceSpeed, baseline);
        }

        [Test]
        public void ADefenderThatCannotParryNeverReachesTheGradient()
        {
            var perfect = DeflectionResolver.Resolve(Input(1f, 1f, 0f), DefenceMode.None);

            Assert.That(perfect.Outcome, Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(perfect.Grade, Is.EqualTo(0f));
            Assert.That(perfect.DefenderCanParry, Is.False);
        }

        /// <summary>Being able to avoid is not being able to parry: avoidance is resolved by geometry.</summary>
        [Test]
        public void AvoidanceAloneDoesNotParry()
        {
            var result = DeflectionResolver.Resolve(Input(1f, 1f, 0f), DefenceMode.Avoid);

            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(result.DefenderCanParry, Is.False);
        }

        [Test]
        public void PerfectTimingAndAngleProducesACleanDeflection()
        {
            var result = DeflectionResolver.Resolve(Input(1f, 1f, 0f), DefenceMode.Parry);

            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Parry));
            Assert.That(result.Grade, Is.GreaterThanOrEqualTo(DeflectionResolver.CleanDeflectionThreshold));
            Assert.That(result.IsBladeOnBlade, Is.True);
        }

        [Test]
        public void TerribleTimingAndAngleIsAHit()
        {
            var result = DeflectionResolver.Resolve(Input(0f, 0f, ReferenceSpeed), DefenceMode.Parry);

            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(result.Grade, Is.LessThan(DeflectionResolver.KnockbackThreshold));
        }

        /// <summary>Better timing can never make the defence worse, all else equal.</summary>
        [Test]
        public void GradeRisesMonotonicallyWithTiming()
        {
            var previous = -1f;

            for (var i = 0; i <= 10; i++)
            {
                var timing = i / 10f;
                var result = DeflectionResolver.Resolve(Input(timing, 0.5f, ModerateSpeed), DefenceMode.Parry);

                Assert.That(result.Grade, Is.GreaterThanOrEqualTo(previous), $"timing {timing:0.0}");
                previous = result.Grade;
            }
        }

        [Test]
        public void GradeRisesMonotonicallyWithContactAngle()
        {
            var previous = -1f;

            for (var i = 0; i <= 10; i++)
            {
                var angle = i / 10f;
                var result = DeflectionResolver.Resolve(Input(0.5f, angle, ModerateSpeed), DefenceMode.Parry);

                Assert.That(result.Grade, Is.GreaterThanOrEqualTo(previous), $"angle {angle:0.0}");
                previous = result.Grade;
            }
        }

        /// <summary>A faster incoming blade is harder to turn aside, so the same defence grades lower.</summary>
        [Test]
        public void AFasterAttackGradesLowerThanASlowerOne()
        {
            var slow = DeflectionResolver.Resolve(Input(0.7f, 0.7f, 1f), DefenceMode.Parry);
            var fast = DeflectionResolver.Resolve(Input(0.7f, 0.7f, ReferenceSpeed * 4f), DefenceMode.Parry);

            Assert.That(fast.Grade, Is.LessThan(slow.Grade));
            Assert.That(fast.SpeedContribution, Is.LessThan(slow.SpeedContribution));
        }

        /// <summary>Speed only ever reduces the grade; it can never drive it to zero on its own.</summary>
        [Test]
        public void ExtremeSpeedStillLeavesSomeGradeWhenEverythingElseIsPerfect()
        {
            var result = DeflectionResolver.Resolve(Input(1f, 1f, ReferenceSpeed * 100f), DefenceMode.Parry);

            Assert.That(result.Grade, Is.GreaterThan(0f));
            Assert.That(result.Outcome, Is.Not.EqualTo(CombatOutcome.HitBody));
        }

        /// <summary>
        /// The parry table is a prior, not the verdict: with identical physics, a favourable guard matchup
        /// must grade higher than an unfavourable one, without either being forced to a fixed outcome.
        /// </summary>
        [Test]
        public void GuardMatchupActsAsAPriorWithoutDecidingTheOutcome()
        {
            var favourable = DeflectionResolver.Resolve(Input(0.6f, 0.6f, ModerateSpeed, CombatOutcome.Parry),
                DefenceMode.Parry);
            var unfavourable = DeflectionResolver.Resolve(Input(0.6f, 0.6f, ModerateSpeed, CombatOutcome.HitBody),
                DefenceMode.Parry);

            Assert.That(favourable.Grade, Is.GreaterThan(unfavourable.Grade));

            // Bad timing must still be able to lose despite a favourable prior.
            var wellMatchedButLate =
                DeflectionResolver.Resolve(Input(0f, 0f, ReferenceSpeed, CombatOutcome.Parry), DefenceMode.Parry);
            Assert.That(wellMatchedButLate.Outcome, Is.EqualTo(CombatOutcome.HitBody));

            // And good timing can still salvage a poor prior.
            var poorlyMatchedButEarly =
                DeflectionResolver.Resolve(Input(1f, 1f, 0f, CombatOutcome.HitBody), DefenceMode.Parry);
            Assert.That(poorlyMatchedButEarly.Outcome, Is.Not.EqualTo(CombatOutcome.HitBody));
        }

        [TestCase(0.95f, CombatOutcome.Parry)]
        [TestCase(0.85f, CombatOutcome.Parry)]
        [TestCase(0.84f, CombatOutcome.Deflect)]
        [TestCase(0.65f, CombatOutcome.Deflect)]
        [TestCase(0.64f, CombatOutcome.Bind)]
        [TestCase(0.45f, CombatOutcome.Bind)]
        [TestCase(0.44f, CombatOutcome.HitBody)]
        [TestCase(0f, CombatOutcome.HitBody)]
        public void Bucket_MapsTheGradientOntoTheDocsOrderedOutcomes(float grade, CombatOutcome expected)
        {
            Assert.That(DeflectionResolver.Bucket(grade), Is.EqualTo(expected));
        }

        [Test]
        public void GradeIsAlwaysWithinUnitRange()
        {
            for (var timing = 0; timing <= 1; timing++)
            {
                for (var angle = 0; angle <= 1; angle++)
                {
                    var result = DeflectionResolver.Resolve(
                        Input(timing, angle, timing * ReferenceSpeed * 3f), DefenceMode.Both);

                    Assert.That(result.Grade, Is.InRange(0f, 1f));
                }
            }
        }

        [Test]
        public void ContributionWeightsSumToOne()
        {
            Assert.That(
                DeflectionResolver.TimingWeight + DeflectionResolver.AngleWeight + DeflectionResolver.SpeedWeight,
                Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void TimingQualityPeaksAtTheCentreOfTheWindowAndFallsToZeroAtTheEdges()
        {
            const float start = 0.2f;
            const float end = 0.8f;

            var atStart = DeflectionResolver.TimingQualityFromWindow(start, start, end);
            var atCentre = DeflectionResolver.TimingQualityFromWindow((start + end) * 0.5f, start, end);
            var atEnd = DeflectionResolver.TimingQualityFromWindow(end, start, end);

            Assert.That(atCentre, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(atStart, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(atEnd, Is.EqualTo(0f).Within(Tolerance));

            // Outside the window there is no defence at all.
            Assert.That(DeflectionResolver.TimingQualityFromWindow(start - 0.1f, start, end), Is.EqualTo(0f));
            Assert.That(DeflectionResolver.TimingQualityFromWindow(end + 0.1f, start, end), Is.EqualTo(0f));
        }

        [Test]
        public void DegenerateWindowYieldsNoTimingQuality()
        {
            Assert.That(DeflectionResolver.TimingQualityFromWindow(0.5f, 0.5f, 0.5f), Is.EqualTo(0f));
            Assert.That(DeflectionResolver.TimingQualityFromWindow(0.5f, 0.7f, 0.3f), Is.EqualTo(0f));
        }
    }
}

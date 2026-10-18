using System;
using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// Transition profiles own the doc's timing rules: when the blade is live, how long a pass lasts and
    /// how Vor and Krieg shorten it. These tests pin the authored windows because hit detection, AI and
    /// presentation all read the same profile rather than deriving their own timing.
    /// </summary>
    [TestFixture]
    public sealed class TransitionProfileTests
    {
        private const float Tolerance = 1e-5f;

        // The authored attack used across the window tests: the blade is live over the middle third.
        private const float AuthoredDuration = 0.42f;
        private const float AuthoredKriegDuration = 0.32f;
        private const float AuthoredDamageStart = 0.30f;
        private const float AuthoredDamageEnd = 0.62f;

        private static TransitionProfile Attack(
            GuardId from = GuardId.VomTag,
            GuardId to = GuardId.Alber,
            float damageStart = AuthoredDamageStart,
            float damageEnd = AuthoredDamageEnd)
        {
            return new TransitionProfile(
                from,
                to,
                TransitionDirection.Oberhau,
                AuthoredDuration,
                damageStart,
                damageEnd)
            {
                KriegDuration = AuthoredKriegDuration,
            };
        }

        [Test]
        public void Constructor_RejectsNoneAsSourceGuard()
        {
            Assert.Throws<ArgumentException>(() =>
                new TransitionProfile(GuardId.None, GuardId.Alber, TransitionDirection.Oberhau, 0.4f, 0.3f, 0.6f));
        }

        [Test]
        public void Constructor_RejectsNoneAsDestinationGuard()
        {
            Assert.Throws<ArgumentException>(() =>
                new TransitionProfile(GuardId.VomTag, GuardId.None, TransitionDirection.Oberhau, 0.4f, 0.3f, 0.6f));
        }

        /// <summary>
        /// Design doc 3.2: a transition may begin and end in the same guard - "Vom Tag -> Vom Tag =
        /// Zornhau". The system must not assume the destination always differs from the source, so a
        /// same-guard pair is a legal self-transition rather than a constructor error.
        /// </summary>
        [Test]
        public void Constructor_AllowsATransitionFromAGuardToItself()
        {
            var zornhau = Attack(GuardId.VomTag, GuardId.VomTag);

            Assert.That(zornhau.From, Is.EqualTo(GuardId.VomTag));
            Assert.That(zornhau.To, Is.EqualTo(GuardId.VomTag));
            Assert.That(zornhau.IsSelfTransition, Is.True);
            Assert.That(zornhau.HasDamageWindow, Is.True, "a self-transition is still an attack");
        }

        /// <summary>A self-transition with no damage window is inert but still legal to author.</summary>
        [Test]
        public void SelfTransition_WithoutADamageWindowIsLegalButNotAnAttack()
        {
            var reset = Attack(GuardId.Ochs, GuardId.Ochs, 0f, 0f);

            Assert.That(reset.IsSelfTransition, Is.True);
            Assert.That(reset.HasDamageWindow, Is.False);
            Assert.That(reset.IsInDamageWindow(0.5f), Is.False);
        }

        [Test]
        public void Sides_DefaultToRightSoExistingAuthoringStaysValid()
        {
            var profile = Attack();

            Assert.That(profile.FromSide, Is.EqualTo(GuardSide.Right));
            Assert.That(profile.ToSide, Is.EqualTo(GuardSide.Right));
            Assert.That(profile.FromKey.Side, Is.EqualTo(GuardSide.Right));
            Assert.That(profile.IsSideSwitch, Is.False);
        }

        /// <summary>Doc 3.2's alternative to a same-side self-transition: R Vom Tag -> L Vom Tag.</summary>
        [Test]
        public void SelfTransition_ThatChangesSideIsASideSwitch()
        {
            var switchSides = Attack(GuardId.VomTag, GuardId.VomTag);
            switchSides.FromSide = GuardSide.Right;
            switchSides.ToSide = GuardSide.Left;

            Assert.That(switchSides.IsSelfTransition, Is.True);
            Assert.That(switchSides.IsSideSwitch, Is.True);
            Assert.That(switchSides.FromKey, Is.EqualTo(GuardKey.Right(GuardId.VomTag)));
            Assert.That(switchSides.ToKey, Is.EqualTo(GuardKey.Left(GuardId.VomTag)));
            Assert.That(switchSides.DisplayName, Does.Contain("side switch"));
        }

        /// <summary>
        /// The technique name is authoring and debug display only. It must not change any mechanical
        /// value, so the doc's Zornhau/Oberhau distinction never becomes a gameplay one.
        /// </summary>
        [Test]
        public void TechniqueName_IsDisplayOnlyAndDoesNotAffectMechanics()
        {
            var unnamed = Attack();
            var named = Attack();
            named.TechniqueName = "Zornhau";

            Assert.That(named.DisplayName, Does.Contain("Zornhau"));
            Assert.That(named.Direction, Is.EqualTo(unnamed.Direction));
            Assert.That(named.Duration, Is.EqualTo(unnamed.Duration));
            Assert.That(named.DamageWindowStart, Is.EqualTo(unnamed.DamageWindowStart));
            Assert.That(named.DamageWindowEnd, Is.EqualTo(unnamed.DamageWindowEnd));
            Assert.That(named.HasDamageWindow, Is.EqualTo(unnamed.HasDamageWindow));
        }

        [Test]
        public void Constructor_ClampsAuthoringMistakesIntoValidRanges()
        {
            var profile = new TransitionProfile(
                GuardId.VomTag,
                GuardId.Alber,
                TransitionDirection.Oberhau,
                0f,
                -1f,
                2f);

            Assert.That(profile.Duration, Is.EqualTo(0.01f).Within(Tolerance));
            Assert.That(profile.DamageWindowStart, Is.EqualTo(0f));
            Assert.That(profile.DamageWindowEnd, Is.EqualTo(1f));
        }

        [Test]
        public void DisplayName_ReadsAsSourceToDestination()
        {
            Assert.That(Attack().DisplayName, Is.EqualTo("Vom Tag -> Alber"));
        }

        [Test]
        public void Defaults_MatchTheAuthoredDesign()
        {
            var profile = Attack();

            // The gate is off by default so the player's existing free guard switching is untouched.
            Assert.That(profile.EnforceDistanceGate, Is.False);
            Assert.That(profile.FeintWhenOutOfRange, Is.True);
            Assert.That(profile.ParryWindowStart, Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(profile.ParryWindowEnd, Is.EqualTo(0.95f).Within(Tolerance));
            Assert.That(profile.Commitment, Is.EqualTo(0.35f).Within(Tolerance));
            Assert.That(profile.TransitionPose, Is.EqualTo(GuardId.Langort));
            Assert.That(profile.PassStateName, Is.EqualTo("Langort"));
            Assert.That(TransitionProfile.VorDurationScale, Is.EqualTo(0.85f).Within(Tolerance));
        }

        [Test]
        public void EvaluateVisualProgress_ProducesNonLinearBiomechanicalCurve()
        {
            var profile = Attack(damageStart: 0.30f, damageEnd: 0.60f);

            // Chambering: early phase progress (0.15) should ease in slower than linear
            float earlyLinear = 0.15f;
            float earlyEvaluated = profile.EvaluateVisualProgress(earlyLinear);
            Assert.That(earlyEvaluated, Is.LessThan(earlyLinear));

            // Damage window: should be inside the live strike window
            float midLinear = 0.45f;
            float midEvaluated = profile.EvaluateVisualProgress(midLinear);
            Assert.That(midEvaluated, Is.GreaterThanOrEqualTo(0.30f));
            Assert.That(midEvaluated, Is.LessThanOrEqualTo(0.60f));

            // End bounds: 0 and 1 clamp cleanly
            Assert.That(profile.EvaluateVisualProgress(0f), Is.EqualTo(0f));
            Assert.That(profile.EvaluateVisualProgress(1f), Is.EqualTo(1f));
        }

        /// <summary>
        /// Every attack reaches through a named pose - the doc's TransitionPose, formerly "pass-through" -
        /// so presentation always has a state to scrub. In Krieg the pose may tighten to a different guard;
        /// a row that does not author that tighter pose must fall back to the same pose, never to nothing.
        /// </summary>
        [Test]
        public void DefaultTable_NamesThePoseEveryTransitionReachesThrough()
        {
            var table = TransitionTable.CreateDefault();
            var kriegSpecificPoses = 0;

            Assert.That(table.Count, Is.GreaterThan(0));

            foreach (var profile in table.Profiles)
            {
                Assert.That(profile.TransitionPose, Is.Not.EqualTo(GuardId.None),
                    $"{profile.DisplayName} must name the pose it reaches through");

                if (profile.TransitionPoseInKrieg != GuardId.None)
                {
                    kriegSpecificPoses++;
                    Assert.That(profile.ResolveTransitionPose(Measure.Krieg),
                        Is.EqualTo(profile.TransitionPoseInKrieg),
                        $"{profile.DisplayName} authors a tighter Krieg pose");
                }
                else
                {
                    Assert.That(profile.ResolveTransitionPose(Measure.Krieg),
                        Is.EqualTo(profile.TransitionPose),
                        $"{profile.DisplayName} must fall back to its normal pose in Krieg");
                }

                Assert.That(profile.ResolveTransitionPose(Measure.Zufechten),
                    Is.EqualTo(profile.TransitionPose),
                    $"{profile.DisplayName} reaches through its normal pose outside Krieg");
            }

            Assert.That(kriegSpecificPoses, Is.GreaterThan(0),
                "the default table must demonstrate a Krieg-specific pose");
        }

        [Test]
        public void HasDamageWindow_IsFalseWhenTheWindowIsEmptyOrInverted()
        {
            Assert.That(Attack(damageStart: 0.5f, damageEnd: 0.5f).HasDamageWindow, Is.False);
            Assert.That(Attack(damageStart: 0.7f, damageEnd: 0.3f).HasDamageWindow, Is.False);
            Assert.That(Attack().HasDamageWindow, Is.True);
        }

        /// <summary>
        /// A transition with no damage window - a synthesised traversal, say - carries no live blade at
        /// all. Without this, a no-damage profile would still appear to strike at every progress value,
        /// and parry resolution would arm itself on a dead blade.
        /// </summary>
        [Test]
        public void IsInDamageWindow_IsFalseEverywhereWhenThereIsNoDamageWindow()
        {
            var traversal = Attack(damageStart: 0.5f, damageEnd: 0.5f);

            for (var step = 0; step <= 20; step++)
            {
                var progress = step / 20f;
                Assert.That(traversal.IsInDamageWindow(progress), Is.False, $"progress {progress}");
            }
        }

        [Test]
        public void IsInDamageWindow_IncludesTheStartAndExcludesTheEnd()
        {
            var profile = Attack();

            Assert.That(profile.IsInDamageWindow(AuthoredDamageStart), Is.True, "start is inclusive");
            Assert.That(profile.IsInDamageWindow(AuthoredDamageEnd), Is.False, "end is exclusive");
            Assert.That(profile.IsInDamageWindow(AuthoredDamageStart - 0.01f), Is.False);
            Assert.That(profile.IsInDamageWindow(AuthoredDamageEnd - 0.01f), Is.True);
        }

        [Test]
        public void IsInParryWindow_IncludesTheStartAndExcludesTheEnd()
        {
            var profile = Attack();

            Assert.That(profile.IsInParryWindow(0.05f), Is.True);
            Assert.That(profile.IsInParryWindow(0.949f), Is.True);
            Assert.That(profile.IsInParryWindow(0.95f), Is.False);
            Assert.That(profile.IsInParryWindow(0.049f), Is.False);
        }

        /// <summary>
        /// Krieg must be the fastest of the three, and Vor must shorten whatever base it is applied to.
        /// This is the whole of the doc's tempo rule, so it is asserted as one ordering rather than as
        /// three independent numbers.
        /// </summary>
        [Test]
        public void ResolveDuration_OrdersVorAndKriegAgainstTheBaseDuration()
        {
            var profile = Attack();

            var kriegVor = profile.ResolveDuration(Measure.Krieg, true);
            var krieg = profile.ResolveDuration(Measure.Krieg, false);
            var zufechten = profile.ResolveDuration(Measure.Zufechten, false);

            Assert.That(kriegVor, Is.LessThan(krieg));
            Assert.That(krieg, Is.LessThan(zufechten));
            Assert.That(krieg, Is.EqualTo(AuthoredKriegDuration).Within(Tolerance));
            Assert.That(kriegVor, Is.EqualTo(AuthoredKriegDuration * TransitionProfile.VorDurationScale).Within(Tolerance));
            Assert.That(zufechten, Is.EqualTo(AuthoredDuration).Within(Tolerance));
        }

        [Test]
        public void ResolveDuration_KeepsTheBaseDurationOutsideKrieg()
        {
            var profile = Attack();

            Assert.That(profile.ResolveDuration(Measure.Ringen, false), Is.EqualTo(AuthoredDuration).Within(Tolerance));
            Assert.That(profile.ResolveDuration(Measure.OutOfRange, false), Is.EqualTo(AuthoredDuration).Within(Tolerance));
        }

        /// <summary>
        /// Enemies clone the shared default table to tighten their own gates. If a clone shared any
        /// mutable state, tuning one enemy would silently retune the player's guard switching.
        /// </summary>
        [Test]
        public void Clone_ProducesAnInstanceThatDoesNotWriteThroughToTheOriginal()
        {
            var original = Attack();
            original.Duration = 0.9f;
            original.DamageWindowStart = 0.1f;
            original.Commitment = 0.8f;
            original.EnforceDistanceGate = false;
            original.LungeDistance = 1.5f;

            var clone = original.Clone();

            clone.Duration = 0.11f;
            clone.KriegDuration = 0.05f;
            clone.DamageWindowStart = 0.4f;
            clone.DamageWindowEnd = 0.9f;
            clone.Commitment = 0.05f;
            clone.EnforceDistanceGate = true;
            clone.FeintWhenOutOfRange = false;
            clone.LungeDistance = 9f;
            clone.PassStateName = "Mutated";

            Assert.That(original.Duration, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(original.KriegDuration, Is.EqualTo(AuthoredKriegDuration).Within(Tolerance));
            Assert.That(original.DamageWindowStart, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(original.DamageWindowEnd, Is.EqualTo(AuthoredDamageEnd).Within(Tolerance));
            Assert.That(original.Commitment, Is.EqualTo(0.8f).Within(Tolerance));
            Assert.That(original.EnforceDistanceGate, Is.False);
            Assert.That(original.FeintWhenOutOfRange, Is.True);
            Assert.That(original.LungeDistance, Is.EqualTo(1.5f).Within(Tolerance));
            Assert.That(original.PassStateName, Is.EqualTo("Langort"));
        }

        [Test]
        public void Clone_CarriesTheAuthoredValuesAcross()
        {
            var original = Attack();
            original.EnforceDistanceGate = true;
            original.Commitment = 0.2f;

            var clone = original.Clone();

            Assert.That(clone.From, Is.EqualTo(original.From));
            Assert.That(clone.To, Is.EqualTo(original.To));
            Assert.That(clone.Direction, Is.EqualTo(original.Direction));
            Assert.That(clone.Duration, Is.EqualTo(original.Duration).Within(Tolerance));
            Assert.That(clone.DamageWindowStart, Is.EqualTo(original.DamageWindowStart).Within(Tolerance));
            Assert.That(clone.DamageWindowEnd, Is.EqualTo(original.DamageWindowEnd).Within(Tolerance));
            Assert.That(clone.Commitment, Is.EqualTo(original.Commitment).Within(Tolerance));
            Assert.That(clone.EnforceDistanceGate, Is.True);
        }
    }
}

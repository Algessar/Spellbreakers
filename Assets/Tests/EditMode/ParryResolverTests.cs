using NUnit.Framework;
using Spellbreakers.Combat;

namespace Spellbreakers.Tests
{
    /// <summary>
    /// The resolver adds exactly one rule on top of the authored table: timing. A defender who is not
    /// inside a parry window cannot parry at all, no matter how favourable the matchup - otherwise the
    /// game would reward holding a guard instead of reading an attack.
    /// </summary>
    [TestFixture]
    public sealed class ParryResolverTests
    {
        private const float Tolerance = 1e-5f;
        private const float LandedProgress = 0.45f;

        private static GuardTransitionState Attack(
            GuardId from = GuardId.VomTag,
            GuardId to = GuardId.Alber,
            TransitionDirection direction = TransitionDirection.Oberhau)
        {
            return new GuardTransitionState(
                new TransitionProfile(from, to, direction, 0.42f, 0.30f, 0.62f));
        }

        /// <summary>
        /// Off time means no parry at all. The cell is deliberately one that says "Parry", so this test
        /// proves the timing rule overrides the table rather than agreeing with it.
        /// </summary>
        [Test]
        public void Resolve_WithTheParryWindowClosed_HitsBodyEvenWhenTheTableSaysParry()
        {
            var table = ParryTable.CreateDefault();
            var attacker = Attack(direction: TransitionDirection.Oberhau);

            Assert.That(table.ResolveGuard(GuardId.VomTag, TransitionDirection.Oberhau), Is.EqualTo(CombatOutcome.Parry));

            var contact = ParryResolver.Resolve(
                attacker,
                GuardId.VomTag,
                default,
                false,
                table,
                LandedProgress);

            Assert.That(contact.Outcome, Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(contact.IsBladeOnBlade, Is.False);
        }

        [TestCase(GuardId.VomTag, TransitionDirection.Oberhau)]
        [TestCase(GuardId.VomTag, TransitionDirection.Unterhau)]
        [TestCase(GuardId.Pflug, TransitionDirection.Thrust)]
        [TestCase(GuardId.Alber, TransitionDirection.Oberhau)]
        [TestCase(GuardId.Ochs, TransitionDirection.Thrust)]
        [TestCase(GuardId.Langort, TransitionDirection.Mittelhau)]
        [TestCase(GuardId.IronGate, TransitionDirection.Unterhau)]
        public void Resolve_WithAWindowOpenAndAStationaryDefender_UsesTheGuardRow(
            GuardId defenderGuard,
            TransitionDirection attackDirection)
        {
            var table = ParryTable.CreateDefault();
            var attacker = Attack(direction: attackDirection);

            var contact = ParryResolver.Resolve(attacker, defenderGuard, default, true, table, LandedProgress);

            Assert.That(contact.Outcome, Is.EqualTo(table.ResolveGuard(defenderGuard, attackDirection)));
            Assert.That(contact.DefenderGuard, Is.EqualTo(defenderGuard));
            Assert.That(contact.DefenderWasTransitioning, Is.False);
            Assert.That(contact.AttackerGuard, Is.EqualTo(attacker.From));
            Assert.That(contact.Progress01, Is.EqualTo(LandedProgress).Within(Tolerance));
        }

        /// <summary>Meeting an attack with an attack is decided by the transition columns, not by the
        /// guard the defender happened to start from.</summary>
        [TestCase(TransitionDirection.Oberhau, TransitionDirection.Oberhau, CombatOutcome.Bind)]
        [TestCase(TransitionDirection.Oberhau, TransitionDirection.Unterhau, CombatOutcome.HitBody)]
        [TestCase(TransitionDirection.Thrust, TransitionDirection.Oberhau, CombatOutcome.Parry)]
        [TestCase(TransitionDirection.Unterhau, TransitionDirection.Mittelhau, CombatOutcome.Deflect)]
        public void Resolve_WithATransitioningDefender_UsesTheTransitionTable(
            TransitionDirection defence,
            TransitionDirection attackDirection,
            CombatOutcome expected)
        {
            var table = ParryTable.CreateDefault();
            var attacker = Attack(direction: attackDirection);
            var defender = Attack(GuardId.Ochs, GuardId.Pflug, defence);

            var contact = ParryResolver.Resolve(attacker, GuardId.Ochs, defender, true, table, LandedProgress);

            Assert.That(contact.Outcome, Is.EqualTo(table.ResolveTransition(defence, attackDirection)));
            Assert.That(contact.Outcome, Is.EqualTo(expected));
            Assert.That(contact.DefenderWasTransitioning, Is.True);
        }

        /// <summary>An attack with no profile has no direction to look up, so there is nothing to parry
        /// and the only safe answer is that the blade reached the body.</summary>
        [Test]
        public void Resolve_WithAnInvalidAttackerTransition_HitsBody()
        {
            var table = ParryTable.CreateDefault();

            var contact = ParryResolver.Resolve(default, GuardId.Ochs, default, true, table, LandedProgress);

            Assert.That(contact.Outcome, Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(contact.AttackerTransition.IsValid, Is.False);
            Assert.That(contact.AttackerGuard, Is.EqualTo(GuardId.None));
        }

        [Test]
        public void Resolve_WithNoTable_HitsBody()
        {
            var contact = ParryResolver.Resolve(Attack(), GuardId.Ochs, default, true, null, LandedProgress);

            Assert.That(contact.Outcome, Is.EqualTo(CombatOutcome.HitBody));
        }

        [Test]
        public void Resolve_WithAnIncompleteTable_ReportsUndefinedRatherThanGuessing()
        {
            var table = new ParryTable();

            var contact = ParryResolver.Resolve(Attack(), GuardId.Ochs, default, true, table, LandedProgress);

            Assert.That(contact.Outcome, Is.EqualTo(CombatOutcome.Undefined));
            Assert.That(contact.IsBladeOnBlade, Is.False);
        }

        [TestCase(CombatOutcome.Parry, true)]
        [TestCase(CombatOutcome.Bind, true)]
        [TestCase(CombatOutcome.Deflect, true)]
        [TestCase(CombatOutcome.HitBody, false)]
        [TestCase(CombatOutcome.Undefined, false)]
        public void IsBladeOnBlade_IsTrueOnlyWhenTheTwoBladesMet(CombatOutcome outcome, bool expected)
        {
            var contact = new CombatContact(outcome, GuardId.VomTag, Attack(), GuardId.Ochs, false, LandedProgress);

            Assert.That(contact.IsBladeOnBlade, Is.EqualTo(expected));
        }

        [Test]
        public void Describe_MentionsTheOutcomeAndWhetherTheDefenderWasMeetingTheAttack()
        {
            var table = ParryTable.CreateDefault();
            var contact = ParryResolver.Resolve(
                Attack(),
                GuardId.Ochs,
                Attack(GuardId.Ochs, GuardId.Pflug, TransitionDirection.Oberhau),
                true,
                table,
                LandedProgress);

            var described = contact.Describe();

            Assert.That(described, Does.Contain(contact.Outcome.ToString()));
            Assert.That(described, Does.Contain("meeting"));
        }

        /// <summary>A deflect is the only outcome that takes the attacker out of line; a bind leaves both
        /// fencers working, and landing a hit staggers the defender instead.</summary>
        [TestCase(CombatOutcome.Deflect, ReactionState.Staggered)]
        [TestCase(CombatOutcome.HitBody, ReactionState.None)]
        [TestCase(CombatOutcome.Parry, ReactionState.None)]
        [TestCase(CombatOutcome.Bind, ReactionState.None)]
        public void AttackerReactionFor_StaggersOnlyOnADeflect(CombatOutcome outcome, ReactionState expected)
        {
            Assert.That(ParryResolver.AttackerReactionFor(outcome, 0.4f), Is.EqualTo(expected));
        }

        [TestCase(CombatOutcome.HitBody, ReactionState.Hit)]
        [TestCase(CombatOutcome.Parry, ReactionState.None)]
        [TestCase(CombatOutcome.Deflect, ReactionState.None)]
        [TestCase(CombatOutcome.Bind, ReactionState.None)]
        public void DefenderReactionFor_FlinchesOnlyWhenTheBladeLands(CombatOutcome outcome, ReactionState expected)
        {
            Assert.That(ParryResolver.DefenderReactionFor(outcome), Is.EqualTo(expected));
        }

        [TestCase(0.5f, true)]
        [TestCase(0f, false)]
        [TestCase(-1f, false)]
        public void HitInfo_IsMeaningfulOnlyForPositiveDamage(float amount, bool meaningful)
        {
            var hit = new HitInfo(amount, GuardId.VomTag, Attack(), CombatOutcome.HitBody, LandedProgress);

            Assert.That(hit.IsMeaningful, Is.EqualTo(meaningful));
            Assert.That(hit.SourceGuard, Is.EqualTo(GuardId.VomTag));
            Assert.That(hit.Outcome, Is.EqualTo(CombatOutcome.HitBody));
            Assert.That(hit.Progress01, Is.EqualTo(LandedProgress).Within(Tolerance));
        }
    }
}

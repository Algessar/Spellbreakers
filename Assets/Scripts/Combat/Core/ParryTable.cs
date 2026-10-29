using System;
using System.Collections.Generic;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Which defensive answer beats which attack. This is the doc's "parrying tables" idea: not every
    /// transition stops every other transition, and the relations are authored data rather than
    /// emergent physics, because a simulation here would be both fragile and unreadable.
    ///
    /// Two lookups are needed. A defender who is holding a guard is answered by
    /// <see cref="ResolveGuard"/>; a defender who is mid-transition and meeting the attack with their
    /// own is answered by <see cref="ResolveTransition"/>.
    ///
    /// Missing cells resolve to <see cref="CombatOutcome.Undefined"/> rather than silently defaulting,
    /// so a hole in the table is caught by a completeness test instead of showing up as a balance bug.
    /// </summary>
    public sealed class ParryTable
    {
        private readonly Dictionary<(GuardId Guard, TransitionDirection Attack), CombatOutcome> _guardCells =
            new Dictionary<(GuardId, TransitionDirection), CombatOutcome>();

        private readonly Dictionary<(TransitionDirection Defence, TransitionDirection Attack), CombatOutcome>
            _transitionCells =
                new Dictionary<(TransitionDirection, TransitionDirection), CombatOutcome>();

        private readonly HashSet<GuardId> _sideSensitive = new HashSet<GuardId>();

        public ParryTable()
        {
            // Which guards only stop a strike that arrives on the opposite side. A cut thrown from the
            // right side guard travels left to right across the defender, so the blade is only in the way
            // if the defender is holding the left-side version of the guard. Same-side guards sit behind
            // the incoming line and do nothing.
            //
            // Iron Gate and Pflug are deliberately absent: parrying a thrust is an active movement rather
            // than a static interception, and that rule is still open. They stay side-agnostic, exactly as
            // they behaved before sides existed.
            _sideSensitive.Add(GuardId.VomTag);
            _sideSensitive.Add(GuardId.Ochs);
            _sideSensitive.Add(GuardId.Alber);
        }

        public int GuardCellCount => _guardCells.Count;

        public int TransitionCellCount => _transitionCells.Count;

        /// <summary>Whether this guard's static parry depends on which side it is held on.</summary>
        public bool IsSideSensitive(GuardId guard) => _sideSensitive.Contains(guard);

        /// <summary>Overrides whether a guard's static parry depends on the side it is held on.</summary>
        public void SetSideSensitive(GuardId guard, bool sensitive)
        {
            if (guard == GuardId.None)
                return;

            if (sensitive)
                _sideSensitive.Add(guard);
            else
                _sideSensitive.Remove(guard);
        }

        /// <summary>Expected number of guard cells: every guard against every attack direction.</summary>
        public static int ExpectedGuardCells => GuardIds.All.Length * GuardIds.Directions.Length;

        /// <summary>Expected number of transition cells: every defence line against every attack line.</summary>
        public static int ExpectedTransitionCells => GuardIds.Directions.Length * GuardIds.Directions.Length;

        public void SetGuardCell(GuardId defenderGuard, TransitionDirection attack, CombatOutcome outcome)
        {
            _guardCells[(defenderGuard, attack)] = outcome;
        }

        public void SetTransitionCell(TransitionDirection defence, TransitionDirection attack, CombatOutcome outcome)
        {
            _transitionCells[(defence, attack)] = outcome;
        }

        /// <summary>The outcome of attacking a defender who is holding <paramref name="defenderGuard"/>.</summary>
        public CombatOutcome ResolveGuard(GuardId defenderGuard, TransitionDirection attack)
        {
            return _guardCells.TryGetValue((defenderGuard, attack), out var outcome)
                ? outcome
                : CombatOutcome.Undefined;
        }

        /// <summary>
        /// The outcome of attacking a defender who is holding a guard on a specific side, thrown from
        /// <paramref name="attackerSide"/>.
        ///
        /// This is the whole static-parry side rule: for a side-sensitive guard the authored outcome only
        /// counts when the defender is holding the *opposite* side to the attacker's, because that is the
        /// guard whose blade is actually on the line the strike travels along. A same-side guard is behind
        /// the cut, so the blade reaches the body no matter how good the matchup looks in the table.
        ///
        /// An unknown attacker side cannot be judged, so the row is returned unchanged rather than
        /// inventing a rule.
        /// </summary>
        public CombatOutcome ResolveGuard(GuardKey defenderGuard, TransitionDirection attack, GuardSide attackerSide)
        {
            var outcome = ResolveGuard(defenderGuard.Id, attack);

            if (outcome == CombatOutcome.Undefined)
                return CombatOutcome.Undefined;

            if (!IsSideSensitive(defenderGuard.Id) ||
                attackerSide == GuardSide.None ||
                defenderGuard.Side == GuardSide.None)
                return outcome;

            return defenderGuard.Side == GuardIds.Opposite(attackerSide)
                ? outcome
                : CombatOutcome.HitBody;
        }

        /// <summary>The outcome of meeting <paramref name="attack"/> with a transition along <paramref name="defence"/>.</summary>
        public CombatOutcome ResolveTransition(TransitionDirection defence, TransitionDirection attack)
        {
            return _transitionCells.TryGetValue((defence, attack), out var outcome)
                ? outcome
                : CombatOutcome.Undefined;
        }

        /// <summary>Every (guard, attack) pair the table can be asked about, for the completeness test.</summary>
        public static IEnumerable<(GuardId Guard, TransitionDirection Attack)> EnumerateGuardCells()
        {
            for (var g = 0; g < GuardIds.All.Length; g++)
            {
                for (var d = 0; d < GuardIds.Directions.Length; d++)
                    yield return (GuardIds.All[g], GuardIds.Directions[d]);
            }
        }

        /// <summary>Every (defence, attack) pair the table can be asked about.</summary>
        public static IEnumerable<(TransitionDirection Defence, TransitionDirection Attack)> EnumerateTransitionCells()
        {
            for (var a = 0; a < GuardIds.Directions.Length; a++)
            {
                for (var b = 0; b < GuardIds.Directions.Length; b++)
                    yield return (GuardIds.Directions[a], GuardIds.Directions[b]);
            }
        }

        /// <summary>
        /// The authored default relations. Reading them as fencing rather than as game balance:
        ///
        /// - Vom Tag is an offensive guard, so it meets a descending cut but is late against a rising one.
        /// - Pflug takes the rising line and deflects the descending one.
        /// - Alber invites the high cut (it loses to it) and snaps up into the rising line.
        /// - Ochs is point-in-line and answers almost everything except a rising cut underneath it.
        /// - Langort owns the thrust and the cross cut, but binds rather than beats a descending cut.
        /// - Iron Gate is a static barrier: strong across the lines, blind below.
        /// </summary>
        public static ParryTable CreateDefault()
        {
            var table = new ParryTable();

            table.SetGuardCell(GuardId.VomTag, TransitionDirection.Neutral, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.VomTag, TransitionDirection.Oberhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.VomTag, TransitionDirection.Mittelhau, CombatOutcome.Deflect);
            table.SetGuardCell(GuardId.VomTag, TransitionDirection.Unterhau, CombatOutcome.HitBody);
            table.SetGuardCell(GuardId.VomTag, TransitionDirection.Thrust, CombatOutcome.Bind);

            table.SetGuardCell(GuardId.Pflug, TransitionDirection.Neutral, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.Pflug, TransitionDirection.Oberhau, CombatOutcome.Deflect);
            table.SetGuardCell(GuardId.Pflug, TransitionDirection.Mittelhau, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.Pflug, TransitionDirection.Unterhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Pflug, TransitionDirection.Thrust, CombatOutcome.Deflect);

            table.SetGuardCell(GuardId.Alber, TransitionDirection.Neutral, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.Alber, TransitionDirection.Oberhau, CombatOutcome.HitBody);
            table.SetGuardCell(GuardId.Alber, TransitionDirection.Mittelhau, CombatOutcome.Deflect);
            table.SetGuardCell(GuardId.Alber, TransitionDirection.Unterhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Alber, TransitionDirection.Thrust, CombatOutcome.Bind);

            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Neutral, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Oberhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Mittelhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Unterhau, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.Ochs, TransitionDirection.Thrust, CombatOutcome.Parry);

            table.SetGuardCell(GuardId.Langort, TransitionDirection.Neutral, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.Langort, TransitionDirection.Oberhau, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.Langort, TransitionDirection.Mittelhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.Langort, TransitionDirection.Unterhau, CombatOutcome.Deflect);
            table.SetGuardCell(GuardId.Langort, TransitionDirection.Thrust, CombatOutcome.Parry);

            table.SetGuardCell(GuardId.IronGate, TransitionDirection.Neutral, CombatOutcome.Bind);
            table.SetGuardCell(GuardId.IronGate, TransitionDirection.Oberhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.IronGate, TransitionDirection.Mittelhau, CombatOutcome.Parry);
            table.SetGuardCell(GuardId.IronGate, TransitionDirection.Unterhau, CombatOutcome.HitBody);
            table.SetGuardCell(GuardId.IronGate, TransitionDirection.Thrust, CombatOutcome.Parry);

            // Transition against transition: meeting an attack with an attack.
            table.SetTransitionCell(TransitionDirection.Neutral, TransitionDirection.Neutral, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Neutral, TransitionDirection.Oberhau, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Neutral, TransitionDirection.Mittelhau, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Neutral, TransitionDirection.Unterhau, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Neutral, TransitionDirection.Thrust, CombatOutcome.HitBody);

            table.SetTransitionCell(TransitionDirection.Oberhau, TransitionDirection.Neutral, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Oberhau, TransitionDirection.Oberhau, CombatOutcome.Bind);
            table.SetTransitionCell(TransitionDirection.Oberhau, TransitionDirection.Mittelhau, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Oberhau, TransitionDirection.Unterhau, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Oberhau, TransitionDirection.Thrust, CombatOutcome.Parry);

            table.SetTransitionCell(TransitionDirection.Mittelhau, TransitionDirection.Neutral, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Mittelhau, TransitionDirection.Oberhau, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Mittelhau, TransitionDirection.Mittelhau, CombatOutcome.Bind);
            table.SetTransitionCell(TransitionDirection.Mittelhau, TransitionDirection.Unterhau, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Mittelhau, TransitionDirection.Thrust, CombatOutcome.Deflect);

            table.SetTransitionCell(TransitionDirection.Unterhau, TransitionDirection.Neutral, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Unterhau, TransitionDirection.Oberhau, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Unterhau, TransitionDirection.Mittelhau, CombatOutcome.Deflect);
            table.SetTransitionCell(TransitionDirection.Unterhau, TransitionDirection.Unterhau, CombatOutcome.Bind);
            table.SetTransitionCell(TransitionDirection.Unterhau, TransitionDirection.Thrust, CombatOutcome.HitBody);

            table.SetTransitionCell(TransitionDirection.Thrust, TransitionDirection.Neutral, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Thrust, TransitionDirection.Oberhau, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Thrust, TransitionDirection.Mittelhau, CombatOutcome.Parry);
            table.SetTransitionCell(TransitionDirection.Thrust, TransitionDirection.Unterhau, CombatOutcome.HitBody);
            table.SetTransitionCell(TransitionDirection.Thrust, TransitionDirection.Thrust, CombatOutcome.Bind);

            return table;
        }

        public ParryTable Clone()
        {
            var copy = new ParryTable();

            foreach (var entry in _guardCells)
                copy._guardCells[entry.Key] = entry.Value;

            foreach (var entry in _transitionCells)
                copy._transitionCells[entry.Key] = entry.Value;

            copy._sideSensitive.Clear();
            foreach (var guard in _sideSensitive)
                copy._sideSensitive.Add(guard);

            return copy;
        }
    }
}

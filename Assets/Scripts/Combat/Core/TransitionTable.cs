using System;
using System.Collections.Generic;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The authored set of legal transitions, plus the guard profiles they resolve against.
    ///
    /// Lookup is by (from, to). A transition that is not in the table simply does not exist - that is
    /// how the guard language stays small and how the AI's action space is bounded by design rather
    /// than by a hand-written list of special cases.
    /// </summary>
    public sealed class TransitionTable
    {
        private readonly Dictionary<(GuardId From, GuardId To), TransitionProfile> _profiles =
            new Dictionary<(GuardId, GuardId), TransitionProfile>();

        private readonly Dictionary<GuardId, GuardProfile> _guards = new Dictionary<GuardId, GuardProfile>();

        private readonly List<TransitionProfile> _ordered = new List<TransitionProfile>();

        public IReadOnlyList<TransitionProfile> Profiles => _ordered;

        public int Count => _ordered.Count;

        public bool AddGuard(GuardProfile guard)
        {
            if (guard == null || guard.Guard == GuardId.None)
                return false;

            _guards[guard.Guard] = guard;
            return true;
        }

        public bool Add(TransitionProfile profile)
        {
            if (profile == null)
                return false;

            var key = (profile.From, profile.To);
            if (_profiles.ContainsKey(key))
                return false;

            _profiles.Add(key, profile);
            _ordered.Add(profile);
            return true;
        }

        public bool TryGet(GuardId from, GuardId to, out TransitionProfile profile)
        {
            profile = null;
            if (from == GuardId.None || to == GuardId.None || from == to)
                return false;

            return _profiles.TryGetValue((from, to), out profile) && profile != null;
        }

        /// <summary>Guard profile, falling back to a neutral default so callers never null-check.</summary>
        public GuardProfile GuardOr(GuardId guard)
        {
            return _guards.TryGetValue(guard, out var profile) && profile != null
                ? profile
                : new GuardProfile(guard);
        }

        /// <summary>Every transition leaving a guard - the AI's candidate list before filters.</summary>
        public void CollectFrom(GuardId from, List<TransitionProfile> results)
        {
            if (results == null)
                return;

            results.Clear();
            for (var i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].From == from)
                    results.Add(_ordered[i]);
            }
        }

        /// <summary>
        /// Turns the measure gate on or off for every transition. The player's table leaves it off so
        /// guard switching stays unrestricted; an enemy's clone turns it on so distance governs it.
        /// </summary>
        public void SetDistanceGateForAll(bool enforce)
        {
            for (var i = 0; i < _ordered.Count; i++)
                _ordered[i].EnforceDistanceGate = enforce;
        }

        /// <summary>
        /// Deep copy. Archetypes mutate distance gates and windows, and must never mutate the shared
        /// default table the player uses.
        /// </summary>
        public TransitionTable Clone()
        {
            var copy = new TransitionTable();

            foreach (var entry in _guards)
                copy.AddGuard(entry.Value);

            for (var i = 0; i < _ordered.Count; i++)
                copy.Add(_ordered[i].Clone());

            return copy;
        }

        /// <summary>
        /// Adds a profile or fails loudly. The default table is code rather than authored data, so a
        /// duplicate pair there is unambiguously a programming mistake - and silently dropping one of the
        /// two rows is exactly the kind of bug that shows up much later as "why can nobody do this".
        /// </summary>
        private static void AddOrThrow(TransitionTable table, TransitionProfile profile)
        {
            if (table.Add(profile))
                return;

            throw new InvalidOperationException(
                $"The default transition table authors {profile.From} -> {profile.To} more than once. " +
                "Each guard change must appear exactly once, as either an attack or a recovery.");
        }

        /// <summary>
        /// An attack: a real pass with a live blade, gated by distance when the caller opts in.
        /// </summary>
        private static TransitionProfile Attack(
            GuardId from,
            GuardId to,
            TransitionDirection direction,
            float duration = 0.42f,
            float kriegDuration = 0.32f,
            float damageStart = 0.30f,
            float damageEnd = 0.62f,
            float lunge = 0.75f,
            float maxDistance = 2.6f,
            string passState = null)
        {
            return new TransitionProfile(from, to, direction, duration, damageStart, damageEnd)
            {
                KriegDuration = kriegDuration,
                LungeDistance = lunge,
                MaxDistance = maxDistance,
                MinDistance = 0f,
                PassStateName = passState ?? TransitionProfile.ResolveDefaultPassStateName(direction),
            };
        }

        /// <summary>
        /// A recovery out of Langort back into a guard. No live blade: the pass has already resolved and
        /// this is the beat an opponent is supposed to punish.
        /// </summary>
        private static TransitionProfile Recovery(GuardId to, float duration = 0.26f)
        {
            return new TransitionProfile(GuardId.Langort, to, TransitionDirection.Neutral, duration, 0f, 0f)
            {
                KriegDuration = duration * 0.8f,
                LungeDistance = 0f,
                MinDistance = 0f,
                MaxDistance = float.MaxValue,
                PassStateName = "Langort",
                Commitment = 0.1f,
            };
        }

        /// <summary>
        /// The canonical table: 18 attacks and 4 recoveries, which is 22 transitions over 6 guards.
        ///
        /// Reach and parry grace come from how each guard actually sits; the attack set is the small,
        /// readable subset of the Lichtenauer plays this game supports.
        /// </summary>
        public static TransitionTable CreateDefault()
        {
            var table = new TransitionTable();

            // Guards: reach reflects how far the point projects, grace reflects how quickly the guard
            // can actually meet an incoming blade. Langort projects furthest; Vom Tag is the most
            // offensive, so it has the shortest window to react from.
            table.AddGuard(new GuardProfile(GuardId.VomTag, 1.40f, 0.18f));
            table.AddGuard(new GuardProfile(GuardId.Pflug, 1.50f, 0.30f));
            table.AddGuard(new GuardProfile(GuardId.Alber, 1.30f, 0.35f));
            table.AddGuard(new GuardProfile(GuardId.Ochs, 1.60f, 0.28f));
            table.AddGuard(new GuardProfile(GuardId.Langort, 1.80f, 0.22f));
            table.AddGuard(new GuardProfile(GuardId.IronGate, 1.40f, 0.32f));

            // --- From Vom Tag: high, offensive. Zornhau and the thrust. ---
            AddOrThrow(table, Attack(GuardId.VomTag, GuardId.Alber, TransitionDirection.Oberhau));
            AddOrThrow(table, Attack(GuardId.VomTag, GuardId.Pflug, TransitionDirection.Oberhau, duration: 0.40f, lunge: 0.7f));
            AddOrThrow(table, Attack(GuardId.VomTag, GuardId.Langort, TransitionDirection.Thrust, duration: 0.38f, lunge: 0.85f));
            AddOrThrow(table, Attack(GuardId.VomTag, GuardId.Ochs, TransitionDirection.Mittelhau, duration: 0.44f));

            // --- From Pflug: patient, low point. Thrust, rising cut, cross cut. ---
            AddOrThrow(table, Attack(GuardId.Pflug, GuardId.Langort, TransitionDirection.Thrust, duration: 0.36f, lunge: 0.9f));
            AddOrThrow(table, Attack(GuardId.Pflug, GuardId.Ochs, TransitionDirection.Mittelhau, duration: 0.44f));
            AddOrThrow(table, Attack(GuardId.Pflug, GuardId.Alber, TransitionDirection.Unterhau, duration: 0.40f));
            AddOrThrow(table, Attack(GuardId.Pflug, GuardId.VomTag, TransitionDirection.Mittelhau, duration: 0.46f));

            // --- From Alber: the fool's guard invites, then snaps up. ---
            AddOrThrow(table, Attack(GuardId.Alber, GuardId.VomTag, TransitionDirection.Unterhau, duration: 0.44f, damageStart: 0.34f));
            AddOrThrow(table, Attack(GuardId.Alber, GuardId.Pflug, TransitionDirection.Unterhau, duration: 0.38f));
            AddOrThrow(table, Attack(GuardId.Alber, GuardId.Langort, TransitionDirection.Thrust, duration: 0.40f, lunge: 0.85f));

            // --- From Ochs: point in line, and the cut down across. ---
            AddOrThrow(table, Attack(GuardId.Ochs, GuardId.Langort, TransitionDirection.Thrust, duration: 0.32f, lunge: 0.95f));
            AddOrThrow(table, Attack(GuardId.Ochs, GuardId.Alber, TransitionDirection.Mittelhau, duration: 0.42f));
            AddOrThrow(table, Attack(GuardId.Ochs, GuardId.Pflug, TransitionDirection.Mittelhau, duration: 0.42f));
            AddOrThrow(table, Attack(GuardId.Ochs, GuardId.VomTag, TransitionDirection.Mittelhau, duration: 0.44f));

            // --- From Langort: the long point, then finishing cuts. ---
            AddOrThrow(table, Attack(GuardId.Langort, GuardId.Alber, TransitionDirection.Oberhau, duration: 0.40f, lunge: 0.5f));

            // --- Iron Gate is carried for the rig; kept to a thrust and a cut. ---
            AddOrThrow(table, Attack(GuardId.IronGate, GuardId.Langort, TransitionDirection.Thrust, duration: 0.36f));
            AddOrThrow(table, Attack(GuardId.IronGate, GuardId.Alber, TransitionDirection.Oberhau, duration: 0.44f));

            // --- Recoveries: a pass resolves by settling back into a guard. ---
            //
            // Alber is deliberately absent: Langort -> Alber is authored above as an Oberhau, because the
            // cut down from the long point is a real answer with a live blade. That keeps the rule crisp -
            // a transition with no damage window is a recovery - instead of "everything out of Langort is".
            AddOrThrow(table, Recovery(GuardId.VomTag));
            AddOrThrow(table, Recovery(GuardId.Pflug));
            AddOrThrow(table, Recovery(GuardId.Ochs));
            AddOrThrow(table, Recovery(GuardId.IronGate));

            return table;
        }
    }
}

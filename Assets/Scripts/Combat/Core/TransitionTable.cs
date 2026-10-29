using System;
using System.Collections.Generic;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The authored set of legal transitions, plus the guard profiles they resolve against.
    ///
    /// Lookup is by (from, to) *and the side each is held on*, because a right-side Vom Tag to Alber cut
    /// and its left-side mirror are different poses. A transition that is not in the table simply does not
    /// exist as an attack - that is how the guard language stays small and how the AI's action space is
    /// bounded by design rather than by a hand-written list of special cases.
    /// </summary>
    public sealed class TransitionTable
    {
        private readonly Dictionary<(GuardId From, GuardSide FromSide, GuardId To, GuardSide ToSide), TransitionProfile>
            _profiles =
                new Dictionary<(GuardId, GuardSide, GuardId, GuardSide), TransitionProfile>();

        private readonly Dictionary<GuardId, GuardProfile> _guards = new Dictionary<GuardId, GuardProfile>();

        /// <summary>Duration of a synthesised traversal: a plain guard change, not an attack.</summary>
        private const float SynthesizedDuration = 0.3f;

        private readonly Dictionary<(GuardId From, GuardSide FromSide, GuardId To, GuardSide ToSide), TransitionProfile>
            _synthesized =
                new Dictionary<(GuardId, GuardSide, GuardId, GuardSide), TransitionProfile>();

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

            var key = (profile.From, profile.FromSide, profile.To, profile.ToSide);
            if (_profiles.ContainsKey(key))
                return false;

            _profiles.Add(key, profile);
            _ordered.Add(profile);
            return true;
        }

        /// <summary>
        /// Authored lookup by guard pair only, ignoring sides, preferring the right-side expression.
        ///
        /// Prefer <see cref="TryResolve"/> or <see cref="TryGet(GuardKey, GuardKey, out TransitionProfile)"/>
        /// unless the caller genuinely does not care which side the transition is authored for.
        /// </summary>
        public bool TryGet(GuardId from, GuardId to, out TransitionProfile profile)
        {
            profile = null;
            if (from == GuardId.None || to == GuardId.None)
                return false;

            return _profiles.TryGetValue((from, GuardSide.Right, to, GuardSide.Right), out profile) &&
                   profile != null;
        }

        /// <summary>Authored lookup for an exact guard-and-side pair.</summary>
        public bool TryGet(GuardKey from, GuardKey to, out TransitionProfile profile)
        {
            profile = null;
            if (!from.IsValid || !to.IsValid)
                return false;

            return _profiles.TryGetValue((from.Id, from.Side, to.Id, to.Side), out profile) && profile != null;
        }

        /// <summary>
        /// Whether a guard pair with no authored expression is still traversable.
        ///
        /// Design doc 3.1: "Any guard should be able to transition to any other guard. The transition
        /// table describes how a particular transition is expressed, not which transitions are
        /// mechanically permitted." So the table is descriptive, not a permission list, and an unauthored
        /// pair resolves to a plain traversal rather than being refused.
        ///
        /// The synthesised traversal deliberately carries **no damage window**: having not authored how
        /// this transition attacks, it does not attack. That keeps the rule honest - movement between any
        /// two guards is never forbidden, but no free attack is invented either.
        /// </summary>
        public bool AllowSynthesis { get; set; } = true;

        /// <summary>
        /// Resolves how moving between two guard keys is expressed: the authored profile if there is one,
        /// otherwise a synthesised non-damaging traversal.
        ///
        /// A request for the exact state already held resolves only if it is an authored self-transition
        /// (a Zornhau thrown from and returning to Vom Tag); otherwise it is a no-op and returns false.
        /// </summary>
        public bool TryResolve(GuardKey from, GuardKey to, out TransitionProfile profile)
        {
            profile = null;
            if (!from.IsValid || !to.IsValid)
                return false;

            // The authored expression belongs to one specific guard-and-side pair. Moving between any
            // other combination is still legal, it just falls through to a synthesised traversal below.
            if (_profiles.TryGetValue((from.Id, from.Side, to.Id, to.Side), out var authored) &&
                authored != null)
            {
                profile = authored;
                return true;
            }

            // Nothing authored. A request for the state already held is a no-op, not a traversal. A side
            // switch is not the same state, so "R Vom Tag -> L Vom Tag" is a traversal and mirror-switching
            // therefore always works, even for a guard pair that has no authored attack.
            if (from == to || !AllowSynthesis)
                return false;

            profile = GetOrCreateSynthesized(from, to);
            return true;
        }

        /// <summary>
        /// Synthesised traversals are cached so repeated lookups do not allocate, and so identity is
        /// stable for callers that compare profiles by reference.
        /// </summary>
        private TransitionProfile GetOrCreateSynthesized(GuardKey from, GuardKey to)
        {
            var key = (from.Id, from.Side, to.Id, to.Side);
            if (_synthesized.TryGetValue(key, out var existing) && existing != null)
                return existing;

            var synthesized = new TransitionProfile(
                from.Id,
                to.Id,
                TransitionDirection.Neutral,
                SynthesizedDuration,
                0f,
                0f)
            {
                FromSide = from.Side,
                ToSide = to.Side,
                KriegDuration = SynthesizedDuration * 0.8f,
                LungeDistance = 0f,
                MinDistance = 0f,
                MaxDistance = float.MaxValue,
                PassStateName = "Langort",
                Commitment = 0.2f,
            };

            _synthesized.Add(key, synthesized);
            return synthesized;
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
                "Each guard change must appear exactly once.");
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
            string passState = "Langort")
        {
            return new TransitionProfile(from, to, direction, duration, damageStart, damageEnd)
            {
                KriegDuration = kriegDuration,
                LungeDistance = lunge,
                MaxDistance = maxDistance,
                MinDistance = 0f,
                PassStateName = passState,
            };
        }


        /// <summary>
        /// The canonical table, aligned to dev doc section 6: 21 attacks over the five standing guards.
        /// Langort appears as no row's source or destination - it is a pose attacks reach *through*, not
        /// one a fighter holds - so the settle-back beat is the Settle phase after each damage window.
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

            // --- The authored transition language, aligned to DEV/v0.2 section 6. ---
            //
            // Every row is an attack: the blade is live through the profile's damage window, and the beat
            // to be punished is the Settle phase after that window closes. There are no "recovery"
            // transitions any more. Langort is a pose an attack reaches *through*, never one a fighter
            // holds, so no row ends in Langort and the settle-back beat is carried by Settle itself.
            //
            // The technique name is display-only. The transition pose is where the attack extends to:
            // generally Langort at Zufechten, where reaching the opponent is the whole problem, and a
            // tighter pose in Krieg, where there is no distance left to bridge.

            void Author(
                GuardId from,
                GuardId to,
                TransitionDirection direction,
                string technique,
                GuardId pose = GuardId.Langort,
                GuardId poseInKrieg = GuardId.None,
                float duration = 0.42f,
                float lunge = 0.75f)
            {
                var authored = Attack(from, to, direction, duration: duration, lunge: lunge);
                authored.TechniqueName = technique;
                authored.TransitionPose = pose;
                authored.TransitionPoseInKrieg = poseInKrieg;
                AddOrThrow(table, authored);
            }

            // From Vom Tag: high and offensive. Zornhau and the thrust.
            Author(GuardId.VomTag, GuardId.VomTag, TransitionDirection.Oberhau, "Zornhau", lunge: 0.35f);
            Author(GuardId.VomTag, GuardId.Pflug, TransitionDirection.Thrust, "Stichen", lunge: 0.7f);
            Author(GuardId.VomTag, GuardId.Alber, TransitionDirection.Oberhau, "Zornhau");
            Author(GuardId.VomTag, GuardId.Ochs, TransitionDirection.Oberhau, "Zornhau");
            Author(GuardId.VomTag, GuardId.IronGate, TransitionDirection.Oberhau, "Zornhau", lunge: 0.6f);

            // From Pflug: patient, low point. Every answer out of it is a thrust.
            Author(GuardId.Pflug, GuardId.VomTag, TransitionDirection.Thrust, "Stichen", duration: 0.46f);
            Author(GuardId.Pflug, GuardId.IronGate, TransitionDirection.Thrust, "Stichen", duration: 0.34f, lunge: 0.5f);
            Author(GuardId.Pflug, GuardId.Alber, TransitionDirection.Thrust, "Stichen", duration: 0.40f);
            Author(GuardId.Pflug, GuardId.Ochs, TransitionDirection.Thrust, "Stichen", duration: 0.44f);

            // From Alber: the fool's guard invites, then snaps up or across.
            Author(GuardId.Alber, GuardId.VomTag, TransitionDirection.Unterhau, "Unterhau",
                pose: GuardId.Pflug, poseInKrieg: GuardId.Langort, duration: 0.44f);
            Author(GuardId.Alber, GuardId.Pflug, TransitionDirection.Thrust, "Stichen", duration: 0.38f);
            Author(GuardId.Alber, GuardId.Ochs, TransitionDirection.Unterhau, "Unterhau");
            Author(GuardId.Alber, GuardId.IronGate, TransitionDirection.Mittelhau, "Mittelhau",
                pose: GuardId.Pflug, lunge: 0.6f);

            // From Ochs: point in line, and the cut down from it.
            Author(GuardId.Ochs, GuardId.VomTag, TransitionDirection.Oberhau, "Schwerhau", duration: 0.44f);
            Author(GuardId.Ochs, GuardId.Pflug, TransitionDirection.Thrust, "Stichen", duration: 0.42f);
            Author(GuardId.Ochs, GuardId.Alber, TransitionDirection.Oberhau, "Oberhau", duration: 0.42f);
            Author(GuardId.Ochs, GuardId.IronGate, TransitionDirection.Oberhau, "Zornhau",
                poseInKrieg: GuardId.Pflug, lunge: 0.6f);

            // From Iron Gate: a standing guard with its own binding, so it needs an answer to each guard.
            Author(GuardId.IronGate, GuardId.VomTag, TransitionDirection.Unterhau, "Unterhau",
                pose: GuardId.Pflug, poseInKrieg: GuardId.Langort);
            Author(GuardId.IronGate, GuardId.Pflug, TransitionDirection.Thrust, "Stichen",
                pose: GuardId.Pflug, poseInKrieg: GuardId.Langort, duration: 0.36f);
            Author(GuardId.IronGate, GuardId.Alber, TransitionDirection.Mittelhau, "Mittelhau",
                pose: GuardId.Pflug, poseInKrieg: GuardId.Langort, duration: 0.44f);
            Author(GuardId.IronGate, GuardId.Ochs, TransitionDirection.Unterhau, "Unterhau",
                pose: GuardId.Pflug, poseInKrieg: GuardId.Langort);

            table.MirrorToLeftSide();

            return table;
        }

        /// <summary>
        /// Authors the left-side counterpart of every right-side transition, so a fighter leading with the
        /// other foot has the same attack language rather than being reduced to non-damaging traversals.
        ///
        /// Handedness lives in the guard side, not in <see cref="TransitionDirection"/> - the direction names
        /// the *line* (Oberhau, Mittelhau ...), and a right-side Oberhau travels right-to-left while its
        /// left-side mirror travels left-to-right. The direction value therefore carries over unchanged, and
        /// only the side and the pass state name (which gains the ".L" suffix) differ.
        /// </summary>
        private void MirrorToLeftSide()
        {
            var rightSide = new List<TransitionProfile>();
            for (var i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].FromSide == GuardSide.Right && _ordered[i].ToSide == GuardSide.Right)
                    rightSide.Add(_ordered[i]);
            }

            for (var i = 0; i < rightSide.Count; i++)
            {
                var source = rightSide[i];

                var mirrored = source.Clone();
                mirrored.FromSide = GuardSide.Left;
                mirrored.ToSide = GuardSide.Left;
                mirrored.PassStateName = GuardIds.ToPassStateName(source.PassStateName, GuardSide.Left);

                AddOrThrow(this, mirrored);
            }
        }
    }
}

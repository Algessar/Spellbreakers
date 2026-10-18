using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The small set of guards that make up the combat language. A transition between two guards is
    /// the attack; there are no separate attack moves. See the design doc, section 2.1.
    ///
    /// Langort is retained as a pass-only guard (section 5): combat motion passes through it without
    /// necessarily ending there, so no authored transition has Langort as its destination.
    /// </summary>
    public enum GuardId
    {
        None = 0,
        VomTag = 1,
        Pflug = 2,
        Alber = 3,
        Ochs = 4,
        Langort = 5,
        IronGate = 6,
    }

    /// <summary>
    /// Which side a guard is held on. Section 4 of the design doc: guards are currently authored
    /// right-side only, where the left foot leads, and left/right variants are dependent on the leading
    /// foot. Mirroring is possible but not assumed to be visually correct, so sides are a data dimension
    /// rather than something the runtime derives.
    ///
    /// <see cref="Right"/> is the default everywhere, which keeps every existing authored asset, Animator
    /// state and test valid without change.
    /// </summary>
    public enum GuardSide
    {
        None = 0,
        Right = 1,
        Left = 2,
    }

    /// <summary>
    /// A guard *and* the side it is held on.
    ///
    /// This exists so that a self-transition can be expressed as the design doc's side-switch
    /// (section 3.2: "R Vom Tag -> L Vom Tag = Zornhau"). Keying transitions on the guard alone cannot
    /// distinguish "stay in Vom Tag on the same side" from "switch sides", and a quadruped that prepares
    /// a left claw versus a right claw is exactly that distinction.
    ///
    /// Note that <see cref="ParryTable"/> stays keyed by <see cref="GuardId"/> alone: the doc does not
    /// propose that left- and right-side guards have different *defensive relations*, only different
    /// poses. Keeping the parry table side-agnostic avoids doubling it to sixty cells for no stated gain.
    /// </summary>
    public readonly struct GuardKey : IEquatable<GuardKey>
    {
        public GuardKey(GuardId id, GuardSide side)
        {
            Id = id;
            Side = id == GuardId.None ? GuardSide.None : side;
        }

        public GuardId Id { get; }

        public GuardSide Side { get; }

        public bool IsValid => Id != GuardId.None;

        public bool IsRight => Side != GuardSide.Left;

        /// <summary>Convenience for the overwhelmingly common right-side case.</summary>
        public static GuardKey Right(GuardId id) => new GuardKey(id, GuardSide.Right);

        public static GuardKey Left(GuardId id) => new GuardKey(id, GuardSide.Left);

        public static readonly GuardKey None = new GuardKey(GuardId.None, GuardSide.None);

        public GuardKey WithSide(GuardSide side) => new GuardKey(Id, side);

        public GuardKey OppositeSide => new GuardKey(Id, IsRight ? GuardSide.Left : GuardSide.Right);

        public bool Equals(GuardKey other) => Id == other.Id && Side == other.Side;

        public override bool Equals(object obj) => obj is GuardKey other && Equals(other);

        public override int GetHashCode() => ((int)Id * 397) ^ (int)Side;

        public static bool operator ==(GuardKey left, GuardKey right) => left.Equals(right);

        public static bool operator !=(GuardKey left, GuardKey right) => !left.Equals(right);

        public override string ToString() => GuardIds.ToDisplayName(this);
    }

    /// <summary>
    /// The line an attack travels along. Used instead of per-pair animation identity so a small
    /// number of pass clips can make every transition readable, and so parry relations can be
    /// expressed as a table rather than a combinatorial explosion.
    /// </summary>
    public enum TransitionDirection
    {
        /// <summary>No live blade - recovering to a guard, or a guard change that is not an attack.</summary>
        Neutral = 0,
        Oberhau = 1,
        Mittelhau = 2,
        Unterhau = 3,
        Thrust = 4,
    }

    /// <summary>Helpers for <see cref="GuardId"/>, <see cref="GuardKey"/> and <see cref="GuardSide"/>.</summary>
    public static class GuardIds
    {
        /// <summary>The guards the design doc names as the core language.</summary>
        public static readonly GuardId[] Canonical =
        {
            GuardId.VomTag,
            GuardId.Pflug,
            GuardId.Alber,
            GuardId.Ochs,
            GuardId.Langort,
        };

        /// <summary>Every guard the build can represent, canonical ones first.</summary>
        public static readonly GuardId[] All =
        {
            GuardId.VomTag,
            GuardId.Pflug,
            GuardId.Alber,
            GuardId.Ochs,
            GuardId.Langort,
            GuardId.IronGate,
        };

        /// <summary>
        /// Guards a transition may legitimately *end* in. Langort is excluded because it is pass-only.
        /// </summary>
        public static readonly GuardId[] Destinations =
        {
            GuardId.VomTag,
            GuardId.Pflug,
            GuardId.Alber,
            GuardId.Ochs,
            GuardId.IronGate,
        };

        /// <summary>Every direction, in a stable order for table-completeness checks.</summary>
        public static readonly TransitionDirection[] Directions =
        {
            TransitionDirection.Neutral,
            TransitionDirection.Oberhau,
            TransitionDirection.Mittelhau,
            TransitionDirection.Unterhau,
            TransitionDirection.Thrust,
        };

        public static readonly GuardSide[] Sides =
        {
            GuardSide.Right,
            GuardSide.Left,
        };

        /// <summary>Suffix appended to an Animator state name for a non-default side.</summary>
        public const string LeftSideSuffix = ".L";

        public static bool IsCanonical(GuardId guard)
        {
            for (var i = 0; i < Canonical.Length; i++)
            {
                if (Canonical[i] == guard)
                    return true;
            }

            return false;
        }

        /// <summary>Human-readable name, used by the debug overlay and by failure messages.</summary>
        public static string ToDisplayName(GuardId guard)
        {
            switch (guard)
            {
                case GuardId.VomTag: return "Vom Tag";
                case GuardId.Pflug: return "Pflug";
                case GuardId.Alber: return "Alber";
                case GuardId.Ochs: return "Ochs";
                case GuardId.Langort: return "Langort";
                case GuardId.IronGate: return "Iron Gate";
                default: return "None";
            }
        }

        public static string ToDisplayName(GuardKey guard)
        {
            if (!guard.IsValid)
                return "None";

            var name = ToDisplayName(guard.Id);
            return guard.Side == GuardSide.Left ? name + " (left)" : name;
        }

        /// <summary>
        /// The Animator state name for a guard. Kept here, next to the guard it belongs to, so the
        /// Animator path is derived rather than repeated as a string literal in several systems.
        /// </summary>
        public static string ToStateName(GuardId guard)
        {
            switch (guard)
            {
                case GuardId.VomTag: return "VomTag";
                case GuardId.Pflug: return "Pflug";
                case GuardId.Alber: return "Alber";
                case GuardId.Ochs: return "Ochs";
                case GuardId.Langort: return "Langort";
                case GuardId.IronGate: return "IronGate";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Animator state name for a guard on a given side. The right side keeps the bare name so every
        /// existing state, clip and asset stays valid; only a left-side guard needs a distinct state.
        /// </summary>
        public static string ToStateName(GuardKey guard)
        {
            var name = ToStateName(guard.Id);
            if (string.IsNullOrEmpty(name) || guard.Side != GuardSide.Left)
                return name;

            return name + LeftSideSuffix;
        }

        public static GuardSide ParseSide(string stateName)
        {
            return !string.IsNullOrEmpty(stateName) &&
                   stateName.EndsWith(LeftSideSuffix, StringComparison.OrdinalIgnoreCase)
                ? GuardSide.Left
                : GuardSide.Right;
        }

        /// <summary>Parses an Animator state name back into a guard, for migration and editor tooling.</summary>
        public static GuardKey Parse(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
                return GuardKey.None;

            var side = ParseSide(stateName);
            var trimmed = side == GuardSide.Left
                ? stateName.Substring(0, stateName.Length - LeftSideSuffix.Length)
                : stateName;

            for (var i = 0; i < All.Length; i++)
            {
                if (string.Equals(ToStateName(All[i]), trimmed, StringComparison.OrdinalIgnoreCase))
                    return new GuardKey(All[i], side);
            }

            return GuardKey.None;
        }
    }
}

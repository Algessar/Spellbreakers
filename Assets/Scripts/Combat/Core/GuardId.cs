namespace Spellbreakers.Combat
{
    /// <summary>
    /// The small set of guards that make up the combat language. A transition between two guards is
    /// the attack; there are no separate attack moves. See the design doc, section 2.1.
    ///
    /// Langort is both a guard and the pose every transition passes through, which is what makes a
    /// transition structurally different from a conventional attack animation.
    /// </summary>
    public enum GuardId
    {
        None = 0,
        VomTag = 1,
        Pflug = 2,
        Alber = 3,
        Ochs = 4,
        Langort = 5,

        /// <summary>
        /// Carried because the rig, the Animator controller and the player's input bindings all have
        /// it, but it is not in the design doc's guard list. Kept out of enemy transition tables until
        /// the doc decides whether it belongs.
        /// </summary>
        IronGate = 6,
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

    /// <summary>Helpers for <see cref="GuardId"/>. Kept allocation-free for hot paths.</summary>
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

        /// <summary>Every direction, in a stable order for table-completeness checks.</summary>
        public static readonly TransitionDirection[] Directions =
        {
            TransitionDirection.Neutral,
            TransitionDirection.Oberhau,
            TransitionDirection.Mittelhau,
            TransitionDirection.Unterhau,
            TransitionDirection.Thrust,
        };

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

        /// <summary>Parses an Animator state name back into a guard, for migration and editor tooling.</summary>
        public static GuardId Parse(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
                return GuardId.None;

            for (var i = 0; i < All.Length; i++)
            {
                if (string.Equals(ToStateName(All[i]), stateName, System.StringComparison.OrdinalIgnoreCase))
                    return All[i];
            }

            return GuardId.None;
        }
    }
}

using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Distance bands from the fencing source material. The design doc treats measure as a first-class
    /// part of combat, so it is modelled explicitly rather than being derived ad hoc at each call site.
    /// </summary>
    public enum Measure
    {
        /// <summary>Beyond any useful engagement distance.</summary>
        OutOfRange = 0,

        /// <summary>Zufechten - out of measure. Only some transitions make sense; feints live here.</summary>
        Zufechten = 1,

        /// <summary>Krieg - close measure. Transitions are faster and parry windows shrink.</summary>
        Krieg = 2,

        /// <summary>
        /// Ringen - grappling distance. Deliberately out of scope beyond a shove or trip reaction;
        /// nothing in this framework should treat it as a full wrestling system.
        /// </summary>
        Ringen = 3,
    }

    /// <summary>Who a combatant will fight. Neutral targets are nobody's enemy.</summary>
    public enum CombatTeam
    {
        Neutral = 0,
        Player = 1,
        Hostile = 2,
    }

    /// <summary>
    /// What a combatant is doing that is not fencing. Reactions freeze the combat clock, which is what
    /// makes a parry or a guard break a real change in the situation rather than a damage modifier.
    /// </summary>
    public enum ReactionState
    {
        None = 0,

        /// <summary>Brief flinch. The blade landed.</summary>
        Hit = 1,

        /// <summary>Blade was displaced; the combatant is off line and cannot transition.</summary>
        Staggered = 2,

        /// <summary>A guard was broken outright, leaving a long opening.</summary>
        GuardBroken = 3,

        /// <summary>Terminal. Never expires.</summary>
        Dead = 4,
    }

    /// <summary>How a blade contact resolved. Undefined means the parry table has a hole in it.</summary>
    public enum CombatOutcome
    {
        Undefined = 0,

        /// <summary>The attacking blade reached the body.</summary>
        HitBody = 1,

        /// <summary>The defending blade met the attack in time and stopped it.</summary>
        Parry = 2,

        /// <summary>Both blades met and neither gave ground - the fight is in Krieg.</summary>
        Bind = 3,

        /// <summary>The attacking blade was pushed off line; the attacker is staggered.</summary>
        Deflect = 4,
    }

    /// <summary>What happened when a guard change was requested.</summary>
    public enum RequestResult
    {
        /// <summary>The transition is running and its blade can land.</summary>
        Accepted = 1,

        /// <summary>Nothing started: unknown transition, wrong guard, reaction, or too close.</summary>
        Rejected = 2,

        /// <summary>A transition is already running; this target is held in the single-slot queue.</summary>
        Queued = 3,

        /// <summary>
        /// Too far away: the transition starts but aborts after a short beat. This is the doc's
        /// Zufechten rule - being out of measure should encourage closing, not reward flailing.
        /// </summary>
        Feinted = 4,
    }

    /// <summary>
    /// Animation phases published by the combat core. They are derived from the transition's authored
    /// windows rather than from Animator normalised time, so hit detection, IK and AI all read the same
    /// clock the decisions were made on.
    /// </summary>
    public enum PassPhase
    {
        None = 0,

        /// <summary>Travelling to the pass-through pose; nothing is live yet.</summary>
        ToPass = 1,

        /// <summary>The blade is live. This is the only phase that can damage.</summary>
        Strike = 2,

        /// <summary>Follow-through and recovery. Punishable by an opponent who read the attack.</summary>
        Settle = 3,
    }

    public static class CombatTeams
    {
        /// <summary>Neutral is hostile to nobody, so props and dummies can be swung at without a war.</summary>
        public static bool AreHostile(CombatTeam a, CombatTeam b)
        {
            if (a == CombatTeam.Neutral || b == CombatTeam.Neutral)
                return false;

            return a != b;
        }

        public static bool IsValid(CombatTeam team)
        {
            return Enum.IsDefined(typeof(CombatTeam), team);
        }
    }
}

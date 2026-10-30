using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// The combat results that are worth hearing.
    ///
    /// These are deliberately named after the things the rules already produce - <see cref="CombatOutcome"/>
    /// and <see cref="PassPhase"/> - rather than after sounds, so the audio layer can be debugged by ear
    /// against the same vocabulary the console log and the debug overlay use. "Parry" here means exactly
    /// what <see cref="CombatOutcome.Parry"/> means: the defending blade met the attack in time and stopped
    /// it. Nothing in this enum is a presentation decision.
    ///
    /// <see cref="Swing"/> and <see cref="Miss"/> are the two cues that carry information no damage number
    /// can: a blade that went live, and a blade that went live and touched nothing. They exist because
    /// "the attack did nothing" is otherwise indistinguishable from "the attack never started".
    /// </summary>
    public enum CombatSoundCue
    {
        /// <summary>A strike window opened. The blade is live; something may yet happen.</summary>
        Swing = 0,

        /// <summary>A strike window closed without contacting anything. The attack whiffed.</summary>
        Miss = 1,

        /// <summary>The attacking blade reached the body. Matches <see cref="CombatOutcome.HitBody"/>.</summary>
        HitBody = 2,

        /// <summary>The defence was clean and the attack stopped. Matches <see cref="CombatOutcome.Parry"/>.</summary>
        Parry = 3,

        /// <summary>The attacking blade was turned aside. Matches <see cref="CombatOutcome.Deflect"/>.</summary>
        Deflect = 4,

        /// <summary>Both blades met and neither gave ground. Matches <see cref="CombatOutcome.Bind"/>.</summary>
        Bind = 5,

        /// <summary>A guard was broken outright. Not yet produced by the rules; available for when it is.</summary>
        GuardBroken = 6,

        /// <summary>The defender's health reached zero.</summary>
        Death = 7,
    }

    /// <summary>
    /// One request to play a <see cref="CombatSoundCue"/> at a place in the world.
    ///
    /// The attacking and defending names are carried along purely so the console line says
    /// "Parry: Raine_Player attack vs ENEMY_Fencer" instead of "Parry"—the same reason
    /// <see cref="CombatContact.Describe"/> exists. They may be null for cues that have no second party.
    /// </summary>
    public readonly struct CombatSoundEvent
    {
        public CombatSoundEvent(
            CombatSoundCue cue,
            Vector3 position,
            string attacker = null,
            string defender = null,
            float progress01 = 0f)
        {
            Cue = cue;
            Position = position;
            Attacker = attacker;
            Defender = defender;
            Progress01 = progress01;
        }

        public CombatSoundCue Cue { get; }

        /// <summary>Where to play it. For a contact this is between the two fighters, so 3D falloff
        /// reads as distance from the player rather than distance from whoever reported it.</summary>
        public Vector3 Position { get; }

        public string Attacker { get; }

        public string Defender { get; }

        /// <summary>How far through the attacking transition the contact landed, for the console line.</summary>
        public float Progress01 { get; }

        public string Describe()
        {
            if (string.IsNullOrEmpty(Attacker))
                return Cue.ToString();

            var who = string.IsNullOrEmpty(Defender)
                ? Attacker
                : $"{Attacker} -> {Defender}";

            return $"{Cue}: {who} at {Progress01:0.00}";
        }
    }

    /// <summary>
    /// The single place a combat result turns into a sound request.
    ///
    /// Combat does not know that audio exists: <see cref="WeaponDamage"/> raises a cue here and moves on,
    /// and the listening <see cref="CombatSoundManager"/> - if there is one - decides whether anything is
    /// heard. With no manager, no clip assigned, or a muted profile, this is a silent no-op, which is what
    /// lets the whole audio layer be deleted or disabled without touching a single rule.
    ///
    /// This is a static event rather than a per-character one because one contact is one sound. Each
    /// contact is reported to both combatants by <see cref="WeaponDamage"/>, so subscribing to the
    /// per-character <see cref="CombatHandler.ContactResolved"/> would have played every parry twice.
    /// </summary>
    public static class CombatSoundEvents
    {
        /// <summary>Raised once per combat result that is worth hearing.</summary>
        public static event System.Action<CombatSoundEvent> CueRequested;

        /// <summary>True when something is listening, so callers can skip building a request at all.</summary>
        public static bool HasListeners => CueRequested != null;

        public static void Request(in CombatSoundEvent request)
        {
            var handler = CueRequested;
            if (handler == null)
                return;

            handler(request);
        }
    }
}

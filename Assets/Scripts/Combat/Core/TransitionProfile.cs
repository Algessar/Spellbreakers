using System;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Everything authored about one guard-to-guard transition: how long it takes, how far it reaches,
    /// when the blade is live and when it may be parried.
    ///
    /// This is the single place where the design doc's distance and timing rules live. Zufechten is
    /// <see cref="MinDistance"/>/<see cref="MaxDistance"/> plus <see cref="FeintWhenOutOfRange"/>;
    /// Krieg is <see cref="KriegDuration"/> and <see cref="GuardProfile.KriegParryGraceScale"/>;
    /// Vor is <see cref="VorDurationScale"/>. No system should hard-code those rules.
    /// </summary>
    public sealed class TransitionProfile
    {
        /// <summary>Having the initiative makes the same transition visibly quicker (the doc's Vor).</summary>
        public const float VorDurationScale = 0.85f;

        public TransitionProfile(
            GuardId from,
            GuardId to,
            TransitionDirection direction,
            float duration,
            float damageWindowStart,
            float damageWindowEnd)
        {
            if (from == GuardId.None)
                throw new ArgumentException("A transition needs a real source guard.", nameof(from));
            if (to == GuardId.None)
                throw new ArgumentException("A transition needs a real destination guard.", nameof(to));

            // A transition may legitimately begin and end in the same guard (design doc 3.2): a Zornhau
            // thrown from Vom Tag returns to Vom Tag. The system must not assume the destination always
            // differs from the source, so this is no longer rejected. Whether a given self-transition is
            // an attack or a no-op is decided entirely by its authored damage window.
            From = from;
            To = to;
            Direction = direction;
            Duration = FencingMath.Max(0.01f, duration);
            DamageWindowStart = FencingMath.Clamp01(damageWindowStart);
            DamageWindowEnd = FencingMath.Clamp01(damageWindowEnd);
        }

        public GuardId From { get; }

        public GuardId To { get; }

        /// <summary>Which side the source guard is held on. Right is the authored default (doc 4).</summary>
        public GuardSide FromSide { get; set; } = GuardSide.Right;

        /// <summary>Which side the destination guard ends on.</summary>
        public GuardSide ToSide { get; set; } = GuardSide.Right;

        public GuardKey FromKey => new GuardKey(From, FromSide);

        public GuardKey ToKey => new GuardKey(To, ToSide);

        /// <summary>Begins and ends in the same guard.</summary>
        public bool IsSelfTransition => From == To;

        /// <summary>
        /// A self-transition that changes which side the guard is held on - the doc's
        /// "R Vom Tag -> L Vom Tag = Zornhau" alternative to a same-side self-transition.
        /// </summary>
        public bool IsSideSwitch => IsSelfTransition && FromSide != ToSide;

        /// <summary>
        /// The technique's name for authoring and debug display only - "Zornhau", "Stichen", "Schwerhau".
        /// It carries no mechanics: the same transition behaves identically whether or not it is named,
        /// and the doc's Zornhau/Oberhau distinction is deliberately not a mechanical one.
        /// </summary>
        public string TechniqueName { get; set; }

        /// <summary>Human-readable identity, built from the guard keys plus the optional technique name.</summary>
        public string DisplayName
        {
            get
            {
                var name = GuardIds.ToDisplayName(FromKey) + " -> " + GuardIds.ToDisplayName(ToKey);
                if (IsSideSwitch)
                    name += " (side switch)";

                return string.IsNullOrEmpty(TechniqueName) ? name : name + " [" + TechniqueName + "]";
            }
        }

        /// <summary>
        /// The pose the transition reaches through - the doc's TransitionPose, formerly "pass-through".
        ///
        /// Every attack has to extend to reach its opponent, so the transition pose is the extreme
        /// position the attack passes through rather than a guard the fighter holds. Langort is the
        /// general case at Zufechten, where reaching is the whole problem.
        /// </summary>
        public GuardId TransitionPose { get; set; } = GuardId.Langort;

        /// <summary>
        /// The transition pose used instead when the fight is in Krieg, where there is no distance left
        /// to bridge and extending is counter-productive. None means "same as <see cref="TransitionPose"/>".
        ///
        /// This is what the doc's "Langort / Pflug*" entries express: the same transition selects a
        /// different intermediate pose depending on measure.
        /// </summary>
        public GuardId TransitionPoseInKrieg { get; set; } = GuardId.None;

        /// <summary>The transition pose for the current measure, falling back to the general one.</summary>
        public GuardId ResolveTransitionPose(Measure measure)
        {
            return measure == Measure.Krieg && TransitionPoseInKrieg != GuardId.None
                ? TransitionPoseInKrieg
                : TransitionPose;
        }

        public TransitionDirection Direction { get; set; }

        /// <summary>Base duration in seconds, used at Zufechten and beyond.</summary>
        public float Duration { get; set; }

        /// <summary>Duration in Krieg, where the doc asks for faster transitions.</summary>
        public float KriegDuration { get; set; }

        /// <summary>Below this distance the transition is refused outright.</summary>
        public float MinDistance { get; set; }

        /// <summary>Above this distance the transition becomes a feint rather than a committed attack.</summary>
        public float MaxDistance { get; set; } = 2.6f;

        /// <summary>
        /// Whether the measure gate applies at all. Off by default so the player's existing free guard
        /// switching is unchanged; enemy profiles and any deliberate attack opt in.
        /// </summary>
        public bool EnforceDistanceGate { get; set; }

        /// <summary>When out of measure, start and abort after <see cref="FeintDurationSeconds"/> instead of refusing.</summary>
        public bool FeintWhenOutOfRange { get; set; } = true;

        /// <summary>Normalised progress at which the blade becomes live.</summary>
        public float DamageWindowStart { get; set; }

        /// <summary>Normalised progress at which the blade stops being live and recovery begins.</summary>
        public float DamageWindowEnd { get; set; }

        /// <summary>Normalised progress at which this transition can meet and stop an incoming attack.</summary>
        public float ParryWindowStart { get; set; } = 0.05f;

        /// <summary>Normalised progress after which this transition can no longer parry.</summary>
        public float ParryWindowEnd { get; set; } = 0.95f;

        /// <summary>Normalised progress after which the transition can no longer be cancelled.</summary>
        public float Commitment { get; set; } = 0.35f;

        /// <summary>How far the pass carries the character forward, used by duel steering.</summary>
        public float LungeDistance { get; set; } = 0.75f;

        /// <summary>State name on the Animator's Passes layer that realises this transition.</summary>
        public string PassStateName { get; set; } = "Langort";

        /// <summary>
        /// Remaps normalized time (0..1) to non-linear biomechanical easing:
        /// slow preparatory chambering, explosive acceleration through the live damage window,
        /// and smooth follow-through settling into the destination guard.
        /// </summary>
        public float EvaluateVisualProgress(float progress01)
        {
            var p = FencingMath.Clamp01(progress01);
            if (!HasDamageWindow)
            {
                // Smoothstep for non-attack recoveries
                return p * p * (3f - 2f * p);
            }

            var start = DamageWindowStart;
            var end = DamageWindowEnd;

            if (p < start)
            {
                if (start <= 0f)
                    return 0f;
                var t = p / start;
                // Ease in: windup chambering
                return start * (t * t);
            }

            if (p < end)
            {
                if (end <= start)
                    return start;
                var t = (p - start) / (end - start);
                // Live strike: snappy, direct acceleration through the apex
                return start + (end - start) * t;
            }

            var rem = 1f - end;
            if (rem <= 0f)
                return 1f;
            var tRem = (p - end) / rem;
            // Ease out: smooth deceleration settling into guard
            var easeOut = 1f - (1f - tRem) * (1f - tRem);
            return end + rem * easeOut;
        }

        /// <summary>
        /// A recovery has no live blade. That is how the model expresses "leaving Langort back into a
        /// guard" without inventing a second kind of state.
        /// </summary>
        public bool HasDamageWindow => DamageWindowEnd > DamageWindowStart;

        public float FeintDurationSeconds => 0.2f;

        public bool IsInDamageWindow(float progress01)
        {
            return HasDamageWindow &&
                   progress01 >= DamageWindowStart &&
                   progress01 < DamageWindowEnd;
        }

        public bool IsInParryWindow(float progress01)
        {
            return progress01 >= ParryWindowStart && progress01 < ParryWindowEnd;
        }

        /// <summary>Duration for the current measure and initiative, per the doc's Zufechten/Krieg/Vor rules.</summary>
        public float ResolveDuration(Measure measure, bool vor)
        {
            var seconds = measure == Measure.Krieg ? KriegDuration : Duration;
            return vor ? seconds * VorDurationScale : seconds;
        }

        /// <summary>
        /// A circular clone. Used to derive per-archetype variants without sharing mutable state between
        /// the player's table and an enemy's.
        /// </summary>
        public TransitionProfile Clone()
        {
            return new TransitionProfile(From, To, Direction, Duration, DamageWindowStart, DamageWindowEnd)
            {
                FromSide = FromSide,
                ToSide = ToSide,
                TechniqueName = TechniqueName,
                TransitionPose = TransitionPose,
                TransitionPoseInKrieg = TransitionPoseInKrieg,
                KriegDuration = KriegDuration,
                MinDistance = MinDistance,
                MaxDistance = MaxDistance,
                EnforceDistanceGate = EnforceDistanceGate,
                FeintWhenOutOfRange = FeintWhenOutOfRange,
                ParryWindowStart = ParryWindowStart,
                ParryWindowEnd = ParryWindowEnd,
                Commitment = Commitment,
                LungeDistance = LungeDistance,
                PassStateName = PassStateName,
            };
        }

        public override string ToString()
        {
            return $"{DisplayName} ({Direction}, {Duration:0.00}s)";
        }
    }
}

namespace Spellbreakers.Combat
{
    /// <summary>
    /// One in-flight (or completed) guard transition. The core works in guards and transitions rather
    /// than in Animator states, so this is the identity that hit detection, parry resolution and AI
    /// perception all reason about.
    /// </summary>
    public readonly struct GuardTransitionState
    {
        public GuardTransitionState(TransitionProfile profile)
        {
            Profile = profile;
        }

        public TransitionProfile Profile { get; }

        public GuardId From => Profile != null ? Profile.From : GuardId.None;

        public GuardId To => Profile != null ? Profile.To : GuardId.None;

        /// <summary>Source guard including which side it is held on.</summary>
        public GuardKey FromKey => Profile != null ? Profile.FromKey : GuardKey.None;

        /// <summary>Destination guard including which side it ends on.</summary>
        public GuardKey ToKey => Profile != null ? Profile.ToKey : GuardKey.None;

        /// <summary>Begins and ends in the same guard. Legal, per design doc 3.2.</summary>
        public bool IsSelfTransition => Profile != null && Profile.IsSelfTransition;

        /// <summary>A self-transition that changes side, e.g. R Vom Tag -> L Vom Tag.</summary>
        public bool IsSideSwitch => Profile != null && Profile.IsSideSwitch;

        /// <summary>The extreme pose this attack reaches through. Formerly "pass-through".</summary>
        public GuardId TransitionPose => Profile != null ? Profile.TransitionPose : GuardId.None;

        /// <summary>The transition pose used in Krieg, when the profile selects a different one.</summary>
        public GuardId TransitionPoseInKrieg => Profile != null ? Profile.TransitionPoseInKrieg : GuardId.None;

        /// <summary>The transition pose for a given measure.</summary>
        public GuardId ResolveTransitionPose(Measure measure)
        {
            return Profile != null ? Profile.ResolveTransitionPose(measure) : GuardId.None;
        }

        public TransitionDirection Direction => Profile != null ? Profile.Direction : TransitionDirection.Neutral;

        public bool IsValid => Profile != null;

        public float LungeDistance => Profile != null ? Profile.LungeDistance : 0f;

        public bool IsInDamageWindow(float progress01)
        {
            return Profile != null && Profile.IsInDamageWindow(progress01);
        }

        public bool IsInParryWindow(float progress01)
        {
            return Profile != null && Profile.IsInParryWindow(progress01);
        }

        /// <summary>True once the transition has passed its commitment point and can no longer be cancelled.</summary>
        public bool IsCommitted(float progress01)
        {
            return Profile == null || progress01 >= Profile.Commitment;
        }

        public override string ToString()
        {
            return Profile == null ? "none" : Profile.DisplayName;
        }
    }
}

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

        public GuardId PassThrough => Profile != null ? Profile.PassThrough : GuardId.None;

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

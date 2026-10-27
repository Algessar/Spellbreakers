using System;
using System.Collections.Generic;
using Player;
using Spellbreakers.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives one character's sword fighting: it owns a <see cref="FencingAgent"/> - the shared, engine-free
/// combat state machine - and realises that state on the Animator.
///
/// The division matters and follows the design doc: the agent decides *when* the blade is live, when a
/// guard can parry, and how long a transition takes. This component only shows the result. The previous
/// implementation had it the other way round, reading the Animator's normalised time to decide when a
/// strike existed, which made combat timing depend on animation assets and impossible to test.
///
/// The same component works for the player and for enemies: only the source of intent differs.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatHandler : MonoBehaviour
{
    [Tooltip("Seconds used to fade the pass layer in and out.")]
    public float BlendDuration = 0.12f;

    [SerializeField] private GuardId _initialGuard = GuardId.VomTag;

    [Tooltip("Distance to the current opponent, supplied by targeting or perception. Used only by " +
             "transitions that opt into the measure gate.")]
    [SerializeField] private float _opponentDistance = float.PositiveInfinity;

    [Tooltip("Optional per-creature combat identity: how this body defends, how heavy it is, whether it " +
             "can evade. Left empty, the combatant can both parry and avoid, as before.")]
    [SerializeField] private CombatProfileAsset _combatProfile;

    [Tooltip("Whether this character already holds a guard on spawn. Enemies keep this; the player's " +
             "CombatEngagement turns it off until a hostile comes into range, so exploration uses the " +
             "ordinary locomotion states instead of a permanently raised sword.")]
    [SerializeField] private bool _startsInCombat = true;

    private readonly Dictionary<string, int> _passStateHashes = new Dictionary<string, int>();

    private Animator _animator;
    private int _guardsLayer = -1;
    private int _passesLayer = -1;
    private float _passWeight;
    private float _guardWeight = 1f;
    private float _guardWeightTarget = 1f;
    private GuardId _renderedGuard = GuardId.None;
    private bool _initialized;

    /// <summary>The shared combat state machine. AI, hit detection and debug tooling all read this.</summary>
    public FencingAgent Agent { get; private set; }

    public PassPhase Phase => Agent?.Phase ?? PassPhase.None;

    public GuardId CurrentGuard => Agent?.Current ?? GuardId.None;

    public bool IsTransitioning => Agent != null && Agent.IsTransitioning;

    /// <summary>
    /// Whether this character is fighting at all. Out of combat the guards and passes layers are silent
    /// and the base locomotion states own the whole body, which is what lets the guard buttons sit idle
    /// during exploration instead of being consumed by a stance the player never asked for.
    /// </summary>
    public bool CombatActive { get; private set; }

    /// <summary>Initiative. An enemy brain raises this when it owns the tempo.</summary>
    public bool Vor
    {
        get => Agent != null && Agent.Vor;
        set
        {
            if (Agent != null)
                Agent.Vor = value;
        }
    }

    public event Action<PassPhase> PhaseChanged;

    /// <summary>Raised when the character enters or leaves combat.</summary>
    public event Action<bool> CombatStateChanged;

    /// <summary>Raised when a blade contact involving this character resolved.</summary>
    public event Action<CombatContact> ContactResolved;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>(true);
        if (_animator == null)
        {
            Debug.LogError("CombatHandler requires an Animator on this object or a child.", this);
            enabled = false;
            return;
        }

        // Targeting is a player concern and is owned by InputHandler, which adds it only where input
        // exists. Creating it here as well used to put a player-only system onto enemies, and made the
        // "which object owns TargetingSystem" question ambiguous.
        Agent = new FencingAgent(CombatRules.CreateTransitionsForCombatant(), _initialGuard);
        Agent.PhaseChanged += OnPhaseChanged;

        // Defence mode belongs to the body, and contact resolution reads it at the moment of the hit -
        // long after any decision - so it has to live on the agent rather than be recomputed later.
        if (_combatProfile != null)
            _combatProfile.ApplyTo(this);

        _guardsLayer = _animator.GetLayerIndex("Guards");
        _passesLayer = _animator.GetLayerIndex("Passes");

        if (_guardsLayer < 0 || _passesLayer < 0)
        {
            Debug.LogError("CombatHandler needs a 'Guards' and a 'Passes' Animator layer.", this);
            enabled = false;
            return;
        }

        if (!HasGuardState(_initialGuard))
        {
            Debug.LogError($"CombatHandler needs a 'Guards.{GuardIds.ToStateName(_initialGuard)}' state.", this);
            enabled = false;
            return;
        }

        // Start settled in the initial guard rather than waiting for a frame: nothing assigns the
        // controller at runtime any more, so the old execution-order dance is gone.
        // Whether that guard is actually shown is a separate question, answered by CombatActive - an
        // enemy holds it from the first frame, the player's CombatEngagement lowers it until a hostile
        // comes close.
        CombatActive = _startsInCombat;
        _guardWeight = _guardWeightTarget = CombatActive ? 1f : 0f;
        _animator.SetLayerWeight(_guardsLayer, _guardWeight);
        _animator.SetLayerWeight(_passesLayer, 0f);
        _animator.Play(GuardStateHash(_initialGuard), _guardsLayer, 0f);
        _renderedGuard = _initialGuard;
        _initialized = true;
    }

    private void OnDestroy()
    {
        if (Agent != null)
            Agent.PhaseChanged -= OnPhaseChanged;
    }

    private void OnDisable()
    {
        if (!_initialized)
            return;

        Agent?.CancelTransition("Component disabled.");
        _passWeight = 0f;
        _animator.SetLayerWeight(_passesLayer, 0f);
        _animator.SetLayerWeight(_guardsLayer, _guardWeightTarget);

        if (Agent != null && HasGuardState(Agent.Current))
            _animator.Play(GuardStateHash(Agent.Current), _guardsLayer, 0f);

        _renderedGuard = Agent?.Current ?? GuardId.None;
    }

    private void Update()
    {
        if (!_initialized)
            return;

        Agent.Tick(Time.deltaTime);
        Realize();
        BlendGuardWeight();
    }

    /// <summary>
    /// Turns combat on or off. Entering is instant when asked to be, because the alternative is the
    /// player watching their guard fade in during the first frames of a fight; leaving is a blend, so
    /// the sword is lowered rather than snapped away.
    /// </summary>
    public void SetCombatActive(bool active, bool instant = false)
    {
        if (!_initialized)
        {
            // Called before Awake finished wiring the Animator. Record the intent and let Awake honour it.
            _startsInCombat = active;
            return;
        }

        if (CombatActive == active)
        {
            if (instant)
            {
                _guardWeight = _guardWeightTarget;
                _animator.SetLayerWeight(_guardsLayer, _guardWeight);
            }

            return;
        }

        CombatActive = active;
        _guardWeightTarget = active ? 1f : 0f;

        if (!active)
        {
            // Walking away ends the attack in progress. Letting a pass finish after combat has ended
            // would leave a live blade in a scene with no opponent.
            Agent?.CancelTransition("Combat ended.");
            _passWeight = 0f;
            _animator.SetLayerWeight(_passesLayer, 0f);
        }

        if (instant)
        {
            _guardWeight = _guardWeightTarget;
            _animator.SetLayerWeight(_guardsLayer, _guardWeight);
        }

        CombatStateChanged?.Invoke(active);
    }

    private void BlendGuardWeight()
    {
        if (Mathf.Approximately(_guardWeight, _guardWeightTarget))
            return;

        _guardWeight = BlendDuration <= 0f
            ? _guardWeightTarget
            : Mathf.MoveTowards(_guardWeight, _guardWeightTarget, Time.deltaTime / BlendDuration);

        _animator.SetLayerWeight(_guardsLayer, _guardWeight);
    }

    /// <summary>Supplies the distance used by measure-gated transitions. Infinity disables the gate.</summary>
    public void SetOpponentDistance(float distance)
    {
        _opponentDistance = distance;
    }

    /// <summary>
    /// The single intent entry point, shared by player input and enemy AI. Validation lives in the
    /// agent so both can never disagree about what is legal.
    /// </summary>
    public RequestResult RequestGuard(GuardId guard)
    {
        // No combat, no guard. This is what frees the guard inputs outside a fight.
        if (!_initialized || !CombatActive)
            return RequestResult.Rejected;

        return Agent.RequestGuard(guard, _opponentDistance);
    }

    /// <summary>
    /// Brings a dead character back, posed in its initial guard. Respawning owns the decision of when;
    /// this only tells the agent to stop being dead so the rules accept input again.
    /// </summary>
    public void Revive()
    {
        if (!_initialized || Agent == null)
            return;

        Agent.Revive(_initialGuard);

        // The guard pose has to be re-asserted: while dead the agent never changed Current, so the
        // rendered guard is already correct, but the layer weights may have been left mid-transition.
        if (CombatActive && HasGuardState(Agent.Current))
        {
            _animator.Play(GuardStateHash(Agent.Current), _guardsLayer, 0f);
            _renderedGuard = Agent.Current;
        }

        _guardWeight = _guardWeightTarget;
        _animator.SetLayerWeight(_guardsLayer, _guardWeight);
        _passWeight = 0f;
        _animator.SetLayerWeight(_passesLayer, 0f);
    }

    /// <summary>Puts this character into a reaction state, freezing its combat clock for a moment.</summary>
    public void ApplyReaction(ReactionState state, float seconds)
    {
        Agent?.SetReaction(state, seconds);
    }

    /// <summary>Called by weapon hit detection once a contact has been resolved.</summary>
    public void ReportContact(CombatContact contact)
    {
        ContactResolved?.Invoke(contact);
    }

    // --- Player input bindings -------------------------------------------------

    public void OnVomTag(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.VomTag); }
    public void OnPflug(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.Pflug); }
    public void OnAlber(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.Alber); }
    public void OnOchs(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.Ochs); }
    public void OnIronGate(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.IronGate); }

    // --- Realisation -----------------------------------------------------------

    /// <summary>
    /// Shows the agent's state. While a transition runs the pass layer is scrubbed to the agent's own
    /// progress, so the blade on screen is live exactly when the rules say it is.
    /// </summary>
    private void Realize()
    {
        // Out of combat the guards layer is faded out by BlendGuardWeight; re-driving guard states here
        // would keep a sheathed sword fighting an invisible opponent.
        if (!CombatActive)
        {
            BlendPassWeight(0f);
            return;
        }

        if (Agent.IsTransitioning)
        {
            RealizeTransition();
            return;
        }

        BlendPassWeight(0f);

        if (_renderedGuard != Agent.Current && HasGuardState(Agent.Current))
        {
            _animator.CrossFadeInFixedTime(GuardStateHash(Agent.Current), BlendDuration, _guardsLayer);
            _renderedGuard = Agent.Current;
        }
    }

    private void RealizeTransition()
    {
        var transition = Agent.ActiveTransition;
        var profile = transition.Profile;
        if (profile == null)
        {
            BlendPassWeight(0f);
            return;
        }

        // Remap progress to non-linear biomechanical easing curve
        var visualProgress = profile.EvaluateVisualProgress(Agent.Progress01);

        // Three-point interpolation across the transition:
        // First half (visualProgress < 0.5): blend from origin guard up to 100% pass apex.
        // Second half (visualProgress >= 0.5): start transitioning guards layer to destination guard,
        // and taper off the pass layer as we settle into the destination guard.
        float passWeightTarget;
        if (visualProgress < 0.5f)
        {
            if (_renderedGuard != transition.From && HasGuardState(transition.From))
            {
                _animator.CrossFadeInFixedTime(GuardStateHash(transition.From), BlendDuration, _guardsLayer);
                _renderedGuard = transition.From;
            }
            passWeightTarget = Mathf.Clamp01(visualProgress * 2f);
        }
        else
        {
            if (_renderedGuard != transition.To && HasGuardState(transition.To))
            {
                _animator.CrossFadeInFixedTime(GuardStateHash(transition.To), BlendDuration, _guardsLayer);
                _renderedGuard = transition.To;
            }
            passWeightTarget = Mathf.Clamp01((1f - visualProgress) * 2f);
        }

        var passHash = PassStateHash(profile.PassStateName);
        if (passHash != 0)
            _animator.Play(passHash, _passesLayer, visualProgress);

        BlendPassWeight(passWeightTarget);
    }

    private void BlendPassWeight(float target)
    {
        if (BlendDuration <= 0f)
        {
            _passWeight = target;
            _animator.SetLayerWeight(_passesLayer, target);
            return;
        }

        var step = Time.deltaTime / BlendDuration;
        _passWeight = Mathf.MoveTowards(_passWeight, target, step);
        _animator.SetLayerWeight(_passesLayer, _passWeight);
    }

    private void OnPhaseChanged(PassPhase phase)
    {
        PhaseChanged?.Invoke(phase);
    }

    private bool HasGuardState(GuardId guard)
    {
        var stateName = GuardIds.ToStateName(guard);
        return !string.IsNullOrEmpty(stateName) &&
               _animator.HasState(_guardsLayer, Animator.StringToHash(stateName));
    }

    private int GuardStateHash(GuardId guard)
    {
        return Animator.StringToHash(GuardIds.ToStateName(guard));
    }

    private int PassStateHash(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
            return 0;

        if (_passStateHashes.TryGetValue(stateName, out var hash))
            return hash;

        hash = Animator.StringToHash(stateName);
        _passStateHashes[stateName] = hash;
        return hash;
    }

    private void OnValidate()
    {
        if (BlendDuration < 0f)
            BlendDuration = 0f;
    }
}

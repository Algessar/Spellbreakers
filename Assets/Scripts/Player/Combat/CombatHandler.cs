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

    [Tooltip("Bone the sword mesh is skinned to, used to keep the grip alive through a pass. Leave it " +
             "empty to turn the grip correction off.")]
    [SerializeField] private string _weaponBoneName = "DEF-WEAPON_MAIN";

    private readonly Dictionary<string, int> _passStateHashes = new Dictionary<string, int>();

    private Animator _animator;
    private int _guardsLayer = -1;
    private int _passesLayer = -1;
    private float _passWeight;
    private float _guardWeight = 1f;
    private float _guardWeightTarget = 1f;
    private GuardKey _renderedGuard = GuardKey.None;
    private bool _initialized;

    private Transform _weaponBone;
    private Transform _gripHand;
    private bool _gripCaptured;
    private Vector3 _gripLocalPosition;
    private Quaternion _gripLocalRotation;

    /// <summary>The shared combat state machine. AI, hit detection and debug tooling all read this.</summary>
    public FencingAgent Agent { get; private set; }

    public PassPhase Phase => Agent?.Phase ?? PassPhase.None;

    public GuardId CurrentGuard => Agent?.Current ?? GuardId.None;

    /// <summary>The guard including which side it is held on. Rendering and the parry side rule both need it.</summary>
    public GuardKey CurrentKey => Agent?.CurrentKey ?? GuardKey.None;

    /// <summary>Which side the guard is held on. Right until the mirror input is used.</summary>
    public GuardSide CurrentSide => Agent?.CurrentSide ?? GuardSide.None;

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

        ResolveWeaponGrip();

        if (!HasGuardState(GuardKey.Right(_initialGuard)))
        {
            Debug.LogError(
                $"CombatHandler needs a 'Guards.{GuardIds.ToStateName(GuardKey.Right(_initialGuard))}' state.", this);
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
        _animator.Play(GuardStateHash(GuardKey.Right(_initialGuard)), _guardsLayer, 0f);
        _renderedGuard = GuardKey.Right(_initialGuard);
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

        if (Agent != null && HasGuardState(Agent.CurrentKey))
            _animator.Play(GuardStateHash(Agent.CurrentKey), _guardsLayer, 0f);

        _renderedGuard = Agent?.CurrentKey ?? GuardKey.None;
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
    /// Keeps the sword in the hand while a pass plays.
    ///
    /// The transition and correction exports this project animates passes from carry humanoid muscle
    /// curves only - no curves for the bone the sword mesh is skinned to. In Blender that bone follows the
    /// grip through a constraint, which FBX cannot carry, so here it holds whatever the guard clip left:
    /// the arm cuts and the blade stays behind up at the guard. That is what makes strikes look aimed off
    /// to the side and makes them sweep empty air, because hit detection sweeps the blade itself.
    ///
    /// The guard clips <em>do</em> carry the bone, so the grip is known exactly whenever a guard is
    /// showing. This records it in right-hand space and, for as long as the pass layer has weight,
    /// re-applies it to the hand instead of the chest. At zero pass weight it does nothing at all, so
    /// authored guard poses stay untouched, and it blends out on the layer's own weight so the
    /// destination guard takes the sword back without a pop.
    ///
    /// LateUpdate is the first point after the Animator has written the pose, and it runs before
    /// <see cref="WeaponDamage"/> samples the blade (that component is ordered later), so the hit test
    /// sees the gripped sword. Once the exports bake the weapon bone, this is redundant and can go.
    /// </summary>
    private void LateUpdate()
    {
        if (!_initialized || _weaponBone == null || _gripHand == null)
            return;

        if (_passWeight <= 0.0001f)
        {
            // No pass on screen: the guard clip owns the weapon bone and is authoritative.
            _gripCaptured = false;
            return;
        }

        var handRotation = _gripHand.rotation;
        var toHandSpace = Quaternion.Inverse(handRotation);
        var livePosition = toHandSpace * (_weaponBone.position - _gripHand.position);
        var liveRotation = toHandSpace * _weaponBone.rotation;

        // Captured on the first frame of the pass, while the pose still is the guard it started from,
        // which is the last moment the grip is known to be authored rather than inherited.
        if (!_gripCaptured)
        {
            _gripLocalPosition = livePosition;
            _gripLocalRotation = liveRotation;
            _gripCaptured = true;
        }

        var blend = Mathf.Clamp01(_passWeight);
        _weaponBone.rotation = handRotation * Quaternion.Slerp(liveRotation, _gripLocalRotation, blend);
        _weaponBone.position = _gripHand.position +
                               handRotation * Vector3.Lerp(livePosition, _gripLocalPosition, blend);
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
    ///
    /// Pressing a guard is also a way *into* a fight: with a hostile in range the press engages and then
    /// runs the transition, so a deliberate guard and the automatic proximity engagement no longer
    /// disagree about who starts the fight. Before this, a guard press outside engagement (18 m) was
    /// dropped without a word, and the same press started working once combat had engaged on its own -
    /// which reads as the character raising its guard on its own and ignoring the player.
    /// </summary>
    public RequestResult RequestGuard(GuardId guard)
    {
        if (!_initialized)
            return RequestResult.Rejected;

        if (!CombatActive && !TryEngageFromGuardPress())
            return RequestResult.Rejected;

        return Agent.RequestGuard(guard, _opponentDistance);
    }

    /// <summary>
    /// Called only from a guard press. Returns false when there is nobody close enough to fight, which
    /// leaves the guard inputs inert during exploration exactly as before.
    /// </summary>
    private bool TryEngageFromGuardPress()
    {
        var engagement = GetComponent<CombatEngagement>();
        if (engagement == null)
            engagement = GetComponentInParent<CombatEngagement>();

        if (engagement == null || !engagement.TryEngage())
            return false;

        return CombatActive;
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
        if (CombatActive && HasGuardState(Agent.CurrentKey))
        {
            _animator.Play(GuardStateHash(Agent.CurrentKey), _guardsLayer, 0f);
            _renderedGuard = Agent.CurrentKey;
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

    /// <summary>
    /// Flips the guard onto its other side: right-side Alber becomes left-side Alber. This is the whole of
    /// the mirror input, and it is delegated to the agent rather than built here, because the intent has to
    /// survive being pressed mid-transition - see <see cref="FencingAgent.RequestMirror"/>. Resolving
    /// "the guard I am in" in this method would resolve the guard being *left*, which is how the mirror
    /// button ended up producing the previous guard's opposite instead of the current one's.
    ///
    /// A side switch is a traversal unless the table expresses it as an attack (the doc's
    /// "R Vom Tag -> L Vom Tag = Zornhau"), so mirroring is always available - including from a guard that
    /// has no authored left-side attack out of it.
    /// </summary>
    public RequestResult RequestGuardMirrored()
    {
        if (!_initialized || Agent == null)
            return RequestResult.Rejected;

        if (!CombatActive && !TryEngageFromGuardPress())
            return RequestResult.Rejected;

        return Agent.RequestMirror(_opponentDistance);
    }

    public void OnVomTag(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.VomTag); }
    public void OnPflug(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.Pflug); }
    public void OnAlber(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.Alber); }
    public void OnOchs(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.Ochs); }
    public void OnIronGate(InputAction.CallbackContext context) { if (context.started) RequestGuard(GuardId.IronGate); }

    public void OnSwitchMirroredGuard(InputAction.CallbackContext context)
    {
        if (context.started)
            RequestGuardMirrored();
    }

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

        if (_renderedGuard != Agent.CurrentKey && HasGuardState(Agent.CurrentKey))
        {
            _animator.CrossFadeInFixedTime(GuardStateHash(Agent.CurrentKey), BlendDuration, _guardsLayer);
            _renderedGuard = Agent.CurrentKey;
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
            if (_renderedGuard != transition.FromKey && HasGuardState(transition.FromKey))
            {
                _animator.CrossFadeInFixedTime(GuardStateHash(transition.FromKey), BlendDuration, _guardsLayer);
                _renderedGuard = transition.FromKey;
            }
            passWeightTarget = Mathf.Clamp01(visualProgress * 2f);
        }
        else
        {
            if (_renderedGuard != transition.ToKey && HasGuardState(transition.ToKey))
            {
                _animator.CrossFadeInFixedTime(GuardStateHash(transition.ToKey), BlendDuration, _guardsLayer);
                _renderedGuard = transition.ToKey;
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

    /// <summary>
    /// Resolves the two transforms the grip correction needs. Silent when the rig does not offer them:
    /// the weapon bone is an extra, non-humanoid bone, so a differently authored model simply leaves the
    /// correction off instead of erroring.
    /// </summary>
    private void ResolveWeaponGrip()
    {
        _gripHand = _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        _weaponBone = string.IsNullOrEmpty(_weaponBoneName)
            ? null
            : FindDescendant(_animator.transform, _weaponBoneName);

        if (_gripHand != null && _weaponBone != null)
            return;

        _gripHand = null;
        _weaponBone = null;
    }

    private static Transform FindDescendant(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindDescendant(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private bool HasGuardState(GuardKey guard)
    {
        var stateName = GuardIds.ToStateName(guard);
        return !string.IsNullOrEmpty(stateName) &&
               _animator.HasState(_guardsLayer, Animator.StringToHash(stateName));
    }

    private int GuardStateHash(GuardKey guard)
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

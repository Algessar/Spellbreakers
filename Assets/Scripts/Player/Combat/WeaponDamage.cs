using System.Collections.Generic;
using Spellbreakers.Combat;
using UnityEngine;

/// <summary>
/// Turns the blade's motion into combat results.
///
/// The blade is tested as a swept volume between its previous and current position on every frame, so a
/// fast pass cannot tunnel through a target between physics steps - the previous contact-based version
/// could miss entirely, which is what the repeat-hit delay was papering over. Each target can only be
/// affected once per strike, which removes that delay hack as well.
///
/// The important behaviour is that contact is resolved through <see cref="ParryResolver"/> rather than
/// applied as damage. Two blades meeting is a real outcome with a real consequence, and the outcome is
/// decided by which guard or transition each fighter is in and by whether the defender timed it.
///
/// It is also the only place that knows an attack whiffed: the strike window opened and closed without
/// touching anything. Because that leaves no other trace, this component reports every result it produces
/// as a <see cref="CombatSoundCue"/> - swing, miss, hit, parry, deflect, bind, death - so the fight can be
/// followed by ear. Audio is not required: with nothing listening, each request is dropped.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponDamage : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _damage = 10f;

    [Tooltip("Blade speed treated as a full-force strike by the deflection model. Faster hits are harder " +
             "to turn aside; slower ones easier.")]
    [SerializeField, Min(0.1f)] private float _referenceBladeSpeed = 8f;

    [Tooltip("Seconds the attacker is left staggered after its attack is parried or deflected.")]
    [SerializeField, Min(0f)] private float _parriedStaggerSeconds = 0.35f;

    [Tooltip("Seconds both fighters are stalled for when two blades bind.")]
    [SerializeField, Min(0f)] private float _bindSeconds = 0.2f;

    [Tooltip("Layers the swept test looks at. Defaults to everything.")]
    [SerializeField] private LayerMask _contactLayers = ~0;

    [Tooltip("Left empty to find the CombatHandler on this character at runtime.")]
    [SerializeField] private CombatHandler _owner;

    [Tooltip("Left empty to find a Blade on this object or a parent.")]
    [SerializeField] private Blade _blade;

    /// <summary>Temporary: logs every resolved contact and why it resolved that way.</summary>
    [SerializeField] private bool _logContacts;

    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Collider[] _overlaps = new Collider[16];
    private readonly HashSet<Damageable> _affectedThisStrike = new HashSet<Damageable>();
    private readonly Dictionary<Damageable, CombatHandler> _handlerCache = new Dictionary<Damageable, CombatHandler>();

    private Vector3 _previousBase;
    private Vector3 _previousTip;
    private bool _hasPreviousSample;
    private bool _armed;

    /// <summary>
    /// Whether this strike window connected with anything. Reset when the window opens, and read when it
    /// closes: a live blade that touched nothing is a miss, which is the one combat result that leaves no
    /// other trace. Without it "I swung and nothing happened" is indistinguishable from "I never swung".
    /// </summary>
    private bool _struckThisStrike;

    /// <summary>Blade direction and speed at the last sweep, read by the deflection model.</summary>
    private Vector3 _lastTravelDirection;
    private float _lastBladeSpeed;

    /// <summary>Last deflection result, kept for diagnostics and the debug view.</summary>
    private DeflectionResult _lastDeflection;
    private Damageable _selfTarget;
    private CombatTeam _selfTeam = CombatTeam.Neutral;

    /// <summary>
    /// Whether the owner declared a side by carrying a <see cref="Damageable"/>. A character without one
    /// must not be silently made unable to hit anything: with no declared team every hostile check would
    /// fail closed, so the check is skipped instead and the weapon behaves as it did before teams existed.
    /// </summary>
    private bool _hasTeam;

    private void Awake()
    {
        if (_owner == null)
            _owner = transform.root.GetComponentInChildren<CombatHandler>(true);

        if (_blade == null)
            _blade = GetComponentInParent<Blade>() ?? transform.root.GetComponentInChildren<Blade>(true);

        if (_blade != null && !_blade.IsValid)
            _blade.ResolveReferences();

        if (_owner != null)
        {
            _selfTarget = _owner.GetComponentInChildren<Damageable>(true) ??
                          _owner.transform.root.GetComponentInChildren<Damageable>(true);

            if (_selfTarget != null)
            {
                _selfTeam = _selfTarget.Team;
                _hasTeam = true;
            }
        }

        if (_owner != null && !_hasTeam)
        {
            Debug.LogWarning($"{name}: '{transform.root.name}' has a CombatHandler but no Damageable, so it " +
                             "has no declared side. This blade will damage anything that is not its own " +
                             "character, and nothing will be able to target that character in return.", this);
        }

        if (_owner == null)
        {
            Debug.LogWarning($"{name}: no CombatHandler found, so this blade can never be live. " +
                             $"Root is '{transform.root.name}'.", this);
        }

        if (_blade == null || !_blade.IsValid)
        {
            Debug.LogWarning($"{name}: no usable Blade found, so the swept hit test cannot run.", this);
        }
    }

    private void OnEnable()
    {
        if (_owner != null)
            _owner.PhaseChanged += OnPhaseChanged;
    }

    private void OnDisable()
    {
        if (_owner != null)
            _owner.PhaseChanged -= OnPhaseChanged;

        _armed = false;
        _struckThisStrike = false;
        _hasPreviousSample = false;
    }

    private void OnPhaseChanged(PassPhase phase)
    {
        var wasArmed = _armed;
        _armed = phase == PassPhase.Strike;

        if (!_armed)
        {
            // The window closed and the swing is following through untouched: a whiff. Only the Settle phase
            // counts, because the other ways out of a strike window - cancelled input, a feint, combat
            // ending, the component being disabled - are not attacks that missed, and sounding like one
            // would teach the ear the wrong lesson.
            if (wasArmed && phase == PassPhase.Settle && !_struckThisStrike)
            {
                CombatSoundEvents.Request(new CombatSoundEvent(
                    CombatSoundCue.Miss,
                    BladePosition(),
                    OwnerName,
                    null,
                    AttackerProgress));
            }

            _struckThisStrike = false;
            _hasPreviousSample = false;
            return;
        }

        _affectedThisStrike.Clear();
        _struckThisStrike = false;
        Sample();
        _hasPreviousSample = true;

        CombatSoundEvents.Request(new CombatSoundEvent(
            CombatSoundCue.Swing,
            BladePosition(),
            OwnerName,
            null,
            AttackerProgress));
    }

    /// <summary>
    /// Runs after the Animator has posed the blade, so the sampled positions are the ones that were
    /// actually rendered - not the previous frame's.
    /// </summary>
    private void LateUpdate()
    {
        if (!_armed || _blade == null || !_blade.IsValid)
            return;

        var currentBase = _blade.BasePosition;
        var currentTip = _blade.TipPosition;

        if (!_hasPreviousSample)
        {
            _previousBase = currentBase;
            _previousTip = currentTip;
            _hasPreviousSample = true;
            return;
        }

        SweepBetween(_previousBase, _previousTip, currentBase, currentTip);

        _previousBase = currentBase;
        _previousTip = currentTip;
    }

    private void Sample()
    {
        _previousBase = _blade.BasePosition;
        _previousTip = _blade.TipPosition;
    }

    private void SweepBetween(Vector3 previousBase, Vector3 previousTip, Vector3 currentBase, Vector3 currentTip)
    {
        var radius = _blade.Radius;
        var travel = ((currentBase + currentTip) - (previousBase + previousTip)) * 0.5f;
        var distance = travel.magnitude;

        // The deflection model needs the blade's direction and speed at the instant of contact, so the
        // sweep records them for ResolveContact rather than each contact re-deriving them.
        if (distance > 0.0001f)
        {
            _lastTravelDirection = travel / distance;
            _lastBladeSpeed = distance / Mathf.Max(0.0001f, Time.deltaTime);
        }
        else
        {
            _lastBladeSpeed = 0f;
        }

        if (distance > 0.0001f)
        {
            var count = Physics.CapsuleCastNonAlloc(
                previousBase,
                previousTip,
                radius,
                travel / distance,
                _hits,
                distance,
                _contactLayers,
                QueryTriggerInteraction.Collide);

            for (var i = 0; i < count; i++)
                ResolveContact(_hits[i].collider);
        }

        var overlapCount = Physics.OverlapCapsuleNonAlloc(
            currentBase,
            currentTip,
            radius,
            _overlaps,
            _contactLayers,
            QueryTriggerInteraction.Collide);

        for (var i = 0; i < overlapCount; i++)
            ResolveContact(_overlaps[i]);
    }

    private void ResolveContact(Collider other)
    {
        if (other == null)
            return;

        var target = other.GetComponentInParent<Damageable>();
        if (target == null || target == _selfTarget || !target.IsAlive)
            return;

        if (_hasTeam && !CombatTeams.AreHostile(_selfTeam, target.Team))
            return;

        if (!_affectedThisStrike.Add(target))
            return;

        var hitBlade = other.GetComponentInParent<Blade>();
        var defenderHandler = ResolveHandler(target);

        // How square the strike arrived: 1 means the blade travelled straight at the defender, 0 means it
        // was already moving across or away from them. This is the "contact angle" of the deflection model.
        var toDefender = target.AimPosition - _blade.TipPosition;
        var contactAngle = toDefender.sqrMagnitude > 0.0001f && _lastTravelDirection.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(_lastTravelDirection, toDefender.normalized))
            : 0f;

        var outcome = ResolveOutcome(hitBlade, defenderHandler, contactAngle);

        Apply(outcome, target, defenderHandler);
    }

    /// <summary>
    /// Turns a contact into an outcome.
    ///
    /// The parry table is consulted for its *disposition* - this guard answers this line well or badly -
    /// but it no longer decides the result. The design doc asks for deflection to be a gradient driven
    /// by the physical relationship between the attack and the defender, so timing quality, contact
    /// angle and the attacking blade's speed decide where on that gradient the contact actually lands,
    /// and a creature that cannot parry never reaches the gradient at all.
    /// </summary>
    private CombatOutcome ResolveOutcome(Blade hitBlade, CombatHandler defenderHandler, float contactAngle)
    {
        // A blade with no live character behind it is just an obstacle: it cannot parry.
        if (hitBlade == null || defenderHandler == null || _owner?.Agent == null)
            return CombatOutcome.HitBody;

        var attacker = _owner.Agent;
        if (!attacker.ActiveTransition.IsValid)
            return CombatOutcome.HitBody;

        var defender = defenderHandler.Agent;

        var baseline = ParryResolver.Resolve(
            attacker.ActiveTransition,
            defender.CurrentKey,
            defender.ActiveTransition,
            defender.IsInParryWindow,
            CombatRules.Parry,
            attacker.Progress01);

        if (baseline.Outcome == CombatOutcome.Undefined)
        {
            // A hole in the parry table. Fail loudly but do not silently invent a rule.
            Debug.LogWarning($"[Weapon] Parry table has no entry for {attacker.ActiveTransition.Direction} " +
                             $"against {defender.Current}; treating it as an unfavourable prior.", this);
        }

        var input = new DeflectionInput(
            TimingQuality(defender),
            contactAngle,
            _lastBladeSpeed,
            _referenceBladeSpeed,
            baseline.Outcome);

        var result = DeflectionResolver.Resolve(input, defender.DefenceMode);
        _lastDeflection = result;

        if (_logContacts)
        {
            Debug.Log($"[Weapon] {name} vs {defenderHandler.CurrentGuard} ({defender.DefenceMode}): " +
                      $"timing {input.TimingQuality:0.00} angle {contactAngle:0.00} " +
                      $"speed {_lastBladeSpeed:0.0}/{_referenceBladeSpeed:0.0} -> {result}", this);
        }

        return result.Outcome;
    }

    /// <summary>
    /// How well the defender timed its answer. Mid-window is best; a defence that barely catches the
    /// edge of the window reads as worse. Holding a guard decays as the arrival grace runs out, which is
    /// what stops "stand still and be safe" from working.
    /// </summary>
    private static float TimingQuality(FencingAgent defender)
    {
        if (defender.IsTransitioning && defender.ActiveTransition.Profile != null)
        {
            var profile = defender.ActiveTransition.Profile;
            return DeflectionResolver.TimingQualityFromWindow(
                defender.Progress01,
                profile.ParryWindowStart,
                profile.ParryWindowEnd);
        }

        var grace = defender.Table.GuardOr(defender.Current).ParryGraceSeconds;
        return grace <= 0.0001f ? 0f : FencingMath.Clamp01(defender.ParryWindowRemaining / grace);
    }

    private void Apply(CombatOutcome outcome, Damageable target, CombatHandler defenderHandler)
    {
        var attacker = _owner != null ? _owner.Agent : null;
        var attackerGuard = attacker != null ? attacker.Current : GuardId.None;
        var attackerTransition = attacker != null ? attacker.ActiveTransition : default;
        var progress = attacker != null ? attacker.Progress01 : 0f;

        var contact = new CombatContact(
            outcome,
            attackerGuard,
            attackerTransition,
            defenderHandler != null ? defenderHandler.CurrentGuard : GuardId.None,
            defenderHandler != null && defenderHandler.IsTransitioning,
            progress);

        _struckThisStrike = true;

        if (_logContacts)
            Debug.Log($"[Weapon] {name} -> {contact.Describe()}", this);

        // One request per resolved contact, not per combatant: the contact is a single event in the world
        // even though both fighters hear about it.
        CombatSoundEvents.Request(new CombatSoundEvent(
            SoundCueFor(outcome),
            ContactPosition(target),
            OwnerName,
            target != null ? target.name : null,
            progress));

        // Both sides hear about the contact, so feedback and AI reactions can respond to either role.
        _owner?.ReportContact(contact);
        defenderHandler?.ReportContact(contact);

        switch (outcome)
        {
            case CombatOutcome.HitBody:
            case CombatOutcome.Undefined:
                target.ApplyDamage(new HitInfo(_damage, attackerGuard, attackerTransition, CombatOutcome.HitBody, progress));
                defenderHandler?.ApplyReaction(ReactionState.Hit, 0.25f);

                // Death is a consequence of the hit, so it is announced after the damage has landed and the
                // health is actually zero - not predicted from the outcome.
                if (target.IsDead)
                {
                    CombatSoundEvents.Request(new CombatSoundEvent(
                        CombatSoundCue.Death,
                        ContactPosition(target),
                        OwnerName,
                        target.name,
                        progress));
                }

                break;

            case CombatOutcome.Parry:
                // The parry is the reward: the attack is stopped and the attacker is left recovering,
                // which is the opening the design doc asks a good defence to create.
                _owner?.ApplyReaction(ReactionState.Staggered, _parriedStaggerSeconds);
                defenderHandler?.Agent.RefreshParryWindow(Measure.Zufechten);
                break;

            case CombatOutcome.Deflect:
                _owner?.ApplyReaction(ReactionState.Staggered, _parriedStaggerSeconds * 1.5f);
                defenderHandler?.Agent.RefreshParryWindow(Measure.Zufechten);
                break;

            case CombatOutcome.Bind:
                // Neither blade gives ground: both fighters stall on the bind.
                _owner?.ApplyReaction(ReactionState.Staggered, _bindSeconds);
                defenderHandler?.ApplyReaction(ReactionState.Staggered, _bindSeconds);
                break;
        }
    }

    /// <summary>
    /// The cue that answers each outcome. Kept as a mapping rather than a switch at the call site so a new
    /// <see cref="CombatOutcome"/> has exactly one place to be given a sound.
    /// </summary>
    private static CombatSoundCue SoundCueFor(CombatOutcome outcome)
    {
        switch (outcome)
        {
            case CombatOutcome.Parry: return CombatSoundCue.Parry;
            case CombatOutcome.Deflect: return CombatSoundCue.Deflect;
            case CombatOutcome.Bind: return CombatSoundCue.Bind;
            default: return CombatSoundCue.HitBody;
        }
    }

    /// <summary>Halfway between the blade that arrived and the body it reached, so 3D falloff reads as
    /// distance from the clash rather than from whoever happened to report it.</summary>
    private Vector3 ContactPosition(Damageable target)
    {
        var blade = BladePosition();
        if (target == null)
            return blade;

        return Vector3.Lerp(blade, target.AimPosition, 0.5f);
    }

    private Vector3 BladePosition()
    {
        if (_blade != null && _blade.IsValid)
            return _blade.TipPosition;

        return transform.position;
    }

    /// <summary>The character's name rather than the weapon object's, because the sound log is read next to
    /// the combat log, which talks about characters.</summary>
    private string OwnerName =>
        _owner != null ? _owner.name : (transform.root != null ? transform.root.name : name);

    private float AttackerProgress => _owner != null && _owner.Agent != null ? _owner.Agent.Progress01 : 0f;

    private CombatHandler ResolveHandler(Damageable target)
    {
        if (target == null)
            return null;

        if (_handlerCache.TryGetValue(target, out var cached) && cached != null)
            return cached;

        var handler = CombatantRegistry.FindOwnerComponent<CombatHandler>(target);
        _handlerCache[target] = handler;
        return handler;
    }
}

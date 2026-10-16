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
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponDamage : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _damage = 10f;

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
        _hasPreviousSample = false;
    }

    private void OnPhaseChanged(PassPhase phase)
    {
        _armed = phase == PassPhase.Strike;

        if (!_armed)
        {
            _hasPreviousSample = false;
            return;
        }

        _affectedThisStrike.Clear();
        Sample();
        _hasPreviousSample = true;
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
        var outcome = ResolveOutcome(hitBlade, defenderHandler);

        Apply(outcome, target, defenderHandler);
    }

    private CombatOutcome ResolveOutcome(Blade hitBlade, CombatHandler defenderHandler)
    {
        // A blade with no live character behind it is just an obstacle: it cannot parry.
        if (hitBlade == null || defenderHandler == null || _owner?.Agent == null)
            return CombatOutcome.HitBody;

        var attacker = _owner.Agent;
        if (!attacker.ActiveTransition.IsValid)
            return CombatOutcome.HitBody;

        var defender = defenderHandler.Agent;
        var contact = ParryResolver.Resolve(
            attacker.ActiveTransition,
            defender.Current,
            defender.ActiveTransition,
            defender.IsInParryWindow,
            CombatRules.Parry,
            attacker.Progress01);

        if (contact.Outcome == CombatOutcome.Undefined)
        {
            // A hole in the parry table. Fail loudly but do not silently invent a rule.
            Debug.LogWarning($"[Weapon] Parry table has no entry for {attacker.ActiveTransition.Direction} " +
                             $"against {defender.Current}; treating it as a hit.", this);
        }

        return contact.Outcome;
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

        if (_logContacts)
            Debug.Log($"[Weapon] {name} -> {contact.Describe()}", this);

        // Both sides hear about the contact, so feedback and AI reactions can respond to either role.
        _owner?.ReportContact(contact);
        defenderHandler?.ReportContact(contact);

        switch (outcome)
        {
            case CombatOutcome.HitBody:
            case CombatOutcome.Undefined:
                target.ApplyDamage(new HitInfo(_damage, attackerGuard, attackerTransition, CombatOutcome.HitBody, progress));
                defenderHandler?.ApplyReaction(ReactionState.Hit, 0.25f);
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

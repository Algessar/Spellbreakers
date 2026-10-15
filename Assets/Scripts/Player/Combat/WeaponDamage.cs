using System.Collections.Generic;
using Player;
using UnityEngine;

/// <summary>Turns contacts from a weapon collider into damage on Damageable targets.</summary>
[DisallowMultipleComponent]
public sealed class WeaponDamage : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _damage = 10f;
    [Tooltip("Minimum seconds between two damaging hits on the same target.")]
    [SerializeField, Min(0.01f)] private float _repeatHitDelay = 0.35f;

    [Tooltip("Layers searched by the strike-window blade sweep. Defaults to the Enemy layer (8).")]
    [SerializeField] private LayerMask _targetLayers;

    /// <summary>Seconds between sweeps that drop entries for targets that no longer exist.</summary>
    private const float PruneIntervalSeconds = 10f;

    /// <summary>Enemy layer index, used when <see cref="_targetLayers"/> is left unset.</summary>
    private const int DefaultTargetLayer = 8;

    private readonly Dictionary<Damageable, float> _lastHitTimes = new Dictionary<Damageable, float>();
    private readonly List<Damageable> _pruneBuffer = new List<Damageable>();
    private readonly Collider[] _overlapBuffer = new Collider[16];

    [Tooltip("Leave empty to find the CombatHandler on a parent at runtime.")]
    [SerializeField] private CombatHandler _combatHandler;
    private Collider _bladeCollider;
    private Transform _ownerRoot;
    private int _resolvedTargetMask;
    private float _nextPruneTime;

    private void Awake()
    {
        // Resolve the owning combat handler. GetComponentInParent does not reliably cross a model
        // sub-hierarchy boundary - the blade lives under the FBX bones while the handler sits on the
        // avatar root - so search the root's subtree first, which is the path that actually works.
        if (_combatHandler == null)
            _combatHandler = transform.root.GetComponentInChildren<CombatHandler>(true);

        if (_combatHandler == null)
            _combatHandler = GetComponentInParent<CombatHandler>();

        if (_combatHandler == null)
        {
            var handlers = FindObjectsByType<CombatHandler>(FindObjectsInactive.Exclude);
            if (handlers.Length > 0)
                _combatHandler = handlers[0];
        }

        _ownerRoot = _combatHandler != null ? _combatHandler.transform : transform.root;
        _bladeCollider = GetComponent<Collider>();
        _resolvedTargetMask = _targetLayers.value != 0 ? _targetLayers.value : 1 << DefaultTargetLayer;

        if (_logHits && _combatHandler != null)
            Debug.Log($"[Weapon] {name} gated by CombatHandler on '{_combatHandler.name}'", this);

        DamagePopup.VerboseLogging = _logHits;

        if (_combatHandler == null)
            Debug.LogWarning($"{name}: no CombatHandler found, so this weapon damages on any contact. " +
                             $"Root is '{transform.root.name}'.", this);

        if (_bladeCollider == null)
            Debug.LogWarning($"{name}: WeaponDamage needs a Collider on the same GameObject (the Event and Stay " +
                             "callbacks only fire there); the strike-window sweep will still run.", this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryDamage(collision.collider, "collision-enter");
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDamage(other, "trigger-enter");
    }

    // Stay matters as much as Enter: a blade resting against a target, or a slow pass, would otherwise
    // only ever get the single Enter callback and feel unresponsive.
    private void OnTriggerStay(Collider other)
    {
        TryDamage(other, "trigger-stay");
    }

    private void OnCollisionStay(Collision collision)
    {
        TryDamage(collision.collider, "collision-stay");
    }

    /// <summary>
    /// Physics events only fire on the exact frame contact begins, so a fast swing or a blade that is
    /// already touching the target when the strike opens can be missed entirely. While the strike phase
    /// is open this sweeps the blade's actual volume, which is what makes hits feel responsive.
    /// </summary>
    private void Update()
    {
        if (!IsStrikeActive || _bladeCollider == null)
            return;

        var bladeTransform = _bladeCollider.transform;
        var count = Physics.OverlapBoxNonAlloc(
            bladeTransform.TransformPoint(GetBladeLocalCenter()),
            GetBladeHalfExtents(),
            _overlapBuffer,
            bladeTransform.rotation,
            _resolvedTargetMask,
            QueryTriggerInteraction.Collide);

        for (var i = 0; i < count; i++)
            TryDamage(_overlapBuffer[i], "sweep");
    }

    /// <summary>Temporary: logs which detection path produced each hit, and why others were ignored.</summary>
    [SerializeField] private bool _logHits;

    /// <summary>Also damage during the settle (follow-through) phase, after the pass animation completes.</summary>
    [Tooltip("Count the settle/follow-through phase as part of the strike window.")]
    [SerializeField] private bool _damageDuringSettle = true;

    /// <summary>
    /// A weapon belonging to a CombatHandler only damages during the strike phase, so brushing the blade
    /// against a target while idle, walking, or settling no longer registers a hit. A weapon with no
    /// CombatHandler keeps its unconditional behaviour.
    /// </summary>
    private bool IsStrikeActive
    {
        get
        {
            if (_combatHandler == null)
                return true;

            var phase = _combatHandler.Phase;
            return phase == PassPhase.Strike || (_damageDuringSettle && phase == PassPhase.Settle);
        }
    }

    private void TryDamage(Collider other, string source)
    {
        if (other == null)
            return;

        if (!IsStrikeActive)
        {
            // The blade is touching something, but the combat phase is not open.
            if (_logHits && other.GetComponentInParent<Damageable>() != null)
                Debug.Log($"[Weapon] contact via {source} ignored - phase is " +
                          $"{(_combatHandler == null ? "None (no CombatHandler)" : _combatHandler.Phase.ToString())}", this);
            return;
        }

        var target = other.GetComponentInParent<Damageable>();
        if (target == null || target.IsDead)
            return;

        // Never let a character damage itself through its own weapon.
        var targetTransform = target.transform;
        if (targetTransform == _ownerRoot || targetTransform.IsChildOf(_ownerRoot))
            return;

        if (Time.time >= _nextPruneTime)
            PruneStaleEntries();

        if (_lastHitTimes.TryGetValue(target, out var lastHitTime) &&
            Time.time - lastHitTime < _repeatHitDelay)
        {
            if (_logHits)
                Debug.Log($"[Weapon] contact via {source} on {target.name} suppressed by repeat delay " +
                          $"({Time.time - lastHitTime:F3}s < {_repeatHitDelay:F2}s)", this);
            return;
        }

        if (_logHits)
            Debug.Log($"[Weapon] HIT {target.name} via {source} for {_damage:F0}", this);

        _lastHitTimes[target] = Time.time;
        target.ApplyDamage(_damage);
    }

    /// <summary>Blade centre in the collider's local space (OverlapBox takes local units).</summary>
    private Vector3 GetBladeLocalCenter()
    {
        return _bladeCollider is BoxCollider box ? box.center : Vector3.zero;
    }

    /// <summary>Blade half-size in the collider's local space.</summary>
    private Vector3 GetBladeHalfExtents()
    {
        if (_bladeCollider is BoxCollider box)
            return box.size * 0.5f;

        // Non-box colliders fall back to their world bounds, converted into the collider's local frame.
        var extents = _bladeCollider.bounds.extents;
        var scale = _bladeCollider.transform.lossyScale;
        return new Vector3(
            scale.x != 0f ? extents.x / Mathf.Abs(scale.x) : extents.x,
            scale.y != 0f ? extents.y / Mathf.Abs(scale.y) : extents.y,
            scale.z != 0f ? extents.z / Mathf.Abs(scale.z) : extents.z);
    }

    /// <summary>Drops entries for destroyed targets, so long sessions do not accumulate stale references.</summary>
    private void PruneStaleEntries()
    {
        _nextPruneTime = Time.time + PruneIntervalSeconds;

        _pruneBuffer.Clear();
        foreach (var entry in _lastHitTimes)
        {
            if (entry.Key == null && Time.time - entry.Value >= _repeatHitDelay)
                _pruneBuffer.Add(entry.Key);
        }

        foreach (var target in _pruneBuffer)
            _lastHitTimes.Remove(target);
    }
}

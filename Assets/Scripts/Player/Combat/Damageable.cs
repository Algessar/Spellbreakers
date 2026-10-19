using System;
using System.Collections.Generic;
using Player;
using Spellbreakers.Combat;
using UnityEngine;

/// <summary>
/// Receives damage and exposes everything the combat systems need to know about a participant: which
/// side it is on, whether it is still alive, and where the blade should aim when fighting it.
///
/// A registry replaces the previous per-frame <c>FindObjectsByType</c> scene searches, so target
/// selection costs nothing. The component also implements <see cref="ICombatTarget"/> so damage
/// resolution can be exercised from a unit test without a scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class Damageable : MonoBehaviour, ICombatTarget
{
    /// <summary>Every enabled damageable. Neutral props are included; they are just nobody's enemy.</summary>
    public static readonly HashSet<Damageable> All = new HashSet<Damageable>();

    [SerializeField, Min(1f)] private float _maxHealth = 100f;

    [Tooltip("Which side this participant fights for. Neutral is nobody's enemy.")]
    [SerializeField] private CombatTeam _team = CombatTeam.Neutral;

    [Tooltip("Optional explicit aim point. Falls back to chest height above the transform.")]
    [SerializeField] private Transform _aimPoint;

    /// <summary>Temporary: logs each applied hit, including the source guard and outcome.</summary>
    [SerializeField] private bool _logDamage;

    public float MaxHealth => _maxHealth;

    public float CurrentHealth { get; private set; }

    public bool IsDead => CurrentHealth <= 0f;

    /// <summary>Alive and participating. Disabled participants are ignored by targeting and AI.</summary>
    public bool IsAlive => !IsDead && isActiveAndEnabled;

    public CombatTeam Team => _team;

    /// <summary>Centre of mass to aim at, used by IK, targeting and the AI's distance maths.</summary>
    public Vector3 AimPosition =>
        _aimPoint != null ? _aimPoint.position : transform.position + Vector3.up * 1.2f;

    public Transform AimTransform => _aimPoint;

    public event Action<Damageable, float, float> Damaged;
    public event Action<Damageable> Died;

    /// <summary>Raised with the full hit description, for reactions and feedback effects.</summary>
    public event Action<Damageable, HitInfo> Hit;

    // --- ICombatTarget is implemented implicitly by the public members below ---

    private void Awake()
    {
        CurrentHealth = _maxHealth;
    }

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>Sets the side. Used by the editor setup tooling so prefab wiring stays a one-liner.</summary>
    public void SetTeam(CombatTeam team)
    {
        _team = team;
    }

    public void SetAimPoint(Transform aimPoint)
    {
        _aimPoint = aimPoint;
    }

    /// <summary>Applies positive damage and returns the amount actually removed from health.</summary>
    public float ApplyDamage(float amount)
    {
        return ApplyDamageCore(new HitInfo(amount, GuardId.None, default, CombatOutcome.HitBody, 0f));
    }

    /// <summary>
    /// The full path. Carrying the source guard and the resolved outcome is what lets a target react to
    /// *how* it was hit rather than only to how much.
    /// </summary>
    public float ApplyDamage(in HitInfo hit)
    {
        return ApplyDamageCore(hit);
    }

    private float ApplyDamageCore(in HitInfo hit)
    {
        if (hit.Amount <= 0f || IsDead || !isActiveAndEnabled)
            return 0f;

        var previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - hit.Amount);
        var appliedDamage = previousHealth - CurrentHealth;

        DamagePopup.Spawn(transform, appliedDamage);

        Damaged?.Invoke(this, appliedDamage, CurrentHealth);
        Hit?.Invoke(this, hit);

        if (CurrentHealth <= 0f)
            Died?.Invoke(this);

        if (_logDamage)
        {
            Debug.Log($"[Damage] {name} -{appliedDamage:F0} -> {CurrentHealth:F0}/{_maxHealth:F0}" +
                      $" via {GuardIds.ToDisplayName(hit.SourceGuard)} ({hit.Outcome})" +
                      $"{(IsDead ? " DEAD" : string.Empty)}", this);
        }

        return appliedDamage;
    }

    /// <summary>Restores this target to full health, useful for a reusable training dummy.</summary>
    public void ResetHealth()
    {
        CurrentHealth = _maxHealth;
    }

    private void OnValidate()
    {
        if (_maxHealth < 1f)
            _maxHealth = 1f;
    }
}

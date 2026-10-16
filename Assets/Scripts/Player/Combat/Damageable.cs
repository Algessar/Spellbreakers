using System;
using Player;
using UnityEngine;

/// <summary>Receives damage from weapon hitboxes and exposes health for UI or combat systems.</summary>
public sealed class Damageable : MonoBehaviour
{
    [SerializeField, Min(1f)] private float _maxHealth = 100f;

    public float MaxHealth => _maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;

    public event Action<Damageable, float, float> Damaged;
    public event Action<Damageable> Died;

    /// <summary>Temporary: logs each applied hit, including where the popup is spawned.</summary>
    [SerializeField] private bool _logDamage;

    private void Awake()
    {
        CurrentHealth = _maxHealth;
    }

    /// <summary>Applies positive damage and returns the amount actually removed from health.</summary>
    public float ApplyDamage(float amount)
    {
        if (amount <= 0f || IsDead)
            return 0f;

        var previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        var appliedDamage = previousHealth - CurrentHealth;

        DamagePopup.Spawn(transform, appliedDamage);
        Damaged?.Invoke(this, appliedDamage, CurrentHealth);

        if (CurrentHealth <= 0f)
            Died?.Invoke(this);

        if (_logDamage)
        {
            Debug.Log($"[Damage] {name} -{appliedDamage:F0} -> {CurrentHealth:F0}/{_maxHealth:F0}" +
                      $"{(IsDead ? " DEAD" : string.Empty)} at {transform.position}", this);
        }

        return appliedDamage;
    }

    /// <summary>Restores this target to full health, useful for a reusable training dummy.</summary>
    public void ResetHealth()
    {
        CurrentHealth = _maxHealth;
    }
}

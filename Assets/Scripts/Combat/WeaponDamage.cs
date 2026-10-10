using System.Collections.Generic;
using UnityEngine;

/// <summary>Turns contacts from a weapon collider into damage on Damageable targets.</summary>
[DisallowMultipleComponent]
public sealed class WeaponDamage : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _damage = 10f;
    [SerializeField, Min(0f)] private float _repeatHitDelay = 0.35f;

    private readonly Dictionary<Damageable, float> _lastHitTimes = new Dictionary<Damageable, float>();

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Weapon OnCollisionEnter");

        TryDamage(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Weapon OnTriggerEnter");
        TryDamage(other);
    }

    private void TryDamage(Collider other)
    {
        Debug.Log("Player TryDamage");

        
        var target = other.GetComponentInParent<Damageable>();
        if (target == null || target.IsDead)
            return;

        if (_lastHitTimes.TryGetValue(target, out var lastHitTime) &&
            Time.time - lastHitTime < _repeatHitDelay)
        {
            return;
        }

        _lastHitTimes[target] = Time.time;
        target.ApplyDamage(_damage);
    }
}

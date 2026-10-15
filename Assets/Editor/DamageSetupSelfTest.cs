using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMPORARY DIAGNOSTIC. Runs once on entering play mode and reports whether the weapon damage
/// wiring is actually valid at runtime - the part static inspection cannot prove.
/// Delete this file (and its .meta) once the damage pipeline is confirmed working.
/// </summary>
public static class DamageSetupSelfTest
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void VerifyWeaponSetup()
    {
        var weapons = Resources.FindObjectsOfTypeAll<WeaponDamage>();
        var damageables = Resources.FindObjectsOfTypeAll<Damageable>();

        if (weapons.Length == 0 && damageables.Length == 0)
            return; // Not a combat scene; stay quiet.

        var problems = new List<string>();
        var notes = new List<string>();

        notes.Add($"found {weapons.Length} WeaponDamage, {damageables.Length} Damageable");

        foreach (var weapon in weapons)
        {
            var weaponObject = weapon.gameObject;
            var collider = weapon.GetComponent<Collider>();

            // Name the owning character root so duplicate rigs are obvious at a glance.
            var owner = weapon.transform.root != weapon.transform
                ? weapon.transform.root.name
                : "(no parent)";

            if (collider == null)
            {
                problems.Add($"[owner '{owner}'] {weaponObject.name}: no Collider on the WeaponDamage GameObject, " +
                             "so no Enter/Stay callback can ever fire (the sweep still runs).");
            }
            else
            {
                var layer = weaponObject.layer;
                notes.Add($"[owner '{owner}'] {weaponObject.name}: layer {layer} ({LayerMask.LayerToName(layer)}), " +
                          $"collider '{collider.GetType().Name}' trigger={collider.isTrigger}, " +
                          $"includeMask={collider.includeLayers.value}, excludeMask={collider.excludeLayers.value}");

                if (collider.includeLayers.value != 0)
                    notes.Add($"  ^ includeLayers restricted to mask {collider.includeLayers.value} - " +
                              "physics events only fire for layers inside that mask.");

                var handler = weapon.GetComponentInParent<CombatHandler>();
                if (handler == null)
                {
                    // WeaponDamage falls back to the root subtree, which is the normal case for a blade
                    // parented under FBX bones. Only a total miss is a problem.
                    handler = weapon.transform.root.GetComponentInChildren<CombatHandler>(true);
                    if (handler == null)
                    {
                        problems.Add($"[owner '{owner}'] {weaponObject.name}: no CombatHandler found at all - " +
                                     "strike-phase gating is disabled and this weapon damages on any contact." +
                                     $"\n      parent chain: {DescribeParentChain(weapon.transform)}");
                    }
                    else
                    {
                        notes.Add($"[owner '{owner}'] {weaponObject.name}: gated via root fallback -> " +
                                  $"'{handler.name}' (GetComponentInParent misses the FBX boundary)");
                    }
                }
                else
                {
                    notes.Add($"[owner '{owner}'] {weaponObject.name}: gated via ancestor -> '{handler.name}'");
                }
            }
        }

        // The strike sweep tests layer 8 by default; make sure something damageable is actually there.
        var hasEnemyLayerTarget = false;
        foreach (var damageable in damageables)
        {
            if (damageable.gameObject.layer == 8)
                hasEnemyLayerTarget = true;

            var hasCollider = damageable.GetComponent<Collider>() != null ||
                              damageable.GetComponentInChildren<Collider>() != null;
            if (!hasCollider)
                problems.Add($"{damageable.name}: Damageable has no Collider on its hierarchy, so nothing can hit it.");

            notes.Add($"Damageable '{damageable.name}': layer {damageable.gameObject.layer}, " +
                      $"collider={hasCollider}, root='{damageable.transform.root.name}'");
        }

        if (!hasEnemyLayerTarget)
            problems.Add("no Damageable on layer 8 (Enemy): the strike-window sweep will find nothing. " +
                         "Set WeaponDamage._targetLayers explicitly or move the target to layer 8.");

        // Layer matrix checks - these are exactly the bits that silently kill a hit.
        var playerLayer = DescribeLayer(6);
        var weaponLayer = DescribeLayer(7);
        var enemyLayer = DescribeLayer(8);

        if (Physics.GetIgnoreLayerCollision(7, 8))
            problems.Add($"{weaponLayer} and {enemyLayer} are set to ignore each other in the layer collision " +
                         "matrix - the blade can never touch the target.");
        else
            notes.Add($"{weaponLayer} <-> {enemyLayer} collide (correct)");

        if (Physics.GetIgnoreLayerCollision(6, 7))
            notes.Add($"{playerLayer} <-> {weaponLayer} collide=off (correct, sword is not world geometry)");
        else
            notes.Add($"{playerLayer} <-> {weaponLayer} still collide: the player's own capsule can shove the blade.");

        notes.Add("QueryTriggerInteraction.Collide is used by the sweep, so trigger targets are found.");
        notes.Add($"Physics.queriesHitTriggers = {Physics.queriesHitTriggers}");

        var report = "[DamageSetupSelfTest] " + string.Join("\n  ", notes);
        if (problems.Count > 0)
            Debug.LogError(report + "\nPROBLEMS:\n  - " + string.Join("\n  - ", problems));
        else
            Debug.Log(report + "\n  all checks passed");
    }

    private static string DescribeLayer(int layer)
    {
        var name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? $"layer {layer}" : $"{name} ({layer})";
    }

    /// <summary>Walks upward from a transform, noting which objects carry a CombatHandler.</summary>
    private static string DescribeParentChain(Transform start)
    {
        var parts = new List<string>();
        var current = start;
        var guard = 0;

        while (current != null && guard++ < 40)
        {
            var handlers = current.GetComponents<CombatHandler>();
            var marker = handlers.Length == 0
                ? string.Empty
                : $" <== CombatHandler x{handlers.Length}" +
                  (handlers[0].enabled ? " (enabled)" : " (DISABLED)");

            parts.Add($"{current.name}{marker}");
            current = current.parent;
        }

        return string.Join(" -> ", parts);
    }
}

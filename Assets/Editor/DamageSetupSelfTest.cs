using System.Collections.Generic;
using System.Reflection;
using Spellbreakers.Combat;
using UnityEngine;

/// <summary>
/// TEMPORARY DIAGNOSTIC. Runs once on entering play mode and reports whether the fencing pipeline is
/// actually wired at runtime - the part static inspection cannot prove.
///
/// It verifies the pieces that decide whether a blade contact can resolve at all: teams that are set and
/// actually opposed, CombatHandlers that built an agent, a Blade inside every fighter's hierarchy, the
/// owner/blade references WeaponDamage needs, and a parry table with no holes in it.
///
/// Delete this file (and its .meta) once the fencing pipeline is confirmed working.
/// </summary>
public static class DamageSetupSelfTest
{
    /// <summary>The property CombatHandler exposes for its state machine; null means Awake bailed out.</summary>
    private const string AgentPropertyName = "Agent";

    private static readonly string[] WeaponOwnerField = { "_owner" };
    private static readonly string[] WeaponBladeField = { "_blade" };
    private static readonly string[] WeaponContactMaskField = { "_contactLayers" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void VerifyWeaponSetup()
    {
        var weapons = Resources.FindObjectsOfTypeAll<WeaponDamage>();
        var damageables = Resources.FindObjectsOfTypeAll<Damageable>();
        var handlers = Resources.FindObjectsOfTypeAll<CombatHandler>();

        if (!HasSceneObject(weapons) && !HasSceneObject(damageables) && !HasSceneObject(handlers))
            return; // Not a combat scene; stay quiet.

        var problems = new List<string>();
        var notes = new List<string>();

        var fightingTeams = new HashSet<CombatTeam>();
        CheckDamageables(damageables, problems, notes, fightingTeams);
        CheckHandlers(handlers, problems, notes);
        CheckWeapons(weapons, problems, notes);
        CheckParryTable(problems, notes);

        // Two sides must exist and they must actually be opposed: two fighters both marked Player, or both
        // marked Hostile, look correctly configured in the inspector and fight nobody.
        var hostilePairFound = false;
        foreach (var a in fightingTeams)
        {
            foreach (var b in fightingTeams)
            {
                if (CombatTeams.AreHostile(a, b))
                    hostilePairFound = true;
            }
        }

        if (!hostilePairFound)
        {
            problems.Add($"no hostile pair among the fighting teams ({string.Join(", ", fightingTeams)}): " +
                         "CombatTeams.AreHostile is false for every combination, so no blade contact will " +
                         "ever resolve. Give the player and the enemy different non-Neutral CombatTeams.");
        }
        else
        {
            notes.Add($"fighting teams: {string.Join(", ", fightingTeams)} (at least one hostile pair)");
        }

        var report = "[DamageSetupSelfTest] " + string.Join("\n  ", notes);
        if (problems.Count > 0)
            Debug.Log(report + "\nPROBLEMS:\n  - " + string.Join("\n  - ", problems));
        else
            Debug.Log(report + "\n  all checks passed");
    }

    /// <summary>
    /// Every Damageable that is part of a fighter - it has a CombatHandler in its hierarchy - must declare
    /// a side, because Neutral is hostile to nobody and silently disables every contact it takes part in.
    /// </summary>
    private static void CheckDamageables(Damageable[] damageables, List<string> problems, List<string> notes,
        HashSet<CombatTeam> fightingTeams)
    {
        for (var i = 0; i < damageables.Length; i++)
        {
            var damageable = damageables[i];
            if (!IsInScene(damageable))
                continue;

            var handler = damageable.GetComponentInParent<CombatHandler>() ??
                          damageable.transform.root.GetComponentInChildren<CombatHandler>(true);
            var isFighter = handler != null;
            var team = damageable.Team;

            // Only fighters contribute a side. A Neutral training dummy is a legitimate thing to have and
            // must not make the "two opposing teams" check pass on its own.
            if (isFighter)
                fightingTeams.Add(team);

            if (isFighter && team == CombatTeam.Neutral)
            {
                problems.Add($"Damageable '{damageable.name}' belongs to fighter '{handler.name}' but its Team " +
                             "is Neutral. CombatTeams.AreHostile is false for Neutral, so this fighter can " +
                             "neither deal nor take a resolvable contact.");
            }

            var hasCollider = damageable.GetComponent<Collider>() != null ||
                              damageable.GetComponentInChildren<Collider>() != null;
            if (!hasCollider)
            {
                problems.Add($"Damageable '{damageable.name}': no Collider anywhere in its hierarchy, so the " +
                             "blade sweep can never touch it.");
            }

            notes.Add($"Damageable '{damageable.name}': team {team}, fighter={isFighter}, collider={hasCollider}");
        }
    }

    /// <summary>
    /// A CombatHandler with a null Agent has disabled itself in Awake (usually a missing Animator layer or
    /// guard state), and without a Blade under its root its blade can never report a contact.
    /// </summary>
    private static void CheckHandlers(CombatHandler[] handlers, List<string> problems, List<string> notes)
    {
        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i];
            if (!IsInScene(handler))
                continue;

            var agent = ReadAgent(handler);
            if (agent == null)
            {
                problems.Add($"CombatHandler '{handler.name}': Agent is null, so it never built a state machine. " +
                             "Its Animator is missing, or it has no 'Guards'/'Passes' layer, or the initial " +
                             "guard has no matching state - see the LogError it printed in Awake.");
            }

            var blade = FindBladeUnder(handler.transform);
            if (blade == null)
            {
                problems.Add($"CombatHandler '{handler.name}': no Blade anywhere under '{handler.transform.root.name}', " +
                             "so blade contact can never resolve for this character.");
            }
            else if (!blade.IsValid)
            {
                problems.Add($"CombatHandler '{handler.name}': the Blade on '{blade.name}' has no tip " +
                             "(a child named 'Sword Tip'), so the swept hit test has nothing to sweep.");
            }
            else
            {
                notes.Add($"CombatHandler '{handler.name}': agent={agent.Current}, blade='{blade.name}' " +
                          $"({blade.Length:0.00}m, radius {blade.Radius:0.00})");
            }
        }
    }

    /// <summary>
    /// Both references WeaponDamage needs are reported together: a missing owner means contacts are never
    /// gated to a strike window, and a missing blade means the sweep cannot run at all.
    /// </summary>
    private static void CheckWeapons(WeaponDamage[] weapons, List<string> problems, List<string> notes)
    {
        for (var i = 0; i < weapons.Length; i++)
        {
            var weapon = weapons[i];
            if (!IsInScene(weapon))
                continue;

            var owner = ReadObjectField<CombatHandler>(weapon, WeaponOwnerField);
            if (owner == null)
                owner = weapon.GetComponentInParent<CombatHandler>() ??
                        weapon.transform.root.GetComponentInChildren<CombatHandler>(true);

            var blade = ReadObjectField<Blade>(weapon, WeaponBladeField);
            if (blade == null)
                blade = FindBladeUnder(weapon.transform) ?? weapon.transform.root.GetComponentInChildren<Blade>(true);

            if (owner == null)
            {
                problems.Add($"WeaponDamage '{weapon.name}': resolves no owner CombatHandler, so its contacts " +
                             "are not gated to a strike window.");
            }

            if (blade == null)
            {
                problems.Add($"WeaponDamage '{weapon.name}': resolves no Blade, so the swept hit test cannot run.");
            }
            else if (!blade.IsValid)
            {
                problems.Add($"WeaponDamage '{weapon.name}': its Blade '{blade.name}' has no tip, so the swept " +
                             "hit test cannot run.");
            }

            notes.Add($"WeaponDamage '{weapon.name}': owner={(owner != null ? owner.name : "NONE")}, " +
                      $"blade={(blade != null ? blade.name : "NONE")}, " +
                      $"contactsMask=0x{GetContactMask(weapon):X8}");
        }
    }

    /// <summary>
    /// A hole in the parry table resolves to Undefined, which WeaponDamage treats as a hit and logs a
    /// warning for. Catching it here names the exact cell instead of surfacing it mid-fight.
    /// </summary>
    private static void CheckParryTable(List<string> problems, List<string> notes)
    {
        var parry = CombatRules.Parry;
        if (parry == null)
        {
            problems.Add("CombatRules.Parry is null: no contact can resolve.");
            return;
        }

        var holeCount = 0;

        foreach (var cell in ParryTable.EnumerateGuardCells())
        {
            if (parry.ResolveGuard(cell.Guard, cell.Attack) != CombatOutcome.Undefined)
                continue;

            holeCount++;
            problems.Add($"parry table hole: guard {cell.Guard} vs attack {cell.Attack} resolves to Undefined.");
        }

        foreach (var cell in ParryTable.EnumerateTransitionCells())
        {
            if (parry.ResolveTransition(cell.Defence, cell.Attack) != CombatOutcome.Undefined)
                continue;

            holeCount++;
            problems.Add($"parry table hole: defence {cell.Defence} vs attack {cell.Attack} resolves to Undefined.");
        }

        if (holeCount == 0)
        {
            notes.Add($"parry table complete: {parry.GuardCellCount}/{ParryTable.ExpectedGuardCells} guard cells, " +
                      $"{parry.TransitionCellCount}/{ParryTable.ExpectedTransitionCells} transition cells");
        }
    }

    /// <summary>
    /// FindObjectsOfTypeAll also returns prefab and project assets held in memory, which are not part of
    /// the running scene and would report as false problems.
    /// </summary>
    private static bool IsInScene(Component component)
    {
        return component != null && component.gameObject.scene.IsValid();
    }

    private static bool HasSceneObject<T>(T[] components) where T : Component
    {
        for (var i = 0; i < components.Length; i++)
        {
            if (IsInScene(components[i]))
                return true;
        }

        return false;
    }

    private static int GetContactMask(WeaponDamage weapon)
    {
        var field = FindField(weapon.GetType(), WeaponContactMaskField);
        if (field == null)
            return -1;

        var mask = (LayerMask)field.GetValue(weapon);
        return mask.value;
    }

    private static FencingAgent ReadAgent(CombatHandler handler)
    {
        var property = handler.GetType().GetProperty(AgentPropertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(handler) as FencingAgent;
    }

    private static T ReadObjectField<T>(object target, string[] candidateNames) where T : class
    {
        var field = FindField(target.GetType(), candidateNames);
        return field?.GetValue(target) as T;
    }

    private static FieldInfo FindField(System.Type type, string[] candidateNames)
    {
        for (var i = 0; i < candidateNames.Length; i++)
        {
            var field = type.GetField(candidateNames[i],
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                return field;
        }

        return null;
    }

    /// <summary>The blade normally sits on the weapon object, but a rig may parent it elsewhere.</summary>
    private static Blade FindBladeUnder(Transform start)
    {
        if (start == null)
            return null;

        var own = start.GetComponent<Blade>();
        if (own != null)
            return own;

        var parent = start.GetComponentInParent<Blade>();
        if (parent != null)
            return parent;

        return start.root.GetComponentInChildren<Blade>(true);
    }
}

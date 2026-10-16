using System.Collections.Generic;
using Spellbreakers.Combat;
using UnityEditor;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Upgrades the existing player prefab to the fencing combat stack.
    ///
    /// The prefab predates the refactor: it carries the Animator, the movement and the camera rig but no
    /// Damageable, no Blade and no debug view. Those are all serialized references that cannot be authored
    /// without an editor, so this tool loads the prefab contents, wires them and writes the prefab back.
    ///
    /// Run via menu: Spellbreakers / Setup / Upgrade Player Prefab
    /// </summary>
    public static class PlayerCombatSetup
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Characters/Raine_Humanoid.prefab";

        [MenuItem("Spellbreakers/Setup/Upgrade Player Prefab")]
        public static void UpgradePlayerPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"{FencingEditorSetup.LogTag} No prefab at '{PlayerPrefabPath}'; nothing to upgrade.");
                return;
            }

            var changes = new List<string>();

            // The contents must be unloaded on every path, including a failure, or the prefab stays locked
            // and the next edit silently operates on stale contents. The catch exists because a menu action
            // must report rather than throw.
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

                if (FencingEditorSetup.EnsureDamageable(contents, CombatTeam.Player))
                    changes.Add("Damageable: ensured on the root with Team = Player and the LookAt aim point");

                if (FencingEditorSetup.EnsureDebugView(contents))
                    changes.Add("added FencingDebugView (F1 toggles the readout)");

                if (FencingEditorSetup.WireBladeStack(contents, out var bladeSummary))
                    changes.Add($"wired the {bladeSummary}");

                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Every ensure-call above is a no-op on an already-upgraded prefab, so an empty diff is
                // exactly the "nothing left to do" case.
                if (changes.Count == 0)
                {
                    Debug.Log($"{FencingEditorSetup.LogTag} '{PlayerPrefabPath}' is already up to date: " +
                              "Damageable, Blade/WeaponDamage wiring and FencingDebugView all present.");
                    return;
                }

                Debug.Log($"{FencingEditorSetup.LogTag} Upgraded '{PlayerPrefabPath}':\n  - " +
                          string.Join("\n  - ", changes));
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"{FencingEditorSetup.LogTag} Upgrade of '{PlayerPrefabPath}' failed: {exception}");
            }
            finally
            {
                if (contents != null)
                    PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}

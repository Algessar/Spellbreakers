using Player;
using Spellbreakers.Ai;
using Spellbreakers.Combat;
using UnityEditor;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Shared editor wiring for the fencing stack, used by both the scene builder and the prefab upgrade
    /// tool so the two can never disagree about where the Damageable, the Blade and the WeaponDamage owner
    /// live.
    ///
    /// Every entry point reports what it changed rather than assuming success, because these tools run
    /// against a rig whose bone hierarchy comes from an FBX and can legitimately be missing pieces.
    /// </summary>
    internal static class FencingEditorSetup
    {
        /// <summary>Prefix for every log line these tools emit, so a build log is greppable.</summary>
        public const string LogTag = "[FencingLab]";

        public const string WeaponObjectName = "weapon_collider";
        public const string WeaponBoneName = "DEF-WEAPON_MAIN";
        public const string SwordTipName = "Sword Tip";
        public const string LookAtName = "LookAt";

        private const string EnemyMaterialFolder = "Assets/Art/Materials/M_Raine";
        private const string EnemyMaterialPath = EnemyMaterialFolder + "/M_Raine_Enemy.mat";
        private const string EnemyMaterialName = "M_Raine_Enemy";
        private const string PlayerMaterialNameFragment = "Raine";

        private static readonly Color EnemyTint = new Color(0.85f, 0.22f, 0.18f, 1f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("color");

        /// <summary>Instantiates a prefab into the active scene, or logs and returns null.</summary>
        public static GameObject InstantiateCharacterPrefab(string path, string name, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"{LogTag} No prefab at '{path}'; '{name}' was not spawned.");
                return null;
            }

            if (!(PrefabUtility.InstantiatePrefab(prefab) is GameObject instance))
            {
                Debug.LogWarning($"{LogTag} Prefab '{path}' did not instantiate as a GameObject; " +
                                 $"'{name}' was not spawned.");
                return null;
            }

            instance.name = name;
            instance.transform.position = position;
            return instance;
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        /// <summary>
        /// Ensures the root carries a Damageable on the requested team, aimed at its LookAt child.
        /// Returns true when anything actually changed, so callers can report an accurate diff.
        /// </summary>
        public static bool EnsureDamageable(GameObject root, CombatTeam team)
        {
            var damageable = root.GetComponent<Damageable>();
            var changed = false;

            if (damageable == null)
            {
                damageable = root.AddComponent<Damageable>();
                changed = true;
            }

            if (damageable.Team != team)
            {
                damageable.SetTeam(team);
                changed = true;
            }

            // An explicit aim point is what the health bar, the sword-aim IK and the AI's distance maths
            // all read, so a rig without one is worth naming rather than silently approximating.
            var aimPoint = FindChildRecursive(root.transform, LookAtName);
            if (aimPoint != null)
            {
                if (damageable.AimTransform != aimPoint)
                {
                    damageable.SetAimPoint(aimPoint);
                    changed = true;
                }
            }
            else
            {
                Debug.LogWarning($"{LogTag} '{root.name}' has no '{LookAtName}' child; Damageable falls " +
                                 "back to 1.2 m above the root for aiming.");
            }

            EditorUtility.SetDirty(damageable);
            return changed;
        }

        /// <summary>
        /// Removes the components that make sense only on a player-controlled character. The search goes
        /// into children as well: TargetingSystem is added to the Animator's GameObject, which on this rig
        /// is not the root.
        /// </summary>
        public static void StripPlayerOnlyComponents(GameObject root)
        {
            DestroyIfPresent<InputHandler>(root);
            DestroyIfPresent<PlayerCamera>(root);
            DestroyIfPresent<TargetingSystem>(root);
        }

        public static void DestroyIfPresent<T>(GameObject root) where T : Component
        {
            var component = root.GetComponent<T>() ?? root.GetComponentInChildren<T>(true);
            if (component != null)
                Object.DestroyImmediate(component);
        }

        /// <summary>
        /// Makes sure a Blade sits on the weapon object and that the WeaponDamage on the same object knows
        /// about both it and the character's CombatHandler. Returns true when anything had to be changed.
        /// </summary>
        public static bool WireBladeStack(GameObject root, out string summary)
        {
            summary = string.Empty;

            var weaponObject = FindWeaponObject(root);
            if (weaponObject == null)
            {
                Debug.LogWarning($"{LogTag} '{root.name}': found neither a '{WeaponObjectName}' child nor a " +
                                 $"'{WeaponBoneName}' bone, so no Blade could be placed. Blade contact will " +
                                 "never resolve for this character.");
                return false;
            }

            var changed = false;

            var blade = weaponObject.GetComponent<Blade>();
            if (blade == null)
            {
                blade = weaponObject.AddComponent<Blade>();
                changed = true;
            }

            // ResolveReferences walks this object and then the root, because on this rig 'Sword Tip' is a
            // sibling of the weapon object under the DEF-WEAPON_MAIN bone rather than a child of the
            // collider.
            blade.ResolveReferences();

            var bladeSo = new SerializedObject(blade);
            var baseProperty = bladeSo.FindProperty("_base");
            var tipProperty = bladeSo.FindProperty("_tip");
            var tip = FindChildRecursive(root.transform, SwordTipName);

            if (tip != null)
            {
                if (baseProperty != null && baseProperty.objectReferenceValue == null)
                {
                    baseProperty.objectReferenceValue = weaponObject.transform;
                    changed = true;
                }

                if (tipProperty != null && tipProperty.objectReferenceValue != tip)
                {
                    tipProperty.objectReferenceValue = tip;
                    changed = true;
                }
            }
            else
            {
                Debug.LogWarning($"{LogTag} '{root.name}': no '{SwordTipName}' transform found; the Blade on " +
                                 $"'{weaponObject.name}' has no tip and the swept hit test cannot run.");
            }

            bladeSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blade);

            var weapon = weaponObject.GetComponent<WeaponDamage>();
            if (weapon == null)
            {
                Debug.LogWarning($"{LogTag} '{root.name}': '{weaponObject.name}' carries no WeaponDamage, " +
                                 "so nothing turns blade motion into combat results.");
                summary = $"Blade on '{weaponObject.name}' (no WeaponDamage present)";
                return changed;
            }

            var handler = root.GetComponent<CombatHandler>() ?? root.GetComponentInChildren<CombatHandler>(true);
            var weaponSo = new SerializedObject(weapon);

            if (handler != null)
            {
                if (SetObjectReference(weaponSo, "_owner", handler, weaponObject.name))
                    changed = true;
            }
            else
            {
                Debug.LogWarning($"{LogTag} '{root.name}': no CombatHandler found, so '{weaponObject.name}' " +
                                 "cannot gate its contacts to a strike window.");
            }

            if (SetObjectReference(weaponSo, "_blade", blade, weaponObject.name))
                changed = true;

            weaponSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(weapon);

            summary = $"Blade on '{weaponObject.name}' (tip '{tip?.name ?? "unresolved"}', owner " +
                      $"'{handler?.name ?? "none"}')";
            return changed;
        }

        public static bool EnsureDebugView(GameObject root)
        {
            if (root.GetComponent<FencingDebugView>() != null)
                return false;

            // The F1 default toggle key and the all-combatants default are what these tools want, so
            // nothing is overridden here.
            root.AddComponent<FencingDebugView>();
            return true;
        }

        /// <summary>Assigns a serialized object reference, returning true when it actually changed.</summary>
        public static bool SetObjectReference(SerializedObject target, string propertyName, Object value,
            string ownerName)
        {
            var property = target.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"{LogTag} '{ownerName}': no serialized field '{propertyName}' on " +
                                 $"{target.targetObject.GetType().Name}; wiring skipped.");
                return false;
            }

            if (property.objectReferenceValue == value)
                return false;

            property.objectReferenceValue = value;
            return true;
        }

        public static void SetBoolProperty(SerializedObject target, string propertyName, bool value, string ownerName)
        {
            var property = target.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"{LogTag} '{ownerName}': no serialized field '{propertyName}' on " +
                                 $"{target.targetObject.GetType().Name}; value not set.");
                return;
            }

            property.boolValue = value;
        }

        /// <summary>
        /// Gives the enemy its own tinted copy of the player's material, so the two fighters are
        /// distinguishable at a glance in the lab.
        /// </summary>
        public static void TintEnemy(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"{LogTag} '{root.name}' has no SkinnedMeshRenderer; the enemy keeps the " +
                                 "player's colour.");
                return;
            }

            var source = ResolveSourceMaterial(renderers);
            if (source == null)
            {
                Debug.LogWarning($"{LogTag} '{root.name}': no source material found on the rig; the enemy " +
                                 "keeps the player's colour.");
                return;
            }

            var tinted = EnsureEnemyMaterial(source);

            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == source)
                        materials[m] = tinted;
                }

                renderers[i].sharedMaterials = materials;
                EditorUtility.SetDirty(renderers[i]);
            }
        }

        /// <summary>Creates or reuses the tinted enemy material, so repeated builds do not stack assets.</summary>
        public static Material EnsureEnemyMaterial(Material source)
        {
            EnsureFolder(EnemyMaterialFolder);

            var material = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
            if (material == null)
            {
                material = new Material(source) { name = EnemyMaterialName };
                AssetDatabase.CreateAsset(material, EnemyMaterialPath);
            }

            ApplyTint(material, EnemyTint);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyTint(Material material, Color tint)
        {
            // URP names its colour '_BaseColor'; built-in pipeline shaders use 'color'. Probe for the
            // property instead of assuming the pipeline, and say so when neither exists.
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, tint);
                return;
            }

            if (material.HasProperty(LegacyColorId))
            {
                material.SetColor(LegacyColorId, tint);
                return;
            }

            Debug.LogWarning($"{LogTag} Material '{material.name}' exposes neither '_BaseColor' nor 'color', " +
                             "so the enemy tint could not be applied.");
        }

        private static Material ResolveSourceMaterial(SkinnedMeshRenderer[] renderers)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    if (materials[m] != null && materials[m].name.Contains(PlayerMaterialNameFragment))
                        return materials[m];
                }
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    if (materials[m] != null)
                        return materials[m];
                }
            }

            return null;
        }

        /// <summary>The object the Blade and WeaponDamage belong on: the collider if present, else the bone.</summary>
        public static GameObject FindWeaponObject(GameObject root)
        {
            var byName = FindChildRecursive(root.transform, WeaponObjectName);
            if (byName != null)
                return byName.gameObject;

            var bone = FindChildRecursive(root.transform, WeaponBoneName);
            return bone != null ? bone.gameObject : null;
        }

        /// <summary>Creates a project folder and any missing parents, using AssetDatabase so it gets a .meta.</summary>
        public static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            var parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(folderPath);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                Debug.LogWarning($"{LogTag} Could not create asset folder '{folderPath}'.");
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;

            if (root.name == childName)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}

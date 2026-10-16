using System.Collections.Generic;
using Player;
using Spellbreakers.Ai;
using Spellbreakers.Combat;
using Spellbreakers.Combat.Ai;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spellbreakers.EditorTools
{
    /// <summary>
    /// Builds SCENE_FencingLab: one player, one enemy fencer and a dummy in an arena whose geometry
    /// forces the fight through a narrow passage.
    ///
    /// A scene is the only place the fencing stack can actually be judged - the guard transitions, the
    /// measure bands and the parry table are all invisible in a prefab inspector - so the lab is built
    /// from code rather than hand-authored. Rebuilding it is then a menu click instead of a merge.
    ///
    /// Run via menu: Spellbreakers / Build Fencing Lab Scene
    /// </summary>
    public static class FencingLabSceneBuilder
    {
        private const string ScenePath = "Assets/World/Scenes/SCENE_FencingLab.unity";
        private const string SceneFolder = "Assets/World/Scenes";
        private const string PlayerPrefabPath = "Assets/Prefabs/Characters/Raine_Humanoid.prefab";
        private const string DummyPrefabPath = "Assets/Prefabs/Training_Dummy.prefab";
        private const string DummyFallbackPrefabPath = "Assets/Prefabs/Environment/Architecture/Training_Dummy.prefab";

        private const string PlayerName = "Raine_Player";
        private const string EnemyName = "ENEMY_Fencer";
        private const string EnemyArchetypeName = "Archetype_Fencer";

        // Placement is hoisted so the arena can be re-proportioned without hunting through the builder.
        private const float GroundScale = 4f;                 // Plane primitive is 10 m, so 4 -> 40 m x 40 m
        private static readonly Vector3 GroundPosition = Vector3.zero;
        private static readonly Vector3 PlayerSpawn = new Vector3(0f, 0f, -4f);
        private static readonly Vector3 EnemySpawn = new Vector3(0f, 0f, 4f);
        private static readonly Vector3 DummyPosition = new Vector3(-10f, 0f, 0f);
        private static readonly Vector3 DummyEuler = new Vector3(0f, 90f, 0f);

        // The arena: a boundary wall with a single narrow gap on +X, plus a flanking block that makes the
        // gap the only fast route between the two halves.
        private static readonly Vector3 BoundaryWallScale = new Vector3(0.5f, 2.5f, 20f);
        private static readonly Vector3 BoundaryWallPosition = new Vector3(10f, 1.25f, 0f);
        private const float PassageWidth = 2.2f;              // Gap cut out of the wall on the Z axis
        private static readonly Vector3 PassageBlockScale = new Vector3(3f, 2.5f, 4f);
        private static readonly Vector3 PassageBlockPosition = new Vector3(-6f, 1.25f, 5f);
        private static readonly Vector3 PassageBlockEuler = new Vector3(0f, 35f, 0f);
        private static readonly Vector3 CornerBlockScale = new Vector3(4f, 2.5f, 4f);
        private static readonly Vector3 CornerBlockPosition = new Vector3(-6f, 1.25f, -6f);

        private static readonly Vector3 CameraOffset = new Vector3(0f, 2.4f, -6f);
        private static readonly Vector3 CameraLookHeight = new Vector3(0f, 1.4f, 0f);
        private static readonly Vector3 LightEuler = new Vector3(50f, -30f, 0f);

        [MenuItem("Spellbreakers/Build Fencing Lab Scene")]
        public static void Build()
        {
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var notes = new List<string>();

                BuildLighting(notes);
                BuildGround();
                BuildArena();

                var player = SpawnPlayer(notes);
                SpawnEnemy(notes);
                SpawnTrainingDummy(notes);
                BuildCamera(player, notes);

                EditorSceneManager.MarkSceneDirty(scene);
                FencingEditorSetup.EnsureFolder(SceneFolder);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"{FencingEditorSetup.LogTag} Built " + ScenePath + "\n  " + string.Join("\n  ", notes) +
                          "\nManual follow-up:\n" +
                          "  - A baked NavMesh is optional: EnemyLocomotion falls back to direct duelling " +
                          "steering when no NavMeshAgent is present, which is how this scene is built.\n" +
                          "  - Press F1 in play mode for the FencingDebugView readout; the enemy logs its " +
                          "decision breakdown to the console because _logDecisions is on.");
            }
            catch (System.Exception exception)
            {
                // A menu action must never throw: report and leave the project in whatever consistent
                // state the scene is in rather than interrupting the editor.
                Debug.LogError($"{FencingEditorSetup.LogTag} Scene build failed: {exception}");
            }
        }

        private static void BuildLighting(List<string> notes)
        {
            var existing = Object.FindAnyObjectByType<Light>();
            if (existing != null)
            {
                notes.Add($"lighting: kept existing '{existing.name}'");
                return;
            }

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(LightEuler);
            notes.Add("lighting: added a directional light (empty scene had none)");
        }

        private static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = GroundPosition;
            ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
            ground.layer = LayerMask.NameToLayer("Ground") >= 0 ? LayerMask.NameToLayer("Ground") : 0;
        }

        private static void BuildArena()
        {
            // The wall is built from two segments rather than one box so the passage is a real gap in a
            // continuous barrier - a single box scaled to leave room would have no edges to fight around.
            var segmentLength = (BoundaryWallScale.z - PassageWidth) * 0.5f;
            var offset = (PassageWidth + segmentLength) * 0.5f;

            CreateBlock(
                "Arena_Wall_South",
                new Vector3(BoundaryWallPosition.x, BoundaryWallPosition.y, -offset),
                new Vector3(BoundaryWallScale.x, BoundaryWallScale.y, segmentLength),
                Vector3.zero);

            CreateBlock(
                "Arena_Wall_North",
                new Vector3(BoundaryWallPosition.x, BoundaryWallPosition.y, offset),
                new Vector3(BoundaryWallScale.x, BoundaryWallScale.y, segmentLength),
                Vector3.zero);

            CreateBlock("Arena_PassageBlock", PassageBlockPosition, PassageBlockScale, PassageBlockEuler);
            CreateBlock("Arena_CornerBlock", CornerBlockPosition, CornerBlockScale, Vector3.zero);
        }

        private static GameObject CreateBlock(string name, Vector3 position, Vector3 scale, Vector3 euler)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.position = position;
            block.transform.localScale = scale;
            block.transform.rotation = Quaternion.Euler(euler);
            return block;
        }

        private static GameObject SpawnPlayer(List<string> notes)
        {
            var player = FencingEditorSetup.InstantiateCharacterPrefab(PlayerPrefabPath, PlayerName, PlayerSpawn);
            if (player == null)
            {
                notes.Add("player: MISSING - prefab could not be loaded");
                return null;
            }

            var damageableAdded = FencingEditorSetup.EnsureDamageable(player, CombatTeam.Player);
            var debugViewAdded = FencingEditorSetup.EnsureDebugView(player);
            FencingEditorSetup.WireBladeStack(player, out var bladeSummary);

            notes.Add($"player: '{player.name}' at {PlayerSpawn}, Damageable(Player)" +
                      (damageableAdded ? " added" : " already present") +
                      (debugViewAdded ? ", FencingDebugView added" : string.Empty) +
                      $", {bladeSummary}");

            return player;
        }

        private static void SpawnEnemy(List<string> notes)
        {
            var enemy = FencingEditorSetup.InstantiateCharacterPrefab(PlayerPrefabPath, EnemyName, EnemySpawn);
            if (enemy == null)
            {
                notes.Add("enemy: MISSING - prefab could not be loaded");
                return;
            }

            var enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer < 0)
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} Layer 'Enemy' does not exist; '{enemy.name}' stays on " +
                                 $"layer {enemy.layer}. Perception still works, but layer-based filtering will not.");
            }
            else
            {
                FencingEditorSetup.SetLayerRecursively(enemy, enemyLayer);
            }

            FencingEditorSetup.StripPlayerOnlyComponents(enemy);
            FencingEditorSetup.EnsureDamageable(enemy, CombatTeam.Hostile);
            FencingEditorSetup.WireBladeStack(enemy, out var bladeSummary);

            enemy.AddComponent<EnemyLocomotion>();
            enemy.AddComponent<FencingPerception>();

            var fencer = enemy.AddComponent<EnemyFencer>();
            var fencerSo = new SerializedObject(fencer);
            var archetypeProperty = fencerSo.FindProperty("_archetype");
            if (archetypeProperty != null)
            {
                archetypeProperty.objectReferenceValue =
                    FencingArchetypeAsset.CreateTransient(FencingArchetype.CreateFencer(), EnemyArchetypeName);
            }
            else
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} '{enemy.name}': EnemyFencer has no '_archetype' field; " +
                                 "the enemy will fall back to the default fencer archetype.");
            }

            FencingEditorSetup.SetBoolProperty(fencerSo, "_logDecisions", true, enemy.name);
            fencerSo.ApplyModifiedPropertiesWithoutUndo();

            FencingEditorSetup.TintEnemy(enemy);

            notes.Add($"enemy: '{enemy.name}' at {EnemySpawn}, layer Enemy({enemyLayer}), " +
                      $"player-only components removed, Damageable(Hostile), {bladeSummary}, " +
                      "EnemyLocomotion + FencingPerception + EnemyFencer with " + EnemyArchetypeName);
        }

        private static void SpawnTrainingDummy(List<string> notes)
        {
            var dummy = FencingEditorSetup.InstantiateCharacterPrefab(DummyPrefabPath, "Training_Dummy", DummyPosition);
            if (dummy == null)
            {
                dummy = FencingEditorSetup.InstantiateCharacterPrefab(
                    DummyFallbackPrefabPath, "Training_Dummy", DummyPosition);
            }

            if (dummy == null)
            {
                notes.Add("dummy: MISSING - neither dummy prefab could be loaded; spawned a placeholder capsule");
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.name = "Training_Dummy_Placeholder";
                placeholder.transform.position = DummyPosition + new Vector3(0f, 1f, 0f);
                placeholder.AddComponent<Damageable>();
                return;
            }

            dummy.transform.rotation = Quaternion.Euler(DummyEuler);
            notes.Add($"dummy: '{dummy.name}' at {DummyPosition} (Neutral, so it is nobody's enemy)");
        }

        private static void BuildCamera(GameObject player, List<string> notes)
        {
            var camera = Camera.main;
            if (camera == null)
                camera = Object.FindAnyObjectByType<Camera>();

            if (camera == null)
            {
                // The prefab's Cinemachine rig needs a Brain and a live follow target to be useful, which
                // is more moving parts than a test arena needs; a plain third-person camera is enough.
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                camera.transform.position = (player != null ? player.transform.position : PlayerSpawn) + CameraOffset;
                camera.transform.LookAt((player != null ? player.transform.position : PlayerSpawn) + CameraLookHeight);
                notes.Add("camera: created a plain third-person 'Main Camera' (prefab has no usable scene camera)");
            }
            else
            {
                notes.Add($"camera: reused existing '{camera.name}'");
            }

            if (player == null)
                return;

            var playerCamera = player.GetComponentInChildren<PlayerCamera>(true);
            if (playerCamera == null)
            {
                Debug.LogWarning($"{FencingEditorSetup.LogTag} '{player.name}' has no PlayerCamera; camera-relative movement " +
                                 "input will fall back to world space.");
                return;
            }

            var cameraSo = new SerializedObject(playerCamera);
            var cameraProperty = cameraSo.FindProperty("_camera");
            if (cameraProperty != null && cameraProperty.objectReferenceValue == null)
            {
                cameraProperty.objectReferenceValue = camera;
                cameraSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}


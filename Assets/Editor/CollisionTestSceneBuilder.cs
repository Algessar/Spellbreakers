using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds SCENE_CollisionTest from raw imported FBX assets - no prefabs - to isolate whether the
/// player's grounding is broken by the project's scene data or by the code itself.
///
/// Run via menu: Spellbreakers / Diagnostics / Build SCENE_CollisionTest
/// </summary>
public static class CollisionTestSceneBuilder
{
    private const string ScenePath = "Assets/World/Scenes/SCENE_CollisionTest.unity";

    private const string WorldFbxPath = "Assets/Imports/Environment/Twin_Keep_ScaleTest1.fbx";
    private const string CharacterFbxPath = "Assets/Animation/HumanoidAvatar/Raine_Humanoid.fbx";
    private const string ControllerPath = "Assets/Animation/HumanoidAvatar/Controller_Masks/Raine_Humanoid.controller";
    private const string AvatarPath = "Assets/Animation/Imported/Raine_Rigify_V8.fbx";

    private const int GroundLayer = 3; // "Ground"

    [MenuItem("Spellbreakers/Diagnostics/Build SCENE_CollisionTest")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---- World (Twin_Keep_ScaleTest1) ----
        var world = InstantiateModel(WorldFbxPath, "Twin_Keep_ScaleTest1");
        if (world == null) return;

        SetLayerRecursively(world, 0); // everything Default by default

        var valleyGround = FindChildRecursive(world.transform, "ValleyGround");
        if (valleyGround == null)
        {
            Debug.LogError("[CollisionTest] Could not find a child named 'ValleyGround' in the world FBX.");
            return;
        }
        SetLayerRecursively(valleyGround.gameObject, GroundLayer); // only ValleyGround = Ground
        EnsureMeshCollider(valleyGround.gameObject); // add the collider explicitly, never trust the importer

        // ---- Character (Raine_Humanoid FBX) ----
        var character = InstantiateModel(CharacterFbxPath, "Raine_Humanoid");
        if (character == null) return;

        // Animator: controller + humanoid avatar
        var animator = character.GetComponent<Animator>();
        if (animator == null) animator = character.AddComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
        animator.applyRootMotion = true;

        // Rigidbody: kinematic, Movement_V3 moves the transform and applies gravity itself
        var body = character.GetComponent<Rigidbody>();
        if (body == null) body = character.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        // Movement_V3 auto-adds InputHandler, CapsuleCollider, PlayerCamera via [RequireComponent]
        var movement = character.AddComponent<Player.Movement>();
        var capsule = character.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
            capsule.center = new Vector3(0f, 0.89f, -0.1f);
            capsule.direction = 1;
        }

        // Ground layer mask for Movement_V3 = Ground only (as requested)
        var movementSo = new SerializedObject(movement);
        movementSo.FindProperty("_groundLayer").intValue = 1 << GroundLayer;
        movementSo.ApplyModifiedPropertiesWithoutUndo();

        // Spawn the character above the ground
        var groundRenderer = valleyGround.GetComponent<Renderer>();
        Vector3 spawn = new Vector3(0f, 5f, 0f);
        if (groundRenderer != null)
        {
            var bounds = groundRenderer.bounds;
            spawn = new Vector3(bounds.center.x, bounds.max.y + 2f, bounds.center.z);
        }
        character.transform.position = spawn;

        // Wire the player camera to the scene's Main Camera
        var camera = Camera.main;
        var playerCamera = character.GetComponent<Player.PlayerCamera>();
        if (camera != null && playerCamera != null)
        {
            var camSo = new SerializedObject(playerCamera);
            camSo.FindProperty("_camera").objectReferenceValue = camera;
            camSo.ApplyModifiedPropertiesWithoutUndo();

            // Aim the scene camera at the spawn point
            camera.transform.position = spawn + new Vector3(0f, 2.5f, -5f);
            camera.transform.LookAt(spawn);
        }

        // ---- Save ----
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CollisionTest] Built {ScenePath}.\n" +
                  $"  world: {world.name} (Default), ValleyGround -> Ground({GroundLayer})\n" +
                  $"  character: {character.name} with Animator, kinematic Rigidbody, CapsuleCollider, Movement_V3\n" +
                  $"  Movement_V3._groundLayer = Ground only\n" +
                  $"  spawn = {spawn}");
    }

    private static GameObject InstantiateModel(string path, string name)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogError($"[CollisionTest] Failed to load model at {path}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = name;
        return instance;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>Adds a MeshCollider from the object's MeshFilter, so grounding never depends on the
    /// FBX importer's "Generate Colliders" flag actually taking effect.</summary>
    private static void EnsureMeshCollider(GameObject go)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            Debug.LogWarning($"[CollisionTest] '{go.name}' has no MeshFilter/sharedMesh; cannot add a MeshCollider.");
            return;
        }

        var collider = go.GetComponent<MeshCollider>();
        if (collider == null)
            collider = go.AddComponent<MeshCollider>();

        collider.sharedMesh = filter.sharedMesh;
        collider.convex = false;
        Debug.Log($"[CollisionTest] '{go.name}' MeshCollider ready: {collider.sharedMesh.name} " +
                  $"({collider.sharedMesh.vertexCount} verts, convex={collider.convex})");
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root.name == childName)
            return root;

        foreach (Transform child in root)
        {
            var found = FindChildRecursive(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }
}

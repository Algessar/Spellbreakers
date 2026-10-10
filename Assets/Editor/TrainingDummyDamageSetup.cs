using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Sets up the HomeBase training dummy as a reusable prefab with a damage receiver.</summary>
public static class TrainingDummyDamageSetup
{
    private const string ScenePath = "Assets/World/Scenes/SCENE_HomeBase.unity";
    private const string PrefabPath = "Assets/Prefabs/Training_Dummy.prefab";
    [MenuItem("Spellbreakers/Combat/Setup Training Dummy Prefab")]
    private static void SetupTrainingDummyPrefab()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError($"Open {ScenePath} before running this command.");
            return;
        }

        var dummy = FindInScene(scene, "Training_Dummy");
        if (dummy == null)
        {
            Debug.LogError("Could not find a GameObject named Training_Dummy in the open scene.");
            return;
        }

        EnsureDamageReceiver(dummy);
        EnsureTrainingDummyPhysics(dummy);

        if (PrefabUtility.IsPartOfPrefabInstance(dummy))
            PrefabUtility.UnpackPrefabInstance(dummy, PrefabUnpackMode.Completely, InteractionMode.UserAction);

        Directory.CreateDirectory("Assets/Prefabs");
        PrefabUtility.SaveAsPrefabAssetAndConnect(dummy, PrefabPath, InteractionMode.UserAction, out var success);
        if (!success)
        {
            Debug.LogError("Unity could not create the Training_Dummy prefab.", dummy);
            return;
        }

        var weaponCount = AddWeaponDamageToSceneWeapons(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (weaponCount == 0)
        {
            Debug.LogWarning("Training_Dummy.prefab is ready. No Ancuil GameObject with a BoxCollider was found in the open scene; add WeaponDamage to the Ancuil BoxCollider to enable hits.", dummy);
        }
        else
        {
            Debug.Log($"Created {PrefabPath} with a damage receiver and configured {weaponCount} Ancuil weapon collider(s).");
        }
    }

    private static GameObject FindInScene(Scene scene, string objectName)
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate.scene == scene && candidate.name == objectName)
                return candidate;
        }

        return null;
    }

    private static void EnsureDamageReceiver(GameObject dummy)
    {
        if (dummy.GetComponent<Damageable>() == null)
            Undo.AddComponent<Damageable>(dummy);
    }

    private static void EnsureTrainingDummyPhysics(GameObject dummy)
    {
        var body = dummy.GetComponent<Rigidbody>();
        if (body == null)
            body = Undo.AddComponent<Rigidbody>(dummy);

        body.isKinematic = true;
        body.useGravity = false;

        var collider = dummy.GetComponent<CapsuleCollider>();
        if (collider == null)
            collider = Undo.AddComponent<CapsuleCollider>(dummy);

        var renderers = dummy.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        var worldBounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

        var extents = worldBounds.extents;
        var localCenter = dummy.transform.InverseTransformPoint(worldBounds.center);
        var localX = dummy.transform.InverseTransformVector(Vector3.right * extents.x).magnitude;
        var localY = dummy.transform.InverseTransformVector(Vector3.up * extents.y).magnitude;
        var localZ = dummy.transform.InverseTransformVector(Vector3.forward * extents.z).magnitude;

        collider.direction = 1;
        collider.center = localCenter;
        collider.radius = Mathf.Max(localX, localZ);
        collider.height = Mathf.Max(localY * 2f, collider.radius * 2f);
    }

    private static int AddWeaponDamageToSceneWeapons(Scene scene)
    {
        var count = 0;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate.scene != scene || candidate.name.IndexOf("Ancuil", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            foreach (var box in candidate.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.GetComponent<WeaponDamage>() == null)
                    Undo.AddComponent<WeaponDamage>(box.gameObject);
                count++;
            }
        }

        return count;
    }
}

using System;
using Player;
using UnityEngine;

/// <summary>
/// One destination rune on a teleport platform.
///
/// The rune owns the destination, not the platform. A platform offering three runes was previously
/// identified by an <c>int</c> that had to match a build index, which meant the platform's wiring silently
/// changed meaning whenever a scene was added, removed or reordered. The id is now the same stable string
/// the scene catalog and the save file use.
///
/// It also decides who counts as an arrival. The old trigger fired for any collider that touched it,
/// including enemies and thrown props, so a fight next to a platform could start a scene load.
/// </summary>
public class EnterRuneTrigger : MonoBehaviour
{
    /// <summary>Raised once when the player enters. The platform decides what that means.</summary>
    public Action _triggerAction;

    [Tooltip("Scene id from the scene catalog, for example 'home-base'. Not a build index.")]
    [SerializeField] private string _sceneId;

    /// <summary>The catalog id this rune travels to.</summary>
    public string SceneId => _sceneId;

    /// <summary>True once the player has entered, until the platform resets it.</summary>
    public bool WasTriggered { get; private set; }

    private void OnTriggerEnter(Collider other)
    {
        if (WasTriggered)
            return;

        if (string.IsNullOrWhiteSpace(_sceneId))
        {
            Debug.LogError($"{name}: this rune has no scene id, so it cannot travel anywhere.", this);
            return;
        }

        // The input handler is on the player and on nothing else, which is the only reliable way to tell
        // the player apart here: the character prefab is untagged.
        if (other.GetComponentInParent<InputHandler>() == null)
            return;

        WasTriggered = true;
        _triggerAction?.Invoke();
    }

    /// <summary>Clears the latch so the rune can fire again after the player steps off and back on.</summary>
    public void ResetTrigger()
    {
        WasTriggered = false;
    }

    public override string ToString()
    {
        return $"EnterRuneTrigger '{name}' -> {_sceneId}";
    }
}

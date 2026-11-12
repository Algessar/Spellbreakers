using Player;
using Spellbreakers.Gameplay;
using UnityEngine;

/// <summary>
/// A teleport platform. Standing on it lights the disc, raises the compass and offers its runes; entering a
/// rune travels to the scene that rune names.
///
/// This replaces a version whose travel was <c>SceneManager.LoadScene(buildIndex)</c>, where the rune's
/// <c>int</c> had to equal a position in the build settings, and whose presentation did not work at all:
/// the glow was computed from two fields that were never assigned and gated behind a flag that was never
/// set, and the compass compared its position against a moving target it could never equal, so it moved
/// once and never came back. Both are fixed here because a platform that does not visibly respond is
/// indistinguishable from one that does not work.
///
/// Two behaviours are deliberate rather than incidental. Only the player can activate the platform - the
/// character prefab is untagged, so the check is for the component that only the player carries - and
/// stepping off clears the rune latches, so a second visit works and a fight next to the platform cannot
/// start a scene load.
/// </summary>
[DisallowMultipleComponent]
public class Teleporter : MonoBehaviour
{
    [Header("Platform")]
    [Tooltip("Mesh whose material glows while the platform is active.")]
    [SerializeField] private GameObject _platformMesh;

    [SerializeField] private Color _emissionColor = Color.red;
    [SerializeField, Min(0f)] private float _emissionIntensity = 5f;

    [Tooltip("Seconds for the glow to reach full once the player steps on.")]
    [SerializeField, Min(0.01f)] private float _emissionFadeSeconds = 1f;

    [Header("Runes")]
    [Tooltip("Destinations offered while the player stands on the platform. Each carries its own scene id.")]
    [SerializeField] private EnterRuneTrigger[] _runes;

    [Header("Compass")]
    [Tooltip("Optional marker raised while the platform is active.")]
    [SerializeField] private GameObject _compass;

    [Tooltip("How far the compass rises, in the teleporter's local space.")]
    [SerializeField, Min(0f)] private float _compassLift = 1f;

    [Tooltip("Metres per second the compass travels.")]
    [SerializeField, Min(0f)] private float _compassSpeed = 5f;

    /// <summary>The instance material, so the glow never writes to the shared asset on disk.</summary>
    private Material _material;

    private Vector3 _compassRestLocalPosition;
    private float _emissionProgress;
    private bool _active;
    private bool _departing;

    private void Awake()
    {
        if (_platformMesh == null)
        {
            Debug.LogError($"{name}: no platform mesh assigned, so the teleporter cannot be seen. " +
                           "Assign one or remove the component.", this);
            enabled = false;
            return;
        }

        var targetRenderer = _platformMesh.GetComponent<Renderer>();
        if (targetRenderer == null)
        {
            Debug.LogError($"{name}: '{_platformMesh.name}' has no Renderer to glow.", this);
            enabled = false;
            return;
        }

        _material = targetRenderer.material;
        _material.EnableKeyword("_EMISSION");

        if (_compass != null)
            _compassRestLocalPosition = _compass.transform.localPosition;
    }

    private void OnEnable()
    {
        if (_runes == null)
            return;

        // Each rune reports its own entry; the platform then reads the id off whichever one fired.
        foreach (var rune in _runes)
        {
            if (rune != null)
                rune._triggerAction += OnRuneEntered;
        }
    }

    private void OnDisable()
    {
        if (_runes == null)
            return;

        foreach (var rune in _runes)
        {
            if (rune != null)
                rune._triggerAction -= OnRuneEntered;
        }
    }

    private void Start()
    {
        SetRunesVisible(false);
        ApplyEmission(0f);
    }

    private void Update()
    {
        UpdateEmission();
        UpdateCompass();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_active || !IsPlayer(other))
            return;

        _active = true;
        SetRunesVisible(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_active || _departing || !IsPlayer(other))
            return;

        _active = false;
        SetRunesVisible(false);

        // Without this a rune fires exactly once ever, and the platform is dead on the second visit.
        if (_runes == null)
            return;

        foreach (var rune in _runes)
        {
            if (rune != null)
                rune.ResetTrigger();
        }
    }

    /// <summary>
    /// The player is the only thing carrying an input handler, which is what this project has instead of a
    /// tag on the character prefab.
    /// </summary>
    private static bool IsPlayer(Collider other)
    {
        return other != null && other.GetComponentInParent<InputHandler>() != null;
    }

    private void OnRuneEntered()
    {
        if (_departing)
            return;

        var rune = FirstTriggeredRune();
        if (rune == null)
        {
            Debug.LogWarning($"{name}: a rune fired but none was marked as triggered.", this);
            return;
        }

        var flow = SceneFlow.Instance;
        if (flow == null)
        {
            // Happens when playing a scene without the game root, for example a bare test scene.
            Debug.LogError($"{name}: cannot travel to '{rune.SceneId}' because no SceneFlow exists in this " +
                           "session.", this);
            return;
        }

        _departing = true;

        if (flow.Travel(rune.SceneId))
            return;

        // SceneFlow has already logged why. Staying put is the correct outcome; stay usable as well.
        _departing = false;
        rune.ResetTrigger();
    }

    private EnterRuneTrigger FirstTriggeredRune()
    {
        if (_runes == null)
            return null;

        foreach (var rune in _runes)
        {
            if (rune != null && rune.WasTriggered)
                return rune;
        }

        return null;
    }

    private void SetRunesVisible(bool visible)
    {
        if (_runes == null)
            return;

        foreach (var rune in _runes)
        {
            if (rune != null)
                rune.gameObject.SetActive(visible);
        }
    }

    private void UpdateEmission()
    {
        if (_material == null)
            return;

        var target = _active ? 1f : 0f;
        var step = Time.deltaTime / Mathf.Max(0.01f, _emissionFadeSeconds);

        _emissionProgress = Mathf.MoveTowards(_emissionProgress, target, step);
        ApplyEmission(_emissionProgress);
    }

    private void ApplyEmission(float progress)
    {
        if (_material == null)
            return;

        _material.SetColor("_EmissionColor", _emissionColor * (_emissionIntensity * Mathf.Clamp01(progress)));
    }

    private void UpdateCompass()
    {
        if (_compass == null)
            return;

        // localPosition throughout: the old version moved the local position and then compared against the
        // world position, so the arrival test could never be true and the compass never finished its rise.
        var target = _active
            ? _compassRestLocalPosition + Vector3.up * _compassLift
            : _compassRestLocalPosition;

        _compass.transform.localPosition = Vector3.MoveTowards(
            _compass.transform.localPosition,
            target,
            _compassSpeed * Time.deltaTime);
    }
}

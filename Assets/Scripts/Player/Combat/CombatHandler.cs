using System;
using System.Collections;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class CombatHandler : MonoBehaviour
{
    public float BlendDuration = 0.12f;
    [SerializeField] private string _initialGuard = "VomTag";
    [SerializeField, Min(0.5f)] private float _passTimeoutSeconds = 5f;

    private Animator _animator;
    private CombatGraph _combatGraph;
    private Coroutine _transitionRoutine;
    private string _queuedGuard;
    private int _guardsLayer;
    private int _passesLayer;
    private bool _initialized;

    public event Action<PassPhase> PhaseChanged;
    public PassPhase Phase { get; private set; }

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>(true);
        if (_animator == null)
        {
            Debug.LogError("CombatHandler requires an Animator on this object or a child.", this);
            enabled = false;
            return;
        }

        TargetingSystem.GetOrAddForAnimator(_animator);
    }

    private void Start()
    {
        StartCoroutine(InitializeAfterControllerAssignment());
    }

    private IEnumerator InitializeAfterControllerAssignment()
    {
        // Preserve the previous one-frame delay for any runtime Animator controller assignment.
        yield return null;
        _guardsLayer = _animator.GetLayerIndex("Guards");
        _passesLayer = _animator.GetLayerIndex("Passes");
        if (_guardsLayer < 0 || _passesLayer < 0 ||
            !_animator.HasState(_guardsLayer, Animator.StringToHash("Guards." + _initialGuard)) ||
            !_animator.HasState(_passesLayer, Animator.StringToHash("Passes.Langort")))
        {
            Debug.LogError("CombatHandler needs Guards and Passes layers with the selected guard and Passes.Langort states.", this);
            enabled = false;
            yield break;
        }

        _combatGraph = new CombatGraph(_initialGuard);
        _animator.SetLayerWeight(_guardsLayer, 1f);
        _animator.SetLayerWeight(_passesLayer, 0f);
        _animator.Play("Guards." + _initialGuard, _guardsLayer, 0f);
        _initialized = true;
    }

    private void OnDisable()
    {
        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        _queuedGuard = null;
        if (_initialized && _animator != null)
        {
            _combatGraph?.CancelTransition();
            var guard = _combatGraph != null ? _combatGraph.CurrentGuard : _initialGuard;
            if (_animator.HasState(_guardsLayer, Animator.StringToHash("Guards." + guard)))
                _animator.Play("Guards." + guard, _guardsLayer, 0f);
            _animator.SetLayerWeight(_guardsLayer, 1f);
            _animator.SetLayerWeight(_passesLayer, 0f);
        }

        SetPhase(PassPhase.None);
    }

    public void OnVomTag(InputAction.CallbackContext context) { if (context.started) RequestGuard("VomTag"); }
    public void OnPflug(InputAction.CallbackContext context) { if (context.started) RequestGuard("Pflug"); }
    public void OnAlber(InputAction.CallbackContext context) { if (context.started) RequestGuard("Alber"); }
    public void OnOchs(InputAction.CallbackContext context) { if (context.started) RequestGuard("Ochs"); }
    public void OnIronGate(InputAction.CallbackContext context) { if (context.started) RequestGuard("IronGate"); }

    private void RequestGuard(string guard)
    {
        if (!_initialized || !_animator.HasState(_guardsLayer, Animator.StringToHash("Guards." + guard)))
            return;

        if (_combatGraph.IsTransitioning)
        {
            _queuedGuard = guard;
            return;
        }

        if (_combatGraph.TryBeginTransition(guard, out var transition))
            _transitionRoutine = StartCoroutine(PlayTransition(transition));
    }

    private IEnumerator PlayTransition(GuardTransition transition)
    {
        SetPhase(PassPhase.ToPass);
        _animator.Play("Passes.Langort", _passesLayer, 0f);
        yield return BlendPassLayer(1f);

        SetPhase(PassPhase.Strike);
        var startTime = Time.time;
        while (_animator.GetCurrentAnimatorStateInfo(_passesLayer).shortNameHash != Animator.StringToHash("Langort"))
        {
            if (Time.time - startTime > _passTimeoutSeconds)
            {
                AbortTransition(transition, "Passes.Langort did not become the active state.");
                yield break;
            }
            yield return null;
        }

        while (_animator.GetCurrentAnimatorStateInfo(_passesLayer).normalizedTime < 1f)
        {
            if (Time.time - startTime > _passTimeoutSeconds)
            {
                AbortTransition(transition, "Passes.Langort exceeded its timeout.");
                yield break;
            }
            yield return null;
        }

        SetPhase(PassPhase.Settle);
        _animator.Play("Guards." + transition.To, _guardsLayer, 0f);
        yield return BlendPassLayer(0f);

        if (!_combatGraph.CompleteTransition(transition))
        {
            AbortTransition(transition, "Combat graph rejected the completed transition.");
            yield break;
        }

        SetPhase(PassPhase.None);
        _transitionRoutine = null;
        var queued = _queuedGuard;
        _queuedGuard = null;
        if (!string.IsNullOrEmpty(queued))
            RequestGuard(queued);
    }

    private IEnumerator BlendPassLayer(float targetWeight)
    {
        var startWeight = _animator.GetLayerWeight(_passesLayer);
        if (BlendDuration <= 0f)
        {
            _animator.SetLayerWeight(_passesLayer, targetWeight);
            yield break;
        }

        var elapsed = 0f;
        while (elapsed < BlendDuration)
        {
            elapsed += Time.deltaTime;
            _animator.SetLayerWeight(_passesLayer, Mathf.Lerp(startWeight, targetWeight, elapsed / BlendDuration));
            yield return null;
        }
        _animator.SetLayerWeight(_passesLayer, targetWeight);
    }

    private void AbortTransition(GuardTransition transition, string reason)
    {
        Debug.LogWarning("Guard transition was cancelled: " + reason, this);
        _combatGraph.CancelTransition();
        _queuedGuard = null;
        _animator.SetLayerWeight(_passesLayer, 0f);
        _animator.SetLayerWeight(_guardsLayer, 1f);
        _animator.Play("Guards." + transition.From, _guardsLayer, 0f);
        _transitionRoutine = null;
        SetPhase(PassPhase.None);
    }

    private void SetPhase(PassPhase phase)
    {
        if (Phase == phase)
            return;
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}

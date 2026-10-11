using System.Collections;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TargetingSystem))]
public sealed class CombatHandler : MonoBehaviour
{
    private const float BlendDuration = 0.12f;

    [SerializeField] private Animator _animator;
    [SerializeField] private string _initialGuard = "VomTag";
    [SerializeField, Min(0f)] private float _langortForwardDistance = 0.75f;

    private int _passLayer = -1;
    private CombatGraph _combatGraph;
    private Movement_V3 _movement;
    private Coroutine _transitionRoutine;
    private string _queuedGuard;

    private static readonly int LangortState = Animator.StringToHash("Langort");

    private IEnumerator Start()
    {
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>(true);

        if (_animator == null)
        {
            Debug.LogError("CombatHandler could not find an Animator on this character or its children.", this);
            enabled = false;
            yield break;
        }

        _movement = GetComponent<Movement_V3>();
        if (_movement == null)
            _movement = GetComponentInParent<Movement_V3>();
        if (_movement == null)
            _movement = GetComponentInChildren<Movement_V3>(true);

        // Movement_V3 assigns the shared controller from Start; resolve the layer afterward.
        yield return null;

        _passLayer = _animator.GetLayerIndex("Passes");
        if (_passLayer < 0)
        {
            Debug.LogError("CombatHandler could not find the Passes Animator layer.", this);
            enabled = false;
            yield break;
        }

        _combatGraph = new CombatGraph(_initialGuard);
    }

    private void OnDisable()
    {
        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        _transitionRoutine = null;
        _queuedGuard = null;
        _combatGraph?.CancelTransition();
    }

    public void OnVomTag(InputAction.CallbackContext context)
    {
        if (context.started) RequestGuard("VomTag");
    }

    public void OnAlber(InputAction.CallbackContext context)
    {
        if (context.started) RequestGuard("Alber");
    }

    public void OnPflug(InputAction.CallbackContext context)
    {
        if (context.started) RequestGuard("Pflug");
    }

    public void OnOchs(InputAction.CallbackContext context)
    {
        if (context.started) RequestGuard("Ochs");
    }

    public void OnIronGate(InputAction.CallbackContext context)
    {
        if (context.started) RequestGuard("IronGate");
    }

    private void RequestGuard(string destination)
    {
        if (_combatGraph == null)
            return;

        if (_combatGraph.IsTransitioning)
        {
            _queuedGuard = destination;
            return;
        }

        BeginTransition(destination);
    }

    private void BeginTransition(string destination)
    {
        if (_combatGraph.TryBeginTransition(destination, out var transition))
            _transitionRoutine = StartCoroutine(PlayTransition(transition));
    }

    private IEnumerator PlayTransition(GuardTransition transition)
    {
        var passPath = Animator.StringToHash($"Passes.{transition.PassThrough}");
        _animator.CrossFadeInFixedTime(passPath, BlendDuration, _passLayer);

        // Time a scripted forward step to the Langort clip while the Base Layer keeps locomotion running.
        AnimatorStateInfo state;
        do
        {
            yield return null;
            state = _animator.GetCurrentAnimatorStateInfo(_passLayer);
        }
        while (state.shortNameHash != LangortState);

        _movement?.AdvanceDuringGuardTransition(_langortForwardDistance, state.length);

        // Wait for the Langort pass to finish. This uses Animator state time, not an Animation Event.
        while (true)
        {
            state = _animator.GetCurrentAnimatorStateInfo(_passLayer);
            if (state.shortNameHash == LangortState &&
                !_animator.IsInTransition(_passLayer) &&
                state.normalizedTime >= 1f)
            {
                break;
            }

            yield return null;
        }

        var destinationPath = Animator.StringToHash($"Passes.{transition.To}");
        _animator.CrossFadeInFixedTime(destinationPath, BlendDuration, _passLayer);
        yield return new WaitForSeconds(BlendDuration);

        _combatGraph.CompleteTransition(transition);
        _transitionRoutine = null;

        if (!string.IsNullOrEmpty(_queuedGuard))
        {
            var queuedGuard = _queuedGuard;
            _queuedGuard = null;
            BeginTransition(queuedGuard);
        }
    }

}

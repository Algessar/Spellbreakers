using System;
using Spellbreakers.World;
using UnityEngine;

namespace Spellbreakers.Gameplay
{
    /// <summary>
    /// Applies the consequences of <see cref="GameState"/>: frozen time, and a single answer to the
    /// question "should the player's input do anything right now".
    ///
    /// The rules themselves are in <see cref="GameStateMachine"/>, which needs no scene to test. This is
    /// the thin engine half: it owns the one <c>Time.timeScale</c> write in the project and the one place
    /// that restores it.
    ///
    /// Restoring the time scale matters more than it looks. Until now nothing in the project wrote
    /// <c>Time.timeScale</c> at all, so a pause that forgot its own undo would leave the editor at zero
    /// after a stop, and the next play session would appear frozen before a single script ran.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class GameStateService : MonoBehaviour
    {
        /// <summary>The service in play, or null before <see cref="GameRoot"/> has built one.</summary>
        public static GameStateService Instance { get; private set; }

        [Header("Diagnostics")]
        [Tooltip("Log every accepted and refused state change. Noisy; for tracking down a stuck pause.")]
        [SerializeField] private bool _logTransitions;

        private readonly GameStateMachine _machine = new GameStateMachine();

        /// <summary>The scale to return to when the freeze is lifted. Captured on the way in.</summary>
        private float _unfrozenTimeScale = 1f;

        private bool _timeFrozen;

        public GameState Current => _machine.Current;

        /// <summary>True while a menu owns the game. Only <see cref="GameState.Paused"/> freezes.</summary>
        public bool IsTimeFrozen => GameStateMachine.FreezesTime(Current);

        /// <summary>True whenever play input should be ignored, which is every state but playing.</summary>
        public bool BlocksPlayerInput => GameStateMachine.BlocksPlayerInput(Current);

        /// <summary>Raised after an accepted transition. Refused requests are silent on purpose.</summary>
        public event Action<GameState, GameState> Changed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[GameState] '{name}' is a second GameStateService; '{Instance.name}' already " +
                                 "owns the state, so this one is removed.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _machine.Changed += OnStateChanged;
        }

        private void OnDestroy()
        {
            _machine.Changed -= OnStateChanged;

            // A scene torn down while paused must not leave the editor frozen.
            RestoreTimeScale();

            if (Instance == this)
                Instance = null;
        }

        // --- Transitions ---------------------------------------------------------

        /// <summary>Enters play from boot or the title, or resumes from a pause.</summary>
        public bool StartGame() => TrySet(GameState.Playing);

        public bool Pause() => TrySet(GameState.Paused);

        public bool Resume() => TrySet(GameState.Playing);

        public bool ShowTitle() => TrySet(GameState.Title);

        /// <summary>Between scenes. Time keeps running so the fade and the load can finish.</summary>
        public bool BeginTransition() => TrySet(GameState.Transition);

        public bool EndTransition() => TrySet(GameState.Playing);

        public bool EnterDead() => TrySet(GameState.Dead);

        /// <summary>Brings the player back after a checkpoint respawn.</summary>
        public bool Revive() => TrySet(GameState.Playing);

        /// <summary>Asks for a state, honouring the transition table. False means nothing happened.</summary>
        public bool TrySet(GameState next)
        {
            if (_machine.TrySet(next))
                return true;

            if (_logTransitions)
            {
                Debug.Log(
                    $"[GameState] Refused {next}: {_machine.DescribeRefusal(next)} " +
                    $"(timeScale {Time.timeScale:F2}).", this);
            }

            return false;
        }

        /// <summary>
        /// Forces a state without consulting the table, for the first decision after boot and for loading
        /// a save. Gameplay must not call this; it has <see cref="TrySet"/>.
        ///
        /// Delegates the "did anything change" decision to the machine, so a forced state that happens to
        /// equal the current one raises nothing. The side effects follow the machine's own event, which is
        /// what keeps a forced transition and a legal one indistinguishable to every subscriber.
        /// </summary>
        public void ResetTo(GameState state)
        {
            _machine.Reset(state);
        }

        // --- Effects -------------------------------------------------------------

        private void OnStateChanged(GameState from, GameState to)
        {
            ApplyTimeScale(GameStateMachine.FreezesTime(to));

            if (_logTransitions && from != to)
                Debug.Log($"[GameState] {from} -> {to}", this);

            Changed?.Invoke(from, to);
        }

        private void ApplyTimeScale(bool freeze)
        {
            if (freeze)
            {
                if (_timeFrozen)
                    return;

                _unfrozenTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                _timeFrozen = true;
                return;
            }

            if (!_timeFrozen)
                return;

            Time.timeScale = _unfrozenTimeScale;
            _timeFrozen = false;
        }

        private void RestoreTimeScale()
        {
            if (_timeFrozen)
                Time.timeScale = _unfrozenTimeScale;

            _timeFrozen = false;
        }
    }
}

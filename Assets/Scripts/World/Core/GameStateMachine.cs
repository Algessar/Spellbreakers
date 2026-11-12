using System;

namespace Spellbreakers.World
{
    /// <summary>
    /// Holds <see cref="GameState"/> and decides which moves between states are legal.
    ///
    /// The rules live here rather than in the MonoBehaviour that drives them for the same reason the
    /// guard table lives in <c>Spellbreakers.Core</c>: they are the part that is easy to get subtly wrong
    /// and cheap to test, and a scene is not required to check them. The engine-facing half
    /// (<c>GameStateService</c>) only applies the consequences of a state - freezing time, gating input.
    ///
    /// A refused transition is not an exception. Pause presses arrive twice, scene loads race a quit to
    /// the title, and a death can land on the same frame as a transition; those are ordinary, and the
    /// caller is told no rather than being handed an error.
    /// </summary>
    public sealed class GameStateMachine
    {
        /// <summary>Every game starts before it has decided anything.</summary>
        public GameState Current { get; private set; } = GameState.Boot;

        /// <summary>Raised after a transition has been applied, with (previous, current).</summary>
        public event Action<GameState, GameState> Changed;

        /// <summary>
        /// Attempts a legal transition. Returns false when it is not allowed, leaving the state alone.
        /// Asking for the state that is already current also returns false: nothing happened, and callers
        /// rely on that to avoid re-running side effects such as a scene load.
        /// </summary>
        public bool TrySet(GameState next)
        {
            if (next == Current || !CanTransition(Current, next))
                return false;

            Apply(next);
            return true;
        }

        /// <summary>
        /// Puts the machine in a state without consulting the table.
        ///
        /// For the two cases the table cannot describe: the very first decision after boot, and loading a
        /// save straight into a state the player did not walk to. Deliberately not called by gameplay.
        /// </summary>
        public void Reset(GameState state)
        {
            if (state == Current)
                return;

            Apply(state);
        }

        private void Apply(GameState next)
        {
            var previous = Current;
            Current = next;
            Changed?.Invoke(previous, next);
        }

        /// <summary>
        /// Whether time should be frozen in this state.
        ///
        /// Only <see cref="GameState.Paused"/> freezes. A transition must not: the fade and the async load
        /// both need frames, and a scene loaded with <c>Time.timeScale</c> at zero arrives half-initialised.
        /// </summary>
        public static bool FreezesTime(GameState state) => state == GameState.Paused;

        /// <summary>
        /// Whether the player's actions should be ignored. Everything except playing counts, including
        /// <see cref="GameState.Transition"/>, so a held movement key cannot walk the character off the
        /// spawn point of the scene that is still fading in.
        /// </summary>
        public static bool BlocksPlayerInput(GameState state) => state != GameState.Playing;

        /// <summary>
        /// The legal moves. Written as one table so the shape of the game is readable in one place.
        ///
        /// The asymmetries are intentional: <see cref="GameState.Paused"/> cannot go straight to
        /// <see cref="GameState.Dead"/> (a corpse cannot open a menu), and nothing returns to
        /// <see cref="GameState.Boot"/>, which exists only to give the first frame a defined state.
        /// </summary>
        public static bool CanTransition(GameState from, GameState to)
        {
            if (from == to)
                return false;

            return from switch
            {
                GameState.Boot => to is GameState.Title or GameState.Playing,

                GameState.Title => to is GameState.Playing,

                GameState.Playing => to is GameState.Paused
                    or GameState.Transition
                    or GameState.Dead
                    or GameState.Title,

                GameState.Paused => to is GameState.Playing or GameState.Title,

                // A transition ends in play, or is abandoned back to the title if the load failed.
                GameState.Transition => to is GameState.Playing or GameState.Title,

                // Death is a state, not the end: the checkpoint brings the player back into play.
                GameState.Dead => to is GameState.Playing or GameState.Title,

                _ => false,
            };
        }

        /// <summary>A readable reason for a refusal, for the log line that explains a dropped request.</summary>
        public string DescribeRefusal(GameState next)
        {
            return next == Current
                ? $"already in {Current}"
                : $"cannot go from {Current} to {next}";
        }
    }
}

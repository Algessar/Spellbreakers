namespace Spellbreakers.World
{
    /// <summary>
    /// The coarse state the game as a whole is in.
    ///
    /// This is deliberately not a combat state and not a UI state. Combat already owns guard states, and
    /// a menu owns its own open/closed flag; what was missing was the one thing both have to agree on,
    /// which is whether the world is being played, held still, or in the middle of swapping scenes.
    ///
    /// The distinction that matters is between the two ways of "not playing":
    /// <see cref="Paused"/> freezes time, because the player is reading a menu and the fight behind it
    /// must not continue; <see cref="Transition"/> lets time run, because a fade or a load needs frames,
    /// but player input is already meaningless while the next scene comes up.
    /// </summary>
    public enum GameState
    {
        /// <summary>Before anything has been decided. The only state that is not reachable again.</summary>
        Boot,

        /// <summary>Title screen. No gameplay scene needs to be loaded yet.</summary>
        Title,

        /// <summary>The game is being played. The only state in which player input is honoured.</summary>
        Playing,

        /// <summary>A menu is open. Time is frozen so the world cannot move behind it.</summary>
        Paused,

        /// <summary>Between scenes. Time runs so the fade and the load can complete; input is blocked.</summary>
        Transition,

        /// <summary>The player is down and waiting to be restored at a checkpoint.</summary>
        Dead,
    }
}

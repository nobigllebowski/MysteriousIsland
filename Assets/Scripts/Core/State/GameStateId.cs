namespace ForgottenIsle.Core.State
{
    /// <summary>
    /// The mutually exclusive top-level modes the application can be in.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> and explicitly numbered because these values are written into
    /// save headers and crash breadcrumbs; renumbering them later would silently reinterpret old
    /// data. Add new states at the end, never in the middle.
    /// </remarks>
    public enum GameStateId : byte
    {
        /// <summary>Process is starting; services are being constructed. No scene is playable.</summary>
        Boot = 0,

        /// <summary>Title and slot selection. No session exists.</summary>
        MainMenu = 1,

        /// <summary>Behind the loading curtain (ADR-0004): a zone is being brought in or swapped.</summary>
        Loading = 2,

        /// <summary>A session exists and the simulation is advancing.</summary>
        InGame = 3,

        /// <summary>A session exists and the simulation is frozen. Saving is allowed here.</summary>
        Paused = 4,

        /// <summary>A load failed. Terminal for the attempt; the only way out is back to the menu.</summary>
        LoadFailed = 5
    }
}

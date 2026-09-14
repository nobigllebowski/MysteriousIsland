namespace ForgottenIsle.Core.Commands
{
    /// <summary>
    /// Begins a fresh run and binds it to a save slot. The slot is chosen up front, rather than at
    /// the first save, so that autosave never has to ask the player where to put a run in progress.
    /// </summary>
    public readonly struct StartNewGameCommand : ICommand
    {
        /// <summary>Zero-based save slot index this run will own.</summary>
        public readonly int Slot;

        /// <summary>Creates the command for <paramref name="slot"/>.</summary>
        public StartNewGameCommand(int slot)
        {
            Slot = slot;
        }
    }

    /// <summary>
    /// Writes the current session to a save slot.
    /// </summary>
    /// <remarks>
    /// The slot is explicit rather than implied by the session, because the same run can be
    /// written to a different slot (a manual "save as" branch) without the session itself
    /// re-binding to it.
    /// </remarks>
    public readonly struct SaveGameCommand : ICommand
    {
        /// <summary>Zero-based save slot index to write into.</summary>
        public readonly int Slot;

        /// <summary>Creates the command for <paramref name="slot"/>.</summary>
        public SaveGameCommand(int slot)
        {
            Slot = slot;
        }
    }

    /// <summary>
    /// Tears the run down and returns to the main menu.
    /// </summary>
    /// <remarks>
    /// Carries no payload and no "save first" flag on purpose: whether quitting implies a save is
    /// a policy decision that belongs to the handler and the confirmation UI, not to the intent.
    /// </remarks>
    public readonly struct QuitToMenuCommand : ICommand
    {
    }

    /// <summary>
    /// Moves the player to another zone, through the loading curtain (ADR-0004).
    /// </summary>
    public readonly struct TravelToZoneCommand : ICommand
    {
        /// <summary>Id of the destination zone, as authored in the zone catalogue.</summary>
        public readonly string ZoneId;

        /// <summary>Creates the command for <paramref name="zoneId"/>.</summary>
        public TravelToZoneCommand(string zoneId)
        {
            ZoneId = zoneId;
        }
    }
}

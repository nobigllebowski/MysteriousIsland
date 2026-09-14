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
    /// Resumes the run stored in a save slot — the main menu's "Continue".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="StartNewGameCommand"/> rather than a flag on it, because the two
    /// intents disagree about what the slot means: for a new game the slot is a destination that
    /// may legitimately be empty and is about to be overwritten, and for a resume it is a source
    /// that must already hold a readable save. One command carrying a boolean would make that
    /// difference invisible at the call site and would give the handler two validation contracts.
    /// </para>
    /// <para>
    /// The slot may be a manual slot or an autosave ring slot: a player continuing after a crash is
    /// resuming the newest autosave far more often than a slot they chose by hand.
    /// </para>
    /// </remarks>
    public readonly struct ResumeSavedRunCommand : ICommand
    {
        /// <summary>Save slot index to resume from, manual or autosave.</summary>
        public readonly int Slot;

        /// <summary>Creates the command for <paramref name="slot"/>.</summary>
        public ResumeSavedRunCommand(int slot)
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
    
    /// <summary>Read an ancient marker. Records it and shows its line of text.</summary>
    /// <remarks>
    /// Separate from <see cref="CollectCommand"/> because the two have different rules: a marker
    /// can be re-read forever and never leaves the world, while a discovery is taken exactly once
    /// and then gone. Collapsing them into one "interact" command would push that difference into
    /// a branch inside the handler, where it is invisible to the validator.
    /// </remarks>
    public readonly struct InspectCommand : ICommand
    {
        /// <summary>A <c>ContentIds</c> marker id.</summary>
        public readonly string MarkerId;

        /// <param name="markerId">A <c>ContentIds</c> marker id.</param>
        public InspectCommand(string markerId)
        {
            MarkerId = markerId;
        }
    }

    /// <summary>Take a discovery. Removes it from the world and records it permanently.</summary>
    public readonly struct CollectCommand : ICommand
    {
        /// <summary>A <c>ContentIds</c> discovery id.</summary>
        public readonly string DiscoveryId;

        /// <param name="discoveryId">A <c>ContentIds</c> discovery id.</param>
        public CollectCommand(string discoveryId)
        {
            DiscoveryId = discoveryId;
        }
    }
}
}

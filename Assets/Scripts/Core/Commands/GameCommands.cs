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

        /// <summary>
        /// A <c>ContentIds</c> remark id whose line is said instead of the marker's own, or null.
        /// The hull line's timed fallback: Nadia says it unprompted, in different words, and the
        /// notebook entry still writes itself.
        /// </summary>
        public readonly string SaidAs;

        /// <param name="markerId">A <c>ContentIds</c> marker id.</param>
        /// <param name="saidAs">A remark id said instead of the marker's line, or null for the marker's own.</param>
        public InspectCommand(string markerId, string saidAs = null)
        {
            MarkerId = markerId;
            SaidAs = saidAs;
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

    /// <summary>Put a found object in the player's hands.</summary>
    public readonly struct TakeItemCommand : ICommand
    {
        /// <summary>An <c>ItemIds</c> id.</summary>
        public readonly string ItemId;

        /// <param name="itemId">An <c>ItemIds</c> id.</param>
        public TakeItemCommand(string itemId)
        {
            ItemId = itemId;
        }
    }

    /// <summary>Put two carried items together.</summary>
    /// <remarks>
    /// Refused rather than ignored when the two do not go together, because "nothing happened" is
    /// indistinguishable from a broken button. The handler answers with a line of narration saying
    /// what was tried, which is the only feedback an adventure game owes a wrong guess.
    /// </remarks>
    public readonly struct CombineItemsCommand : ICommand
    {
        /// <summary>One carried item.</summary>
        public readonly string First;

        /// <summary>The other carried item.</summary>
        public readonly string Second;

        /// <param name="first">One carried item.</param>
        /// <param name="second">The other carried item.</param>
        public CombineItemsCommand(string first, string second)
        {
            First = first;
            Second = second;
        }
    }

    /// <summary>Use a carried item on something in the world.</summary>
    public readonly struct UseItemCommand : ICommand
    {
        /// <summary>The carried item being applied.</summary>
        public readonly string ItemId;

        /// <summary>A <c>ContentIds</c> id for the thing it is being used on.</summary>
        public readonly string TargetId;

        /// <param name="itemId">The carried item being applied.</param>
        /// <param name="targetId">What it is being used on.</param>
        public UseItemCommand(string itemId, string targetId)
        {
            ItemId = itemId;
            TargetId = targetId;
        }
    }

    /// <summary>Interact with the radio: diagnose it when broken, open its dial when working.</summary>
    /// <remarks>
    /// One command for both, because from the player's side it is one act -- reaching for the set.
    /// What that act means is the handler's decision, taken from the set's state, not the UI's.
    /// </remarks>
    public readonly struct OpenRadioCommand : ICommand
    {
    }

    /// <summary>Put the radio down: close the dial.</summary>
    public readonly struct CloseRadioCommand : ICommand
    {
    }

    /// <summary>Move the needle.</summary>
    public readonly struct TuneRadioCommand : ICommand
    {
        /// <summary>Requested needle position, in MHz. Clamped to the band by the handler.</summary>
        public readonly float Mhz;

        /// <param name="mhz">Requested needle position.</param>
        public TuneRadioCommand(float mhz)
        {
            Mhz = mhz;
        }
    }

    /// <summary>
    /// Leave the set on and let it sweep the band by itself (hint tier 4, §3.5).
    /// </summary>
    /// <remarks>
    /// The safety net: Nadia sets the radio down and the needle crawls across the band on its own
    /// until a hand touches the dial or the signal locks. Opens the dial if it is not open, so the
    /// spectrogram is live while it sweeps. Refused once the transmission has been received: there
    /// is nothing left to sweep for.
    /// </remarks>
    public readonly struct BeginSweepCommand : ICommand
    {
    }

    /// <summary>Advance the self-sweeping needle by this much play time.</summary>
    /// <remarks>
    /// Dispatched by the hint director on every tick while the set sweeps, so the needle moves
    /// through the same handler path a thumb does and a lock on the way is heard and narrated
    /// exactly as a hand-tuned one is.
    /// </remarks>
    public readonly struct SweepRadioCommand : ICommand
    {
        /// <summary>Seconds of play since the last sweep step.</summary>
        public readonly float Seconds;

        /// <param name="seconds">Seconds of play since the last step.</param>
        public SweepRadioCommand(float seconds)
        {
            Seconds = seconds;
        }
    }

    /// <summary>
    /// Nadia carries whatever fire kit is laid in the open into the lee of the hull and sets it
    /// down there. The fire hints' last rung (§7:10 FAILURE, seven blow-outs). The player lights it.
    /// </summary>
    public readonly struct CarryFireKitCommand : ICommand
    {
    }

    /// <summary>Squeeze the hand-mic and speak.</summary>
    /// <remarks>
    /// A real verb that will matter again. The world will still not give the player what they
    /// want: the answer is hiss, every time, and the game lets them try anyway.
    /// </remarks>
    public readonly struct SqueezeMicCommand : ICommand
    {
    }

    /// <summary>Hold an item up for use on whatever comes next, or put it down.</summary>
    /// <remarks>
    /// The aimed use is game state, not a HUD selection: once an item is held, the prompt reads
    /// USE <item> and a press -- key, button or the prompt card -- uses it on the target. Held
    /// state is transient: it is not saved and a new run or a load puts everything down.
    /// </remarks>
    public readonly struct HoldItemCommand : ICommand
    {
        /// <summary>The item to hold, or empty to put down whatever is held.</summary>
        public readonly string ItemId;

        /// <param name="itemId">The item to hold, or empty to release.</param>
        public HoldItemCommand(string itemId)
        {
            ItemId = itemId ?? string.Empty;
        }
    }

    /// <summary>Nadia says something about what the player just did. Records nothing.</summary>
    /// <remarks>
    /// The partial hull line is the first: aligned from the wrong hull, three hulls chalk in and
    /// she says "Try the far end." That is a hint, not a discovery, so it must not credit the
    /// marker -- but it is still a thing the world says in answer to an act, and it goes through
    /// the dispatcher like every other answer so it is validated, logged and refused outside
    /// gameplay. Whether a remark repeats is the remarking object's business; the handler is
    /// stateless. (ADR-0024)
    /// </remarks>
    public readonly struct RemarkCommand : ICommand
    {
        /// <summary>A <c>ContentIds</c> remark id. The narration key is "narration." + this.</summary>
        public readonly string RemarkId;

        /// <param name="remarkId">A <c>ContentIds</c> remark id.</param>
        public RemarkCommand(string remarkId)
        {
            RemarkId = remarkId;
        }
    }
}

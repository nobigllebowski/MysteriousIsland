// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Signals/TickCompletedSignal.cs
// and GameSpeedChangedSignal.cs.
// Adapted for Vardholm: Nation's calendar/economy payloads dropped entirely; the Phase 1 signal set is
// collapsed into one file and retargeted at state, zone, save and sim-hour events.

using ForgottenIsle.Core.State;

namespace ForgottenIsle.Core.Signals
{
    /// <summary>
    /// Published after the game state machine has completed a transition.
    /// </summary>
    /// <remarks>
    /// Fired AFTER the move, never before: subscribers reading the machine will see
    /// <see cref="To"/> as the current state. <see cref="From"/> is carried because several listeners
    /// react to the edge rather than the destination — the pause overlay only animates out on
    /// Paused to InGame, not on every arrival at InGame.
    /// </remarks>
    public readonly struct GameStateChangedSignal : ISignal
    {
        /// <summary>The state the machine left.</summary>
        public GameStateId From { get; }

        /// <summary>The state the machine is now in.</summary>
        public GameStateId To { get; }

        public GameStateChangedSignal(GameStateId from, GameStateId to)
        {
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// Published when the player's current zone changes and the new zone is resident and ready.
    /// </summary>
    /// <remarks>
    /// Deliberately carries the zone ID only, not a display name: the ID is the stable data key, and any
    /// listener that needs a label resolves it through <c>ILocalizedText</c> so it re-resolves correctly
    /// after a locale change.
    /// </remarks>
    public readonly struct ZoneChangedSignal : ISignal
    {
        /// <summary>The data-catalog identifier of the zone now occupied, for example <c>zone.ribcage</c>.</summary>
        public string ZoneId { get; }

        public ZoneChangedSignal(string zoneId)
        {
            ZoneId = zoneId;
        }
    }

    /// <summary>
    /// Published once a save has been fully written and renamed into place.
    /// </summary>
    /// <remarks>
    /// Fired only on success and only after the atomic rename, so a listener that shows a "saved"
    /// confirmation can never promise the player a save that did not survive a write failure.
    /// </remarks>
    public readonly struct GameSavedSignal : ISignal
    {
        /// <summary>The slot index that was written.</summary>
        public int Slot { get; }

        public GameSavedSignal(int slot)
        {
            Slot = slot;
        }
    }

    /// <summary>
    /// Published at the end of every simulation tick.
    /// </summary>
    /// <remarks>
    /// A single frame can run several ticks when the scheduler catches up after a hitch, so this fires
    /// once per TICK and not once per frame. Listeners that only need to refresh a display should read
    /// state on their own schedule rather than doing work in every one of a burst of handlers.
    /// </remarks>
    public readonly struct TickCompletedSignal : ISignal
    {
        /// <summary>Total in-fiction hours elapsed since the game began, as of the end of this tick.</summary>
        public double SimHours { get; }

        public TickCompletedSignal(double simHours)
        {
            SimHours = simHours;
        }
    }

    /// <summary>Progress changed: something was inspected, collected, or a zone opened.</summary>
    /// <remarks>
    /// Carries no payload beyond what changed, because every listener re-reads the authoritative
    /// <c>WorldProgress</c> anyway. Putting the whole progress object in the signal would hand
    /// listeners a mutable reference to state only one service is allowed to change.
    /// </remarks>
    public readonly struct ProgressChangedSignal : ISignal
    {
        /// <summary>The content id that changed.</summary>
        public string ContentId { get; }

        /// <summary>What kind of change it was.</summary>
        public ProgressChangeKind Kind { get; }

        /// <param name="contentId">The content id that changed.</param>
        /// <param name="kind">What kind of change it was.</param>
        public ProgressChangedSignal(string contentId, ProgressChangeKind kind)
        {
            ContentId = contentId;
            Kind = kind;
        }
    }

    /// <summary>What a <see cref="ProgressChangedSignal"/> describes.</summary>
    public enum ProgressChangeKind : byte
    {
        /// <summary>A marker was read for the first time.</summary>
        Inspected = 0,

        /// <summary>A discovery was taken.</summary>
        Collected = 1,

        /// <summary>A zone became travellable.</summary>
        ZoneUnlocked = 2,

        /// <summary>Progress was wiped or replaced wholesale (new game, or a save was loaded).</summary>
        Replaced = 3
    }

    /// <summary>The objective line changed.</summary>
    public readonly struct ObjectiveChangedSignal : ISignal
    {
        /// <summary>Localization key of the new objective. Empty means "show nothing".</summary>
        public string ObjectiveKey { get; }

        /// <param name="objectiveKey">Localization key of the new objective.</param>
        public ObjectiveChangedSignal(string objectiveKey)
        {
            ObjectiveKey = objectiveKey;
        }
    }

    /// <summary>What the player is currently close enough to interact with.</summary>
    /// <remarks>
    /// This is how gameplay tells the HUD to show a prompt without touching a VisualElement. The
    /// interaction system publishes it; the HUD controller listens. Neither knows the other exists.
    /// </remarks>
    public readonly struct InteractionTargetChangedSignal : ISignal
    {
        /// <summary>Localization key naming the object. Empty when nothing is in range.</summary>
        public string NameKey { get; }

        /// <summary>Localization key of the verb -- INSPECT, TAKE, TRAVEL. Empty when none.</summary>
        public string PromptKey { get; }

        /// <summary>False when the player has walked out of range of everything.</summary>
        public bool HasTarget { get; }

        /// <param name="nameKey">Localization key naming the object.</param>
        /// <param name="promptKey">Localization key of the verb.</param>
        /// <param name="hasTarget">Whether anything is in range.</param>
        public InteractionTargetChangedSignal(string nameKey, string promptKey, bool hasTarget)
        {
            NameKey = nameKey;
            PromptKey = promptKey;
            HasTarget = hasTarget;
        }
    }

    /// <summary>A line of atmospheric text to show the player, from an inspection or a pickup.</summary>
    public readonly struct NarrationSignal : ISignal
    {
        /// <summary>Localization key of the line.</summary>
        public string LineKey { get; }

        /// <param name="lineKey">Localization key of the line.</param>
        public NarrationSignal(string lineKey)
        {
            LineKey = lineKey;
        }
    }
}

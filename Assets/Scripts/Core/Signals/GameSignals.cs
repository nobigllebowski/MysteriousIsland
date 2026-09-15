// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Signals/TickCompletedSignal.cs
// and GameSpeedChangedSignal.cs.
// Adapted for Vardholm: Nation's calendar/economy payloads dropped entirely; the Phase 1 signal set is
// collapsed into one file and retargeted at state, zone, save and sim-hour events.

using System.Collections.Generic;
using ForgottenIsle.Core.Radio;
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

    /// <summary>What changed in the player's hands.</summary>
    public enum InventoryChangeKind : byte
    {
        /// <summary>An item was picked up.</summary>
        Added = 0,

        /// <summary>An item left the inventory, consumed by a combination or a use.</summary>
        Removed = 1,

        /// <summary>The whole inventory was replaced — a load, or a new run.</summary>
        Replaced = 2
    }

    /// <summary>Raised whenever the inventory changes.</summary>
    /// <remarks>
    /// It carries a SNAPSHOT of the whole inventory, not just the item that moved, and that is a
    /// requirement of the architecture rather than convenience. The panel that renders this lives
    /// in the UI assembly, and ADR-0002 forbids the UI from touching a service — so "re-read the
    /// inventory" is not something the listener is allowed to do. Either the list travels in the
    /// signal or the guarantee is a comment.
    /// <para>
    /// The list is a copy taken at publish time. Handing out the live collection would be a
    /// reference the UI could hold across a change, which is the same leak in a slower form.
    /// </para>
    /// </remarks>
    public readonly struct InventoryChangedSignal : ISignal
    {
        /// <summary>What happened.</summary>
        public readonly InventoryChangeKind Kind;

        /// <summary>The item involved. Empty for <see cref="InventoryChangeKind.Replaced"/>.</summary>
        public readonly string ItemId;

        /// <summary>Everything carried, in the order it was found. Never null.</summary>
        public readonly IReadOnlyList<string> Items;

        /// <param name="kind">What happened.</param>
        /// <param name="itemId">The item involved.</param>
        /// <param name="items">Snapshot of everything carried. Null is read as empty.</param>
        public InventoryChangedSignal(InventoryChangeKind kind, string itemId, IReadOnlyList<string> items)
        {
            Kind = kind;
            ItemId = itemId;
            Items = items ?? System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// Several lines of narration, in order, to be shown one after another.
    /// </summary>
    /// <remarks>
    /// The GAME decides what is said and in what order; the HUD decides only how long each line
    /// stays up. A transmission is content, and content is not the controller's to compose — the
    /// first version had the HUD assembling the transmission from key names, which put a story beat
    /// in the UI assembly where no test of the game could see it.
    /// </remarks>
    public readonly struct NarrationSequenceSignal : ISignal
    {
        /// <summary>Localization keys, in order. Never null.</summary>
        public readonly IReadOnlyList<string> LineKeys;

        /// <param name="lineKeys">Localization keys, in order. Null is read as empty.</param>
        public NarrationSequenceSignal(IReadOnlyList<string> lineKeys)
        {
            LineKeys = lineKeys ?? System.Array.Empty<string>();
        }
    }

    /// <summary>What the radio just did.</summary>
    public enum RadioChangeKind : byte
    {
        /// <summary>The set was inspected while broken. <see cref="RadioChangedSignal.LineKey"/> carries the diagnosis.</summary>
        Diagnosed = 0,

        /// <summary>A fault was cleared. <see cref="RadioChangedSignal.LineKey"/> carries the line for it.</summary>
        Repaired = 1,

        /// <summary>The last fault was cleared and the set powered up.</summary>
        PoweredUp = 2,

        /// <summary>The dial is open on screen.</summary>
        Opened = 3,

        /// <summary>The dial was closed.</summary>
        Closed = 4,

        /// <summary>The needle moved. Frequency and reception fields are current.</summary>
        Tuned = 5,

        /// <summary>The needle locked onto a station for the first time. <see cref="RadioChangedSignal.StationId"/> says which.</summary>
        Heard = 6,

        /// <summary>The mic was squeezed. <see cref="RadioChangedSignal.LineKey"/> is what was said.</summary>
        MicSqueezed = 7
    }

    /// <summary>Raised by the radio handlers for every change the HUD needs to reflect.</summary>
    /// <remarks>
    /// Carries the full tuning state on every publish rather than just what changed, for the
    /// same reason the inventory signal carries a snapshot: the panel that renders this is UI and
    /// may not read the service.
    /// </remarks>
    public readonly struct RadioChangedSignal : ISignal
    {
        /// <summary>What happened.</summary>
        public readonly RadioChangeKind Kind;

        /// <summary>Needle position in MHz.</summary>
        public readonly float Mhz;

        /// <summary>Signal strength at the needle, 0..1.</summary>
        public readonly float Strength;

        /// <summary>Reception tier at the needle.</summary>
        public readonly Reception Reception;

        /// <summary>Nearest station within range, or null.</summary>
        public readonly string StationId;

        /// <summary>A narration key to show for this change, or null.</summary>
        public readonly string LineKey;

        /// <summary>True while the dial is open on screen.</summary>
        public readonly bool IsOpen;

        /// <param name="kind">What happened.</param>
        /// <param name="mhz">Needle position.</param>
        /// <param name="strength">Signal strength at the needle.</param>
        /// <param name="reception">Reception tier at the needle.</param>
        /// <param name="stationId">Nearest station within range, or null.</param>
        /// <param name="lineKey">A narration key for this change, or null.</param>
        /// <param name="isOpen">Whether the dial is open.</param>
        public RadioChangedSignal(
            RadioChangeKind kind, float mhz, float strength, Reception reception,
            string stationId, string lineKey, bool isOpen)
        {
            Kind = kind;
            Mhz = mhz;
            Strength = strength;
            Reception = reception;
            StationId = stationId;
            LineKey = lineKey;
            IsOpen = isOpen;
        }
    }
}

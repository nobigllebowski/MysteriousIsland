// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Bootstrap/GameContext.cs.
// Adapted for Vardholm: the static Current property, the Create/Clear statics and the AttachUI mutator are GONE
// (ADR-0012 forbids a service locator, so dependencies are passed to constructors instead); the country, catalog,
// map and economy members are gone with Nation's design; every field is now assigned once through the constructor
// and exposed read-only, so the object graph is fixed at composition time and cannot be reshaped afterwards.

using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Input;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Radio;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// The composed set of application-wide services, built once by <see cref="AppCompositionRoot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY there is no <c>Current</c> static: Nation had one, and it meant any class anywhere could
    /// reach any service without declaring that it did. The cost was invisible coupling — a type's
    /// real dependencies were whatever it happened to touch at runtime, discoverable only by reading
    /// its whole body. ADR-0012 makes every dependency a constructor parameter instead, which is what
    /// keeps the graph legible and makes adopting a container later a change to one file.
    /// </para>
    /// <para>
    /// WHY this type exists at all if there is no locator: it is the composition root's return value
    /// and the bootstrap behaviour's field — a bundle handed down one edge of the graph, not looked
    /// up sideways. Nothing constructs it except the composition root, and nothing but the bootstrap
    /// holds it.
    /// </para>
    /// </remarks>
    public sealed class GameContext
    {
        private readonly ISaveParticipant[] _saveParticipants;

        /// <summary>
        /// Binds an already-constructed graph. Every parameter is required: a context with a hole in
        /// it would push null checks onto every consumer and defer a composition bug to whichever
        /// screen first needed the missing service.
        /// </summary>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public GameContext(
            ICoreLog log,
            SignalBus signals,
            IslandClock clock,
            ILocalizedText localization,
            GameStateMachine states,
            CommandDispatcher commands,
            SessionService session,
            SceneLoader sceneLoader,
            ZoneRegistry zones,
            SaveSlotService slots,
            InputRouter input,
            ProgressService progress,
            InteractionSystem interactions,
            InventoryService inventory,
            RadioService radio,
            AutosaveDirector autosave,
            RecordKeeper recordKeeper,
            SlateDirector slate,
            IReadOnlyList<ISaveParticipant> saveParticipants)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            if (signals == null)
            {
                throw new ArgumentNullException(nameof(signals));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (localization == null)
            {
                throw new ArgumentNullException(nameof(localization));
            }

            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (sceneLoader == null)
            {
                throw new ArgumentNullException(nameof(sceneLoader));
            }

            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (saveParticipants == null)
            {
                throw new ArgumentNullException(nameof(saveParticipants));
            }

            Log = log;
            Signals = signals;
            Clock = clock;
            Localization = localization;
            States = states;
            Commands = commands;
            Session = session;
            SceneLoader = sceneLoader;
            Zones = zones;
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Radio = radio ?? throw new ArgumentNullException(nameof(radio));
            Autosave = autosave;
            RecordKeeper = recordKeeper;
            Slate = slate;
            Input = input;

            // Copied defensively. The participant list is what a save iterates; handing out the
            // caller's array would let a later holder add a participant after the fact and change what
            // a save contains without going through composition.
            _saveParticipants = new ISaveParticipant[saveParticipants.Count];
            for (var i = 0; i < saveParticipants.Count; i++)
            {
                _saveParticipants[i] = saveParticipants[i];
            }
        }

        /// <summary>Diagnostics sink shared by every service in the graph.</summary>
        public ICoreLog Log { get; }

        /// <summary>Application-wide signal bus.</summary>
        public SignalBus Signals { get; }

        /// <summary>The run clock. Advanced only through <see cref="Session"/>.</summary>
        public IslandClock Clock { get; }

        /// <summary>Localized text lookup. The only legitimate source of player-visible strings.</summary>
        public ILocalizedText Localization { get; }

        /// <summary>The application mode machine.</summary>
        public GameStateMachine States { get; }

        /// <summary>The command dispatcher, with every Phase 1 handler already registered.</summary>
        public CommandDispatcher Commands { get; }

        /// <summary>The live run.</summary>
        public SessionService Session { get; }

        /// <summary>Low-level additive scene loading. Most callers want <see cref="Zones"/>.</summary>
        public SceneLoader SceneLoader { get; }

        /// <summary>Zone residency and the ADR-0004 two-zone cap.</summary>
        public ZoneRegistry Zones { get; }

        /// <summary>The save slot register: three manual slots plus the autosave ring.</summary>
        /// <remarks>
        /// Exposed so that presentation code can READ slot metadata (to render CONTINUE) without
        /// standing up a second <see cref="SaveSlotService"/> over the same files. Two services over one
        /// directory means two caches that disagree the moment either writes, so there is exactly one
        /// instance and it lives here. Reading metadata is safe for UI; writing still goes through a
        /// command, because only a command handler may mutate.
        /// </remarks>
        public SaveSlotService Slots { get; }

        /// <summary>Progression: what the player has inspected, collected and unlocked.</summary>
        public ProgressService Progress { get; }

        /// <summary>Proximity interaction: what the player can act on right now.</summary>
        public InteractionSystem Interactions { get; }

        /// <summary>What the player is carrying. The fourth save participant.</summary>
        public InventoryService Inventory { get; }

        /// <summary>The radio: faults, needle, and what has been heard. Fifth save participant.</summary>
        public RadioService Radio { get; }

        /// <summary>The autosave ring's writer. Null tolerated by everything that reads it.</summary>
        public AutosaveDirector Autosave { get; }

        /// <summary>Keeps the session's recorded-percent figure in step with progression. Null tolerated.</summary>
        public RecordKeeper RecordKeeper { get; }

        /// <summary>The Field Slate's director: derives and announces the notebook. Null tolerated.</summary>
        public SlateDirector Slate { get; }

        /// <summary>
        /// Player input, already gated on the state machine so it reads as centred outside
        /// <c>GameStateId.InGame</c>.
        /// </summary>
        /// <remarks>
        /// Owned by <see cref="AppBootstrap"/>, which disposes it: the router subscribes to the state
        /// machine and to the Input System's action callbacks, and both outlive it.
        /// </remarks>
        public InputRouter Input { get; }

        /// <summary>
        /// Every subsystem that contributes a section to a save, in capture order.
        /// </summary>
        /// <remarks>
        /// Phase 1 registers two (ADR-0011). The list grows by one in the same pull request that adds
        /// each later system, which is the discipline that stops persistence being retrofitted.
        /// </remarks>
        public IReadOnlyList<ISaveParticipant> SaveParticipants => _saveParticipants;
    }
}

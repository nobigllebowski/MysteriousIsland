using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.UI.Core;
using ForgottenIsle.UI.Hud;

namespace ForgottenIsle.UI.Controllers
{
    /// <summary>
    /// Turns gameplay signals into HUD text, and HUD taps into requests.
    /// </summary>
    /// <remarks>
    /// The whole reason the HUD has a controller: gameplay publishes
    /// <c>ObjectiveChangedSignal</c>, <c>InteractionTargetChangedSignal</c> and
    /// <c>NarrationSignal</c> without knowing a screen exists, and this is the one place that knows
    /// both. Nothing in <c>Game</c> touches a VisualElement, and nothing in the HUD reads a service.
    /// <para>
    /// It also owns localization for the HUD, so a key that has no row renders as <c>#key#</c>
    /// through the normal facade rather than being special-cased anywhere in gameplay.
    /// </para>
    /// </remarks>
    public sealed class HudController : IDisposable
    {
        private readonly HudScreen _screen;
        private readonly ILocalizedText _loc;
        private readonly ICoreLog _log;
        private readonly CommandDispatcher _commands;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(4);
        private readonly List<InventoryItemView> _itemViews = new List<InventoryItemView>(8);
        private readonly List<string> _sequence = new List<string>(10);

        private bool _disposed;

        /// <param name="screen">The HUD view this drives.</param>
        /// <param name="signals">Bus carrying the gameplay signals. Null leaves the HUD static.</param>
        /// <param name="loc">Localization facade.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        /// <param name="commands">Dispatcher, the only route from a tap to a state change. Null
        /// leaves the inventory tray readable but inert.</param>
        public HudController(
            HudScreen screen,
            SignalBus signals,
            ILocalizedText loc,
            ICoreLog log,
            CommandDispatcher commands = null)
        {
            _screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _log = log;
            _commands = commands;

            // Subscribed before the screen has been built. The event lives on the screen rather
            // than on the tray for exactly this reason -- the tray does not exist yet.
            _screen.CombineRequested += OnCombineRequested;
            _screen.RadioTuneRequested += OnRadioTuneRequested;
            _screen.RadioMicSqueezed += OnRadioMicSqueezed;
            _screen.RadioCloseRequested += OnRadioCloseRequested;

            if (signals == null)
            {
                return;
            }

            _subscriptions.Add(signals.Subscribe<ObjectiveChangedSignal>(OnObjectiveChanged));
            _subscriptions.Add(signals.Subscribe<InteractionTargetChangedSignal>(OnTargetChanged));
            _subscriptions.Add(signals.Subscribe<NarrationSignal>(OnNarration));
            _subscriptions.Add(signals.Subscribe<InventoryChangedSignal>(OnInventoryChanged));
            _subscriptions.Add(signals.Subscribe<RadioChangedSignal>(OnRadioChanged));
            _subscriptions.Add(signals.Subscribe<NarrationSequenceSignal>(OnNarrationSequence));
        }

        /// <summary>The HUD view, so the installer can push it onto the screen stack.</summary>
        public HudScreen Screen => _screen;

        /// <summary>
        /// Pushes the current objective into the view.
        /// </summary>
        /// <remarks>
        /// Needed because the HUD is built when the player enters a zone, which is after the
        /// objective signal for a restored save has already been published. Without a pull at build
        /// time, a continued run would show an empty objective until the next thing it did.
        /// </remarks>
        /// <param name="objectiveKey">Localization key, or empty to show nothing.</param>
        public void PrimeObjective(string objectiveKey)
        {
            ApplyObjective(objectiveKey);
        }

        /// <summary>
        /// Pushes what the player is already carrying into the tray.
        /// </summary>
        /// <remarks>
        /// Needed for the same reason <see cref="PrimeObjective"/> is. A continued run restores its
        /// inventory during load, which publishes the change long before the HUD is built — so
        /// without a pull at build time the player comes back from a save with an empty tray and no
        /// way to reach the items a puzzle needs. They are still carried; they are just invisible,
        /// which is worse than being gone.
        /// </remarks>
        /// <param name="items">Item ids, in the order they were found. Null is read as empty.</param>
        public void PrimeInventory(IReadOnlyList<string> items)
        {
            ApplyInventory(items);
        }

        /// <summary>Shows or hides the touch controls and prompt.</summary>
        /// <param name="active">False while paused, loading, or in a menu.</param>
        public void SetGameplayActive(bool active)
        {
            _screen.SetControlsVisible(active);

            if (!active)
            {
                // The prompt describes something the player can act on right now. While paused they
                // cannot, so leaving it up would advertise a button that does nothing.
                _screen.SetPrompt(string.Empty, string.Empty, false);

                // And the radio is put down. The dial is game state, so this goes through the
                // dispatcher like any other change; the close handler accepts it from any mode.
                if (_commands != null)
                {
                    _commands.Dispatch(new CloseRadioCommand());
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _screen.CombineRequested -= OnCombineRequested;
            _screen.RadioTuneRequested -= OnRadioTuneRequested;
            _screen.RadioMicSqueezed -= OnRadioMicSqueezed;
            _screen.RadioCloseRequested -= OnRadioCloseRequested;

            for (var i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        private void OnInventoryChanged(InventoryChangedSignal signal)
        {
            ApplyInventory(signal.Items);
        }

        private void ApplyInventory(IReadOnlyList<string> items)
        {
            _itemViews.Clear();

            var count = items != null ? items.Count : 0;
            for (var i = 0; i < count; i++)
            {
                // The id doubles as the localization key, so an item that reaches the tray without
                // a row in the table renders as #item.whatever# rather than as a blank chip the
                // player cannot tap with any confidence.
                _itemViews.Add(new InventoryItemView(items[i], _loc.Get(new LocKey(items[i]))));
            }

            _screen.SetInventory(_itemViews);
        }

        private void OnCombineRequested(string first, string second)
        {
            if (_commands == null)
            {
                // A HUD built without a dispatcher is a HUD in a test. The tray still renders; it
                // simply cannot change anything, which is the correct behaviour for a view.
                return;
            }

            // Whether these two go together is not this class's question, and deliberately so. The
            // handler decides, refuses through a result code, and answers the player with a line of
            // narration either way -- including when the answer is that nothing happened.
            _commands.Dispatch(new CombineItemsCommand(first, second));
        }

        private void OnRadioChanged(RadioChangedSignal signal)
        {
            // The band follows the game's idea of open, not the panel's. A refused open (paused)
            // publishes nothing, so nothing appears; a close from any source takes it down.
            _screen.SetRadioVisible(signal.IsOpen);

            if (!signal.IsOpen)
            {
                return;
            }

            // The caption names a station only on a lock. Below that the player knows something
            // human is in there and not what -- which is the pull the puzzle is built on, and a
            // label would hand it to them at twelve kilohertz.
            var stationText = string.Empty;
            if (signal.Reception == Reception.Locked && !string.IsNullOrEmpty(signal.StationId))
            {
                // "radio.the_voice" -> "ui.radio.station.the_voice". The station id is the tail of
                // the key, so a station added to the table needs one row and no code.
                var tail = signal.StationId.StartsWith("radio.", StringComparison.Ordinal)
                    ? signal.StationId.Substring("radio.".Length)
                    : signal.StationId;
                stationText = _loc.Get(new LocKey("ui.radio.station." + tail));
            }
            else if (signal.Reception != Reception.Grass)
            {
                stationText = _loc.Get(new LocKey("ui.radio.station.something"));
            }

            _screen.SetRadioState(signal.Mhz, signal.Reception, stationText);
        }

        private void OnNarrationSequence(NarrationSequenceSignal signal)
        {
            // The game composed it; this only translates and paces it.
            _sequence.Clear();
            for (var i = 0; i < signal.LineKeys.Count; i++)
            {
                var key = signal.LineKeys[i];
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var line = _loc.Get(new LocKey(key));
                _sequence.Add(line);

                if (_log != null && line.StartsWith("#", StringComparison.Ordinal))
                {
                    _log.Warn(LogCode.MissingLocKey, key);
                }
            }

            _screen.ShowNarrationSequence(_sequence);
        }

        private void OnRadioTuneRequested(float mhz)
        {
            if (_commands != null)
            {
                _commands.Dispatch(new TuneRadioCommand(mhz));
            }
        }

        private void OnRadioMicSqueezed()
        {
            if (_commands != null)
            {
                _commands.Dispatch(new SqueezeMicCommand());
            }
        }

        private void OnRadioCloseRequested()
        {
            if (_commands != null)
            {
                _commands.Dispatch(new CloseRadioCommand());
            }
        }

        private void OnObjectiveChanged(ObjectiveChangedSignal signal)
        {
            ApplyObjective(signal.ObjectiveKey);
        }

        private void ApplyObjective(string objectiveKey)
        {
            if (string.IsNullOrEmpty(objectiveKey))
            {
                _screen.SetObjective(string.Empty);
                return;
            }

            _screen.SetObjective(_loc.Get(new LocKey(objectiveKey)));
        }

        private void OnTargetChanged(InteractionTargetChangedSignal signal)
        {
            if (!signal.HasTarget)
            {
                _screen.SetPrompt(string.Empty, string.Empty, false);
                return;
            }

            _screen.SetPrompt(
                _loc.Get(new LocKey(signal.NameKey)),
                _loc.Get(new LocKey(signal.PromptKey)),
                true);
        }

        private void OnNarration(NarrationSignal signal)
        {
            if (string.IsNullOrEmpty(signal.LineKey))
            {
                return;
            }

            var line = _loc.Get(new LocKey(signal.LineKey));
            _screen.ShowNarration(line);

            if (_log != null && line.StartsWith("#", StringComparison.Ordinal))
            {
                // A missing narration row is content that was written in code and never written in
                // the table. It renders as #key# rather than blank, but it is still a content bug.
                _log.Warn(LogCode.MissingLocKey, signal.LineKey);
            }
        }
    }
}

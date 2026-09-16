using System;
using System.Globalization;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.UI.Core;
using ForgottenIsle.UI.Screens;

namespace ForgottenIsle.UI.Controllers
{
    /// <summary>
    /// Turns main-menu taps into commands. The worked example of the UI -> controller -> command path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This class does not touch <c>GameState</c>, and no controller may.</b> It holds no session, no
    /// player state and no game state object; it cannot start a run, move the player or write a save by
    /// itself. What it does is translate an intent the player expressed with a finger — "continue", "new
    /// game" — into a value (<see cref="StartNewGameCommand"/>) and hand it to the
    /// <see cref="CommandDispatcher"/>. Whether that intent is legal is the handler's <c>Validate</c> to
    /// decide, and what it changes is the handler's <c>Execute</c> to perform.
    /// </para>
    /// <para>
    /// WHY that indirection is worth a class: it makes every state change in the game a named, validated,
    /// loggable value with one implementation. A menu that called <c>session.BeginNewRun(...)</c> directly
    /// would be a second implementation of starting a run — one with no validation, no logging, and no
    /// chance of being replayed in a test or a bug report.
    /// </para>
    /// <para>
    /// WHY the screen is a field and not rebuilt each time: returning to the menu after a run is the most
    /// common navigation in the game, and it should be instant. <see cref="Refresh"/> re-reads the slot
    /// headers; the tree is reused.
    /// </para>
    /// </remarks>
    public sealed class MainMenuController
    {
        // Keys follow the string table. "Nothing to continue" is the menu's own ui.menu.no_save row
        // rather than a second toast-only phrasing of the same sentence.
        private static readonly LocKey SlotsFullKey = new LocKey("ui.toast.slots_full");
        private static readonly LocKey NoSaveKey = new LocKey("ui.menu.no_save");
        private static readonly LocKey OverwriteArmedKey = new LocKey("ui.menu.overwrite_armed");

        /// <summary>Seconds a second tap on NEW GAME counts as confirming the overwrite.</summary>
        private const double OverwriteWindowSeconds = 8.0;

        private readonly UIService _ui;
        private readonly CommandDispatcher _commands;
        private readonly SaveSlotService _slots;
        private readonly ICoreLog _log;
        private readonly string _buildVersion;

        private SettingsScreen _settings;
        private int _armedOverwriteSlot = -1;
        private double _armedAt;
        private int _continueSlot = -1;

        /// <summary>
        /// Wires the menu to the dispatcher and the slot service.
        /// </summary>
        /// <param name="ui">The UI service. Provides the screen stack and the toast surface.</param>
        /// <param name="commands">The dispatcher. The only route from this class to a state change.</param>
        /// <param name="slots">Slot headers, read to decide whether CONTINUE is offered and what it says.</param>
        /// <param name="log">Diagnostics sink.</param>
        /// <param name="buildVersion">Build string shown on the settings screen.</param>
        /// <exception cref="ArgumentNullException"><paramref name="ui"/>, <paramref name="commands"/>, <paramref name="slots"/> or <paramref name="log"/> is null.</exception>
        public MainMenuController(
            UIService ui,
            CommandDispatcher commands,
            SaveSlotService slots,
            ICoreLog log,
            string buildVersion = "")
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _buildVersion = buildVersion ?? string.Empty;

            Screen = new MainMenuScreen(_ui.Context, OnContinue, OnNewGame, OnSettings);
        }

        /// <summary>The menu screen this controller drives. Exposed so composition can pre-warm or test it.</summary>
        public MainMenuScreen Screen { get; }

        /// <summary>Slot CONTINUE would resume, or -1 when there is nothing to resume.</summary>
        public int ContinueSlot => _continueSlot;

        /// <summary>
        /// Re-reads the slots and makes the menu the only screen on the stack.
        /// </summary>
        /// <remarks>
        /// <see cref="ScreenStack.ReplaceAll"/> rather than a push: arriving at the menu means the run is
        /// over, and a back stack that could return to a pause screen belonging to a run that no longer
        /// exists is a crash waiting for a back button.
        /// </remarks>
        public void Show()
        {
            Refresh();
            _ui.Screens.ReplaceAll(Screen);
        }

        /// <summary>
        /// Re-reads the save slots and updates what CONTINUE offers.
        /// </summary>
        /// <remarks>
        /// Called on every show rather than cached, because a save can be written, deleted or replaced by a
        /// cloud sync while the menu is off screen, and a menu offering a save that is gone fails on the one
        /// tap the player cared about.
        /// </remarks>
        public void Refresh()
        {
            SaveMetadata metadata;
            int slot;

            // The dispatcher is asked whether it can service a resume at all. A build whose composition
            // root never registered the handler would otherwise offer CONTINUE and answer the tap with a
            // NoHandler toast, which blames the player's save for a wiring mistake.
            if (CanResume() && _slots.TryFindMostRecent(out slot, out metadata) && metadata != null)
            {
                _continueSlot = slot;
                Screen.ShowContinueSlot(metadata);
                return;
            }

            _continueSlot = -1;
            Screen.ShowNoSave();
        }

        /// <summary>Resumes the most recent save, as a command.</summary>
        /// <remarks>
        /// CONTINUE is a <see cref="ResumeSavedRunCommand"/> like every other menu action is a command:
        /// the handler validates the slot, raises the curtain, restores the participants and decides
        /// what a corrupt file means. This method chooses a slot and reports the outcome, and that is
        /// the whole of its authority.
        /// </remarks>
        private void OnContinue()
        {
            if (_continueSlot < 0 || !CanResume())
            {
                // Reachable if a save disappeared between the last Refresh and this tap. Saying so is better
                // than a dead button: the player learns the state changed, and Refresh corrects the label.
                _ui.Toast(NoSaveKey, ToastKind.Warning);
                Refresh();
                return;
            }

            var result = _commands.Dispatch(new ResumeSavedRunCommand(_continueSlot));
            if (!result.Success)
            {
                _log.Warn(LogCode.SaveCorrupt, "continue slot " + _continueSlot.ToString(CultureInfo.InvariantCulture) + ": " + result.Code);
                _ui.ToastResult(result.Code);
                Refresh();
            }
        }

        /// <summary>True when a resume can actually be serviced by the dispatcher this menu was given.</summary>
        private bool CanResume()
        {
            return _commands.HasHandler<ResumeSavedRunCommand>();
        }

        /// <summary>Starts a new run in the first free slot, as a command.</summary>
        private void OnNewGame()
        {
            var slot = FirstFreeSlot();
            if (slot < 0)
            {
                // Every slot holds a run. Silently destroying one is the worst thing this menu could
                // do, and refusing forever (which is what it did) was the second worst: after three
                // runs there was no way to start a fourth. So: the first tap names what will happen
                // and arms it; a second tap within the window is the confirmation. There is no
                // dialog widget in this UI yet, and a two-tap confirm with a plain sentence is a
                // smaller thing to get right than a modal.
                var oldest = OldestSlot();
                if (oldest < 0)
                {
                    // Full, and nothing readable to overwrite: every slot is corrupt. Refuse, so the
                    // one save a player might yet recover is not the one that gets destroyed.
                    _ui.Toast(SlotsFullKey, ToastKind.Warning);
                    return;
                }

                var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                if (_armedOverwriteSlot != oldest || now - _armedAt > OverwriteWindowSeconds)
                {
                    _armedOverwriteSlot = oldest;
                    _armedAt = now;
                    _ui.Toast(OverwriteArmedKey, ToastKind.Warning);
                    return;
                }

                _armedOverwriteSlot = -1;
                slot = oldest;
            }

            // The whole point of this class, in one line: a tap becomes a value, and the dispatcher decides
            // whether it is legal and what it does.
            var result = _commands.Dispatch(new StartNewGameCommand(slot));
            if (!result.Success)
            {
                _ui.ToastResult(result.Code);
            }
        }

        /// <summary>Pushes the settings screen, building it on first use.</summary>
        private void OnSettings()
        {
            if (_settings == null)
            {
                _settings = new SettingsScreen(_ui.Context, _buildVersion, () => _ui.Screens.Pop());
            }

            _ui.Screens.Push(_settings);
        }

        /// <summary>
        /// The lowest manual slot with nothing in it, or -1 when all three hold a save.
        /// </summary>
        /// <remarks>
        /// A corrupt slot counts as occupied. It still holds a file, and treating it as free would have a
        /// new run quietly overwrite the one save a player might otherwise have recovered.
        /// </remarks>
        /// <summary>
        /// The readable manual slot saved longest ago, or -1 when none is readable.
        /// </summary>
        /// <remarks>
        /// Ordinal comparison of the ISO stamp, which the slot service documents as correct for
        /// its own format. A corrupt slot is never the answer: it may be the one save the player
        /// could still recover, and an overwrite is the one thing that makes that impossible.
        /// </remarks>
        private int OldestSlot()
        {
            var oldest = -1;
            string oldestStamp = null;
            for (var slot = 0; slot < SaveSlotService.SlotCount; slot++)
            {
                SaveMetadata metadata;
                if (_slots.ReadMetadata(slot, out metadata) != ResultCode.Ok || metadata == null)
                {
                    continue;
                }

                var stamp = metadata.SavedAtIso ?? string.Empty;
                if (oldest < 0 || string.CompareOrdinal(stamp, oldestStamp) < 0)
                {
                    oldest = slot;
                    oldestStamp = stamp;
                }
            }

            return oldest;
        }

        private int FirstFreeSlot()
        {
            for (var slot = 0; slot < SaveSlotService.SlotCount; slot++)
            {
                SaveMetadata metadata;
                if (_slots.ReadMetadata(slot, out metadata) == ResultCode.SlotEmpty)
                {
                    return slot;
                }
            }

            return -1;
        }
    }
}

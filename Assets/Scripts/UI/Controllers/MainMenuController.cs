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
        private static readonly LocKey SlotsFullKey = new LocKey("ui.toast.slots.full");
        private static readonly LocKey NoSaveKey = new LocKey("ui.toast.continue.unavailable");

        private readonly UIService _ui;
        private readonly CommandDispatcher _commands;
        private readonly SaveSlotService _slots;
        private readonly ICoreLog _log;
        private readonly Func<int, ResultCode> _resumeSavedRun;
        private readonly string _buildVersion;

        private SettingsScreen _settings;
        private int _continueSlot = -1;

        /// <summary>
        /// Wires the menu to the dispatcher and the slot service.
        /// </summary>
        /// <param name="ui">The UI service. Provides the screen stack and the toast surface.</param>
        /// <param name="commands">The dispatcher. The only route from this class to a state change.</param>
        /// <param name="slots">Slot headers, read to decide whether CONTINUE is offered and what it says.</param>
        /// <param name="log">Diagnostics sink.</param>
        /// <param name="resumeSavedRun">
        /// Resumes an existing save in the given slot and returns the outcome.
        /// <para>
        /// WHY this is a delegate and not a command: Phase 1's command set has no LoadGameCommand (the
        /// contract lists StartNewGame, SaveGame, QuitToMenu and TravelToZone), and inventing one here would
        /// put a second, unreviewed definition of "load a game" in the UI assembly. Composition supplies the
        /// load path instead, and this controller still never touches state itself. When the command exists,
        /// this parameter disappears and CONTINUE becomes one more <c>Dispatch</c> like the others.
        /// </para>
        /// Null disables CONTINUE entirely, which is the honest behaviour when nothing can service it.
        /// </param>
        /// <param name="buildVersion">Build string shown on the settings screen.</param>
        /// <exception cref="ArgumentNullException"><paramref name="ui"/>, <paramref name="commands"/>, <paramref name="slots"/> or <paramref name="log"/> is null.</exception>
        public MainMenuController(
            UIService ui,
            CommandDispatcher commands,
            SaveSlotService slots,
            ICoreLog log,
            Func<int, ResultCode> resumeSavedRun = null,
            string buildVersion = "")
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _resumeSavedRun = resumeSavedRun;
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

            if (_resumeSavedRun != null && _slots.TryFindMostRecent(out slot, out metadata) && metadata != null)
            {
                _continueSlot = slot;
                Screen.ShowContinueSlot(metadata);
                return;
            }

            _continueSlot = -1;
            Screen.ShowNoSave();
        }

        /// <summary>Resumes the most recent save, reporting whatever the load path returns.</summary>
        private void OnContinue()
        {
            if (_continueSlot < 0 || _resumeSavedRun == null)
            {
                // Reachable if a save disappeared between the last Refresh and this tap. Saying so is better
                // than a dead button: the player learns the state changed, and Refresh corrects the label.
                _ui.Toast(NoSaveKey, ToastKind.Warning);
                Refresh();
                return;
            }

            var code = _resumeSavedRun(_continueSlot);
            if (code != ResultCode.Ok)
            {
                _log.Warn(LogCode.SaveCorrupt, "continue slot " + _continueSlot.ToString(CultureInfo.InvariantCulture) + ": " + code);
                _ui.ToastResult(code);
                Refresh();
            }
        }

        /// <summary>Starts a new run in the first free slot, as a command.</summary>
        private void OnNewGame()
        {
            var slot = FirstFreeSlot();
            if (slot < 0)
            {
                // Deliberately refuses rather than overwriting the oldest save. Phase 1 has no confirmation
                // dialog, and silently destroying a run because every slot was full is the single worst
                // thing this menu could do.
                _ui.Toast(SlotsFullKey, ToastKind.Warning);
                return;
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

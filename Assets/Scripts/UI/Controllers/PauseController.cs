using System;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Session;
using ForgottenIsle.UI.Core;
using ForgottenIsle.UI.Screens;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Controllers
{
    /// <summary>
    /// Turns pause-menu taps into commands and mode transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like <see cref="MainMenuController"/>, <b>this class never touches <c>GameState</c></b>. It reads two
    /// facts from <see cref="SessionService"/> — whether a run exists and which slot it is bound to — to
    /// decide whether SAVE should be offered, and it writes nothing. Saving and quitting go through the
    /// dispatcher; pausing and resuming go through <see cref="GameStateMachine"/>, which is the mode
    /// machine, not the run's data.
    /// </para>
    /// <para>
    /// WHY pause/resume are transitions rather than commands: the Phase 1 command set has no pause command,
    /// and pausing changes only which mode the application is in. The state machine already rejects illegal
    /// transitions and returns a <see cref="CommandResult"/>, so the outcome is reported exactly like a
    /// dispatched command's.
    /// </para>
    /// <para>
    /// KNOWN GAP: the legal transition table has no <c>Paused -&gt; MainMenu</c> or <c>InGame -&gt;
    /// MainMenu</c> edge, so <see cref="QuitToMenuCommand"/>'s validator can refuse a quit that the player
    /// can plainly see a button for. This controller dispatches the command anyway and surfaces the result
    /// rather than reaching around the dispatcher to force the mode change: a refusal the player can see is
    /// a bug report about the transition table, while a UI that bypasses validation is a bug nobody finds.
    /// </para>
    /// </remarks>
    public sealed class PauseController
    {
        private static readonly LocKey SavedKey = new LocKey("ui.toast.saved");

        private readonly UIService _ui;
        private readonly CommandDispatcher _commands;
        private readonly GameStateMachine _states;
        private readonly SessionService _session;
        private readonly ICoreLog _log;
        private readonly Action _onReturnedToMenu;

        /// <summary>
        /// Wires the pause menu to the dispatcher and the mode machine.
        /// </summary>
        /// <param name="ui">The UI service. Provides the screen stack and the toast surface.</param>
        /// <param name="commands">The dispatcher, for SAVE and QUIT.</param>
        /// <param name="states">The mode machine, for pause and resume.</param>
        /// <param name="session">Read only, to learn whether a run exists and which slot it is bound to.</param>
        /// <param name="log">Diagnostics sink.</param>
        /// <param name="onReturnedToMenu">
        /// Invoked after a successful quit, so composition can show the main menu. Optional: without it the
        /// quit still happens, the menu simply is not raised by this controller.
        /// </param>
        /// <exception cref="ArgumentNullException">Any required argument is null.</exception>
        public PauseController(
            UIService ui,
            CommandDispatcher commands,
            GameStateMachine states,
            SessionService session,
            ICoreLog log,
            Action onReturnedToMenu = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _onReturnedToMenu = onReturnedToMenu;

            Screen = new PauseScreen(_ui.Context, OnResume, OnSave, OnQuit);
        }

        /// <summary>The pause screen this controller drives.</summary>
        public PauseScreen Screen { get; }

        /// <summary>True while the application is in the paused mode and the panel is up.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Pauses the run and shows the panel.
        /// </summary>
        /// <remarks>
        /// The transition comes first and the panel only follows a success, so the panel can never be up
        /// while the simulation is still running — which is what a pause menu exists to prevent.
        /// </remarks>
        /// <returns>The transition's result, so a caller can report a refusal.</returns>
        public CommandResult Open()
        {
            if (IsOpen)
            {
                return CommandResult.Ok;
            }

            var result = _states.TryTransition(GameStateId.Paused);
            if (!result.Success)
            {
                _ui.ToastResult(result.Code);
                return result;
            }

            IsOpen = true;
            Screen.SetSaveEnabled(CanSave());
            ShowPanel();
            return result;
        }

        /// <summary>Resumes the run and hides the panel. Safe to call when already closed.</summary>
        /// <returns>The transition's result.</returns>
        public CommandResult Close()
        {
            if (!IsOpen)
            {
                return CommandResult.Ok;
            }

            var result = _states.TryTransition(GameStateId.InGame);
            if (!result.Success)
            {
                _ui.ToastResult(result.Code);
                return result;
            }

            IsOpen = false;
            HidePanel();
            return result;
        }

        /// <summary>RESUME.</summary>
        private void OnResume()
        {
            Close();
        }

        /// <summary>SAVE: writes the run to the slot it is already bound to.</summary>
        private void OnSave()
        {
            var slot = _session.BoundSlot;

            var result = _commands.Dispatch(new SaveGameCommand(slot));
            if (!result.Success)
            {
                _log.Warn(LogCode.SaveCorrupt, "pause save: " + result.Code);
                _ui.ToastResult(result.Code);

                // The offer was wrong if we got here, so stop offering it until something changes.
                Screen.SetSaveEnabled(CanSave());
                return;
            }

            // The one success worth a toast in this menu: nothing on screen changes when a save works, so
            // without it the player cannot tell the button did anything.
            _ui.Toast(SavedKey, ToastKind.Success);
        }

        /// <summary>QUIT TO MENU: ends the run through the dispatcher and hands off to the menu.</summary>
        private void OnQuit()
        {
            var result = _commands.Dispatch(new QuitToMenuCommand());
            if (!result.Success)
            {
                _log.Warn(LogCode.IllegalTransition, "pause quit: " + result.Code);
                _ui.ToastResult(result.Code);
                return;
            }

            IsOpen = false;
            HidePanel();
            _onReturnedToMenu?.Invoke();
        }

        /// <summary>Whether a save can succeed right now, mirroring the save handler's own preconditions.</summary>
        private bool CanSave()
        {
            return _session.HasRun && SaveSlotService.IsValidSlot(_session.BoundSlot);
        }

        /// <summary>
        /// Puts the panel on screen.
        /// </summary>
        /// <remarks>
        /// WHY the two paths: in Phase 1 a running zone is rendered by the scene, not by a UI screen, so the
        /// stack can be empty when the player pauses. The panel is then the only screen on the stack, and
        /// <see cref="ScreenStack.Pop"/> refuses to empty the stack — correctly, since an empty stack in the
        /// general case is a black screen. Reopening therefore re-reveals the screen that is already on top
        /// instead of pushing a second copy.
        /// </remarks>
        private void ShowPanel()
        {
            if (_ui.Screens.Current == Screen)
            {
                Screen.EnsureBuilt();
                Screen.Root.style.display = DisplayStyle.Flex;
                Screen.Root.BringToFront();
                Screen.OnShown();
                return;
            }

            _ui.Screens.Push(Screen);
        }

        /// <summary>Takes the panel off screen, popping it when there is something underneath to return to.</summary>
        private void HidePanel()
        {
            if (_ui.Screens.Current != Screen)
            {
                return;
            }

            if (_ui.Screens.Count > 1)
            {
                _ui.Screens.Pop();
                return;
            }

            Screen.OnHidden();
            Screen.Root.style.display = DisplayStyle.None;
        }
    }
}

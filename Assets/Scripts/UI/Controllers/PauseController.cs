using System;
using System.Globalization;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.UI.Core;
using ForgottenIsle.UI.Screens;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Controllers
{
    /// <summary>
    /// The read-only facts the pause menu renders about the run it is covering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY a snapshot instead of the live run object: ADR-0002 and <c>ci/check-layering.sh</c> forbid the
    /// UI assembly from naming the service that owns <c>GameState</c>, because a reference a screen can
    /// read is a reference a screen can write — and a write from here bypasses every command validator in
    /// the project. A value type carrying five facts cannot mutate anything, cannot be walked back to the
    /// object it was taken from, and says in its own shape exactly what the pause menu is allowed to know.
    /// </para>
    /// <para>
    /// WHY it is taken per open rather than held: the run keeps advancing until the pause transition
    /// lands, so a snapshot captured once at composition time would show the playtime the player had when
    /// the game booted. The controller asks for a fresh one every time the panel goes up.
    /// </para>
    /// </remarks>
    public readonly struct PauseSnapshot
    {
        /// <summary>Captures the state of the run at one instant.</summary>
        /// <param name="hasRun">True when a run exists at all.</param>
        /// <param name="boundSlot">Slot the run is bound to, or the no-slot sentinel.</param>
        /// <param name="zoneDisplayKey">Loc key of the occupied zone's display name; empty when unknown.</param>
        /// <param name="playtimeSeconds">Real seconds played in this run.</param>
        /// <param name="recordedPercent">Discovery completion, 0..100.</param>
        public PauseSnapshot(
            bool hasRun,
            int boundSlot,
            string zoneDisplayKey,
            double playtimeSeconds,
            int recordedPercent)
        {
            HasRun = hasRun;
            BoundSlot = boundSlot;
            ZoneDisplayKey = zoneDisplayKey ?? string.Empty;
            PlaytimeSeconds = playtimeSeconds;
            RecordedPercent = recordedPercent;
        }

        /// <summary>True when a run exists. False means SAVE cannot succeed whatever the slot says.</summary>
        public bool HasRun { get; }

        /// <summary>The slot the run is bound to. Not every value is a savable slot.</summary>
        public int BoundSlot { get; }

        /// <summary>Loc key of the zone display name, for example <c>zone.ribcage.name</c>. May be empty.</summary>
        public string ZoneDisplayKey { get; }

        /// <summary>Real seconds played in this run.</summary>
        public double PlaytimeSeconds { get; }

        /// <summary>Discovery completion as a whole percentage.</summary>
        public int RecordedPercent { get; }
    }

    /// <summary>
    /// Turns pause-menu taps into commands and mode transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like <see cref="MainMenuController"/>, <b>this class never touches <c>GameState</c></b>. It reads
    /// the run through a <see cref="PauseSnapshot"/> — a value, taken for it by composition — and writes
    /// nothing. Saving and quitting go through the dispatcher; pausing and resuming go through
    /// <see cref="GameStateMachine"/>, which is the mode machine, not the run's data.
    /// </para>
    /// <para>
    /// WHY pause/resume are transitions rather than commands: the Phase 1 command set has no pause command,
    /// and pausing changes only which mode the application is in. The state machine already rejects illegal
    /// transitions and returns a <see cref="CommandResult"/>, so the outcome is reported exactly like a
    /// dispatched command's.
    /// </para>
    /// <para>
    /// QUIT is dispatched, never forced. There is deliberately no direct <c>Paused -&gt; MainMenu</c> edge:
    /// <see cref="QuitToMenuCommand"/>'s handler routes the quit through <c>Loading</c> so the zones are
    /// unloaded behind the curtain rather than in front of the player. If the validator ever refuses, this
    /// controller surfaces the refusal instead of reaching around the dispatcher to flip the mode itself —
    /// a refusal the player can see is a bug report, while a UI that bypasses validation is a bug nobody finds.
    /// </para>
    /// </remarks>
    public sealed class PauseController
    {
        // Keys follow the string table: the success toast is ui.toast.game_saved there, and the two
        // value patterns are shared with the save-slot card so both render a run identically.
        private static readonly LocKey SavedKey = new LocKey("ui.toast.game_saved");
        private static readonly LocKey PlaytimePatternKey = new LocKey("ui.save.playtime");
        private static readonly LocKey RecordedPatternKey = new LocKey("ui.save.recorded_percent");
        private static readonly LocKey UnknownKey = new LocKey("ui.common.unknown");

        private readonly UIService _ui;
        private readonly CommandDispatcher _commands;
        private readonly GameStateMachine _states;
        private readonly Func<PauseSnapshot> _snapshot;
        private readonly ICoreLog _log;
        private readonly Action _onReturnedToMenu;

        /// <summary>
        /// Wires the pause menu to the dispatcher and the mode machine.
        /// </summary>
        /// <param name="ui">The UI service. Provides the screen stack and the toast surface.</param>
        /// <param name="commands">The dispatcher, for SAVE and QUIT.</param>
        /// <param name="states">The mode machine, for pause and resume.</param>
        /// <param name="snapshot">
        /// Takes a fresh read-only view of the run. Supplied by composition, which is the only layer that
        /// can see the object the facts come from. Called once per open, never stored.
        /// </param>
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
            Func<PauseSnapshot> snapshot,
            ICoreLog log,
            Action onReturnedToMenu = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
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

            // The presentation itself is Show, which the state signal may already have run inside the
            // transition above. Calling it again is free: Show returns immediately when the panel is up.
            Show();
            return result;
        }

        /// <summary>
        /// Puts the panel on screen and fills it from a fresh snapshot, without transitioning.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Open"/> because the application can arrive in
        /// <see cref="GameStateId.Paused"/> without this controller being what asked — composition
        /// subscribes to the state signal and calls this, so the panel follows the mode rather than the
        /// button. Idempotent, so it does not matter which of the two paths gets here first.
        /// </remarks>
        public void Show()
        {
            if (IsOpen)
            {
                return;
            }

            IsOpen = true;

            var snapshot = _snapshot();
            Screen.SetSaveEnabled(CanSave(snapshot));
            Screen.SetSessionSummary(
                ZoneText(snapshot),
                PlaytimeText(snapshot),
                RecordedText(snapshot));

            ShowPanel();
        }

        /// <summary>Takes the panel off screen without transitioning. Safe to call when already closed.</summary>
        /// <remarks>The counterpart to <see cref="Show"/>: composition calls it when the mode leaves Paused.</remarks>
        public void Hide()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            HidePanel();
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

            Hide();
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
            var snapshot = _snapshot();

            var result = _commands.Dispatch(new SaveGameCommand(snapshot.BoundSlot));
            if (!result.Success)
            {
                _log.Warn(LogCode.SaveCorrupt, "pause save: " + result.Code);
                _ui.ToastResult(result.Code);

                // The offer was wrong if we got here, so stop offering it until something changes.
                Screen.SetSaveEnabled(CanSave(_snapshot()));
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

            Hide();
            _onReturnedToMenu?.Invoke();
        }

        /// <summary>Whether a save can succeed right now, mirroring the save handler's own preconditions.</summary>
        /// <param name="snapshot">The run as it stood when the panel was opened.</param>
        private static bool CanSave(in PauseSnapshot snapshot)
        {
            return snapshot.HasRun && SaveSlotService.IsValidSlot(snapshot.BoundSlot);
        }

        /// <summary>
        /// The occupied zone's display name, or the shared "unknown" text.
        /// </summary>
        /// <remarks>
        /// The key travels in the snapshot and is resolved here rather than at the source, so the label
        /// re-resolves in the new language if the locale changes between two opens of the panel.
        /// </remarks>
        private string ZoneText(in PauseSnapshot snapshot)
        {
            if (!snapshot.HasRun)
            {
                return string.Empty;
            }

            var key = snapshot.ZoneDisplayKey;
            return string.IsNullOrEmpty(key) ? _ui.Loc.Get(UnknownKey) : _ui.Loc.Get(new LocKey(key));
        }

        /// <summary>
        /// The run's playtime as hours and minutes.
        /// </summary>
        /// <remarks>
        /// Numbers are formatted invariantly and the unit letters come from the pattern, because only the
        /// translation knows whether its language writes "2h 14m" or something else. Seconds are dropped:
        /// a pause menu is not a stopwatch.
        /// </remarks>
        private string PlaytimeText(in PauseSnapshot snapshot)
        {
            if (!snapshot.HasRun)
            {
                return string.Empty;
            }

            var totalSeconds = snapshot.PlaytimeSeconds;
            if (totalSeconds < 0d || double.IsNaN(totalSeconds))
            {
                totalSeconds = 0d;
            }

            var totalMinutes = (long)(totalSeconds / 60d);
            var hours = totalMinutes / 60L;
            var minutes = totalMinutes % 60L;

            return _ui.Loc.Get(
                PlaytimePatternKey,
                hours.ToString(CultureInfo.InvariantCulture),
                minutes.ToString("00", CultureInfo.InvariantCulture));
        }

        /// <summary>The run's discovery percentage, or empty when there is no run to describe.</summary>
        private string RecordedText(in PauseSnapshot snapshot)
        {
            if (!snapshot.HasRun)
            {
                return string.Empty;
            }

            return _ui.Loc.Get(
                RecordedPatternKey,
                snapshot.RecordedPercent.ToString(CultureInfo.InvariantCulture));
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

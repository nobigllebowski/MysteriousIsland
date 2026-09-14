using System;
using System.Globalization;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Screens
{
    /// <summary>
    /// The title screen: CONTINUE, NEW GAME, SETTINGS over a dark, cinematic ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY CONTINUE is disabled rather than hidden when there is no save: a menu whose buttons move between
    /// launches teaches the player nothing about where things are, and the first launch is exactly when that
    /// matters most. A disabled button with an explanation under it says "this will be here later".
    /// </para>
    /// <para>
    /// WHY the screen shows slot metadata: continuing is the one destructive-feeling choice in the menu —
    /// the player wants to know which run they are about to re-enter before they press it, not after.
    /// </para>
    /// <para>
    /// The screen decides nothing. It reports taps through the callbacks it was constructed with; the
    /// controller turns those into commands.
    /// </para>
    /// </remarks>
    public sealed class MainMenuScreen : UIScreen
    {
        // Keys follow the string table, which is the authority: lowercase segments, snake_case in the
        // final one, under the sub-namespace that already owns the string. Where a row exists for what
        // this screen needs it is reused rather than duplicated under ui.menu.* — the slot wording is
        // the save area's (ui.save.*) and the subtitle is the product's (game.*).
        private static readonly LocKey TitleKey = new LocKey("ui.menu.title");
        private static readonly LocKey SubtitleKey = new LocKey("game.subtitle");
        private static readonly LocKey ContinueKey = new LocKey("ui.menu.continue");
        private static readonly LocKey NewGameKey = new LocKey("ui.menu.new_game");
        private static readonly LocKey SettingsKey = new LocKey("ui.menu.settings");
        private static readonly LocKey NoSaveKey = new LocKey("ui.save.slot_empty");
        private static readonly LocKey SlotSummaryKey = new LocKey("ui.menu.slot_summary");
        private static readonly LocKey UnknownZoneKey = new LocKey("ui.common.unknown");
        private static readonly LocKey DurationKey = new LocKey("ui.save.playtime");

        private readonly Action _onContinue;
        private readonly Action _onNewGame;
        private readonly Action _onSettings;

        private Button _continueButton;
        private Label _slotLabel;

        private bool _hasSave;
        private string _slotText = string.Empty;

        /// <summary>Creates the menu. Nothing is built until the stack shows it.</summary>
        /// <param name="context">Text lookup and logging.</param>
        /// <param name="onContinue">Raised when CONTINUE is pressed. Required.</param>
        /// <param name="onNewGame">Raised when NEW GAME is pressed. Required.</param>
        /// <param name="onSettings">Raised when SETTINGS is pressed. Required.</param>
        /// <exception cref="ArgumentNullException">Any callback is null — a dead menu button is never intended.</exception>
        public MainMenuScreen(IUiContext context, Action onContinue, Action onNewGame, Action onSettings)
            : base(context, "main-menu")
        {
            _onContinue = onContinue ?? throw new ArgumentNullException(nameof(onContinue));
            _onNewGame = onNewGame ?? throw new ArgumentNullException(nameof(onNewGame));
            _onSettings = onSettings ?? throw new ArgumentNullException(nameof(onSettings));

            _slotText = Text(NoSaveKey);
        }

        /// <summary>True when CONTINUE is currently offered.</summary>
        public bool HasSave => _hasSave;

        /// <summary>
        /// Offers CONTINUE and describes the run it would resume.
        /// </summary>
        /// <param name="metadata">
        /// Slot header read without deserializing the whole save. Null is treated as "no save", because a
        /// menu that offers a run it cannot describe is a menu that is about to fail on the next tap.
        /// </param>
        public void ShowContinueSlot(SaveMetadata metadata)
        {
            if (metadata == null)
            {
                ShowNoSave();
                return;
            }

            _hasSave = true;
            _slotText = Summarize(metadata);
            ApplyState();
        }

        /// <summary>Disables CONTINUE and explains why it is there.</summary>
        public void ShowNoSave()
        {
            _hasSave = false;
            _slotText = Text(NoSaveKey);
            ApplyState();
        }

        /// <inheritdoc />
        protected override void Build(VisualElement root)
        {
            root.style.backgroundColor = Theme.Background;

            BuildBackdrop(root);

            var content = new VisualElement { name = "menu-content" };
            content.style.flexGrow = 1f;
            content.style.flexDirection = FlexDirection.Column;
            content.style.paddingLeft = Theme.Space24;
            content.style.paddingRight = Theme.Space24;
            content.style.paddingTop = Theme.Space32;
            content.style.paddingBottom = Theme.Space24;
            root.Add(content);

            var topSpacer = new VisualElement { name = "menu-spacer-top", pickingMode = PickingMode.Ignore };
            topSpacer.style.flexGrow = 1f;
            content.Add(topSpacer);

            var title = Typography.Display(Text(TitleKey), "menu__title");
            content.Add(title);

            var subtitle = Typography.Body(Text(SubtitleKey), "menu__subtitle");
            subtitle.style.marginTop = Theme.Space8;
            content.Add(subtitle);

            var midSpacer = new VisualElement { name = "menu-spacer-mid", pickingMode = PickingMode.Ignore };
            midSpacer.style.flexGrow = 1f;
            midSpacer.style.minHeight = Theme.Space32;
            content.Add(midSpacer);

            _continueButton = Buttons.Primary(Text(ContinueKey), _onContinue);
            _continueButton.name = "menu-continue";
            content.Add(_continueButton);

            _slotLabel = Typography.Caption(_slotText, "menu__slot");
            _slotLabel.style.marginTop = Theme.Space8;
            _slotLabel.style.marginBottom = Theme.Space20;
            _slotLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            content.Add(_slotLabel);

            var newGame = Buttons.Secondary(Text(NewGameKey), _onNewGame);
            newGame.name = "menu-new-game";
            newGame.style.marginBottom = Theme.Space12;
            content.Add(newGame);

            var settings = Buttons.Ghost(Text(SettingsKey), _onSettings);
            settings.name = "menu-settings";
            content.Add(settings);

            ApplyState();
        }

        /// <summary>
        /// Paints the cinematic ground: a black-green field, a low ocean band and a single soft accent bloom.
        /// </summary>
        /// <remarks>
        /// Three flat elements rather than an image: UI Toolkit has no gradient primitive, and a full-screen
        /// background texture costs memory on every device to render something the player looks at for six
        /// seconds. The bloom sits low and left of centre so the title is not centred on it, which is what
        /// keeps the composition from looking like a splash screen template.
        /// </remarks>
        private static void BuildBackdrop(VisualElement root)
        {
            var backdrop = new VisualElement { name = "menu-backdrop", pickingMode = PickingMode.Ignore };
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0f;
            backdrop.style.right = 0f;
            backdrop.style.top = 0f;
            backdrop.style.bottom = 0f;
            backdrop.style.overflow = Overflow.Hidden;
            root.Add(backdrop);

            var horizon = new VisualElement { name = "menu-horizon", pickingMode = PickingMode.Ignore };
            horizon.style.position = Position.Absolute;
            horizon.style.left = 0f;
            horizon.style.right = 0f;
            horizon.style.bottom = 0f;
            horizon.style.height = Length.Percent(42f);
            horizon.style.backgroundColor = Theme.WithAlpha(Theme.Ocean, 0.9f);
            backdrop.Add(horizon);

            var bloom = new VisualElement { name = "menu-bloom", pickingMode = PickingMode.Ignore };
            bloom.style.position = Position.Absolute;
            bloom.style.width = 420f;
            bloom.style.height = 420f;
            bloom.style.left = -80f;
            bloom.style.bottom = Length.Percent(28f);
            bloom.style.backgroundColor = Theme.WithAlpha(Theme.Accent, 0.12f);
            bloom.style.borderTopLeftRadius = 210f;
            bloom.style.borderTopRightRadius = 210f;
            bloom.style.borderBottomLeftRadius = 210f;
            bloom.style.borderBottomRightRadius = 210f;
            backdrop.Add(bloom);

            var vignette = new VisualElement { name = "menu-vignette", pickingMode = PickingMode.Ignore };
            vignette.style.position = Position.Absolute;
            vignette.style.left = 0f;
            vignette.style.right = 0f;
            vignette.style.top = 0f;
            vignette.style.bottom = 0f;
            vignette.style.backgroundColor = Theme.WithAlpha(Theme.Background, 0.45f);
            backdrop.Add(vignette);
        }

        /// <summary>Pushes the current save state onto the built tree. A no-op before the first build.</summary>
        private void ApplyState()
        {
            if (!IsBuilt)
            {
                return;
            }

            _continueButton.SetEnabled(_hasSave);
            _continueButton.style.opacity = _hasSave ? 1f : 0.45f;
            _slotLabel.text = _slotText;
            _slotLabel.style.color = _hasSave ? Theme.TextSecondary : Theme.TextMuted;
        }

        /// <summary>Renders a slot header as one line: where the run is, how long it has run, how much is recorded.</summary>
        private string Summarize(SaveMetadata metadata)
        {
            var zoneKey = metadata.ZoneDisplayKey;
            var zoneName = string.IsNullOrEmpty(zoneKey)
                ? Text(UnknownZoneKey)
                : Loc.Get(new LocKey(zoneKey));

            return TextFormat(
                SlotSummaryKey,
                zoneName,
                FormatDuration(metadata.PlaytimeSeconds),
                metadata.RecordedPercent.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Renders a playtime as hours and minutes.
        /// </summary>
        /// <remarks>
        /// The numbers are formatted invariantly here and the unit letters come from the pattern, because
        /// only the translation knows whether its language writes "2h 14m", "2 t 14 m" or something else
        /// entirely. Seconds are dropped: nobody reads a save slot to the second.
        /// </remarks>
        private string FormatDuration(double totalSeconds)
        {
            if (totalSeconds < 0d || double.IsNaN(totalSeconds))
            {
                totalSeconds = 0d;
            }

            var totalMinutes = (long)(totalSeconds / 60d);
            var hours = totalMinutes / 60L;
            var minutes = totalMinutes % 60L;

            return TextFormat(
                DurationKey,
                hours.ToString(CultureInfo.InvariantCulture),
                minutes.ToString("00", CultureInfo.InvariantCulture));
        }
    }
}

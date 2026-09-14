using System;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Screens
{
    /// <summary>
    /// The pause panel: RESUME, SAVE, QUIT TO MENU, over a scrim that leaves the zone dimly visible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY a scrim rather than an opaque screen: the player paused to think, and keeping the zone faintly
    /// visible behind the panel keeps them in the place they paused in. It also makes the panel read as
    /// temporary, which is the whole point of a pause.
    /// </para>
    /// <para>
    /// WHY the scrim does not dismiss on tap: RESUME and QUIT sit a few dp apart, and a mis-tap that
    /// silently resumes is harmless while a mis-tap that quits is not. Every exit from this screen is an
    /// explicit press on a labelled button.
    /// </para>
    /// </remarks>
    public sealed class PauseScreen : UIScreen
    {
        // Keys follow the string table: the quit row is ui.pause.quit_to_menu there, and the three
        // status labels already exist as ui.hud.zone, ui.pause.playtime and ui.pause.recorded.
        private static readonly LocKey TitleKey = new LocKey("ui.pause.title");
        private static readonly LocKey HintKey = new LocKey("ui.pause.hint");
        private static readonly LocKey ResumeKey = new LocKey("ui.pause.resume");
        private static readonly LocKey SaveKey = new LocKey("ui.pause.save");
        private static readonly LocKey QuitKey = new LocKey("ui.pause.quit_to_menu");
        private static readonly LocKey ZoneLabelKey = new LocKey("ui.hud.zone");
        private static readonly LocKey PlaytimeLabelKey = new LocKey("ui.pause.playtime");
        private static readonly LocKey RecordedLabelKey = new LocKey("ui.pause.recorded");

        private readonly Action _onResume;
        private readonly Action _onSave;
        private readonly Action _onQuit;

        private Button _saveButton;
        private bool _saveEnabled = true;

        private Label _zoneValue;
        private Label _playtimeValue;
        private Label _recordedValue;

        private string _zoneText = string.Empty;
        private string _playtimeText = string.Empty;
        private string _recordedText = string.Empty;

        /// <summary>Creates the pause panel. Nothing is built until the stack shows it.</summary>
        /// <param name="context">Text lookup and logging.</param>
        /// <param name="onResume">Raised when RESUME is pressed. Required.</param>
        /// <param name="onSave">Raised when SAVE is pressed. Required.</param>
        /// <param name="onQuit">Raised when QUIT TO MENU is pressed. Required.</param>
        /// <exception cref="ArgumentNullException">Any callback is null.</exception>
        public PauseScreen(IUiContext context, Action onResume, Action onSave, Action onQuit)
            : base(context, "pause")
        {
            _onResume = onResume ?? throw new ArgumentNullException(nameof(onResume));
            _onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
            _onQuit = onQuit ?? throw new ArgumentNullException(nameof(onQuit));
        }

        /// <summary>
        /// Enables or disables SAVE.
        /// </summary>
        /// <remarks>
        /// Offered as state rather than as a decision the screen makes: whether a save is possible depends on
        /// the run and the application mode, and neither is visible from here. The controller knows, and it
        /// is better to grey the button out than to let a press bounce off a command validator.
        /// </remarks>
        /// <param name="enabled">True when a save is currently possible.</param>
        public void SetSaveEnabled(bool enabled)
        {
            _saveEnabled = enabled;
            if (_saveButton == null)
            {
                return;
            }

            _saveButton.SetEnabled(enabled);
            _saveButton.style.opacity = enabled ? 1f : 0.45f;
        }

        /// <summary>
        /// Sets the three facts the panel reports about the paused run.
        /// </summary>
        /// <remarks>
        /// Already-rendered text rather than raw numbers, and deliberately so: only the caller knows
        /// which zone key the run is in and which pattern renders a duration, and a screen that took
        /// a <c>double</c> and a percentage would have to grow its own formatting rules — a second
        /// place for them to drift from the save-slot card that shows the same three facts.
        /// </remarks>
        /// <param name="zone">Display name of the zone the run is in. Empty hides the row.</param>
        /// <param name="playtime">Rendered playtime. Empty hides the row.</param>
        /// <param name="recorded">Rendered discovery percentage. Empty hides the row.</param>
        public void SetSessionSummary(string zone, string playtime, string recorded)
        {
            _zoneText = zone ?? string.Empty;
            _playtimeText = playtime ?? string.Empty;
            _recordedText = recorded ?? string.Empty;
            ApplySummary();
        }

        /// <inheritdoc />
        protected override void Build(VisualElement root)
        {
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = Theme.Space24;
            root.style.paddingRight = Theme.Space24;

            var scrim = new VisualElement { name = "pause-scrim" };
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0f;
            scrim.style.right = 0f;
            scrim.style.top = 0f;
            scrim.style.bottom = 0f;
            scrim.style.backgroundColor = Theme.WithAlpha(Theme.Background, 0.82f);

            // Takes input on purpose: while paused, the scrim is what stops a tap reaching the zone below.
            scrim.pickingMode = PickingMode.Position;
            root.Add(scrim);

            var card = new VisualElement { name = "pause-card" };
            card.AddToClassList("card");
            card.style.width = Length.Percent(100f);
            card.style.maxWidth = 340f;
            card.style.flexDirection = FlexDirection.Column;
            card.style.paddingLeft = Theme.Space24;
            card.style.paddingRight = Theme.Space24;
            card.style.paddingTop = Theme.Space24;
            card.style.paddingBottom = Theme.Space24;
            card.style.backgroundColor = Theme.Surface;
            card.style.borderTopLeftRadius = Theme.RadiusLg;
            card.style.borderTopRightRadius = Theme.RadiusLg;
            card.style.borderBottomLeftRadius = Theme.RadiusLg;
            card.style.borderBottomRightRadius = Theme.RadiusLg;
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopColor = Theme.Border;
            card.style.borderBottomColor = Theme.Border;
            card.style.borderLeftColor = Theme.Border;
            card.style.borderRightColor = Theme.Border;
            root.Add(card);

            var title = Typography.Title(Text(TitleKey), "pause__title");
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(title);

            var hint = Typography.Caption(Text(HintKey), "pause__hint");
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.marginTop = Theme.Space8;
            hint.style.marginBottom = Theme.Space20;
            card.Add(hint);

            var status = new VisualElement { name = "pause-status", pickingMode = PickingMode.Ignore };
            status.style.flexDirection = FlexDirection.Column;
            status.style.marginBottom = Theme.Space20;
            card.Add(status);

            _zoneValue = AddStatusRow(status, "pause-status-zone", Text(ZoneLabelKey));
            _playtimeValue = AddStatusRow(status, "pause-status-playtime", Text(PlaytimeLabelKey));
            _recordedValue = AddStatusRow(status, "pause-status-recorded", Text(RecordedLabelKey));
            ApplySummary();

            var resume = Buttons.Primary(Text(ResumeKey), _onResume);
            resume.name = "pause-resume";
            resume.style.marginBottom = Theme.Space12;
            card.Add(resume);

            _saveButton = Buttons.Secondary(Text(SaveKey), _onSave);
            _saveButton.name = "pause-save";
            _saveButton.style.marginBottom = Theme.Space12;
            card.Add(_saveButton);

            var quit = Buttons.Ghost(Text(QuitKey), _onQuit);
            quit.name = "pause-quit";
            quit.style.color = Theme.Danger;
            card.Add(quit);

            SetSaveEnabled(_saveEnabled);
        }

        /// <summary>
        /// Adds one label-and-value line to the status block and returns the value element.
        /// </summary>
        /// <remarks>
        /// The label is resolved by the caller and passed in, so this helper never touches a key and
        /// therefore cannot introduce an untranslated literal.
        /// </remarks>
        /// <param name="parent">The status block.</param>
        /// <param name="name">Element name, for UI debugging.</param>
        /// <param name="label">Already-localized label text.</param>
        /// <returns>The value label, to be filled by <see cref="SetSessionSummary"/>.</returns>
        private static Label AddStatusRow(VisualElement parent, string name, string label)
        {
            var row = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            row.AddToClassList("pause-status__row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = Theme.Space4;
            parent.Add(row);

            var labelElement = Typography.Caption(label, "pause-status__label");
            labelElement.style.flexShrink = 1f;
            row.Add(labelElement);

            var valueElement = Typography.Caption(string.Empty, "pause-status__value");
            valueElement.style.color = Theme.Text;
            valueElement.style.marginLeft = Theme.Space16;
            valueElement.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(valueElement);

            return valueElement;
        }

        /// <summary>
        /// Pushes the stored summary onto the built tree, hiding any row with nothing to say.
        /// </summary>
        /// <remarks>
        /// A row is hidden rather than shown empty because a label with a blank value reads as a bug;
        /// a run with no recorded discoveries yet, or a boot that adopted a zone with no display key,
        /// is a normal state and should simply show one line fewer.
        /// </remarks>
        private void ApplySummary()
        {
            ApplyRow(_zoneValue, _zoneText);
            ApplyRow(_playtimeValue, _playtimeText);
            ApplyRow(_recordedValue, _recordedText);
        }

        /// <summary>Fills one status value and shows or hides the row that holds it.</summary>
        private static void ApplyRow(Label value, string text)
        {
            if (value == null)
            {
                return;
            }

            value.text = text;

            var row = value.parent;
            if (row != null)
            {
                row.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
    }
}

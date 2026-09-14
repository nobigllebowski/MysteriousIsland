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
        private static readonly LocKey TitleKey = new LocKey("ui.pause.title");
        private static readonly LocKey HintKey = new LocKey("ui.pause.hint");
        private static readonly LocKey ResumeKey = new LocKey("ui.pause.resume");
        private static readonly LocKey SaveKey = new LocKey("ui.pause.save");
        private static readonly LocKey QuitKey = new LocKey("ui.pause.quit");

        private readonly Action _onResume;
        private readonly Action _onSave;
        private readonly Action _onQuit;

        private Button _saveButton;
        private bool _saveEnabled = true;

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
            hint.style.marginBottom = Theme.Space24;
            card.Add(hint);

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
    }
}

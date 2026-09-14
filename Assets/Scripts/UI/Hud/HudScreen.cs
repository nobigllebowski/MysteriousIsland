using ForgottenIsle.Core.Primitives;
using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Hud
{
    /// <summary>
    /// The in-world HUD: one objective line, one interaction prompt, one narration line, one button.
    /// </summary>
    /// <remarks>
    /// Four elements is the entire heads-up display, and that is the design. The Mobile UX Plan's
    /// rule is diegetic minimalism — the screen belongs to the island, and anything permanently
    /// drawn over it has to earn the space. A quest log, a compass, a minimap and a vitals cluster
    /// would each be a decision to show the player a panel instead of a place.
    /// <para>
    /// Nothing here reads game state. The controller feeds it strings; it renders them. That is what
    /// keeps gameplay code out of VisualElements.
    /// </para>
    /// </remarks>
    public sealed class HudScreen : UIScreen
    {
        private static readonly LocKey ObjectiveLabelKey = new LocKey("ui.hud.objective_label");
        private static readonly LocKey PauseKey = new LocKey("ui.hud.pause");

        /// <summary>Seconds a narration line stays up before fading.</summary>
        private const long NarrationVisibleMs = 6400;

        private readonly System.Action _onPause;

        private Label _objectiveLabel;
        private Label _objectiveText;
        private VisualElement _promptCard;
        private Label _promptName;
        private Label _promptVerb;
        private Label _narration;
        private VisualElement _narrationCard;
        private TouchControls _touch;
        private IVisualElementScheduledItem _narrationTimer;

        /// <param name="context">Localization and logging.</param>
        /// <param name="onPause">Raised when the pause button is tapped. Required.</param>
        public HudScreen(IUiContext context, System.Action onPause)
            : base(context, "hud")
        {
            _onPause = onPause ?? throw new System.ArgumentNullException(nameof(onPause));
        }

        /// <summary>The thumb controls, so the input bridge can read them.</summary>
        public TouchControls Touch => _touch;

        /// <inheritdoc />
        protected override void Build(VisualElement root)
        {
            // The HUD must not swallow taps meant for the world underneath; only its own controls
            // are pickable.
            root.pickingMode = PickingMode.Ignore;

            _touch = new TouchControls(root);

            BuildObjective(root);
            BuildPauseButton(root);
            BuildPrompt(root);
            BuildNarration(root);
        }

        /// <summary>Sets the objective line. Empty hides the whole block.</summary>
        /// <param name="text">Already-localized objective text.</param>
        public void SetObjective(string text)
        {
            // A screen builds lazily, on first show. The HUD is told to hide its controls the moment
            // the app reaches the main menu -- before it has ever been shown, so before Build has run
            // and while every field here is still null. Asking a screen that does not exist yet to
            // hide something is a reasonable thing for a caller to do; throwing at it is not.
            if (!IsBuilt)
            {
                return;
            }

            var show = !string.IsNullOrEmpty(text);
            _objectiveLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _objectiveText.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _objectiveText.text = text ?? string.Empty;
        }

        /// <summary>Shows or hides the interaction prompt.</summary>
        /// <param name="name">Already-localized object name.</param>
        /// <param name="verb">Already-localized verb.</param>
        /// <param name="visible">False when nothing is in range.</param>
        public void SetPrompt(string name, string verb, bool visible)
        {
            if (!IsBuilt)
            {
                return;
            }

            _promptCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            _promptName.text = name ?? string.Empty;
            _promptVerb.text = verb ?? string.Empty;
        }

        /// <summary>Shows a line of narration, replacing any line already up.</summary>
        /// <param name="text">Already-localized line. Empty hides the card.</param>
        public void ShowNarration(string text)
        {
            if (!IsBuilt)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                _narrationCard.style.display = DisplayStyle.None;
                return;
            }

            _narration.text = text;
            _narrationCard.style.display = DisplayStyle.Flex;

            // Re-reading a marker restarts the timer rather than stacking a second one, so the line
            // is always visible for its full duration from the most recent read.
            _narrationTimer?.Pause();
            _narrationTimer = _narrationCard.schedule
                .Execute(() => _narrationCard.style.display = DisplayStyle.None)
                .StartingIn(NarrationVisibleMs);
        }

        /// <summary>Shows or hides the touch controls.</summary>
        /// <param name="visible">False while paused, loading, or in a menu.</param>
        public void SetControlsVisible(bool visible)
        {
            if (!IsBuilt)
            {
                return;
            }

            _touch?.SetVisible(visible);
        }

        private void BuildObjective(VisualElement root)
        {
            var block = new VisualElement { name = "objective" };
            block.style.position = Position.Absolute;
            block.style.left = Theme.Space16;
            block.style.top = Theme.Space16;
            block.style.maxWidth = Length.Percent(66f);
            block.pickingMode = PickingMode.Ignore;
            root.Add(block);

            _objectiveLabel = Typography.Caption(Loc.Get(ObjectiveLabelKey));
            _objectiveLabel.style.color = Theme.TextMuted;
            _objectiveLabel.style.letterSpacing = 2f;
            _objectiveLabel.pickingMode = PickingMode.Ignore;
            block.Add(_objectiveLabel);

            _objectiveText = Typography.Body(string.Empty);
            _objectiveText.style.color = Theme.Text;
            _objectiveText.style.whiteSpace = WhiteSpace.Normal;
            _objectiveText.pickingMode = PickingMode.Ignore;
            block.Add(_objectiveText);
        }

        private void BuildPauseButton(VisualElement root)
        {
            var pause = Buttons.Ghost(Loc.Get(PauseKey), _onPause);
            pause.name = "pause-button";
            pause.style.position = Position.Absolute;
            pause.style.right = Theme.Space16;
            pause.style.top = Theme.Space16;

            // 48dp minimum touch target, per the UX plan's accessibility commitment. The button is
            // visually small and dark; the tappable area is not.
            pause.style.minWidth = 48f;
            pause.style.minHeight = 48f;
            root.Add(pause);
        }

        private void BuildPrompt(VisualElement root)
        {
            _promptCard = new VisualElement { name = "prompt" };
            _promptCard.style.position = Position.Absolute;
            _promptCard.style.left = 0;
            _promptCard.style.right = 0;

            // Above the thumb, not under it: at the bottom centre a prompt sits exactly where the
            // right hand covers the screen while looking around.
            _promptCard.style.bottom = Length.Percent(26f);
            _promptCard.style.alignItems = Align.Center;
            _promptCard.style.display = DisplayStyle.None;
            _promptCard.pickingMode = PickingMode.Ignore;
            root.Add(_promptCard);

            var card = new VisualElement();
            card.style.backgroundColor = Theme.Surface;
            card.style.paddingLeft = Theme.Space16;
            card.style.paddingRight = Theme.Space16;
            card.style.paddingTop = Theme.Space8;
            card.style.paddingBottom = Theme.Space8;
            card.style.borderTopLeftRadius = Theme.RadiusSm;
            card.style.borderTopRightRadius = Theme.RadiusSm;
            card.style.borderBottomLeftRadius = Theme.RadiusSm;
            card.style.borderBottomRightRadius = Theme.RadiusSm;
            card.style.alignItems = Align.Center;
            card.pickingMode = PickingMode.Ignore;
            _promptCard.Add(card);

            _promptName = Typography.Body(string.Empty);
            _promptName.style.color = Theme.Text;
            _promptName.pickingMode = PickingMode.Ignore;
            card.Add(_promptName);

            _promptVerb = Typography.Caption(string.Empty);
            _promptVerb.style.color = Theme.Gold;
            _promptVerb.style.letterSpacing = 3f;
            _promptVerb.pickingMode = PickingMode.Ignore;
            card.Add(_promptVerb);
        }

        private void BuildNarration(VisualElement root)
        {
            _narrationCard = new VisualElement { name = "narration" };
            _narrationCard.style.position = Position.Absolute;
            _narrationCard.style.left = Theme.Space24;
            _narrationCard.style.right = Theme.Space24;
            _narrationCard.style.bottom = Length.Percent(12f);
            _narrationCard.style.display = DisplayStyle.None;
            _narrationCard.pickingMode = PickingMode.Ignore;
            root.Add(_narrationCard);

            _narration = Typography.Body(string.Empty);
            _narration.style.color = Theme.TextSecondary;
            _narration.style.whiteSpace = WhiteSpace.Normal;
            _narration.style.unityTextAlign = TextAnchor.MiddleCenter;
            _narration.pickingMode = PickingMode.Ignore;
            _narrationCard.Add(_narration);
        }
    }
}

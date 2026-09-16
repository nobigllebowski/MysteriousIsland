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
        private static readonly LocKey InventoryTabKey = new LocKey("ui.hud.inventory_open");
        private static readonly LocKey InventoryHeadingKey = new LocKey("ui.hud.inventory");
        private static readonly LocKey InventoryHintKey = new LocKey("ui.hud.combine_hint");
        private static readonly LocKey RadioCloseKey = new LocKey("ui.radio.close");
        private static readonly LocKey RadioMicKey = new LocKey("ui.radio.mic");
        private static readonly LocKey RadioFineKey = new LocKey("ui.radio.fine");
        private static readonly LocKey RadioUnitKey = new LocKey("ui.radio.unit");
        private static readonly LocKey SlateTabKey = new LocKey("ui.hud.slate_open");
        private static readonly LocKey InventoryEmptyKey = new LocKey("ui.hud.inventory_empty");

        /// <summary>Milliseconds a narration line stays up before it has been read at all.</summary>
        private const long NarrationBaseMs = 3200;

        /// <summary>Milliseconds per character on top of the base. ~45 is a comfortable reading pace.</summary>
        private const long NarrationPerCharMs = 45;

        /// <summary>Longest any one line stays up. The battery-door inscription is the case.</summary>
        private const long NarrationMaxMs = 14000;

        private readonly System.Action _onPause;

        private Label _objectiveLabel;
        private Label _objectiveText;
        private VisualElement _promptCard;
        private Label _promptName;
        private Label _promptVerb;
        private Label _narration;
        private VisualElement _narrationCard;
        private TouchControls _touch;
        private InventoryPanel _inventory;
        private RadioPanel _radio;
        private Button _slateTab;
        private Label _slateBadge;
        private IVisualElementScheduledItem _sequenceTimer;
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

        /// <summary>The carried-items tray. Null until the screen has been built.</summary>
        public InventoryPanel Inventory => _inventory;


        /// <summary>The tuning band. Null until the screen has been built.</summary>
        public RadioPanel Radio => _radio;

        /// <summary>Raised with the frequency the thumb asks for on the dial.</summary>
        public event System.Action<float> RadioTuneRequested;

        /// <summary>Raised when the mic is squeezed.</summary>
        public event System.Action RadioMicSqueezed;

        /// <summary>Raised when the player puts the radio down.</summary>
        public event System.Action RadioCloseRequested;

        /// <summary>
        /// Raised when the prompt card is tapped. On touch this IS the world verb.
        /// </summary>
        /// <remarks>
        /// There is no touchscreen binding for Interact, on purpose: an action bound to the raw
        /// tap fires REGARDLESS of what UI element was under the finger (VERIFY on device; see the
        /// note in VardholmControls), so every tap on the pause button or a chip was also the world
        /// verb. The card is a real control now, 48 dp tall, and what it does is decided by the
        /// router's gate, not here.
        /// </remarks>
        public event System.Action InteractRequested;

        /// <summary>Raised when the Slate tab is tapped.</summary>
        public event System.Action SlateRequested;

        /// <inheritdoc />
        protected override void Build(VisualElement root)
        {
            // The HUD must not swallow taps meant for the world underneath; only its own controls
            // are pickable.
            root.pickingMode = PickingMode.Ignore;

            _touch = new TouchControls(root);

            BuildObjective(root);
            BuildPauseButton(root);
            BuildSlateTab(root);
            BuildPrompt(root);
            BuildNarration(root);

            _inventory = new InventoryPanel(
                root,
                Loc.Get(InventoryTabKey),
                Loc.Get(InventoryHeadingKey),
                Loc.Get(InventoryEmptyKey),
                Loc.Get(InventoryHintKey));
            _inventory.ItemTapped += id =>
            {
                var handler = ItemTapped;
                if (handler != null)
                {
                    handler(id);
                }
            };

            _radio = new RadioPanel(
                root,
                Loc.Get(RadioCloseKey),
                Loc.Get(RadioMicKey),
                Loc.Get(RadioFineKey),
                Loc.Get(RadioUnitKey));
            _radio.TuneRequested += mhz =>
            {
                var handler = RadioTuneRequested;
                if (handler != null)
                {
                    handler(mhz);
                }
            };
            _radio.MicSqueezed += () =>
            {
                var handler = RadioMicSqueezed;
                if (handler != null)
                {
                    handler();
                }
            };
            _radio.CloseRequested += () =>
            {
                var handler = RadioCloseRequested;
                if (handler != null)
                {
                    handler();
                }
            };
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

            ShowLine(text, VisibleMsFor(text));
        }

        /// <summary>Puts a line on the card for a given time. The one place the card is written.</summary>
        private void ShowLine(string text, long visibleMs)
        {
            _narration.text = text;
            _narrationCard.style.display = DisplayStyle.Flex;

            // Re-reading a marker restarts the timer rather than stacking a second one, so the line
            // is always visible for its full duration from the most recent read.
            _narrationTimer?.Pause();
            _narrationTimer = _narrationCard.schedule
                .Execute(() => _narrationCard.style.display = DisplayStyle.None)
                .StartingIn(visibleMs);
        }

        /// <summary>How long a line stays up: long enough to read, scaled by its length, capped.</summary>
        public static long VisibleMsFor(string text)
        {
            var length = text != null ? text.Length : 0;
            var ms = NarrationBaseMs + NarrationPerCharMs * length;
            return ms > NarrationMaxMs ? NarrationMaxMs : ms;
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

            // The tray goes with the controls. Left up while paused it would offer a combination
            // the handler is going to refuse for being out of state, which reads as a broken
            // button rather than as a rule.
            _inventory?.SetVisible(visible);

            if (!visible)
            {
                // The dial goes down with the controls. Its own open/closed state is the game's,
                // and the controller will put it back up if the game still says it is open.
                SetRadioVisible(false);
            }

            // Narration timers stop with the game. A transmission that keeps stepping through a
            // pause menu is read by nobody, and one that finishes during quit-to-menu leaves its
            // last line on the next run's HUD.
            if (visible)
            {
                _narrationTimer?.Resume();
                _sequenceTimer?.Resume();
            }
            else
            {
                _narrationTimer?.Pause();
                _sequenceTimer?.Pause();
            }
        }

        /// <summary>Raised when a tray chip is tapped, with its item id.</summary>
        public event System.Action<string> ItemTapped;

        /// <summary>Draws one chip as held, or none.</summary>
        public void SetHeldItem(string id)
        {
            _inventory?.SetHeld(id);
        }

        /// <summary>Replaces what the tray shows.</summary>
        /// <remarks>
        /// Restored after a rewrite of the narration sequence sliced it out of the file while the
        /// controller kept calling it -- a compile error neither CI gate could see, because
        /// neither checks that a called member exists. The validator now does.
        /// </remarks>
        /// <param name="items">Item ids paired with already-localized names.</param>
        public void SetInventory(System.Collections.Generic.IReadOnlyList<InventoryItemView> items)
        {
            if (!IsBuilt)
            {
                return;
            }

            _inventory.SetItems(items);
        }

        /// <summary>Shows or hides the tuning band, and moves the cards above it while it is up.</summary>
        /// <remarks>
        /// The band is the last child of the HUD root and covers the bottom third, and UI Toolkit
        /// paints later siblings over earlier ones (VERIFY:
        /// https://docs.unity3d.com/Manual/UIE-VisualTree.html). Left where they were, the prompt
        /// and the narration card -- the transmission itself -- would be drawn underneath the dial
        /// the player is looking at. So they climb above it while it is open.
        /// </remarks>
        public void SetRadioVisible(bool visible)
        {
            if (!IsBuilt)
            {
                return;
            }

            _radio.SetVisible(visible);
            _narrationCard.style.bottom = Length.Percent(visible ? 37f : 12f);
            _promptCard.style.bottom = Length.Percent(visible ? 46f : 26f);
        }

        /// <summary>Draws the tuning state the game reports.</summary>
        /// <param name="mhz">Needle position.</param>
        /// <param name="reception">Reception tier at the needle.</param>
        /// <param name="stationText">Already-localized caption for what is being received, or empty.</param>
        public void SetRadioState(float mhz, ForgottenIsle.Core.Radio.Reception reception, string stationText)
        {
            if (!IsBuilt)
            {
                return;
            }

            _radio.SetState(mhz, reception, stationText);
        }

        /// <summary>
        /// Shows several lines of narration one after another, each for as long as it takes to read.
        /// </summary>
        /// <remarks>
        /// For the transmission, which is forty-four seconds of someone reading a list and cannot
        /// be one card. Each line stays up for its own reading time (<see cref="VisibleMsFor"/>)
        /// and is replaced by the next; nothing goes blank between lines, and the last line fades
        /// on its own clock. A single line from elsewhere shows at once and the sequence takes the
        /// card back on its next step -- a stray prompt during the transmission costs one line, not
        /// the transmission. A new sequence replaces a running one.
        /// </remarks>
        /// <param name="lines">Already-localized lines, in order. Copied; the caller may reuse the list.</param>
        public void ShowNarrationSequence(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            if (!IsBuilt || lines == null || lines.Count == 0)
            {
                return;
            }

            _sequenceTimer?.Pause();

            var copy = new string[lines.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = lines[i] ?? string.Empty;
            }

            var index = 0;
            ShowLine(copy[0], VisibleMsFor(copy[0]));
            if (copy.Length == 1)
            {
                return;
            }

            // Each step shows the next line and re-arms itself for that line's own duration.
            // The scheduler's Every() cannot vary its interval per step, so the step re-schedules.
            System.Action step = null;
            step = () =>
            {
                index++;
                if (index >= copy.Length)
                {
                    return;
                }

                ShowLine(copy[index], VisibleMsFor(copy[index]));
                if (index < copy.Length - 1)
                {
                    _sequenceTimer = _narrationCard.schedule.Execute(step).StartingIn(VisibleMsFor(copy[index]));
                }
            };

            _sequenceTimer = _narrationCard.schedule.Execute(step).StartingIn(VisibleMsFor(copy[0]));
        }

        /// <summary>Sets the number on the Slate tab. Zero hides it.</summary>
        public void SetOpenQuestions(int count)
        {
            if (!IsBuilt)
            {
                return;
            }

            _slateBadge.text = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        }

        private void BuildSlateTab(VisualElement root)
        {
            // Under the pause button, above the Items tab's row: the top strip, where no thumb rests.
            // The design puts the notebook's tab on the bottom edge; on this game's portrait layout
            // the bottom edge is where both thumbs live, so it joins the other top-right controls.
            _slateTab = Buttons.Ghost(Loc.Get(SlateTabKey), () =>
            {
                var handler = SlateRequested;
                if (handler != null)
                {
                    handler();
                }
            });
            _slateTab.name = "slate-tab";
            _slateTab.style.position = Position.Absolute;
            _slateTab.style.right = Theme.Space16;
            _slateTab.style.top = 72f + 48f + Theme.Space8;
            _slateTab.style.minWidth = 48f;
            _slateTab.style.minHeight = 48f;
            root.Add(_slateTab);

            _slateBadge = Typography.Caption(string.Empty);
            _slateBadge.style.position = Position.Absolute;
            _slateBadge.style.top = 2f;
            _slateBadge.style.right = 4f;
            _slateBadge.style.color = Theme.Gold;
            _slateBadge.pickingMode = PickingMode.Ignore;
            _slateTab.Add(_slateBadge);
        }

        private void RaiseInteractRequested()
        {
            var handler = InteractRequested;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>Takes any narration off the card and stops its timers. A run ended.</summary>
        public void ClearNarration()
        {
            if (!IsBuilt)
            {
                return;
            }

            _narrationTimer?.Pause();
            _narrationTimer = null;
            _sequenceTimer?.Pause();
            _sequenceTimer = null;
            _narrationCard.style.display = DisplayStyle.None;
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

            // The card is a button. Tapping the prompt is how a thumb interacts; the strip around
            // it stays unpickable so the look pad underneath still gets the rest of the screen.
            var card = new Button(RaiseInteractRequested);
            card.name = "prompt-card";
            card.style.minHeight = 48f;
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
            card.text = string.Empty;
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

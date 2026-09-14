using System;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Screens
{
    /// <summary>
    /// Settings: the active language, a comfort preference, and the build string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the language is shown but not changed here: Phase 1 ships one locale table, and a picker with one
    /// entry is a control that teaches the player it does nothing. Showing the active locale is still worth
    /// the row — it is the first thing anyone checks when the text comes out wrong.
    /// </para>
    /// <para>
    /// WHY the build string is on this screen: it is the number a player reads out in a bug report, and a
    /// settings screen is the one place they can be told to find without a screenshot.
    /// </para>
    /// </remarks>
    public sealed class SettingsScreen : UIScreen
    {
        // Keys follow the string table: the language and version rows are ui.settings.language and
        // ui.settings.version there, BACK is shared chrome (ui.common.back) rather than a settings
        // string of its own, and an absent build string reuses ui.common.unknown.
        private static readonly LocKey TitleKey = new LocKey("ui.settings.title");
        private static readonly LocKey LanguageLabelKey = new LocKey("ui.settings.language");
        private static readonly LocKey ComfortLabelKey = new LocKey("ui.settings.comfort");
        private static readonly LocKey ComfortHintKey = new LocKey("ui.settings.comfort_hint");
        private static readonly LocKey VersionKey = new LocKey("ui.settings.version");
        private static readonly LocKey UnknownKey = new LocKey("ui.common.unknown");
        private static readonly LocKey BackKey = new LocKey("ui.common.back");

        /// <summary>Prefix of the per-locale display-name keys, e.g. <c>ui.locale.en.name</c>.</summary>
        private const string LocaleNamePrefix = "ui.locale.";

        /// <summary>Suffix of the per-locale display-name keys.</summary>
        private const string LocaleNameSuffix = ".name";

        private readonly Action _onBack;
        private readonly string _buildVersion;

        private Toggle _comfortToggle;
        private bool _comfortEnabled;

        /// <summary>Creates the settings screen. Nothing is built until the stack shows it.</summary>
        /// <param name="context">Text lookup and logging.</param>
        /// <param name="buildVersion">Build string to display. Empty renders as the unknown-version text.</param>
        /// <param name="onBack">Raised when BACK is pressed. Required — this screen must always be leavable.</param>
        /// <exception cref="ArgumentNullException"><paramref name="onBack"/> is null.</exception>
        public SettingsScreen(IUiContext context, string buildVersion, Action onBack)
            : base(context, "settings")
        {
            _onBack = onBack ?? throw new ArgumentNullException(nameof(onBack));
            _buildVersion = buildVersion ?? string.Empty;
        }

        /// <summary>
        /// The comfort preference's current value.
        /// </summary>
        /// <remarks>
        /// A real preference with no consumer yet: the systems it will damp — camera shake, head bob, the
        /// motion in zone transitions — arrive in a later phase. It is here now because a comfort option
        /// retro-fitted after those systems ship is a comfort option that reaches nobody who needed it, and
        /// because the row proves the plumbing (<see cref="ComfortChanged"/>) works before anything depends
        /// on it.
        /// </remarks>
        public bool ComfortEnabled => _comfortEnabled;

        /// <summary>Raised when the player changes the comfort preference. Carries the new value.</summary>
        public event Action<bool> ComfortChanged;

        /// <summary>
        /// Sets the comfort preference without raising <see cref="ComfortChanged"/>.
        /// </summary>
        /// <remarks>
        /// Silent because this is how a stored preference is pushed into the screen at startup, and echoing
        /// that back as a change would have the screen tell its owner something the owner just said.
        /// </remarks>
        /// <param name="enabled">The stored value.</param>
        public void SetComfortEnabled(bool enabled)
        {
            _comfortEnabled = enabled;
            if (_comfortToggle != null)
            {
                _comfortToggle.SetValueWithoutNotify(enabled);
            }
        }

        /// <inheritdoc />
        protected override void Build(VisualElement root)
        {
            root.style.backgroundColor = Theme.Background;
            root.style.paddingLeft = Theme.Space24;
            root.style.paddingRight = Theme.Space24;
            root.style.paddingTop = Theme.Space32;
            root.style.paddingBottom = Theme.Space24;

            var title = Typography.Title(Text(TitleKey), "settings__title");
            title.style.marginBottom = Theme.Space24;
            root.Add(title);

            var card = BuildCard();
            root.Add(card);

            card.Add(BuildValueRow(Text(LanguageLabelKey), LocaleDisplayName()));
            card.Add(BuildSeparator());
            card.Add(BuildComfortRow());
            card.Add(BuildSeparator());

            // The build string is a value row rather than a formatted line, because
            // ui.settings.version is a LABEL in the string table ("Version") and has no placeholder:
            // formatting the build number into it would have rendered the label alone and silently
            // dropped the one number this screen exists to show.
            card.Add(BuildValueRow(Text(VersionKey), VersionText()));

            var spacer = new VisualElement { name = "settings-spacer", pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1f;
            spacer.style.minHeight = Theme.Space24;
            root.Add(spacer);

            var back = Buttons.Secondary(Text(BackKey), _onBack);
            back.name = "settings-back";
            root.Add(back);
        }

        /// <summary>Creates the panel the setting rows live in.</summary>
        private static VisualElement BuildCard()
        {
            var card = new VisualElement { name = "settings-card" };
            card.AddToClassList("card");
            card.style.flexDirection = FlexDirection.Column;
            card.style.paddingLeft = Theme.Space16;
            card.style.paddingRight = Theme.Space16;
            card.style.paddingTop = Theme.Space8;
            card.style.paddingBottom = Theme.Space8;
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
            return card;
        }

        /// <summary>A hairline between rows, inset so it does not touch the card's rounded edge.</summary>
        private static VisualElement BuildSeparator()
        {
            var line = new VisualElement { name = "settings-separator", pickingMode = PickingMode.Ignore };
            line.style.height = 1f;
            line.style.backgroundColor = Theme.Border;
            return line;
        }

        /// <summary>A read-only row: label on the left, value on the right.</summary>
        private static VisualElement BuildValueRow(string label, string value)
        {
            var row = NewRow("settings-row-value");

            var labelElement = Typography.Body(label, "settings-row__label");
            labelElement.style.color = Theme.Text;
            labelElement.style.flexShrink = 1f;
            row.Add(labelElement);

            var valueElement = Typography.Mono(value, "settings-row__value");
            valueElement.style.marginLeft = Theme.Space16;
            row.Add(valueElement);

            return row;
        }

        /// <summary>The comfort row: label and hint on the left, toggle on the right.</summary>
        private VisualElement BuildComfortRow()
        {
            var row = NewRow("settings-row-comfort");

            var texts = new VisualElement { name = "settings-row__texts", pickingMode = PickingMode.Ignore };
            texts.style.flexDirection = FlexDirection.Column;
            texts.style.flexShrink = 1f;
            texts.style.marginRight = Theme.Space16;
            row.Add(texts);

            var label = Typography.Body(Text(ComfortLabelKey), "settings-row__label");
            label.style.color = Theme.Text;
            texts.Add(label);

            var hint = Typography.Caption(Text(ComfortHintKey), "settings-row__hint");
            hint.style.marginTop = Theme.Space4;
            texts.Add(hint);

            _comfortToggle = new Toggle { name = "settings-comfort-toggle", value = _comfortEnabled };
            _comfortToggle.AddToClassList("settings-row__toggle");

            // The toggle is the only control in this row, so it carries the row's touch target itself.
            _comfortToggle.style.minHeight = Buttons.MinimumTouchTargetDp;
            _comfortToggle.style.minWidth = Buttons.MinimumTouchTargetDp;
            _comfortToggle.style.justifyContent = Justify.Center;
            _comfortToggle.style.alignItems = Align.Center;
            _comfortToggle.style.marginLeft = 0f;
            _comfortToggle.style.marginRight = 0f;
            _comfortToggle.RegisterValueChangedCallback(evt =>
            {
                _comfortEnabled = evt.newValue;
                ComfortChanged?.Invoke(evt.newValue);
            });
            row.Add(_comfortToggle);

            return row;
        }

        /// <summary>Creates the shared settings-row shape.</summary>
        private static VisualElement NewRow(string name)
        {
            var row = new VisualElement { name = name };
            row.AddToClassList("settings-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.minHeight = Buttons.MinimumTouchTargetDp;
            row.style.paddingTop = Theme.Space8;
            row.style.paddingBottom = Theme.Space8;
            return row;
        }

        /// <summary>
        /// The active locale's display name, in that locale.
        /// </summary>
        /// <remarks>
        /// Looked up as <c>ui.locale.&lt;code&gt;.name</c> so adding a language adds a row of text rather
        /// than a branch of code. A locale with no name entry falls back to its raw code, which is still
        /// useful to a player reading it out in a bug report — better than <c>#ui.locale.xx.name#</c> in a
        /// screen the player is expected to use.
        /// </remarks>
        private string LocaleDisplayName()
        {
            var code = Loc.CurrentLocale;
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }

            var key = new LocKey(LocaleNamePrefix + code + LocaleNameSuffix);
            return Loc.Has(key) ? Loc.Get(key) : code;
        }

        /// <summary>The build string, or the shared "unknown" text when nothing was supplied.</summary>
        private string VersionText()
        {
            return string.IsNullOrEmpty(_buildVersion) ? Text(UnknownKey) : _buildVersion;
        }
    }
}

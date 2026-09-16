using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Screens
{
    /// <summary>
    /// The Field Slate: Nadia's notebook, full screen, three tabs along the fore-edge.
    /// </summary>
    /// <remarks>
    /// From <c>design/04-first-30-minutes.md</c> §3:30. OBSERVED is what she has seen, PEOPLE who
    /// she thinks is here, UNRESOLVED the questions -- and the number on that tab is the entire
    /// quest system. One tap on the page closes it.
    /// <para>
    /// Paper is a colour and a margin here, not a texture: there is no art pipeline yet, and a
    /// warm off-white page with a ruled left margin reads as a notebook before a texture would
    /// read as anything. The handwriting, the wet-ink reveal and the sketches are art tasks and are
    /// named as such in the changelog rather than faked.
    /// </para>
    /// <para>
    /// It renders lines it is handed. It reads nothing; the controller localizes and feeds it.
    /// </para>
    /// </remarks>
    public sealed class SlateScreen : UIScreen
    {
        private static readonly LocKey ObservedKey = new LocKey("ui.slate.tab.observed");
        private static readonly LocKey PeopleKey = new LocKey("ui.slate.tab.people");
        private static readonly LocKey UnresolvedKey = new LocKey("ui.slate.tab.unresolved");
        private static readonly LocKey EmptyKey = new LocKey("ui.slate.empty");
        private static readonly LocKey HintKey = new LocKey("ui.slate.hint");

        // Paper. Warm, not white; ink, not black. The one place in the game that is not dark.
        private static readonly Color Paper = new Color(0.93f, 0.89f, 0.80f, 1f);
        private static readonly Color Rule = new Color(0.72f, 0.60f, 0.52f, 1f);
        private static readonly Color Ink = new Color(0.16f, 0.14f, 0.16f, 1f);
        private static readonly Color InkFaint = new Color(0.16f, 0.14f, 0.16f, 0.55f);
        private static readonly Color TabDark = new Color(0.20f, 0.18f, 0.17f, 1f);

        private readonly Action _onClose;

        private readonly List<Button> _tabs = new List<Button>(3);
        private readonly Label[] _badges = new Label[3];
        private Label _next;
        private string _nextText = string.Empty;
        private static readonly LocKey NextKey = new LocKey("ui.slate.next");
        private readonly List<string>[] _titles = { new List<string>(), new List<string>(), new List<string>() };
        private readonly List<string>[] _bodies = { new List<string>(), new List<string>(), new List<string>() };
        private readonly List<bool>[] _resolved = { new List<bool>(), new List<bool>(), new List<bool>() };

        private ScrollView _page;
        private int _tab;
        private bool _built;

        /// <param name="context">Localization and logging.</param>
        /// <param name="onClose">Raised by a tap on the page. Required.</param>
        public SlateScreen(IUiContext context, Action onClose)
            : base(context, "slate")
        {
            _onClose = onClose ?? throw new ArgumentNullException(nameof(onClose));
        }

        /// <summary>Tab indices, in fore-edge order.</summary>
        public const int Observed = 0;
        public const int People = 1;
        public const int Unresolved = 2;

        /// <summary>Replaces one tab's lines. Already-localized text; titles and bodies pair by index.</summary>
        public void SetTab(int tab, IReadOnlyList<string> titles, IReadOnlyList<string> bodies, IReadOnlyList<bool> resolved)
        {
            if (tab < 0 || tab > Unresolved)
            {
                return;
            }

            _titles[tab].Clear();
            _bodies[tab].Clear();
            _resolved[tab].Clear();
            var count = titles != null ? titles.Count : 0;
            for (var i = 0; i < count; i++)
            {
                _titles[tab].Add(titles[i] ?? string.Empty);
                _bodies[tab].Add(bodies != null && i < bodies.Count ? bodies[i] ?? string.Empty : string.Empty);
                _resolved[tab].Add(resolved != null && i < resolved.Count && resolved[i]);
            }

            if (_built)
            {
                RefreshBadges();
                if (_tab == tab)
                {
                    RenderPage();
                }
            }
        }

        /// <summary>Opens on a tab. UNRESOLVED when a question was just added is the usual reason.</summary>
        public void ShowTab(int tab)
        {
            _tab = tab < 0 ? 0 : tab > Unresolved ? Unresolved : tab;
            if (_built)
            {
                RenderPage();
                RefreshTabStyles();
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Writes the "Next:" line at the head of the page. Already-localized; empty hides it.
        /// </summary>
        /// <remarks>
        /// The design's rule for a player who comes back mid-puzzle: the notebook says what she
        /// was about to do, at the puzzle's granularity, so the answer she was about to get is
        /// not handed over and the thread is not lost either.
        /// </remarks>
        public void SetNext(string text)
        {
            _nextText = text ?? string.Empty;
            if (_next != null)
            {
                _next.text = _nextText;
                _next.style.display = _nextText.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        protected override void Build(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Row;
            root.style.backgroundColor = TabDark;

            // The page. One tap anywhere on it closes the notebook; the tabs and the scroll are its
            // only other controls, and they stop the tap reaching the page.
            var page = new VisualElement { name = "slate-page" };
            page.style.flexGrow = 1f;
            page.style.backgroundColor = Paper;
            page.style.paddingTop = Theme.Space32;
            page.style.paddingBottom = Theme.Space32;
            page.style.paddingLeft = Theme.Space32;
            page.style.paddingRight = Theme.Space16;
            page.style.borderLeftWidth = 1f;
            page.style.borderLeftColor = Rule;
            page.RegisterCallback<ClickEvent>(_ => _onClose());
            root.Add(page);

            // The ruled margin: a notebook's one unmistakable mark.
            var margin = new VisualElement { name = "slate-margin" };
            margin.style.position = Position.Absolute;
            margin.style.left = Theme.Space24;
            margin.style.top = 0;
            margin.style.bottom = 0;
            margin.style.width = 1f;
            margin.style.backgroundColor = Rule;
            margin.pickingMode = PickingMode.Ignore;
            page.Add(margin);

            var hint = Typography.Caption(Loc.Get(HintKey));
            hint.style.color = InkFaint;
            hint.style.letterSpacing = 2f;
            hint.style.marginBottom = Theme.Space16;
            hint.pickingMode = PickingMode.Ignore;
            page.Add(hint);

            // "Next:" in her hand, above the entries, on every tab.
            _next = Typography.Body(_nextText);
            _next.name = "slate-next";
            _next.style.color = Ink;
            _next.style.unityFontStyleAndWeight = FontStyle.Italic;
            _next.style.marginBottom = Theme.Space16;
            _next.style.display = _nextText.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _next.pickingMode = PickingMode.Ignore;
            page.Add(_next);

            _page = new ScrollView(ScrollViewMode.Vertical) { name = "slate-lines" };
            _page.style.flexGrow = 1f;
            _page.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            page.Add(_page);

            // The fore-edge: three tabs down the right side, the way they sit on the real object.
            var edge = new VisualElement { name = "slate-edge" };
            edge.style.width = 72f;
            edge.style.paddingTop = Theme.Space32;
            edge.style.alignItems = Align.Stretch;
            root.Add(edge);

            AddTab(edge, Observed, Loc.Get(ObservedKey));
            AddTab(edge, People, Loc.Get(PeopleKey));
            AddTab(edge, Unresolved, Loc.Get(UnresolvedKey));

            _built = true;
            RefreshBadges();
            RefreshTabStyles();
            RenderPage();
        }

        private void AddTab(VisualElement edge, int index, string text)
        {
            var tab = Buttons.Ghost(text, () => ShowTab(index));
            tab.name = "slate-tab-" + index;
            tab.style.minHeight = 64f;
            tab.style.marginBottom = Theme.Space8;
            tab.style.whiteSpace = WhiteSpace.Normal;
            tab.style.fontSize = 11f;
            tab.style.letterSpacing = 1.5f;
            edge.Add(tab);
            _tabs.Add(tab);

            // The number. On UNRESOLVED it is the quest system; on the others it is a count.
            var badge = Typography.Caption(string.Empty);
            badge.style.position = Position.Absolute;
            badge.style.top = 4f;
            badge.style.right = 6f;
            badge.style.color = Theme.Gold;
            badge.pickingMode = PickingMode.Ignore;
            tab.Add(badge);
            _badges[index] = badge;
        }

        private void RefreshBadges()
        {
            for (var i = 0; i < 3; i++)
            {
                if (_badges[i] == null)
                {
                    continue;
                }

                var count = i == Unresolved ? OpenQuestions() : _titles[i].Count;
                _badges[i].text = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            }
        }

        private int OpenQuestions()
        {
            var count = 0;
            for (var i = 0; i < _resolved[Unresolved].Count; i++)
            {
                if (!_resolved[Unresolved][i])
                {
                    count++;
                }
            }

            return count;
        }

        private void RefreshTabStyles()
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].style.opacity = i == _tab ? 1f : 0.55f;
            }
        }

        private void RenderPage()
        {
            _page.Clear();

            var titles = _titles[_tab];
            var bodies = _bodies[_tab];
            var resolved = _resolved[_tab];

            if (titles.Count == 0)
            {
                var empty = Typography.Body(Loc.Get(EmptyKey));
                empty.style.color = InkFaint;
                empty.pickingMode = PickingMode.Ignore;
                _page.Add(empty);
                return;
            }

            for (var i = 0; i < titles.Count; i++)
            {
                var entry = new VisualElement();
                entry.style.marginBottom = Theme.Space24;
                entry.pickingMode = PickingMode.Ignore;

                var title = Typography.Caption(titles[i]);
                title.style.color = resolved[i] ? InkFaint : Ink;
                title.style.letterSpacing = 3f;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.whiteSpace = WhiteSpace.Normal;
                title.pickingMode = PickingMode.Ignore;
                entry.Add(title);

                if (!string.IsNullOrEmpty(bodies[i]))
                {
                    var body = Typography.Body(bodies[i]);
                    body.style.color = resolved[i] ? InkFaint : Ink;
                    body.style.whiteSpace = WhiteSpace.Normal;
                    body.style.marginTop = Theme.Space8;
                    body.pickingMode = PickingMode.Ignore;
                    entry.Add(body);
                }

                _page.Add(entry);
            }
        }
    }
}

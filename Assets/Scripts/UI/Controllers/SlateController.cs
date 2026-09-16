using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.UI.Core;
using ForgottenIsle.UI.Screens;

namespace ForgottenIsle.UI.Controllers
{
    /// <summary>
    /// Keeps the Field Slate's pages in step with the record, and opens and closes the notebook.
    /// </summary>
    /// <remarks>
    /// The notebook is a view: the game derives its contents (<see cref="Slate"/>) and announces
    /// them; this translates the keys and hands the screen its lines. Opening it is navigation,
    /// not a state change -- the world keeps going under it, as it does under a real notebook --
    /// so it goes onto the screen stack without a command, the way settings does.
    /// </remarks>
    public sealed class SlateController : IDisposable
    {
        private readonly UIService _ui;
        private readonly SlateScreen _screen;
        private readonly ILocalizedText _loc;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(1);

        private readonly List<string> _titles = new List<string>(12);
        private readonly List<string> _bodies = new List<string>(12);
        private readonly List<bool> _resolved = new List<bool>(12);

        private int _openQuestions;

        /// <param name="ui">The screen stack.</param>
        /// <param name="signals">Bus the notebook arrives on. Null leaves it empty.</param>
        /// <param name="loc">Localization facade.</param>
        public SlateController(UIService ui, SignalBus signals, ILocalizedText loc)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _screen = new SlateScreen(_ui.Context, Close);

            if (signals != null)
            {
                _subscriptions.Add(signals.Subscribe<SlateChangedSignal>(OnSlateChanged));
            }
        }

        /// <summary>The number on the UNRESOLVED tab, for the HUD's badge.</summary>
        public int OpenQuestions => _openQuestions;

        /// <summary>Raised when the open-question count changes, with the new count.</summary>
        public event Action<int> OpenQuestionsChanged;

        /// <summary>Puts the notebook on screen, on the tab given.</summary>
        public void Open(int tab)
        {
            if (_ui.Screens.Current == _screen)
            {
                _screen.ShowTab(tab);
                return;
            }

            _screen.ShowTab(tab);
            _ui.Screens.Push(_screen);
        }

        /// <summary>Takes the notebook off screen if it is up.</summary>
        public void Close()
        {
            if (_ui.Screens.Current == _screen && _ui.Screens.Count > 1)
            {
                _ui.Screens.Pop();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        private void OnSlateChanged(SlateChangedSignal signal)
        {
            var contents = signal.Contents;
            if (contents == null)
            {
                return;
            }

            Feed(SlateScreen.Observed, contents.Observed);
            Feed(SlateScreen.People, contents.People);
            Feed(SlateScreen.Unresolved, contents.Unresolved);

            var open = contents.OpenQuestions;
            if (open != _openQuestions)
            {
                _openQuestions = open;
                var handler = OpenQuestionsChanged;
                if (handler != null)
                {
                    handler(open);
                }
            }
        }

        private void Feed(int tab, IReadOnlyList<SlateLine> lines)
        {
            _titles.Clear();
            _bodies.Clear();
            _resolved.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                _titles.Add(_loc.Get(new LocKey(lines[i].TitleKey)));
                _bodies.Add(_loc.Get(new LocKey(lines[i].BodyKey)));
                _resolved.Add(lines[i].Resolved);
            }

            _screen.SetTab(tab, _titles, _bodies, _resolved);
        }
    }
}

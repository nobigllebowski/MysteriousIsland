// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Core/UIScreen.cs.
// Adapted for Vardholm: the GameContext / country / UiFormat dependencies are replaced by a single narrow
// IUiContext, so a screen structurally cannot reach game state (ADR-0002); EnsureBuilt is public rather than
// internal; the root element is positioned and styled here instead of by a USS class that may not be loaded;
// and the Text/TextFormat helpers were added so screens never handle a raw literal.

using System;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// One full-screen view managed by a <see cref="ScreenStack"/>. Screens build their tree in code from
    /// the shared component factories and read localized text; they never mutate game state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the tree is built lazily in <see cref="Build"/> rather than in the constructor: a screen is often
    /// constructed long before it is first shown (a controller creates it at composition time), and paying
    /// the element-allocation cost during a scene load — the one moment the frame budget is already gone —
    /// is how a menu transition stutters. <see cref="EnsureBuilt"/> is called by the stack immediately
    /// before the first show.
    /// </para>
    /// <para>
    /// WHY the tree is kept after the screen is covered: rebuilding on every return throws away scroll
    /// positions and toggle states and makes going back feel slower than going forward. The stack hides a
    /// covered screen and shows it again untouched; only removal from the stack destroys it.
    /// </para>
    /// </remarks>
    public abstract class UIScreen
    {
        /// <summary>USS hook applied to every screen root.</summary>
        public const string RootClass = "screen";

        /// <summary>
        /// Creates the screen's root element. The tree itself is not built until the first show.
        /// </summary>
        /// <param name="context">The narrow UI surface. Required — a screen with no text lookup cannot render.</param>
        /// <param name="name">Element name, used for UI debugging and USS selectors. Required.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
        protected UIScreen(IUiContext context, string name)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A screen needs a name for UI debugging.", nameof(name));
            }

            Context = context;

            Root = new VisualElement { name = name };
            Root.AddToClassList(RootClass);

            // Screens are siblings that occupy the whole layer, so they must be absolutely positioned:
            // in flow layout a second screen would push the first one off-layer instead of covering it.
            Root.style.position = Position.Absolute;
            Root.style.left = 0f;
            Root.style.right = 0f;
            Root.style.top = 0f;
            Root.style.bottom = 0f;
            Root.style.flexDirection = FlexDirection.Column;
            Root.style.color = Theme.Text;
        }

        /// <summary>The screen's root element. Owned by the screen, parented by the stack.</summary>
        public VisualElement Root { get; }

        /// <summary>True once <see cref="Build"/> has run. Never runs twice.</summary>
        public bool IsBuilt { get; private set; }

        /// <summary>Text lookup and diagnostics. Deliberately the entire outside world, as far as a screen knows.</summary>
        protected IUiContext Context { get; }

        /// <summary>Shorthand for <c>Context.Loc</c>.</summary>
        protected ILocalizedText Loc => Context.Loc;

        /// <summary>Shorthand for <c>Context.Log</c>.</summary>
        protected ICoreLog Log => Context.Log;

        /// <summary>
        /// Builds the element tree if it has not been built yet. Called by the stack before the first show.
        /// </summary>
        /// <remarks>
        /// Public rather than internal so a controller can pre-warm a screen during an idle moment — for
        /// example while the main menu is up, ahead of a pause menu that must appear within one frame.
        /// </remarks>
        public void EnsureBuilt()
        {
            if (IsBuilt)
            {
                return;
            }

            // Set before Build so that a re-entrant call from inside Build (a component that shows a toast
            // that pokes the screen, say) cannot start a second build of the same tree.
            IsBuilt = true;
            Build(Root);
        }

        /// <summary>Creates the screen's element tree. Called exactly once, right before the first show.</summary>
        /// <param name="root">The screen's root element, already parented and styled.</param>
        protected abstract void Build(VisualElement root);

        /// <summary>
        /// The screen became the visible top of the stack — either its first show, or a return after the
        /// screen above it was popped. Refresh anything that may have changed while it was covered.
        /// </summary>
        public virtual void OnShown()
        {
        }

        /// <summary>
        /// Another screen covered this one, or it is being removed. Release subscriptions here; the tree
        /// stays alive and may be shown again.
        /// </summary>
        public virtual void OnHidden()
        {
        }

        /// <summary>The screen left the stack for good and its tree has been removed. Release everything.</summary>
        public virtual void OnDestroyed()
        {
        }

        /// <summary>
        /// Resolves a key to display text.
        /// </summary>
        /// <remarks>
        /// WHY every screen goes through this: it is the shortest path to correct text, which makes writing
        /// a literal the longer path. A missing key renders as <c>#key#</c> rather than blank, so an
        /// untranslated string is visible in play rather than discovered by a player.
        /// </remarks>
        protected string Text(in LocKey key)
        {
            return Loc.Get(key);
        }

        /// <summary>Resolves a key and fills its <c>{0}</c>, <c>{1}</c>, ... placeholders.</summary>
        /// <param name="key">The pattern key.</param>
        /// <param name="args">Arguments, already formatted by the caller if they need locale-aware rendering.</param>
        protected string TextFormat(in LocKey key, params object[] args)
        {
            return Loc.Get(key, args);
        }
    }
}

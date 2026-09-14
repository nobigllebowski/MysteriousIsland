// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Core/ScreenStack.cs.
// Adapted for Vardholm: the transitions no longer depend on USS classes from an authored Theme.uss — they are
// driven in code from Theme tokens, so a missing stylesheet cannot leave a screen stuck at opacity 0; Pop and
// ReplaceAll now raise the IsTransitioning guard too (in Nation, Pop left it clear, so a double-tap on a back
// button could pop twice); and a screen is only hidden/destroyed if it is still the same screen when the timer
// fires, which closes the race where a fast Push-Pop-Push removed the newest screen.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// Navigation stack of full-screen views with fade-and-slide transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Covered screens stay built but hidden, so returning to one is instant and free of rebuilds. Only
    /// leaving the stack destroys a screen.
    /// </para>
    /// <para>
    /// WHY <see cref="IsTransitioning"/> guards every mutator: a button on a touch screen is pressed twice
    /// more often than anyone expects — a fat finger, a slow frame, an impatient player. Without the guard
    /// the second tap pushes a second copy of the settings screen, and the back button then appears to do
    /// nothing. Dropping input for the length of one transition is the cheapest correct answer; queuing the
    /// second tap would replay an intent the player has already stopped meaning.
    /// </para>
    /// </remarks>
    public sealed class ScreenStack
    {
        /// <summary>Transition length. One value for enter and exit, so a push and its pop feel symmetrical.</summary>
        public const long TransitionMs = Theme.DurationNormal;

        /// <summary>
        /// Grace period added before a hidden screen is actually hidden or removed.
        /// </summary>
        /// <remarks>
        /// The animation and the scheduler are driven by different clocks; tearing an element out on exactly
        /// the same millisecond the animation ends produces a visible one-frame pop on slow devices.
        /// </remarks>
        private const long SettleMs = 30;

        /// <summary>How far a screen slides during its transition, in panel units.</summary>
        private const float SlideDistance = 28f;

        private const string EnterRightClass = "screen--enter-right";
        private const string EnterLeftClass = "screen--enter-left";
        private const string ExitRightClass = "screen--exit-right";
        private const string ExitFadeClass = "screen--exit-fade";

        private readonly VisualElement _layer;
        private readonly List<UIScreen> _screens = new List<UIScreen>();

        /// <summary>Binds the stack to the layer that will host its screens.</summary>
        /// <param name="layer">The screen layer, created and owned by <see cref="UIService"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="layer"/> is null.</exception>
        public ScreenStack(VisualElement layer)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        }

        /// <summary>The visible screen, or null when the stack is empty.</summary>
        public UIScreen Current => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        /// <summary>Number of screens held, visible and covered.</summary>
        public int Count => _screens.Count;

        /// <summary>True while a transition is playing. Every mutator refuses to act while this is set.</summary>
        public bool IsTransitioning { get; private set; }

        /// <summary>Pushes a screen on top of the current one, sliding in from the right.</summary>
        /// <param name="screen">The screen to show. Ignored if null or already in the stack.</param>
        public void Push(UIScreen screen)
        {
            if (IsTransitioning || screen == null || _screens.Contains(screen))
            {
                return;
            }

            var previous = Current;
            _screens.Add(screen);
            Show(screen, EnterRightClass, SlideDistance);

            if (previous != null)
            {
                previous.OnHidden();
                HideAfterTransition(previous);
            }
        }

        /// <summary>
        /// Removes the top screen and reveals the one below, sliding out to the right.
        /// </summary>
        /// <remarks>
        /// Refuses to empty the stack: a UI with no screen is a black rectangle the player cannot leave.
        /// Use <see cref="ReplaceAll"/> to change what the whole stack shows.
        /// </remarks>
        public void Pop()
        {
            if (IsTransitioning || _screens.Count <= 1)
            {
                return;
            }

            var leaving = Current;
            _screens.RemoveAt(_screens.Count - 1);
            var revealed = Current;

            BeginTransition();

            revealed.Root.style.display = DisplayStyle.Flex;
            revealed.Root.BringToFront();
            AddTransientClass(revealed, EnterLeftClass);
            Animate(revealed.Root, 0f, 1f, -SlideDistance, 0f);
            revealed.OnShown();

            leaving.OnHidden();
            AddTransientClass(leaving, ExitRightClass);
            leaving.Root.BringToFront();
            Animate(leaving.Root, 1f, 0f, 0f, SlideDistance);
            Remove(leaving);
        }

        /// <summary>Swaps the top screen for another one. The screen below, if any, stays covered.</summary>
        /// <param name="screen">The replacement. Ignored if null or already in the stack.</param>
        public void Replace(UIScreen screen)
        {
            if (IsTransitioning || screen == null || _screens.Contains(screen))
            {
                return;
            }

            var previous = Current;
            if (previous != null)
            {
                _screens.RemoveAt(_screens.Count - 1);
                previous.OnHidden();
                AddTransientClass(previous, ExitFadeClass);
                Animate(previous.Root, 1f, 0f, 0f, 0f);
                Remove(previous);
            }

            _screens.Add(screen);
            Show(screen, EnterRightClass, SlideDistance);
        }

        /// <summary>
        /// Clears every screen and shows the given one. Used when the application mode changes — entering a
        /// run, or quitting back to the menu — where the old back stack has no meaning any more.
        /// </summary>
        /// <param name="screen">The only screen that will remain. Ignored if null.</param>
        public void ReplaceAll(UIScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            // Deliberately not guarded by IsTransitioning: this is called from mode changes that have
            // already happened in the model, so refusing would leave the UI showing a mode the game has
            // left. It clears the guard instead, cancelling whatever transition was in flight.
            IsTransitioning = false;

            for (var i = 0; i < _screens.Count; i++)
            {
                var old = _screens[i];
                if (old == screen)
                {
                    continue;
                }

                old.OnHidden();

                // REMOVED SYNCHRONOUSLY, not faded and then removed on a timer. Every screen here is
                // full-bleed and opaque, so an outgoing one that is still in the tree is a black
                // sheet over whatever replaced it — and that is exactly what happened: the main
                // menu's opaque backdrop stayed on top of the game, so the world rendered perfectly
                // and the player saw the menu's background and its one circular bloom.
                //
                // The fade depended on `experimental.animation` and a scheduled callback, and the
                // project's own risk audit lists that API as HIGH RISK precisely because it fails
                // silently. A cross-fade is not worth a mode change that can get stuck: ReplaceAll
                // is called when the game has ALREADY changed mode, so the old screen has no claim
                // on the screen for even one more frame.
                old.Root.RemoveFromHierarchy();
                old.Root.style.opacity = 1f;
                old.Root.style.translate = new Translate(0f, 0f);
                old.OnDestroyed();
            }

            _screens.Clear();
            _screens.Add(screen);
            Show(screen, EnterRightClass, SlideDistance);

            // Then sweep the layer itself. The loop above removes the screens this stack KNOWS
            // about; this removes anything else that reached the layer by any route at all. The
            // guarantee worth having is "exactly one screen is on screen", and it should not depend
            // on the bookkeeping having been perfect — a leftover full-bleed screen is an opaque
            // sheet over the game, and that failure has already cost this project enough.
            for (var i = _layer.childCount - 1; i >= 0; i--)
            {
                var child = _layer[i];
                if (child != screen.Root)
                {
                    child.RemoveFromHierarchy();
                }
            }
        }

        /// <summary>Parents, reveals and animates a screen in, then marks the stack busy for the transition.</summary>
        private void Show(UIScreen screen, string enterClass, float fromX)
        {
            screen.EnsureBuilt();

            if (screen.Root.parent != _layer)
            {
                _layer.Add(screen.Root);
            }

            screen.Root.style.display = DisplayStyle.Flex;
            screen.Root.BringToFront();

            BeginTransition();
            AddTransientClass(screen, enterClass);
            Animate(screen.Root, 0f, 1f, fromX, 0f);
            screen.OnShown();
        }

        /// <summary>Raises the busy guard and schedules its release one transition later.</summary>
        private void BeginTransition()
        {
            IsTransitioning = true;

            // Scheduled on the layer rather than on a screen: a screen that is removed mid-transition takes
            // its scheduled items with it, and the guard would then never be released.
            _layer.schedule.Execute(() => IsTransitioning = false).StartingIn(TransitionMs);
        }

        /// <summary>
        /// Hides a covered screen once the transition is over, unless it has become visible again in the
        /// meantime (a push immediately followed by a pop).
        /// </summary>
        private void HideAfterTransition(UIScreen screen)
        {
            _layer.schedule.Execute(() =>
            {
                if (_screens.Contains(screen) && screen != Current)
                {
                    screen.Root.style.display = DisplayStyle.None;
                }
            }).StartingIn(TransitionMs + SettleMs);
        }

        /// <summary>Detaches and destroys a screen once its exit animation has finished.</summary>
        private void Remove(UIScreen screen)
        {
            _layer.schedule.Execute(() =>
            {
                // The same screen instance can be pushed again before this fires; destroying it then would
                // tear down a screen the player is currently looking at.
                if (_screens.Contains(screen))
                {
                    return;
                }

                screen.Root.RemoveFromHierarchy();
                screen.Root.style.opacity = 1f;
                screen.Root.style.translate = new Translate(0f, 0f);
                screen.OnDestroyed();
            }).StartingIn(TransitionMs + SettleMs);
        }

        /// <summary>
        /// Adds a USS marker class for the duration of the transition and removes it afterwards.
        /// </summary>
        /// <remarks>
        /// The classes drive nothing by themselves — the movement is the code animation below. They exist so
        /// a stylesheet added later can decorate a transition (a shadow under the incoming screen, say)
        /// without the stack having to learn about it.
        /// </remarks>
        private void AddTransientClass(UIScreen screen, string className)
        {
            screen.Root.AddToClassList(className);
            _layer.schedule.Execute(() => screen.Root.RemoveFromClassList(className))
                .StartingIn(TransitionMs + SettleMs);
        }

        /// <summary>Fades and slides an element between two states over one transition.</summary>
        /// <param name="element">The element to animate. Must already be attached to the panel.</param>
        /// <param name="fromOpacity">Starting opacity, applied immediately.</param>
        /// <param name="toOpacity">Ending opacity.</param>
        /// <param name="fromX">Starting horizontal offset in panel units.</param>
        /// <param name="toX">Ending horizontal offset in panel units.</param>
        private static void Animate(VisualElement element, float fromOpacity, float toOpacity, float fromX, float toX)
        {
            element.style.opacity = fromOpacity;
            element.style.translate = new Translate(fromX, 0f);

            element.experimental.animation
                .Start(0f, 1f, (int)TransitionMs, (target, t) =>
                {
                    target.style.opacity = Mathf.Lerp(fromOpacity, toOpacity, t);
                    target.style.translate = new Translate(Mathf.Lerp(fromX, toX, t), 0f);
                })
                .Ease(Easing.OutCubic);
        }
    }
}

// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Core/ToastLayer.cs.
// Adapted for Vardholm: simplified — the IconElement glyph is gone (Vardholm has no icon atlas yet, and the
// kind now reads as a colored edge instead), the styling is applied from Theme in code rather than from USS
// classes that may not be loaded, the queue is capped so a failing subsystem cannot build an unbounded backlog
// of notifications, and entries carry resolved text because localization happens at the UIService boundary.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace ForgottenIsle.UI.Core
{
    /// <summary>Severity of a toast, which selects its edge color.</summary>
    public enum ToastKind
    {
        /// <summary>Neutral confirmation or hint.</summary>
        Info = 0,

        /// <summary>Something the player asked for worked.</summary>
        Success = 1,

        /// <summary>Something was refused, but nothing was lost.</summary>
        Warning = 2,

        /// <summary>Something failed and the player's progress may be affected.</summary>
        Error = 3
    }

    /// <summary>
    /// Queued, non-blocking notifications that slide in under the top safe area and leave on their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY a queue rather than a stack of simultaneous toasts: two toasts on screen at once are read as one
    /// paragraph and neither is read properly. Showing them in turn costs time but keeps each one legible.
    /// </para>
    /// <para>
    /// WHY the layer never takes input: a toast that swallows a tap is worse than no toast, because the tap
    /// the player aimed at a button silently disappears. Every element here is
    /// <see cref="PickingMode.Ignore"/>.
    /// </para>
    /// </remarks>
    public sealed class ToastLayer
    {
        /// <summary>How long a toast stays at full opacity.</summary>
        public const long VisibleMs = 2400;

        /// <summary>Fade in / fade out length.</summary>
        public const long AnimationMs = Theme.DurationNormal;

        /// <summary>
        /// Maximum number of queued toasts.
        /// </summary>
        /// <remarks>
        /// A subsystem failing once per frame would otherwise queue minutes of notifications, and the player
        /// would still be reading the backlog long after the problem was over. Beyond the cap the oldest
        /// waiting entry is dropped: the newest message describes the current state of the world.
        /// </remarks>
        public const int MaxQueued = 8;

        private const string ToastClass = "toast";
        private const float EnterOffset = -12f;

        private readonly VisualElement _layer;
        private readonly Queue<Entry> _queue = new Queue<Entry>();
        private bool _showing;

        /// <summary>Binds the layer that will host toasts.</summary>
        /// <param name="layer">The toast layer, created and owned by <see cref="UIService"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="layer"/> is null.</exception>
        public ToastLayer(VisualElement layer)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _layer.pickingMode = PickingMode.Ignore;
            _layer.style.alignItems = Align.Center;
            _layer.style.justifyContent = Justify.FlexStart;
            _layer.style.paddingTop = Theme.Space12;
        }

        /// <summary>Number of toasts waiting behind the one on screen.</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>
        /// Queues a notification. Shown immediately if nothing else is on screen.
        /// </summary>
        /// <param name="message">Already-localized text. Ignored if null or empty.</param>
        /// <param name="kind">Severity, which selects the edge color.</param>
        public void Show(string message, ToastKind kind = ToastKind.Info)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (_queue.Count >= MaxQueued)
            {
                _queue.Dequeue();
            }

            _queue.Enqueue(new Entry(message, kind));

            if (!_showing)
            {
                ShowNext();
            }
        }

        /// <summary>Drops every queued toast. The one on screen finishes its own life.</summary>
        public void Clear()
        {
            _queue.Clear();
        }

        /// <summary>Builds and animates the next queued toast, chaining to the one after it.</summary>
        private void ShowNext()
        {
            if (_queue.Count == 0)
            {
                _showing = false;
                return;
            }

            _showing = true;
            var entry = _queue.Dequeue();

            var toast = BuildToast(entry);
            _layer.Add(toast);

            toast.style.opacity = 0f;
            toast.style.translate = new Translate(0f, EnterOffset);
            toast.experimental.animation
                .Start(0f, 1f, (int)AnimationMs, (target, t) =>
                {
                    target.style.opacity = t;
                    target.style.translate = new Translate(0f, Mathf.Lerp(EnterOffset, 0f, t));
                })
                .Ease(Easing.OutCubic);

            toast.schedule.Execute(() =>
            {
                toast.experimental.animation
                    .Start(1f, 0f, (int)AnimationMs, (target, t) => target.style.opacity = t)
                    .Ease(Easing.InCubic);
            }).StartingIn(VisibleMs);

            // Scheduled on the layer, not on the toast: the toast is about to be removed from the hierarchy
            // and a scheduled item on a detached element never runs, which would strand the queue forever.
            _layer.schedule.Execute(() =>
            {
                toast.RemoveFromHierarchy();
                ShowNext();
            }).StartingIn(VisibleMs + AnimationMs);
        }

        /// <summary>Creates the pill for one entry: a colored edge, then the message.</summary>
        private static VisualElement BuildToast(Entry entry)
        {
            var toast = new VisualElement { name = ToastClass, pickingMode = PickingMode.Ignore };
            toast.AddToClassList(ToastClass);
            toast.AddToClassList(ToastClass + "--" + entry.Kind.ToString().ToLowerInvariant());

            toast.style.flexDirection = FlexDirection.Row;
            toast.style.alignItems = Align.Center;
            toast.style.maxWidth = 340f;
            toast.style.paddingTop = Theme.Space12;
            toast.style.paddingBottom = Theme.Space12;
            toast.style.paddingRight = Theme.Space16;
            toast.style.marginBottom = Theme.Space8;
            toast.style.backgroundColor = Theme.WithAlpha(Theme.Surface, 0.94f);
            toast.style.borderTopLeftRadius = Theme.RadiusMd;
            toast.style.borderTopRightRadius = Theme.RadiusMd;
            toast.style.borderBottomLeftRadius = Theme.RadiusMd;
            toast.style.borderBottomRightRadius = Theme.RadiusMd;
            toast.style.borderTopWidth = 1f;
            toast.style.borderBottomWidth = 1f;
            toast.style.borderLeftWidth = 1f;
            toast.style.borderRightWidth = 1f;
            toast.style.borderTopColor = Theme.Border;
            toast.style.borderBottomColor = Theme.Border;
            toast.style.borderLeftColor = Theme.Border;
            toast.style.borderRightColor = Theme.Border;

            var accent = ColorFor(entry.Kind);

            // The severity edge. A 3dp bar rather than a glyph: it is unambiguous at a glance, costs no
            // texture, and does not need a localized alt text.
            var edge = new VisualElement { name = "toast__edge", pickingMode = PickingMode.Ignore };
            edge.AddToClassList("toast__edge");
            edge.style.width = 3f;
            edge.style.alignSelf = Align.Stretch;
            edge.style.marginRight = Theme.Space12;
            edge.style.marginLeft = Theme.Space4;
            edge.style.backgroundColor = accent;
            edge.style.borderTopLeftRadius = 2f;
            edge.style.borderBottomLeftRadius = 2f;
            edge.style.borderTopRightRadius = 2f;
            edge.style.borderBottomRightRadius = 2f;
            toast.Add(edge);

            var label = new Label(entry.Message) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("toast__text");
            label.style.color = Theme.Text;
            label.style.fontSize = 14f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 1f;
            toast.Add(label);

            return toast;
        }

        /// <summary>Maps a severity to its edge color.</summary>
        private static Color ColorFor(ToastKind kind)
        {
            switch (kind)
            {
                case ToastKind.Success: return Theme.Accent;
                case ToastKind.Warning: return Theme.Warning;
                case ToastKind.Error: return Theme.Danger;
                default: return Theme.TextSecondary;
            }
        }

        /// <summary>One queued notification.</summary>
        private readonly struct Entry
        {
            /// <summary>Already-localized text.</summary>
            public readonly string Message;

            /// <summary>Severity, which selects the edge color.</summary>
            public readonly ToastKind Kind;

            /// <summary>Creates an entry.</summary>
            public Entry(string message, ToastKind kind)
            {
                Message = message;
                Kind = kind;
            }
        }
    }
}

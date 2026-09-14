using System;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Game.Scenes;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// The full-screen cover shown while a zone loads: fades in, reports determinate progress, and fades out
    /// again once the load is done and it has been on screen long enough to be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY a minimum display time: a warm additive load can finish in under 100 ms, and a curtain that
    /// appears and vanishes inside three frames reads as a graphical glitch rather than as a transition.
    /// <see cref="MinimumVisibleMs"/> holds it just long enough to be perceived as deliberate. It delays a
    /// fast load by a fraction of a second and it is worth it: the alternative looks broken.
    /// </para>
    /// <para>
    /// WHY progress is polled rather than pushed: <see cref="SceneLoader"/> exposes
    /// <see cref="SceneLoader.Progress"/> as a value, not an event, and a per-frame event for a number that
    /// changes every frame would allocate for nothing. The curtain samples it on its own scheduler and stops
    /// sampling the moment it is hidden.
    /// </para>
    /// <para>
    /// WHY the bar is determinate: an indeterminate spinner cannot distinguish "nearly there" from "wedged",
    /// so a hung load looks identical to a slow one — to the player and to QA.
    /// </para>
    /// </remarks>
    public sealed class LoadingCurtain
    {
        /// <summary>Fade in / fade out length.</summary>
        public const long FadeMs = Theme.DurationNormal;

        /// <summary>Shortest time the curtain stays fully visible before it is allowed to leave.</summary>
        public const long MinimumVisibleMs = 400;

        /// <summary>Progress sampling interval, roughly one sample per frame at 60 Hz.</summary>
        private const long PollMs = 16;

        /// <summary>Caption shown when a caller does not supply one.</summary>
        private static readonly LocKey DefaultCaptionKey = new LocKey("ui.loading.title");

        /// <summary>Pattern for the numeric readout, e.g. <c>{0}%</c>.</summary>
        private static readonly LocKey PercentKey = new LocKey("ui.loading.percent");

        private readonly VisualElement _layer;
        private readonly ILocalizedText _loc;
        private readonly VisualElement _root;
        private readonly Label _caption;
        private readonly VisualElement _fill;
        private readonly Label _percent;

        private IVisualElementScheduledItem _poll;
        private Func<float> _progressSource;
        private Action _pendingHidden;
        private double _shownAtSeconds;
        private float _progress;
        private bool _hideRequested;

        /// <summary>Builds the curtain inside its layer, hidden.</summary>
        /// <param name="layer">The curtain layer, created and owned by <see cref="UIService"/>.</param>
        /// <param name="loc">Text lookup for the caption and the percentage readout.</param>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public LoadingCurtain(VisualElement layer, ILocalizedText loc)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));

            _root = new VisualElement { name = "curtain" };
            _root.AddToClassList("curtain");
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.right = 0f;
            _root.style.top = 0f;
            _root.style.bottom = 0f;
            _root.style.backgroundColor = Theme.Background;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.paddingLeft = Theme.Space32;
            _root.style.paddingRight = Theme.Space32;

            _caption = new Label(string.Empty);
            _caption.AddToClassList("curtain__caption");
            _caption.style.color = Theme.TextSecondary;
            _caption.style.fontSize = 15f;
            _caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            _caption.style.whiteSpace = WhiteSpace.Normal;
            _caption.style.marginBottom = Theme.Space20;
            _root.Add(_caption);

            var track = new VisualElement { name = "curtain__track" };
            track.AddToClassList("curtain__track");
            track.style.width = Length.Percent(100f);
            track.style.maxWidth = 280f;
            track.style.height = 4f;
            track.style.backgroundColor = Theme.WithAlpha(Theme.Border, 0.5f);
            track.style.borderTopLeftRadius = 2f;
            track.style.borderTopRightRadius = 2f;
            track.style.borderBottomLeftRadius = 2f;
            track.style.borderBottomRightRadius = 2f;
            track.style.overflow = Overflow.Hidden;
            _root.Add(track);

            _fill = new VisualElement { name = "curtain__fill" };
            _fill.AddToClassList("curtain__fill");
            _fill.style.width = Length.Percent(0f);
            _fill.style.height = Length.Percent(100f);
            _fill.style.backgroundColor = Theme.Accent;
            track.Add(_fill);

            _percent = new Label(string.Empty);
            _percent.AddToClassList("curtain__percent");
            _percent.style.color = Theme.TextMuted;
            _percent.style.fontSize = 12f;
            _percent.style.marginTop = Theme.Space12;
            _root.Add(_percent);

            _layer.Add(_root);
            _layer.style.display = DisplayStyle.None;
        }

        /// <summary>True between <see cref="Show(in LocKey, Func{float})"/> and the end of the fade out.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>Last reported progress, clamped to 0..1.</summary>
        public float Progress => _progress;

        /// <summary>
        /// Shows the curtain and drives its bar from a scene loader.
        /// </summary>
        /// <param name="captionKey">Caption key; pass <see cref="LocKey.Empty"/> for the default.</param>
        /// <param name="loader">The loader whose <see cref="SceneLoader.Progress"/> the bar follows.</param>
        /// <exception cref="ArgumentNullException"><paramref name="loader"/> is null.</exception>
        public void Show(in LocKey captionKey, SceneLoader loader)
        {
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            Show(captionKey, () => loader.Progress);
        }

        /// <summary>
        /// Shows the curtain and drives its bar from an arbitrary progress source.
        /// </summary>
        /// <param name="captionKey">Caption key; pass <see cref="LocKey.Empty"/> for the default.</param>
        /// <param name="progressSource">
        /// Sampled every frame while visible and expected to return 0..1. Null means the caller will push
        /// values through <see cref="SetProgress"/> instead.
        /// </param>
        public void Show(in LocKey captionKey, Func<float> progressSource = null)
        {
            _progressSource = progressSource;
            _hideRequested = false;
            _pendingHidden = null;

            var key = captionKey.IsEmpty ? DefaultCaptionKey : captionKey;
            _caption.text = _loc.Get(key);

            SetProgress(progressSource != null ? progressSource() : 0f);

            if (!IsVisible)
            {
                IsVisible = true;
                _shownAtSeconds = NowSeconds();

                _layer.style.display = DisplayStyle.Flex;
                _layer.pickingMode = PickingMode.Position;
                _root.style.opacity = 0f;
                _root.experimental.animation
                    .Start(0f, 1f, (int)FadeMs, (target, t) => target.style.opacity = t)
                    .Ease(Easing.OutCubic);
            }

            StartPolling();
        }

        /// <summary>
        /// Sets the bar directly. Use when there is no pollable source — a save write, say, that reports
        /// progress in steps.
        /// </summary>
        /// <param name="value">Progress in 0..1. Values outside the range are clamped.</param>
        public void SetProgress(float value)
        {
            var clamped = Mathf.Clamp01(value);

            // Guarded because this runs every frame while a zone loads: assigning an identical style value
            // still dirties the element and costs a layout pass for nothing.
            if (Mathf.Approximately(clamped, _progress) && _percent.text.Length > 0)
            {
                return;
            }

            _progress = clamped;
            _fill.style.width = Length.Percent(clamped * 100f);
            _percent.text = _loc.Get(PercentKey, Mathf.RoundToInt(clamped * 100f));
        }

        /// <summary>
        /// Asks the curtain to leave. It fades out once it has been visible for at least
        /// <see cref="MinimumVisibleMs"/>, so a fast load does not flash.
        /// </summary>
        /// <param name="onHidden">
        /// Invoked after the fade out completes, or immediately if the curtain was not visible. Use it to
        /// start whatever should only run behind a fully open screen.
        /// </param>
        public void Hide(Action onHidden = null)
        {
            if (!IsVisible)
            {
                onHidden?.Invoke();
                return;
            }

            // A second Hide while one is already pending only adds its callback; re-arming the timer would
            // extend the curtain's life every time a caller asked it to leave.
            _pendingHidden += onHidden;
            if (_hideRequested)
            {
                return;
            }

            _hideRequested = true;

            var elapsedMs = (long)((NowSeconds() - _shownAtSeconds) * 1000d);
            var remaining = MinimumVisibleMs - elapsedMs;
            if (remaining < 0)
            {
                remaining = 0;
            }

            _layer.schedule.Execute(BeginFadeOut).StartingIn(remaining);
        }

        /// <summary>Plays the fade out and tears down the curtain's state when it finishes.</summary>
        private void BeginFadeOut()
        {
            // Show may have been called again during the minimum-display wait — a second zone change queued
            // behind the first. The curtain must then stay up rather than uncover a half-built zone.
            if (!_hideRequested || !IsVisible)
            {
                return;
            }

            _root.experimental.animation
                .Start(1f, 0f, (int)FadeMs, (target, t) => target.style.opacity = t)
                .Ease(Easing.InCubic);

            _layer.schedule.Execute(() =>
            {
                if (!_hideRequested)
                {
                    return;
                }

                IsVisible = false;
                _hideRequested = false;
                _progressSource = null;
                StopPolling();

                _layer.style.display = DisplayStyle.None;
                _layer.pickingMode = PickingMode.Ignore;
                _root.style.opacity = 1f;

                var callback = _pendingHidden;
                _pendingHidden = null;
                callback?.Invoke();
            }).StartingIn(FadeMs);
        }

        /// <summary>Starts or resumes sampling the progress source.</summary>
        private void StartPolling()
        {
            if (_progressSource == null)
            {
                return;
            }

            if (_poll == null)
            {
                _poll = _layer.schedule.Execute(() =>
                {
                    var source = _progressSource;
                    if (source != null)
                    {
                        SetProgress(source());
                    }
                }).Every(PollMs);
                return;
            }

            _poll.Resume();
        }

        /// <summary>Stops sampling. The item is kept so the next show does not allocate another one.</summary>
        private void StopPolling()
        {
            _poll?.Pause();
        }

        /// <summary>
        /// Unscaled wall-clock seconds.
        /// </summary>
        /// <remarks>
        /// Unscaled on purpose: a load often happens with the simulation paused at <c>timeScale</c> 0, and a
        /// scaled clock would make the minimum display time never elapse.
        /// </remarks>
        private static double NowSeconds()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
    }
}

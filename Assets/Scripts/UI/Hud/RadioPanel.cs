using System;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Hud
{
    /// <summary>
    /// The tuning band: the radio's own printed dial strip, a hairline needle, a fine-tune knob,
    /// the spectrogram ribbon, and the hand-mic.
    /// </summary>
    /// <remarks>
    /// Built to <c>design/04-first-30-minutes.md</c> §21:00 and §3.2. A full-width band in the
    /// bottom third, dragged with one thumb; the strip has inertia and friction and ticks at the
    /// detents; the right fifth of the band is the fine knob at ten times the resolution, with no
    /// mode switch, because on the real object it is a different part of the same control.
    /// <para>
    /// Above the strip, the ribbon: phosphor-green on black, grass for noise, a vertical line for a
    /// carrier. It is the same visual language as the opening frame and the Act 2 hydrophone; the
    /// player learns to read a spectrogram here and uses it for twenty hours. The ribbon draws
    /// from <see cref="RadioTuner.Spectrum"/> — pure arithmetic in Core, the same function the
    /// lock rule uses — so what the player sees and what the game grants can never disagree.
    /// </para>
    /// <para>
    /// It renders what it is told and reports intent. A drag becomes a requested frequency, the
    /// mic becomes a squeeze, the close button a close; all of it goes to the controller, which
    /// turns each into a command. The panel never moves the needle itself: the needle it draws is
    /// the one the last signal reported, which is how a refused tune (paused, say) leaves the
    /// needle where the game says it is rather than where the thumb wanted it.
    /// </para>
    /// <para>
    /// The band covers the bottom third, including the thumb controls, on purpose: the player is
    /// at the set with both hands on it, and walking while tuning is not a thing the real object
    /// allows either. Put the radio down to move.
    /// </para>
    /// <para>
    /// The flywheel and the ribbon tick on the UI Toolkit scheduler, not on the Ticker. That is
    /// not a second game loop: nothing on the C# side learns anything from either, they stop with
    /// the panel, and the one-Update() rule is about game logic having one clock. ADR-0022 notes it.
    /// </para>
    /// </remarks>
    public sealed class RadioPanel
    {
        /// <summary>Columns in the ribbon. One per ~4 points at phone width; enough to read a line.</summary>
        private const int RibbonColumns = 96;

        /// <summary>Width of band the ribbon shows, in kHz. A locked carrier is a hard line at centre.</summary>
        private const float RibbonSpanKhz = 120f;

        /// <summary>Milliseconds between ribbon scrolls and inertia steps. ~30 Hz is plenty for a phosphor.</summary>
        private const long TickMs = 33;

        /// <summary>Inertia: fraction of velocity kept per tick. Heavy, like a real tuning flywheel.</summary>
        private const float Friction = 0.92f;

        /// <summary>Below this many points per tick the flywheel is considered stopped.</summary>
        private const float RestVelocity = 0.15f;

        private readonly VisualElement _root;
        private readonly VisualElement _ribbon;
        private readonly VisualElement[] _columns = new VisualElement[RibbonColumns];
        private readonly float[] _spectrum = new float[RibbonColumns];
        private readonly VisualElement _strip;
        private readonly Label _readout;
        private readonly Label _stationLabel;
        private readonly Button _mic;

        private IVisualElementScheduledItem _ticker;
        private int _pointerId = -1;
        private float _lastPointerX;
        private long _lastPointerMs;
        private float _velocity;
        private bool _fineDrag;
        private float _shownMhz = RadioBand.MinMhz;
        private float _requestedMhz = RadioBand.MinMhz;
        private int _seed;
        private bool _visible;

        /// <summary>Builds the band into <paramref name="parent"/>, hidden.</summary>
        /// <param name="parent">The HUD root.</param>
        /// <param name="closeText">Already-localized label for the close button.</param>
        /// <param name="micText">Already-localized label for the press-to-talk bar.</param>
        /// <param name="fineText">Already-localized caption over the fine-tune zone.</param>
        /// <param name="unitText">Already-localized unit shown after the readout.</param>
        public RadioPanel(VisualElement parent, string closeText, string micText, string fineText, string unitText)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            _root = new VisualElement { name = "radio" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.height = Length.Percent(34f);
            _root.style.backgroundColor = Theme.WithAlpha(Theme.Background, 0.92f);
            _root.style.paddingLeft = Theme.Space16;
            _root.style.paddingRight = Theme.Space16;
            _root.style.paddingTop = Theme.Space8;
            _root.style.display = DisplayStyle.None;
            parent.Add(_root);

            // Header row: readout on the left, close on the right.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.pickingMode = PickingMode.Ignore;
            _root.Add(header);

            var readoutRow = new VisualElement();
            readoutRow.style.flexDirection = FlexDirection.Row;
            readoutRow.style.alignItems = Align.FlexEnd;
            readoutRow.pickingMode = PickingMode.Ignore;
            header.Add(readoutRow);

            _readout = Typography.Mono(string.Empty);
            _readout.style.color = Theme.Gold;
            _readout.style.fontSize = 28f;
            _readout.pickingMode = PickingMode.Ignore;
            readoutRow.Add(_readout);

            var unit = Typography.Caption(unitText);
            unit.style.color = Theme.TextMuted;
            unit.style.marginLeft = Theme.Space8;
            unit.style.marginBottom = 4f;
            unit.pickingMode = PickingMode.Ignore;
            readoutRow.Add(unit);

            var close = Buttons.Ghost(closeText, RaiseClose);
            close.name = "radio-close";
            close.style.minWidth = 48f;
            close.style.minHeight = 48f;
            header.Add(close);

            _stationLabel = Typography.Caption(string.Empty);
            _stationLabel.style.color = Theme.TextSecondary;
            _stationLabel.style.letterSpacing = 2f;
            _stationLabel.style.marginTop = 2f;
            _stationLabel.pickingMode = PickingMode.Ignore;
            _root.Add(_stationLabel);

            // The ribbon. Phosphor green on black, 40 points tall, one column per element. Each
            // column is a bottom-anchored bar whose height is the amplitude — cheaper than a
            // texture, and the whole thing is ninety-six elements.
            _ribbon = new VisualElement { name = "radio-ribbon" };
            _ribbon.style.height = 40f;
            _ribbon.style.marginTop = Theme.Space8;
            _ribbon.style.backgroundColor = Color.black;
            _ribbon.style.flexDirection = FlexDirection.Row;
            _ribbon.style.alignItems = Align.FlexEnd;
            _ribbon.pickingMode = PickingMode.Ignore;
            _root.Add(_ribbon);

            for (var i = 0; i < RibbonColumns; i++)
            {
                var column = new VisualElement();
                column.style.flexGrow = 1f;
                column.style.height = 0f;
                column.style.backgroundColor = Phosphor(0f);
                column.pickingMode = PickingMode.Ignore;
                _ribbon.Add(column);
                _columns[i] = column;
            }

            // The strip: the drag surface. The needle is a child at the centre; the scale marks
            // are drawn relative to the shown frequency each update so the strip appears to slide
            // under a fixed needle, which is how the real object reads.
            _strip = new VisualElement { name = "radio-strip" };
            _strip.style.height = 64f;
            _strip.style.marginTop = Theme.Space8;
            _strip.style.backgroundColor = Theme.Surface;
            _strip.style.borderTopLeftRadius = Theme.RadiusSm;
            _strip.style.borderTopRightRadius = Theme.RadiusSm;
            _strip.style.borderBottomLeftRadius = Theme.RadiusSm;
            _strip.style.borderBottomRightRadius = Theme.RadiusSm;
            _strip.style.overflow = Overflow.Hidden;
            _strip.pickingMode = PickingMode.Position;
            _root.Add(_strip);

            var needle = new VisualElement { name = "radio-needle" };
            needle.style.position = Position.Absolute;
            needle.style.left = Length.Percent(40f);
            needle.style.top = 0;
            needle.style.bottom = 0;
            needle.style.width = 2f;
            needle.style.backgroundColor = Theme.Gold;
            needle.pickingMode = PickingMode.Ignore;
            _strip.Add(needle);

            // The fine knob: the right fifth, marked so the player can see it is a different part.
            var fine = new VisualElement { name = "radio-fine" };
            fine.style.position = Position.Absolute;
            fine.style.right = 0;
            fine.style.top = 0;
            fine.style.bottom = 0;
            fine.style.width = Length.Percent(RadioBand.FineZoneFraction * 100f);
            fine.style.backgroundColor = Theme.AccentSoft;
            fine.style.borderLeftWidth = 1f;
            fine.style.borderLeftColor = Theme.Border;
            fine.style.alignItems = Align.Center;
            fine.style.justifyContent = Justify.Center;
            fine.pickingMode = PickingMode.Ignore;
            _strip.Add(fine);

            var fineLabel = Typography.Caption(fineText);
            fineLabel.style.color = Theme.TextMuted;
            fineLabel.style.letterSpacing = 3f;
            fineLabel.pickingMode = PickingMode.Ignore;
            fine.Add(fineLabel);

            RegisterDrag();

            // The mic. Press-and-hold shaped like the real PTT bar; a press is a squeeze.
            _mic = Buttons.Secondary(micText, RaiseMic);
            _mic.name = "radio-mic";
            _mic.style.marginTop = Theme.Space8;
            _mic.style.minHeight = 48f;
            _root.Add(_mic);
        }

        /// <summary>Raised with the frequency the thumb is asking for. The controller decides.</summary>
        public event Action<float> TuneRequested;

        /// <summary>
        /// Raised with the shown frequency the moment a thumb lands on the strip, before any drag.
        /// </summary>
        /// <remarks>
        /// A hand on the dial is itself an act: it is how the player stops the set sweeping by
        /// itself ("tap once to stop the sweep"). The controller turns it into a tune to where the
        /// needle already is, which the game reads as a hand on the dial and the panel reads as
        /// nothing at all.
        /// </remarks>
        public event Action<float> Touched;

        /// <summary>Raised when the mic is squeezed.</summary>
        public event Action MicSqueezed;

        /// <summary>Raised when the player puts the radio down.</summary>
        public event Action CloseRequested;

        /// <summary>The frequency the needle is drawn at.</summary>
        public float ShownMhz => _shownMhz;

        /// <summary>
        /// Converts a horizontal drag into a frequency change.
        /// </summary>
        /// <remarks>
        /// Public and static because it is the panel's one piece of arithmetic worth pinning: the
        /// design's coarse rate, the fine ratio, and the direction (dragging the strip left moves
        /// the needle UP the band, because the strip slides under a fixed needle).
        /// </remarks>
        /// <param name="deltaPoints">Thumb travel this step, in points; right is positive.</param>
        /// <param name="stripWidthPoints">Width of the strip, in points.</param>
        /// <param name="fine">True inside the fine-tune zone.</param>
        /// <returns>Change in MHz.</returns>
        public static float MhzForDrag(float deltaPoints, float stripWidthPoints, bool fine)
        {
            if (stripWidthPoints <= 0f)
            {
                return 0f;
            }

            var khzPerPoint = RadioBand.CoarseKhzPerScreen / stripWidthPoints;
            if (fine)
            {
                khzPerPoint *= RadioBand.FineRatio;
            }

            return -deltaPoints * khzPerPoint / 1000f;
        }

        /// <summary>Shows or hides the band. Hiding stops the flywheel and the ribbon.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (visible)
            {
                if (_ticker == null)
                {
                    _ticker = _root.schedule.Execute(Tick).Every(TickMs);
                }
                else
                {
                    _ticker.Resume();
                }
            }
            else
            {
                _ticker?.Pause();
                CancelDrag();
                _velocity = 0f;
            }
        }

        /// <summary>Draws the state the game reports.</summary>
        /// <param name="mhz">Needle position.</param>
        /// <param name="reception">Reception tier at the needle.</param>
        /// <param name="stationText">Already-localized name of what is being received, or empty.</param>
        public void SetState(float mhz, Reception reception, string stationText)
        {
            _shownMhz = mhz;
            if (_pointerId == -1 && Mathf.Abs(_velocity) < RestVelocity)
            {
                // Not mid-drag: the requested value follows the shown one, so the next drag starts
                // from where the game put the needle rather than where the last drag ended.
                _requestedMhz = mhz;
            }

            _readout.text = mhz.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            _stationLabel.text = stationText ?? string.Empty;

            var locked = reception == Reception.Locked;
            _readout.style.color = locked ? Theme.Gold : Theme.Text;
        }

        private void RegisterDrag()
        {
            _strip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_pointerId != -1)
                {
                    return;
                }

                _pointerId = evt.pointerId;
                _lastPointerX = evt.position.x;
                _lastPointerMs = evt.timestamp;
                _velocity = 0f;

                // The fine knob is decided at touch-down and held for the drag, so a thumb that
                // starts fine and drifts left stays fine. On the real object your thumb is on the
                // small knob or it is not. A strip that has not been laid out yet has no width and
                // therefore no fine zone: coarse, never a surprise ten-times-slower drag.
                var width = _strip.resolvedStyle.width;
                var local = _strip.WorldToLocal((Vector2)evt.position).x;
                _fineDrag = width > 0f && local >= width * (1f - RadioBand.FineZoneFraction);

                _strip.CapturePointer(evt.pointerId);
                evt.StopPropagation();

                var touched = Touched;
                if (touched != null)
                {
                    touched(_requestedMhz);
                }
            });

            _strip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != _pointerId)
                {
                    return;
                }

                var delta = evt.position.x - _lastPointerX;
                _lastPointerX = evt.position.x;

                // Velocity in points PER TICK, not per pointer event: pointer events arrive at
                // whatever rate the touch hardware reports, and a flywheel fed per-event speed
                // would spin ten times slower on a 120 Hz digitiser than on a 60 Hz one.
                // IPointerEvent.timestamp is milliseconds (VERIFY:
                // https://docs.unity3d.com/ScriptReference/UIElements.IPointerEvent-timestamp.html).
                var dtMs = Mathf.Max(1f, evt.timestamp - _lastPointerMs);
                _lastPointerMs = evt.timestamp;
                var perTick = delta / dtMs * TickMs;
                _velocity = 0.5f * _velocity + 0.5f * perTick;

                Nudge(delta);
                evt.StopPropagation();
            });

            _strip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != _pointerId)
                {
                    return;
                }

                _strip.ReleasePointer(evt.pointerId);
                ReleaseDrag();
                evt.StopPropagation();
            });

            _strip.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseDrag());
            _strip.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (evt.pointerId == _pointerId)
                {
                    CancelDrag();
                }
            });
        }

        private void ReleaseDrag()
        {
            _pointerId = -1;
        }

        /// <summary>Ends a drag from the panel's side: releases capture the thumb still holds.</summary>
        private void CancelDrag()
        {
            if (_pointerId != -1 && _strip.HasPointerCapture(_pointerId))
            {
                _strip.ReleasePointer(_pointerId);
            }

            ReleaseDrag();
        }

        private void Nudge(float deltaPoints)
        {
            var width = _strip.resolvedStyle.width;
            var change = MhzForDrag(deltaPoints, width, _fineDrag);
            if (Mathf.Approximately(change, 0f))
            {
                return;
            }

            _requestedMhz = RadioBand.Clamp(_requestedMhz + change);

            var handler = TuneRequested;
            if (handler != null)
            {
                handler(_requestedMhz);
            }
        }

        private void Tick()
        {
            if (!_visible)
            {
                return;
            }

            // The flywheel. After the thumb lifts, the strip keeps sliding and slows; the number to
            // tune is Friction, and it is the difference between a dial and a scrollbar.
            if (_pointerId == -1 && Mathf.Abs(_velocity) >= RestVelocity)
            {
                _velocity *= Friction;
                Nudge(_velocity);
                if (Mathf.Abs(_velocity) < RestVelocity)
                {
                    _velocity = 0f;
                }
            }

            // The ribbon scrolls by re-sampling with a new seed: grass changes every tick, a
            // carrier stays put, and the eye reads that difference as a signal instantly.
            _seed++;
            RadioTuner.Spectrum(_shownMhz, RibbonSpanKhz, _seed, _spectrum);
            for (var i = 0; i < RibbonColumns; i++)
            {
                var amplitude = _spectrum[i];
                _columns[i].style.height = Length.Percent(amplitude * 100f);
                _columns[i].style.backgroundColor = Phosphor(amplitude);
            }
        }

        private static Color Phosphor(float amplitude)
        {
            // P1 phosphor green, brighter with amplitude. Never pure white: a saturated line is
            // the look, and a white one reads as a rendering error.
            var a = Mathf.Clamp01(amplitude);
            return new Color(0.25f * a, 0.55f + 0.45f * a, 0.30f * a, 1f);
        }

        private void RaiseClose()
        {
            var handler = CloseRequested;
            if (handler != null)
            {
                handler();
            }
        }

        private void RaiseMic()
        {
            var handler = MicSqueezed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}

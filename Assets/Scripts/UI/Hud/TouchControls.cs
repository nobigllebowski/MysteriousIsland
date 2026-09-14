using ForgottenIsle.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Hud
{
    /// <summary>
    /// The on-screen thumb controls: a floating joystick on the left, a look pad on the right.
    /// </summary>
    /// <remarks>
    /// Built from UI Toolkit pointer events rather than the Input System's on-screen controls,
    /// because the whole HUD is already a <c>UIDocument</c> and mixing the two would mean two input
    /// paths, two z-orders, and an Input System asset that has to be authored in the Inspector —
    /// which the project's runtime-wiring rule forbids.
    /// <para>
    /// The joystick is FLOATING: it appears where the thumb lands rather than at a fixed spot. On a
    /// phone held one-handed, a fixed stick is a stick your thumb misses. ADR-0008 chose a floating
    /// joystick over tap-to-move; this is that decision made real.
    /// </para>
    /// <para>
    /// Nothing here reads game state or moves a player. It produces two normalised vectors and hands
    /// them to whoever asks, which keeps it a view.
    /// </para>
    /// </remarks>
    public sealed class TouchControls
    {
        /// <summary>Radius in points at which the stick reads fully deflected.</summary>
        private const float StickRadius = 74f;

        /// <summary>Points the thumb must travel before a drag counts, to survive a shaky tap.</summary>
        private const float LookDeadzonePoints = 1.5f;

        /// <summary>Scales raw finger travel into the look input the rig expects.</summary>
        private const float LookSensitivity = 0.06f;

        private readonly VisualElement _root;
        private readonly VisualElement _stickBase;
        private readonly VisualElement _stickKnob;

        private int _movePointerId = -1;
        private int _lookPointerId = -1;
        private Vector2 _stickOrigin;
        private Vector2 _move;
        private Vector2 _look;
        private Vector2 _lastLookPosition;

        /// <summary>Builds the controls into <paramref name="parent"/>.</summary>
        /// <param name="parent">Element the two touch zones are added to.</param>
        public TouchControls(VisualElement parent)
        {
            _root = new VisualElement { name = "touch-controls" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;

            // The container must not eat taps meant for the buttons above it; only the two zones
            // below are interactive.
            _root.pickingMode = PickingMode.Ignore;
            parent.Add(_root);

            var moveZone = CreateZone("move-zone", 0f);
            var lookZone = CreateZone("look-zone", 0.5f);

            _stickBase = new VisualElement { name = "stick-base" };
            _stickBase.style.position = Position.Absolute;
            _stickBase.style.width = StickRadius * 2f;
            _stickBase.style.height = StickRadius * 2f;
            _stickBase.style.borderTopLeftRadius = StickRadius;
            _stickBase.style.borderTopRightRadius = StickRadius;
            _stickBase.style.borderBottomLeftRadius = StickRadius;
            _stickBase.style.borderBottomRightRadius = StickRadius;
            _stickBase.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
            _stickBase.pickingMode = PickingMode.Ignore;
            _stickBase.style.display = DisplayStyle.None;
            _root.Add(_stickBase);

            _stickKnob = new VisualElement { name = "stick-knob" };
            _stickKnob.style.position = Position.Absolute;
            _stickKnob.style.width = 54f;
            _stickKnob.style.height = 54f;
            _stickKnob.style.borderTopLeftRadius = 27f;
            _stickKnob.style.borderTopRightRadius = 27f;
            _stickKnob.style.borderBottomLeftRadius = 27f;
            _stickKnob.style.borderBottomRightRadius = 27f;
            _stickKnob.style.backgroundColor = new Color(0.82f, 0.86f, 0.80f, 0.20f);
            _stickKnob.pickingMode = PickingMode.Ignore;
            _stickKnob.style.display = DisplayStyle.None;
            _root.Add(_stickKnob);

            RegisterMove(moveZone);
            RegisterLook(lookZone);
        }

        /// <summary>Normalised movement, -1..1 per axis.</summary>
        public Vector2 Move => _move;

        /// <summary>
        /// Look delta for this frame.
        /// </summary>
        /// <remarks>
        /// Consumed on read and zeroed. A drag produces motion only while the finger is actually
        /// moving; holding a finger still must not keep turning the camera.
        /// </remarks>
        public Vector2 ConsumeLook()
        {
            var look = _look;
            _look = Vector2.zero;
            return look;
        }

        /// <summary>Shows or hides the whole control layer.</summary>
        /// <param name="visible">False while paused, loading, or in a menu.</param>
        public void SetVisible(bool visible)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible)
            {
                // Releasing here matters: without it, pausing mid-drag leaves the stick deflected
                // and the player walks into a wall for as long as the menu is open.
                ReleaseMove();
                _look = Vector2.zero;
                _lookPointerId = -1;
            }
        }

        private VisualElement CreateZone(string name, float leftFraction)
        {
            var zone = new VisualElement { name = name };
            zone.style.position = Position.Absolute;
            zone.style.top = 0;
            zone.style.bottom = 0;
            zone.style.left = Length.Percent(leftFraction * 100f);
            zone.style.width = Length.Percent(50f);

            // Transparent but pickable. The zones are invisible by design: the brief asks for
            // controls that do not dominate the screen, and an always-drawn stick outline on a
            // dark atmospheric game is exactly that kind of clutter.
            zone.pickingMode = PickingMode.Position;
            _root.Add(zone);
            return zone;
        }

        private void RegisterMove(VisualElement zone)
        {
            zone.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_movePointerId != -1)
                {
                    return;
                }

                _movePointerId = evt.pointerId;
                _stickOrigin = evt.position;

                PlaceStick(_stickBase, _stickOrigin, StickRadius);
                PlaceStick(_stickKnob, _stickOrigin, 27f);
                _stickBase.style.display = DisplayStyle.Flex;
                _stickKnob.style.display = DisplayStyle.Flex;

                // Capture so the drag keeps reporting even when the thumb slides outside the zone,
                // which it always does near the screen edge.
                zone.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            zone.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != _movePointerId)
                {
                    return;
                }

                var offset = (Vector2)evt.position - _stickOrigin;
                var clamped = Vector2.ClampMagnitude(offset, StickRadius);

                PlaceStick(_stickKnob, _stickOrigin + clamped, 27f);

                // Screen Y grows downward; movement Y grows forward. The negation is the whole
                // reason pushing up walks forward.
                _move = new Vector2(clamped.x / StickRadius, -clamped.y / StickRadius);
                evt.StopPropagation();
            });

            zone.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != _movePointerId)
                {
                    return;
                }

                zone.ReleasePointer(evt.pointerId);
                ReleaseMove();
                evt.StopPropagation();
            });

            // A pointer that leaves the window never sends PointerUp. Without this the stick stays
            // stuck on and the player keeps walking.
            zone.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseMove());
        }

        private void RegisterLook(VisualElement zone)
        {
            zone.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_lookPointerId != -1)
                {
                    return;
                }

                _lookPointerId = evt.pointerId;
                _lastLookPosition = evt.position;
                zone.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            zone.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != _lookPointerId)
                {
                    return;
                }

                var current = (Vector2)evt.position;
                var delta = current - _lastLookPosition;
                _lastLookPosition = current;

                if (delta.sqrMagnitude < LookDeadzonePoints * LookDeadzonePoints)
                {
                    return;
                }

                // Accumulated, not assigned: several move events can arrive between two frames, and
                // dropping all but the last would lose most of a fast flick.
                _look += new Vector2(delta.x, delta.y) * LookSensitivity;
                evt.StopPropagation();
            });

            zone.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != _lookPointerId)
                {
                    return;
                }

                zone.ReleasePointer(evt.pointerId);
                _lookPointerId = -1;
                evt.StopPropagation();
            });

            zone.RegisterCallback<PointerCaptureOutEvent>(_ => _lookPointerId = -1);
        }

        private void ReleaseMove()
        {
            _movePointerId = -1;
            _move = Vector2.zero;
            _stickBase.style.display = DisplayStyle.None;
            _stickKnob.style.display = DisplayStyle.None;
        }

        private static void PlaceStick(VisualElement element, Vector2 centre, float radius)
        {
            element.style.left = centre.x - radius;
            element.style.top = centre.y - radius;
        }
    }
}

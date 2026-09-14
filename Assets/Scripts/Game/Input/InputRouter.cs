using System;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ForgottenIsle.Game.Input
{
    /// <summary>
    /// The only thing gameplay code talks to about input: two vectors it can read and two events it can
    /// subscribe to, with a single gate deciding whether any of it is live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A ROUTER AT ALL, rather than gameplay reading <see cref="VardholmControls"/> directly. Because
    /// then every reader would have to remember to ask "am I allowed to move right now?", and the day one
    /// of them forgets is the day the player walks off a ledge while reading a journal entry. The question
    /// is asked once, here, and the answer is enforced by turning the devices off.
    /// </para>
    /// <para>
    /// THE GATE, AND WHY IT IS ONE PLACE. <see cref="ApplyGate"/> is the only method in the codebase that
    /// enables or disables an action map. Everything that could suppress input — the game state, an
    /// external suppression such as a cutscene — feeds into that one decision, and the decision is applied
    /// by disabling the map rather than by setting a flag. A disabled action reads as zero and raises no
    /// callbacks, so blocked input is blocked at the source: there is no code path that reads a stale
    /// value, and no queued callback that fires one frame after the pause screen opens.
    /// </para>
    /// <para>
    /// BLOCKED MEANS BLOCKED — FOR THE WORLD. Move, Look and Interact are live in
    /// <see cref="GameStateId.InGame"/> and in no other state: not while paused, not in the menu, not on
    /// the loading screen. While a screen is on the stack, the UI Toolkit layer owns navigation and submit,
    /// and it is a separate input path from this one (ADR-0014). Leaving the gameplay map live underneath
    /// a screen would mean the player's stick both moved the menu cursor and turned the camera behind it.
    /// </para>
    /// <para>
    /// PAUSE IS THE ONE EXCEPTION, AND IT HAS TO BE. It used to sit in the gameplay map and be gated the
    /// same way, which meant the act of pausing switched off the only button that could unpause: a player
    /// on a controller opened the pause screen with Start and then had no way to leave it. Pause therefore
    /// lives in <see cref="VardholmControls.SystemMap"/> and is enabled across BOTH
    /// <see cref="GameStateId.InGame"/> and <see cref="GameStateId.Paused"/>. Which of the two it means is
    /// decided here rather than by the subscriber: a press in InGame raises <see cref="Pause"/>, a press in
    /// Paused raises <see cref="Resume"/>. That keeps "what does this button do right now" answered in the
    /// one place that already owns the gate, and leaves the existing guarantee intact — the gameplay map is
    /// still dark while paused, so no press of this button can also nudge the character.
    /// </para>
    /// <para>
    /// Not a <see cref="MonoBehaviour"/>. It has no transform, no update, and no reason to be findable in
    /// a scene; bootstrap constructs it, hands it to whoever needs it, and disposes it. That also makes it
    /// constructible in a test with a bare state machine.
    /// </para>
    /// </remarks>
    public sealed class InputRouter : IDisposable
    {
        private readonly GameStateMachine _stateMachine;
        private readonly VardholmControls _controls;
        private readonly bool _ownsControls;

        private bool _suppressed;
        private Vector2 _virtualMove;
        private Vector2 _virtualLook;
        private bool _disposed;

        /// <summary>
        /// Wires the router to the state machine whose transitions open and close the gate.
        /// </summary>
        /// <param name="stateMachine">Source of truth for whether the player is in the world. Required.</param>
        /// <param name="controls">
        /// The action set. Null means "build and own one", which is what production does; a caller that
        /// passes its own keeps responsibility for disposing it, so a test can inspect the actions after
        /// the router is gone.
        /// </param>
        public InputRouter(GameStateMachine stateMachine, VardholmControls controls = null)
        {
            if (stateMachine == null)
            {
                throw new ArgumentNullException(nameof(stateMachine));
            }

            _stateMachine = stateMachine;
            _ownsControls = controls == null;
            _controls = controls ?? new VardholmControls();

            _controls.Interact.performed += OnInteractPerformed;
            _controls.Pause.performed += OnPausePerformed;
            _stateMachine.Changed += OnStateChanged;

            // The router may be built after the machine has already left Boot, so the gate is evaluated
            // once up front rather than waiting for the next transition.
            ApplyGate();
        }

        /// <summary>
        /// Raised when the player presses the interact button. Never raised while input is gated, so
        /// subscribers need no state check of their own.
        /// </summary>
        public event Action Interact;

        /// <summary>
        /// Raised when the player presses the pause button while in the world. Only ever raised from
        /// <see cref="GameStateId.InGame"/>.
        /// </summary>
        public event Action Pause;

        /// <summary>
        /// Raised when the player presses the same button again while the game is paused. Only ever
        /// raised from <see cref="GameStateId.Paused"/>.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Pause"/> rather than one event the subscriber has to disambiguate:
        /// the router already knows the mode, and a single event would put a state check back into every
        /// subscriber — which is the duplication this class exists to remove. A subscriber that wires
        /// <see cref="Pause"/> to "open the pause screen" and this to "close it" needs no mode check of
        /// its own and cannot get the two the wrong way round.
        /// </remarks>
        public event Action Resume;

        /// <summary>
        /// Movement intent on the ground plane this frame, or <see cref="Vector2.zero"/> while gated.
        /// </summary>
        /// <remarks>
        /// Read from the action every call rather than cached in an update, so a caller running in
        /// <c>FixedUpdate</c> and a caller running in <c>Update</c> both see the value the Input System has
        /// right now instead of one that is a frame stale in one of them.
        /// </remarks>
        public Vector2 Move
        {
            get
            {
                if (IsBlocked)
                {
                    return Vector2.zero;
                }

                // Touch wins when a thumb is actually on the stick, and hardware input is read
                // otherwise. Summing them instead would make a resting stick cancel a keyboard key,
                // and clamping the sum would make a full-deflection stick feel weaker with a
                // keyboard plugged in.
                var virtualMove = _virtualMove;
                return virtualMove.sqrMagnitude > 0.0001f
                    ? virtualMove
                    : _controls.Move.ReadValue<Vector2>();
            }
        }

        /// <summary>Camera aim contribution this frame, or <see cref="Vector2.zero"/> while gated.</summary>
        public Vector2 Look
        {
            get
            {
                if (IsBlocked)
                {
                    return Vector2.zero;
                }

                // Look is a per-frame delta rather than a held state, so touch is consumed here and
                // added to the hardware value. A flick and a mouse move in the same frame should
                // both count.
                var consumed = _virtualLook;
                _virtualLook = Vector2.zero;
                return consumed + _controls.Look.ReadValue<Vector2>();
            }
        }

        /// <summary>
        /// Supplies movement from an on-screen stick.
        /// </summary>
        /// <remarks>
        /// The bridge between UI Toolkit touch controls and the rig. It lives here rather than in
        /// the rig so that the gate — paused, loading, in a menu — applies to a thumb exactly as it
        /// applies to a key, in one place, with no second code path to forget.
        /// </remarks>
        /// <param name="move">Normalised movement, -1..1 per axis.</param>
        public void SetVirtualMove(Vector2 move)
        {
            _virtualMove = move;
        }

        /// <summary>Adds look delta from an on-screen drag. Consumed on the next <see cref="Look"/> read.</summary>
        /// <param name="look">Look delta for this frame.</param>
        public void AddVirtualLook(Vector2 look)
        {
            _virtualLook += look;
        }

        /// <summary>
        /// True when world input is being withheld — because the game is not in
        /// <see cref="GameStateId.InGame"/>, because something suppressed it, or because the router has
        /// been disposed.
        /// </summary>
        /// <remarks>
        /// Says nothing about the pause button, which is deliberately still live while this reads true in
        /// <see cref="GameStateId.Paused"/>. This is the answer to "may the player move or interact", and
        /// that is what every caller asks it.
        /// </remarks>
        public bool IsBlocked => !IsInputAllowed;

        /// <summary>The action set, for a HUD that needs to draw the glyph currently bound to Interact.</summary>
        public VardholmControls Controls => _controls;

        /// <summary>
        /// Withholds input for a reason the state machine does not model — a cutscene, a cinematic camera,
        /// a scripted sequence.
        /// </summary>
        /// <remarks>
        /// A boolean rather than a counter on purpose. A counter invites unbalanced push/pop pairs, and an
        /// unbalanced pop leaves the player permanently unable to move with no visible cause. One owner, at
        /// a time, sets it and clears it.
        /// </remarks>
        public void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed)
            {
                return;
            }

            _suppressed = suppressed;
            ApplyGate();
        }

        /// <summary>
        /// Unsubscribes from the state machine and the actions, and disposes the controls if this router
        /// built them.
        /// </summary>
        /// <remarks>
        /// The state machine outlives the router, so failing to unsubscribe would keep a dead router alive
        /// through the event's delegate list and let it keep enabling a disposed action map.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _stateMachine.Changed -= OnStateChanged;
            _controls.Interact.performed -= OnInteractPerformed;
            _controls.Pause.performed -= OnPausePerformed;

            Interact = null;
            Pause = null;
            Resume = null;

            if (_ownsControls)
            {
                _controls.Dispose();
            }
            else
            {
                _controls.Disable();
            }
        }

        /// <summary>
        /// The gameplay gate's predicate, stated once. Move, Look and Interact are live only in the
        /// world, only when nothing has suppressed them, and never after disposal.
        /// </summary>
        private bool IsInputAllowed => !_disposed && !_suppressed && _stateMachine.Current == GameStateId.InGame;

        /// <summary>
        /// The system gate's predicate. The pause button is live wherever a run exists — in the world and
        /// on the pause screen — because the press that opens the screen has to be able to close it.
        /// </summary>
        /// <remarks>
        /// Suppression still applies. A cutscene that has taken the camera has taken the pause button with
        /// it, which is the behaviour <see cref="SetSuppressed"/> was written for: one owner, holding
        /// everything, for a bounded stretch. Widening the exception to cover suppression as well would
        /// let a screen open on top of a sequence that is mid-way through moving the player.
        /// </remarks>
        private bool IsSystemAllowed
        {
            get
            {
                if (_disposed || _suppressed)
                {
                    return false;
                }

                var current = _stateMachine.Current;
                return current == GameStateId.InGame || current == GameStateId.Paused;
            }
        }

        /// <summary>
        /// THE GATE. The single point at which either action map is enabled or disabled.
        /// </summary>
        /// <remarks>
        /// Every caller that wants to change whether input flows changes an input to
        /// <see cref="IsInputAllowed"/> or <see cref="IsSystemAllowed"/> and then calls this. Nothing else
        /// touches the maps' enabled state. The two maps are evaluated independently and in one pass, so
        /// there is no window in which the world is live and the pause button is not, or the reverse.
        /// </remarks>
        private void ApplyGate()
        {
            if (IsInputAllowed)
            {
                _controls.EnableGameplay();
            }
            else
            {
                _controls.DisableGameplay();
            }

            if (IsSystemAllowed)
            {
                _controls.EnableSystem();
            }
            else
            {
                _controls.DisableSystem();
            }
        }

        private void OnStateChanged(GameStateId from, GameStateId to)
        {
            ApplyGate();
        }

        /// <summary>
        /// Forwards a button callback as a plain event.
        /// </summary>
        /// <remarks>
        /// The gate is re-checked here even though a disabled action cannot fire. The Input System
        /// dispatches queued callbacks at a fixed point in the frame, so a press that landed in the same
        /// frame as the transition into the pause screen can still be delivered after the map was disabled.
        /// This check is what stops that press from being acted on in a state that forbids it.
        /// </remarks>
        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            if (IsBlocked)
            {
                return;
            }

            var handler = Interact;
            handler?.Invoke();
        }

        /// <summary>
        /// Routes one pause press to whichever of the two events the current mode calls for.
        /// </summary>
        /// <remarks>
        /// The gate is re-checked here for the reason given on <see cref="OnInteractPerformed"/>, and the
        /// mode is read once so that a press cannot be classified against one state and delivered against
        /// another. A press arriving in any other mode is dropped: the system map is disabled outside
        /// InGame and Paused, so the only way to reach here otherwise is a callback queued across the
        /// transition that disabled it.
        /// </remarks>
        private void OnPausePerformed(InputAction.CallbackContext context)
        {
            if (_disposed || _suppressed)
            {
                return;
            }

            var current = _stateMachine.Current;
            if (current == GameStateId.InGame)
            {
                var pause = Pause;
                pause?.Invoke();
                return;
            }

            if (current == GameStateId.Paused)
            {
                var resume = Resume;
                resume?.Invoke();
            }
        }
    }
}

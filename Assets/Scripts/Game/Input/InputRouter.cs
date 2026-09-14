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
    /// enables or disables the action map. Everything that could suppress input — the game state, an
    /// external suppression such as a cutscene — feeds into that one decision, and the decision is applied
    /// by disabling the map rather than by setting a flag. A disabled action reads as zero and raises no
    /// callbacks, so blocked input is blocked at the source: there is no code path that reads a stale
    /// value, and no queued callback that fires one frame after the pause screen opens.
    /// </para>
    /// <para>
    /// BLOCKED MEANS BLOCKED. Input is live in <see cref="GameStateId.InGame"/> and in no other state —
    /// not while paused, not in the menu, not on the loading screen. That includes the Pause action itself,
    /// which raises the obvious question of how a paused game is ever resumed: it is resumed by the UI.
    /// While a screen is on the stack, the UI Toolkit layer owns navigation and submit, and it is a
    /// separate input path from this one (ADR-0014). Leaving the gameplay map live underneath a screen
    /// would mean the player's stick both moved the menu cursor and turned the camera behind it.
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
        /// Raised when the player asks to pause. Only ever raised from
        /// <see cref="GameStateId.InGame"/> — see the class remarks on how the pause screen is dismissed.
        /// </summary>
        public event Action Pause;

        /// <summary>
        /// Movement intent on the ground plane this frame, or <see cref="Vector2.zero"/> while gated.
        /// </summary>
        /// <remarks>
        /// Read from the action every call rather than cached in an update, so a caller running in
        /// <c>FixedUpdate</c> and a caller running in <c>Update</c> both see the value the Input System has
        /// right now instead of one that is a frame stale in one of them.
        /// </remarks>
        public Vector2 Move => IsBlocked ? Vector2.zero : _controls.Move.ReadValue<Vector2>();

        /// <summary>Camera aim contribution this frame, or <see cref="Vector2.zero"/> while gated.</summary>
        public Vector2 Look => IsBlocked ? Vector2.zero : _controls.Look.ReadValue<Vector2>();

        /// <summary>
        /// True when input is being withheld — because the game is not in
        /// <see cref="GameStateId.InGame"/>, because something suppressed it, or because the router has
        /// been disposed.
        /// </summary>
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
        /// The gate's predicate, stated once. Input is live only in the world, only when nothing has
        /// suppressed it, and never after disposal.
        /// </summary>
        private bool IsInputAllowed => !_disposed && !_suppressed && _stateMachine.Current == GameStateId.InGame;

        /// <summary>
        /// THE GATE. The single point at which the action map is enabled or disabled.
        /// </summary>
        /// <remarks>
        /// Every caller that wants to change whether input flows changes an input to
        /// <see cref="IsInputAllowed"/> and then calls this. Nothing else touches
        /// <see cref="VardholmControls.Enable"/> or <see cref="VardholmControls.Disable"/>.
        /// </remarks>
        private void ApplyGate()
        {
            if (IsInputAllowed)
            {
                _controls.Enable();
            }
            else
            {
                _controls.Disable();
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

        /// <inheritdoc cref="OnInteractPerformed" />
        private void OnPausePerformed(InputAction.CallbackContext context)
        {
            if (IsBlocked)
            {
                return;
            }

            var handler = Pause;
            handler?.Invoke();
        }
    }
}

using System;
using UnityEngine.InputSystem;

namespace ForgottenIsle.Game.Input
{
    /// <summary>
    /// Vardholm's entire input surface — Move, Look, Interact, Pause — built in code as two
    /// <see cref="InputActionMap"/>s: one for the world, one for the actions that must outlive it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY NO <c>.inputactions</c> ASSET. The generated asset is JSON with a Unity-controlled shape and a
    /// generated C# wrapper beside it: a pair of files that no human reviews, that regenerate on import,
    /// and whose diffs are unreadable. A rebinding that silently disappears in a merge is invisible in
    /// review and shows up as "the interact button stopped working on controllers" three weeks later.
    /// Every binding below is a line of source. It can be read, diffed, commented, and reasoned about
    /// without opening the Editor — which, for a project whose contracts are verified by reading rather
    /// than by compiling, is the difference between a reviewable input layer and an opaque one.
    /// </para>
    /// <para>
    /// WHY TWO MAPS AND NOT ONE. There used to be one map holding all four actions, enabled only in
    /// <c>GameStateId.InGame</c>. That made pausing irreversible from the controller: opening the pause
    /// screen leaves InGame, the whole map goes dark, and the Pause button — sitting inside the map that
    /// was just disabled — can never be pressed again. The player could open the pause screen with Start
    /// and then had no way to close it with Start. Splitting the actions is what fixes it. Move, Look and
    /// Interact live in <see cref="GameplayMapName"/> and stay gated to the world. Pause lives alone in
    /// <see cref="SystemMapName"/>, which <see cref="InputRouter"/> keeps enabled across both InGame and
    /// Paused, so the button that opens the screen is the button that closes it.
    /// </para>
    /// <para>
    /// The split does not weaken the blocking guarantee, because the guarantee was about the gameplay
    /// actions and those are still switched off wholesale. Nothing in the system map can move the
    /// character or touch the world: it holds exactly one button, and that button asks the mode machine
    /// to change mode.
    /// </para>
    /// <para>
    /// WHY IT IS NOT AN <c>IInputActionCollection</c>. That interface exists to let the generated wrapper
    /// be handed to <c>PlayerInput</c> and to control-scheme machinery this game does not use. Vardholm
    /// gates its maps from <see cref="InputRouter"/>. Implementing the interface would mean implementing
    /// device filtering, binding masks and scheme switching in order to satisfy callers that do not exist.
    /// </para>
    /// <para>
    /// A map may only be modified while disabled, so everything is built in the constructor and nothing
    /// mutates afterwards. Callers read the actions; they do not rebind them.
    /// </para>
    /// </remarks>
    public sealed class VardholmControls : IDisposable
    {
        /// <summary>Name of the world map, as it appears in the Input Debugger.</summary>
        public const string GameplayMapName = "Gameplay";

        /// <summary>
        /// Name of the map holding actions that must remain pressable outside the world.
        /// </summary>
        /// <remarks>
        /// Named for what it is rather than for its single member, because "the map that is not gated to
        /// InGame" is the property that matters and the next action to earn it (a screenshot key, a
        /// debug console) belongs beside Pause rather than in a third map.
        /// </remarks>
        public const string SystemMapName = "System";

        /// <summary>
        /// Deadzone applied to both sticks.
        /// </summary>
        /// <remarks>
        /// 0.15 rather than the default 0.125 because Vardholm is played in long traversal stretches where
        /// a worn stick's resting drift walks the character off a cliff over a minute of no input. The
        /// upper bound clamps slightly below 1 so a stick that cannot quite reach its corner still produces
        /// full-speed movement.
        /// </remarks>
        public const string StickDeadzone = "stickDeadzone(min=0.15,max=0.95)";

        /// <summary>
        /// Scale applied to raw mouse delta.
        /// </summary>
        /// <remarks>
        /// Mouse delta arrives in pixels and stick input in the range -1..1. Without this they would drive
        /// the same camera code at wildly different magnitudes, and the sensitivity setting would mean two
        /// different things depending on the device in the player's hand.
        /// </remarks>
        public const string MouseLookScale = "scaleVector2(x=0.05,y=0.05)";

        private readonly InputActionMap _gameplay;
        private readonly InputActionMap _system;
        private bool _disposed;

        /// <summary>
        /// Builds both maps and every binding. Cheap enough to do at boot; the Input System resolves
        /// bindings lazily when a map is first enabled.
        /// </summary>
        public VardholmControls()
        {
            _gameplay = new InputActionMap(GameplayMapName);

            // MOVE — the character's intended direction on the ground plane, as a sustained value.
            Move = _gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddBinding("<Gamepad>/leftStick", processors: StickDeadzone);
            // mode=2 is "digital normalized": the four keys produce a unit vector, so holding two of them
            // walks diagonally at walking speed rather than at 1.41x walking speed.
            Move.AddCompositeBinding("2DVector(mode=2)")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector(mode=2)")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            // LOOK — camera aim. Mouse contributes a per-frame delta, the stick a rate; both are read the
            // same way and the scale processors above are what makes that honest.
            Look = _gameplay.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            Look.AddBinding("<Mouse>/delta", processors: MouseLookScale);
            Look.AddBinding("<Gamepad>/rightStick", processors: StickDeadzone);

            // INTERACT — the single context-sensitive verb: pick up, open, read, use.
            Interact = _gameplay.AddAction("Interact", InputActionType.Button);
            Interact.AddBinding("<Keyboard>/e");
            Interact.AddBinding("<Gamepad>/buttonSouth");

            // NOT <Touchscreen>/primaryTouch/tap. An action bound to that control fires from the
            // device regardless of what UI element was under the finger (VERIFY on device; the
            // UI Toolkit runtime panel does not consume Input System actions:
            // https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/UISupport.html),
            // so on a phone every tap on
            // the pause button, an inventory chip or the radio's dial was ALSO the world verb: with
            // the brass tag in range, tapping "Put down" could take the tag. On touch the verb is
            // the prompt card itself, which the HUD raises through InputRouter.RequestInteract.

            _system = new InputActionMap(SystemMapName);

            // PAUSE — opens the pause screen, and closes it again. Bound to Escape and Start only;
            // deliberately not to a face button, where a mistimed press during traversal would throw the
            // player out of the world. It is the only action in this map, and the map is the only reason
            // the pause screen is escapable without a mouse.
            Pause = _system.AddAction("Pause", InputActionType.Button);
            Pause.AddBinding("<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");
        }

        /// <summary>Ground-plane movement intent, -1..1 on each axis. Read every frame.</summary>
        public InputAction Move { get; }

        /// <summary>Camera aim contribution for this frame, already scaled to a device-independent range.</summary>
        public InputAction Look { get; }

        /// <summary>Context-sensitive interaction. Consumed as an event, not polled.</summary>
        public InputAction Interact { get; }

        /// <summary>Request to open or close the pause screen. Consumed as an event.</summary>
        public InputAction Pause { get; }

        /// <summary>
        /// The world map — Move, Look and Interact — exposed so the router can gate all three at once.
        /// </summary>
        public InputActionMap Gameplay => _gameplay;

        /// <summary>The map holding Pause, gated separately so pausing stays reversible.</summary>
        public InputActionMap SystemMap => _system;

        /// <summary>True while the world map is listening to devices.</summary>
        public bool IsGameplayEnabled => _gameplay != null && _gameplay.enabled;

        /// <summary>True while the system map is listening to devices.</summary>
        public bool IsSystemEnabled => _system != null && _system.enabled;

        /// <summary>True only when both maps are listening.</summary>
        public bool IsEnabled => IsGameplayEnabled && IsSystemEnabled;

        /// <summary>
        /// Starts listening on both maps.
        /// </summary>
        /// <remarks>
        /// Kept as a convenience for callers that want everything live at once. The router does not use
        /// it: the whole point of the split is that the two maps are enabled on different conditions.
        /// </remarks>
        public void Enable()
        {
            EnableGameplay();
            EnableSystem();
        }

        /// <summary>
        /// Stops listening on both maps.
        /// </summary>
        /// <remarks>
        /// A disabled action reads as its default value and raises no callbacks, so disabling a map is a
        /// genuine block rather than a flag that every read site has to remember to check. That property is
        /// what <see cref="InputRouter"/>'s gate is built on.
        /// </remarks>
        public void Disable()
        {
            DisableGameplay();
            DisableSystem();
        }

        /// <summary>
        /// Starts listening for Move, Look and Interact. Idempotent — enabling an enabled map is a no-op
        /// in the Input System, and callers should not have to track which state they left it in.
        /// </summary>
        public void EnableGameplay()
        {
            if (_disposed)
            {
                return;
            }

            _gameplay.Enable();
        }

        /// <summary>Stops listening for Move, Look and Interact. Idempotent.</summary>
        public void DisableGameplay()
        {
            if (_disposed)
            {
                return;
            }

            _gameplay.Disable();
        }

        /// <summary>Starts listening for Pause. Idempotent.</summary>
        public void EnableSystem()
        {
            if (_disposed)
            {
                return;
            }

            _system.Enable();
        }

        /// <summary>Stops listening for Pause. Idempotent.</summary>
        public void DisableSystem()
        {
            if (_disposed)
            {
                return;
            }

            _system.Disable();
        }

        /// <summary>
        /// Releases both maps' unmanaged state.
        /// </summary>
        /// <remarks>
        /// Required, not optional: an undisposed map keeps its action state allocated and its callbacks
        /// registered with the Input System for the lifetime of the process, which in the Editor means it
        /// survives exiting play mode and accumulates one copy per session.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _gameplay.Disable();
            _system.Disable();
            _gameplay.Dispose();
            _system.Dispose();
        }
    }
}

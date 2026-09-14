using ForgottenIsle.Core.Logging;
using ForgottenIsle.Game.Input;
using ForgottenIsle.Game.Session;
using UnityEngine;

namespace ForgottenIsle.Game.Player
{
    /// <summary>
    /// A capsule that walks and a camera that yaws. Phase 1's entire locomotion system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY this is not a <c>CharacterController</c> and not a rigidbody: Phase 1's job is to prove the
    /// architecture — that input reaches a view, that the view's pose reaches the session, and that
    /// the session's pose survives a save. Real locomotion (slopes, steps, water, the sickness-safe
    /// camera work ADR-0008 commits to) is Phase 2's problem and will be built against a movement
    /// spec that does not exist yet. A capsule moved by <c>transform.position</c> proves everything
    /// Phase 1 needs to prove and throws away cleanly, whereas a half-tuned character controller would
    /// be argued with for months.
    /// </para>
    /// <para>
    /// WHY the router is polled rather than subscribed to: <see cref="InputRouter"/> reads its actions
    /// on demand and returns centred sticks whenever input is gated, so a poll is always the value the
    /// Input System holds right now and needs no unsubscribe on teardown. The router is handed in by
    /// the bootstrap rather than reached for, so the dependency is still declared rather than
    /// discovered — which is the part ADR-0012 actually cares about.
    /// </para>
    /// <para>
    /// WHY <c>LateUpdate</c> and not <c>Update</c>: <see cref="Bootstrap.Ticker"/> owns the only
    /// <c>Update</c> because the simulation must advance exactly once per frame, before anything
    /// observes it. This rig is presentation, not simulation: it reads input that already arrived and
    /// moves a transform. Running it after the tick is precisely the ordering guarantee the
    /// single-<c>Update</c> rule exists to produce, so it is honoured here rather than broken.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerRig : MonoBehaviour
    {
        /// <summary>Default walking speed, metres per second.</summary>
        public const float DefaultMoveSpeed = 3.2f;

        /// <summary>Default yaw rate, degrees per second at full stick deflection.</summary>
        public const float DefaultYawDegreesPerSecond = 120f;

        /// <summary>
        /// Default seconds between pose writes to the session.
        /// </summary>
        /// <remarks>
        /// One second, matching the architecture's "written once per second and on every save". The
        /// value only has to be correct when a save is taken, and a per-frame domain write would put a
        /// mutation on the hottest path in the game to buy an accuracy nothing can observe.
        /// </remarks>
        public const float DefaultPoseWriteIntervalSeconds = 1f;

        /// <summary>Below this magnitude a stick reading is treated as centred.</summary>
        private const float InputDeadzone = 0.01f;

        [Header("Movement")]
        [SerializeField, Tooltip("Metres per second at full stick deflection.")]
        private float _moveSpeed = DefaultMoveSpeed;

        [SerializeField, Tooltip("Degrees per second at full look deflection.")]
        private float _yawDegreesPerSecond = DefaultYawDegreesPerSecond;

        [Header("Persistence")]
        [SerializeField, Tooltip("Seconds between writes of this rig's pose into the session.")]
        private float _poseWriteIntervalSeconds = DefaultPoseWriteIntervalSeconds;

        [Header("References")]
        [SerializeField, Tooltip("Transform yawed by look input. Usually the camera's parent pivot.")]
        private Transform _cameraPivot;

        private SessionService _session;
        private InputRouter _input;
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private float _yawDegrees;
        private float _sinceLastPoseWrite;

        /// <summary>True once the rig has been given a session to report its pose to.</summary>
        public bool IsBound => _session != null;

        /// <summary>Current facing in degrees, matching what is written to the session.</summary>
        public float YawDegrees => _yawDegrees;

        /// <summary>
        /// Binds the rig to the run it reports into.
        /// </summary>
        /// <remarks>
        /// Called by <c>AppBootstrap</c> once the zone containing this rig is resident. Until then, and
        /// if it is never called at all, the rig still moves — it simply does not persist. That is the
        /// right degradation for a scene opened directly in the editor: the thing under test still
        /// works, and nothing writes to a session that does not exist.
        /// </remarks>
        /// <param name="session">The run that owns player state. Required for persistence.</param>
        /// <param name="input">
        /// The gated input router. Null leaves the rig driven only by <see cref="SetMoveInput"/> and
        /// <see cref="SetLookInput"/>, which is how an EditMode test moves it without an Input System
        /// device.
        /// </param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public void Initialize(SessionService session, InputRouter input, ICoreLog log)
        {
            _session = session;
            _input = input;

            if (session == null)
            {
                if (log != null)
                {
                    log.Warn(LogCode.CatalogMissing, nameof(PlayerRig));
                }

                return;
            }

            // Adopt the persisted pose so a loaded game puts the player back where they stood rather
            // than wherever the scene author left the prefab.
            transform.position = session.PlayerPosition.ToVector3();
            SetYaw(session.PlayerYawDegrees);
            _sinceLastPoseWrite = 0f;
        }

        /// <summary>
        /// Overrides the locomotion stick, as x = strafe, y = forward, each in -1..1.
        /// </summary>
        /// <remarks>
        /// Used only when no router is bound. Stored rather than applied, because movement must scale
        /// with frame time and not with how many times a caller set the value.
        /// </remarks>
        public void SetMoveInput(Vector2 move)
        {
            _moveInput = move;
        }

        /// <summary>Overrides the look stick, as x = yaw, in -1..1. Pitch is Phase 2's problem.</summary>
        public void SetLookInput(Vector2 look)
        {
            _lookInput = look;
        }

        /// <summary>
        /// Clears both sticks.
        /// </summary>
        /// <remarks>
        /// Needed only on the override path: a stick left set by a test or a cutscene script would
        /// otherwise keep the rig walking forever, because nothing else overwrites the stored value.
        /// A bound router gates itself and needs no help here.
        /// </remarks>
        public void ClearInput()
        {
            _moveInput = Vector2.zero;
            _lookInput = Vector2.zero;
        }

        /// <summary>
        /// Writes the current pose into the session immediately, ignoring the cadence.
        /// </summary>
        /// <remarks>
        /// Called before a save so the written file reflects where the player is standing rather than
        /// where they were up to a second ago. This is the "and on every save" half of the cadence
        /// rule; without it the cadence would be a visible teleport backwards on every reload.
        /// </remarks>
        public void FlushPose()
        {
            if (_session == null)
            {
                return;
            }

            _session.SetPlayerPose(transform.position.ToVec3(), _yawDegrees);
            _sinceLastPoseWrite = 0f;
        }

        private void LateUpdate()
        {
            var delta = UnityEngine.Time.deltaTime;
            if (delta <= 0f)
            {
                return;
            }

            // A bound router is authoritative: it already returns centred sticks while input is gated,
            // so reading it unconditionally is also what stops the rig moving during a load or a pause.
            if (_input != null)
            {
                _moveInput = _input.Move;
                _lookInput = _input.Look;
            }

            ApplyLook(delta);
            ApplyMove(delta);
            AdvancePoseCadence(delta);
        }

        private void ApplyLook(float delta)
        {
            if (Mathf.Abs(_lookInput.x) < InputDeadzone)
            {
                return;
            }

            SetYaw(_yawDegrees + _lookInput.x * _yawDegreesPerSecond * delta);
        }

        private void ApplyMove(float delta)
        {
            if (_moveInput.sqrMagnitude < InputDeadzone * InputDeadzone)
            {
                return;
            }

            // Clamp rather than normalise: a stick pushed half way should walk at half speed, and only
            // a diagonal at full deflection needs bringing back under one.
            var input = _moveInput;
            if (input.sqrMagnitude > 1f)
            {
                input = input.normalized;
            }

            var yawRadians = _yawDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(yawRadians);
            var cos = Mathf.Cos(yawRadians);

            // Rotate the stick into the rig's facing, so "forward" means where the camera is pointed.
            var worldX = input.x * cos + input.y * sin;
            var worldZ = input.y * cos - input.x * sin;

            transform.position += new Vector3(worldX, 0f, worldZ) * (_moveSpeed * delta);
        }

        private void AdvancePoseCadence(float delta)
        {
            if (_session == null)
            {
                return;
            }

            var interval = _poseWriteIntervalSeconds > 0f ? _poseWriteIntervalSeconds : DefaultPoseWriteIntervalSeconds;
            _sinceLastPoseWrite += delta;
            if (_sinceLastPoseWrite < interval)
            {
                return;
            }

            FlushPose();
        }

        private void SetYaw(float degrees)
        {
            // Kept in 0..360 so the serialized value never grows without bound over a long session,
            // which would eventually cost float precision on the angle itself.
            _yawDegrees = Mathf.Repeat(degrees, 360f);

            var rotation = CoreInterop.YawToRotation(_yawDegrees);
            transform.rotation = rotation;
            if (_cameraPivot != null)
            {
                _cameraPivot.rotation = rotation;
            }
        }

        private void OnDisable()
        {
            // The zone is being unloaded or the rig switched off. Persist the final pose while the
            // transform still exists, so travel and quit both capture where the player actually stood.
            FlushPose();
        }
    }
}

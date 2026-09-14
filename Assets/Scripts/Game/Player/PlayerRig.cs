using System;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Game.Input;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Session;
using UnityEngine;

namespace ForgottenIsle.Game.Player
{
    /// <summary>
    /// A capsule that walks and a camera that yaws. Phase 1's entire locomotion system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PHASE 2: this now drives a <c>CharacterController</c>. Phase 1 moved the transform directly,
    /// which was right while the only thing being proved was that input reaches a view and a pose
    /// reaches the session — but it walks through hills, and the zones now have hills. The controller
    /// buys ground following, slope handling and step-over for the cost of one component.
    /// <para>
    /// Feel is deliberately asymmetric: acceleration is slower than deceleration, so starting has
    /// weight and stopping is crisp. Yaw turns the body; pitch moves only the camera pivot, clamped,
    /// because pitching the body would tilt the direction the player walks.
    /// </para>
    /// <para>
    /// The transform-move path is kept as a fallback for a rig with no controller — a hand-authored
    /// one, or an EditMode test — so movement degrades rather than failing.
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

        /// <summary>Metres per second squared while the stick is pushed.</summary>
        public const float DefaultAcceleration = 18f;

        /// <summary>Metres per second squared while it is not. Higher, so stopping feels crisp.</summary>
        public const float DefaultDeceleration = 26f;

        /// <summary>Downward acceleration. Earth gravity reads as floaty at this scale.</summary>
        public const float DefaultGravity = -22f;

        /// <summary>How far the player may look up or down, in degrees.</summary>
        public const float DefaultPitchLimit = 78f;

        [Header("Movement feel")]
        [SerializeField, Tooltip("Metres per second squared while accelerating.")]
        private float _acceleration = DefaultAcceleration;

        [SerializeField, Tooltip("Metres per second squared while stopping.")]
        private float _deceleration = DefaultDeceleration;

        [SerializeField, Tooltip("Downward acceleration in metres per second squared.")]
        private float _gravity = DefaultGravity;

        [SerializeField, Tooltip("Maximum look pitch above and below the horizon, in degrees.")]
        private float _pitchLimit = DefaultPitchLimit;

        [Header("References")]
        [SerializeField, Tooltip("Transform yawed by look input. Usually the camera's parent pivot.")]
        private Transform _cameraPivot;

        /// <summary>The transform this rig yaws with look input, if it has one.</summary>
        /// <remarks>
        /// Exposed so the runtime furnisher can ask whether a pivot is already wired before building
        /// one. Read-only: nothing outside this component may re-parent or re-point the camera.
        /// </remarks>
        public Transform CameraPivot
        {
            get { return _cameraPivot; }
        }

        /// <summary>
        /// Supplies the camera pivot at runtime, for a rig that was created rather than authored.
        /// </summary>
        /// <remarks>
        /// The field is <c>[SerializeField]</c> so a hand-authored rig can be wired in the Inspector,
        /// but a furnished rig has no Inspector pass to be wired in. This was the last dependency in
        /// the project that required manual assignment; with it settable at runtime, a zone scene can
        /// be completely empty and still produce a playable camera.
        /// <para>
        /// An already-assigned pivot wins: an authored rig's own camera is never replaced.
        /// </para>
        /// </remarks>
        /// <param name="pivot">Transform to yaw. Ignored when null, or when one is already set.</param>
        public void AttachCameraPivot(Transform pivot)
        {
            if (pivot == null || _cameraPivot != null)
            {
                return;
            }

            _cameraPivot = pivot;
            _cameraPivot.rotation = Quaternion.Euler(0f, _yawDegrees, 0f);
        }

        private SessionService _session;
        private InputRouter _input;
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private float _yawDegrees;
        private float _sinceLastPoseWrite;
        private CharacterController _controller;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _pitchDegrees;
        private InteractionSystem _interactions;

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
        private void Awake()
        {
            // May legitimately be absent: the furnisher adds one, but a hand-authored rig might not,
            // and ApplyMove falls back to a transform move in that case.
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// Moves the rig without the controller fighting the write.
        /// </summary>
        /// <remarks>
        /// A <c>CharacterController</c> caches its own position and will snap back if the transform
        /// is written underneath it. Disabling it across the write is the documented way to teleport,
        /// and every spawn, anchor placement and restored pose goes through here for that reason.
        /// </remarks>
        /// <param name="position">Where to put the rig.</param>
        public void Teleport(Vector3 position)
        {
            if (_controller != null)
            {
                _controller.enabled = false;
                transform.position = position;
                _controller.enabled = true;
            }
            else
            {
                transform.position = position;
            }

            // Velocity from before the teleport is meaningless at the destination, and carrying it
            // over launches the player sideways out of a fresh zone.
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        public void Initialize(SessionService session, InputRouter input, ICoreLog log)
        {
            Initialize(session, input, null, log);
        }

        /// <summary>
        /// Binds the rig, including the interaction system it drives.
        /// </summary>
        /// <remarks>
        /// The rig owns the interaction tick because it is the one component that already runs every
        /// frame with the player's final position in hand, and the project's rule is one central
        /// per-frame loop rather than an Update per system. A proximity scan wants exactly what
        /// LateUpdate has just finished computing.
        /// </remarks>
        /// <param name="session">Run state the pose is written into.</param>
        /// <param name="input">Input source. Null leaves the rig inert.</param>
        /// <param name="interactions">Interaction system to drive. Null disables interaction.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public void Initialize(
            SessionService session, InputRouter input, InteractionSystem interactions, ICoreLog log)
        {
            _session = session;

            if (_input != null)
            {
                // Rebinding happens on every zone entry; without this the previous zone's rig stays
                // subscribed and a single button press fires two interactions.
                _input.Interact -= OnInteractPressed;
            }

            _input = input;
            _interactions = interactions;

            if (_input != null)
            {
                _input.Interact += OnInteractPressed;
            }

            if (session == null)
            {
                if (log != null)
                {
                    log.Warn(LogCode.CatalogMissing, nameof(PlayerRig));
                }

                return;
            }

            if (IsContinuingInThisZone(session))
            {
                // A genuine continue: the run was already standing in this zone, so put it back exactly
                // where it stood rather than wherever the scene author left the prefab.
                Teleport(session.PlayerPosition.ToVector3());
                SetYaw(session.PlayerYawDegrees);
            }
            else
            {
                PlaceOnEntryAnchor(log);
            }

            // Written through immediately rather than waited for: until this lands, the session still
            // holds the pose the OUTGOING zone's rig flushed on its way out, and a save taken inside the
            // cadence window would record the new zone's id against the old zone's coordinates.
            _sinceLastPoseWrite = 0f;
            FlushPose();
        }

        /// <summary>
        /// Decides whether the restored pose belongs to the zone this rig is standing in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// THE ARRIVAL RULE. A run's stored pose is coordinates in one specific zone, and coordinates do
        /// not survive being carried into another one — the same numbers that put the player on the wreck
        /// shelf in Ribcage put them inside a fern bank, or in open air, in Fernmaw. So the pose is
        /// restored ONLY when the zone recorded in the session is the zone this rig belongs to, which is
        /// exactly the continue-a-saved-run case: the save named the zone, the loader brought that zone
        /// in, and the two agree. On every other arrival — travel, which loads the destination while the
        /// session still names the zone being left, and a new run, which has no pose yet — the rig is
        /// placed on the destination's own <see cref="ZoneEntryAnchor"/> instead.
        /// </para>
        /// <para>
        /// WHY THE ORIGIN COUNTS AS "NO POSE": a fresh run is zeroed by <c>SessionService.BeginNewRun</c>,
        /// which sets the starting zone AND a zero pose, so the zone check alone would drop a new game at
        /// world origin. The origin with zero yaw is a default rather than a place anybody stood, and it
        /// is indistinguishable from one; treating it as unset costs a genuine save taken at exactly
        /// (0, 0, 0) facing north its handful of centimetres, and buys every new run a spawn point that
        /// was authored rather than assumed.
        /// </para>
        /// </remarks>
        private bool IsContinuingInThisZone(SessionService session)
        {
            var zoneKey = gameObject.scene.name;
            if (string.IsNullOrEmpty(zoneKey) || !string.Equals(session.ZoneId, zoneKey, StringComparison.Ordinal))
            {
                return false;
            }

            return session.PlayerPosition != Vec3.Zero || session.PlayerYawDegrees != 0f;
        }

        /// <summary>
        /// Stands the rig on this zone's authored entry anchor.
        /// </summary>
        /// <remarks>
        /// A zone with no anchor leaves the rig exactly where the scene author placed it and says so in
        /// the log. That is the honest degradation: the prefab's authored position is a real position in
        /// this zone's coordinates, which is already better than the previous zone's, and Phase 1 zones
        /// are permitted to be unfinished.
        /// </remarks>
        private void PlaceOnEntryAnchor(ICoreLog log)
        {
            var anchor = ZoneEntryAnchor.FindInScene(gameObject.scene);
            if (anchor == null)
            {
                if (log != null)
                {
                    log.Warn(LogCode.CatalogMissing, nameof(ZoneEntryAnchor) + ":" + gameObject.scene.name);
                }

                // Adopt the authored rotation rather than leaving _yawDegrees at its default, which would
                // snap the capsule to north on the first frame and write that to the session.
                SetYaw(CoreInterop.RotationToYaw(transform.rotation));
                return;
            }

            Teleport(anchor.Position);
            SetYaw(anchor.YawDegrees);
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

            if (_interactions != null)
            {
                _interactions.Tick(transform.position);
            }
        }

        private void ApplyLook(float delta)
        {
            // Yaw turns the whole body, because movement is relative to facing. Pitch moves only the
            // camera pivot -- pitching the body would tilt the direction the player walks, which is
            // the classic "I look down and sink into the floor" bug.
            if (Mathf.Abs(_lookInput.x) >= InputDeadzone)
            {
                SetYaw(_yawDegrees + _lookInput.x * _yawDegreesPerSecond * delta);
            }

            if (Mathf.Abs(_lookInput.y) >= InputDeadzone)
            {
                // Inverted deliberately: dragging down on a touchscreen looks down, matching how
                // every other phone camera control behaves.
                _pitchDegrees = Mathf.Clamp(
                    _pitchDegrees - _lookInput.y * _yawDegreesPerSecond * delta,
                    -_pitchLimit,
                    _pitchLimit);

                ApplyPitch();
            }
        }

        private void ApplyPitch()
        {
            if (_cameraPivot == null)
            {
                return;
            }

            _cameraPivot.localRotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
        }

        private void ApplyMove(float delta)
        {
            var input = _moveInput;
            if (input.sqrMagnitude < InputDeadzone * InputDeadzone)
            {
                input = Vector2.zero;
            }
            else if (input.sqrMagnitude > 1f)
            {
                // Clamp rather than normalise: a stick pushed half way walks at half speed, and only
                // a diagonal at full deflection needs bringing back under one.
                input = input.normalized;
            }

            var yawRadians = _yawDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(yawRadians);
            var cos = Mathf.Cos(yawRadians);

            // Rotate the stick into the rig's facing, so "forward" means where the camera points.
            var desired = new Vector3(
                input.x * cos + input.y * sin,
                0f,
                input.y * cos - input.x * sin) * _moveSpeed;

            // Accelerating and decelerating at different rates is most of what separates "a capsule
            // teleporting around" from "a person walking". Stopping is faster than starting.
            var rate = desired.sqrMagnitude > 0.0001f ? _acceleration : _deceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, desired, rate * delta);

            if (_controller == null)
            {
                // No controller (a hand-authored rig without one, or a test): fall back to the
                // transform move that Phase 1 used. Movement still works; ground following does not.
                transform.position += _horizontalVelocity * delta;
                return;
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                // A small downward bias rather than zero: exactly zero makes isGrounded flicker on
                // slopes, which makes the character stutter and stick.
                _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity += _gravity * delta;
            }

            var motion = _horizontalVelocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * delta);
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

        private void OnInteractPressed()
        {
            if (_interactions != null)
            {
                _interactions.Activate();
            }
        }

        private void OnDestroy()
        {
            if (_input != null)
            {
                _input.Interact -= OnInteractPressed;
            }
        }

        private void OnDisable()
        {
            // The zone is being unloaded or the rig switched off. Persist the final pose while the
            // transform still exists, so a quit or a save captures where the player actually stood.
            //
            // On travel this writes the OUTGOING zone's coordinates into the session, and that is
            // harmless rather than a leak: the arriving rig compares the session's zone against its own
            // (see IsContinuingInThisZone) and ignores a pose that belongs to the zone being left.
            FlushPose();
        }
    }
}

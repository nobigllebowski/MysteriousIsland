using System;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
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

            if (IsContinuingInThisZone(session))
            {
                // A genuine continue: the run was already standing in this zone, so put it back exactly
                // where it stood rather than wherever the scene author left the prefab.
                transform.position = session.PlayerPosition.ToVector3();
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

            transform.position = anchor.Position;
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
            // transform still exists, so a quit or a save captures where the player actually stood.
            //
            // On travel this writes the OUTGOING zone's coordinates into the session, and that is
            // harmless rather than a leak: the arriving rig compares the session's zone against its own
            // (see IsContinuingInThisZone) and ignores a pose that belongs to the zone being left.
            FlushPose();
        }
    }
}

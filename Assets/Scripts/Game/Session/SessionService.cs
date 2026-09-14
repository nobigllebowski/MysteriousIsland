using System;
using System.Globalization;
using System.Text;
using ForgottenIsle.Core.Data;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Core.Time;

namespace ForgottenIsle.Game.Session
{
    /// <summary>
    /// Owns the live <see cref="GameState"/> and is the only object in the project permitted to
    /// mutate it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY sole ownership is a rule and not a convention: <see cref="GameState"/> is a mutable POCO
    /// graph with public fields, chosen for allocation-free access on tick paths. Anything holding a
    /// reference to it can change it, and the moment two objects can, "when did this change and who
    /// changed it" stops having an answer — which is the failure mode that makes save bugs
    /// irreproducible. The reference never leaves this class. Readers get scalar properties;
    /// <see cref="Snapshot"/> hands out a clone.
    /// </para>
    /// <para>
    /// WHY the clock lives here too: <c>SessionState.SimHours</c> and <see cref="IslandClock"/> are
    /// the same fact stored twice — one for serialization, one for display arithmetic. Keeping them
    /// behind one owner is what stops them drifting apart across a save/load cycle, which would show
    /// up to the player as the sun being in the wrong place after a reload.
    /// </para>
    /// </remarks>
    public sealed class SessionService : ISaveParticipant
    {
        /// <summary>Act a fresh run begins in. An identifier, never shown to the player.</summary>
        public const string DefaultActId = "act1";

        /// <summary>Slot value meaning "this run is not bound to a save slot".</summary>
        public const int NoSlot = -1;

        private const string KeySlot = "slot";
        private const string KeyActId = "actId";
        private const string KeyZoneId = "zoneId";
        private const string KeySimHours = "simHours";
        private const string KeyPlaytime = "playtimeSeconds";
        private const string KeyRecordedPercent = "recordedPercent";
        private const string KeyRngState = "rngState";
        private const string KeyPositionX = "x";
        private const string KeyPositionY = "y";
        private const string KeyPositionZ = "z";
        private const string KeyYaw = "yaw";
        private const string KeyEquippedTool = "tool";

        private readonly IslandClock _clock;
        private readonly ICoreLog _log;
        private readonly SignalBus _signals;
        private readonly GameState _state;
        private readonly PcgRandom _random;
        private readonly ISaveParticipant _playerParticipant;

        private bool _hasRun;
        private int _boundSlot;

        /// <summary>
        /// Creates a service holding an empty, valid, not-yet-started run.
        /// </summary>
        /// <param name="clock">The run clock this service advances. Required.</param>
        /// <param name="log">Diagnostics sink. Null is tolerated.</param>
        /// <param name="signals">
        /// Bus for <see cref="ZoneChangedSignal"/>. Null is tolerated so the service can be driven by
        /// an EditMode test with no bus.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
        public SessionService(IslandClock clock, ICoreLog log, SignalBus signals)
        {
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _clock = clock;
            _log = log;
            _signals = signals;
            _state = new GameState();
            _random = new PcgRandom(0UL);
            _playerParticipant = new PlayerParticipant(this);
            _boundSlot = NoSlot;
            _hasRun = false;
        }

        /// <summary>True once a run has been started or loaded, and until it is ended.</summary>
        public bool HasRun => _hasRun;

        /// <summary>The save slot this run writes to, or <see cref="NoSlot"/>.</summary>
        public int BoundSlot => _boundSlot;

        /// <summary>Current act identifier. Never null.</summary>
        public string ActId => _state.Session.ActId ?? string.Empty;

        /// <summary>Current zone identifier — a <see cref="Scenes.SceneKeys"/> zone key. Never null.</summary>
        public string ZoneId => _state.Session.ZoneId ?? string.Empty;

        /// <summary>In-fiction hours elapsed since this run began.</summary>
        public double SimHours => _state.Session.SimHours;

        /// <summary>Real seconds the player has spent in this run, across all sessions.</summary>
        public double PlaytimeSeconds => _state.Session.PlaytimeSeconds;

        /// <summary>Completion percentage recorded for the slot list, 0..100.</summary>
        public int RecordedPercent => _state.Session.RecordedPercent;

        /// <summary>Last persisted player position, in engine-free coordinates.</summary>
        public Vec3 PlayerPosition => _state.Player.Position;

        /// <summary>Last persisted player facing, in degrees.</summary>
        public float PlayerYawDegrees => _state.Player.YawDegrees;

        /// <summary>Instance id of the equipped tool, or an empty string. Never null.</summary>
        public string EquippedToolInstanceId => _state.Player.EquippedToolInstanceId ?? string.Empty;

        /// <summary>
        /// Read-only view of the run clock, for display and for systems that need the hour of day.
        /// </summary>
        public IClock Clock => _clock;

        /// <summary>
        /// The run's random source. Its state round-trips through the save, so a loaded game draws
        /// the numbers the original run would have drawn.
        /// </summary>
        public PcgRandom Random => _random;

        /// <summary>
        /// The participant that owns the <c>player</c> save section.
        /// </summary>
        /// <remarks>
        /// WHY player state is a separate participant with the same owner: the save format keys
        /// sections by subsystem so an old build can skip a section it does not recognise, and player
        /// state will grow its own schema (inventory leases, tool condition) on a different cadence
        /// from session state. WHY it is nested here rather than free-standing: it has to mutate the
        /// same <see cref="GameState"/>, and the whole point of this class is that nothing else does.
        /// </remarks>
        public ISaveParticipant PlayerParticipant => _playerParticipant;

        /// <summary>Stable on-disk id of the session section.</summary>
        public string ParticipantId => SaveSections.Session;

        /// <summary>
        /// Returns an independent copy of the whole run.
        /// </summary>
        /// <remarks>
        /// The clone exists so a caller can read a coherent view across several fields without the
        /// simulation changing under it mid-read. Mutating the result changes nothing.
        /// </remarks>
        public GameState Snapshot()
        {
            return _state.Clone();
        }

        /// <summary>
        /// Resets everything and begins a fresh run bound to <paramref name="slot"/>.
        /// </summary>
        /// <param name="slot">Save slot the run will write to.</param>
        /// <param name="actId">Starting act identifier. Empty falls back to <see cref="DefaultActId"/>.</param>
        /// <param name="zoneId">Starting zone key.</param>
        /// <param name="worldSeed">
        /// Seed for the run's random stream. Distinct runs must pass distinct seeds; the same seed
        /// reproduces the same run exactly, which is what the determinism tests rely on.
        /// </param>
        public void BeginNewRun(int slot, string actId, string zoneId, int worldSeed)
        {
            _state.Session.ActId = string.IsNullOrEmpty(actId) ? DefaultActId : actId;
            _state.Session.ZoneId = zoneId ?? string.Empty;
            _state.Session.SimHours = 0d;
            _state.Session.PlaytimeSeconds = 0d;
            _state.Session.RecordedPercent = 0;

            _state.Player.Position = Vec3.Zero;
            _state.Player.YawDegrees = 0f;
            _state.Player.EquippedToolInstanceId = string.Empty;

            _random.SetState(PcgRandom.Derive(worldSeed, 0));
            _state.RngState = _random.State;

            _clock.SetSimHours(0d);
            _boundSlot = slot;
            _hasRun = true;

            PublishZoneChanged();
        }

        /// <summary>
        /// Ends the run and returns the state to its not-started shape.
        /// </summary>
        /// <remarks>
        /// The state object itself is reused rather than replaced, so no caller holding a long-lived
        /// reference obtained from <see cref="Snapshot"/> ends up comparing against a dead instance,
        /// and no allocation happens on the quit path.
        /// </remarks>
        public void EndRun()
        {
            _state.Session.ActId = string.Empty;
            _state.Session.ZoneId = string.Empty;
            _state.Session.SimHours = 0d;
            _state.Session.PlaytimeSeconds = 0d;
            _state.Session.RecordedPercent = 0;
            _state.Player.Position = Vec3.Zero;
            _state.Player.YawDegrees = 0f;
            _state.Player.EquippedToolInstanceId = string.Empty;
            _clock.SetSimHours(0d);
            _boundSlot = NoSlot;
            _hasRun = false;

            PublishZoneChanged();
        }

        /// <summary>
        /// Advances in-fiction time by one tick's worth of hours.
        /// </summary>
        /// <remarks>
        /// Called only by <c>Ticker</c>. The clock and the serialized field are advanced together and
        /// the clock is then treated as authoritative, so the two can never disagree by accumulated
        /// floating-point error.
        /// </remarks>
        /// <param name="simHours">Hours to advance. Non-positive and non-finite values are ignored.</param>
        public void AdvanceTick(double simHours)
        {
            if (!_hasRun || !(simHours > 0d) || double.IsInfinity(simHours))
            {
                return;
            }

            _clock.Advance(simHours);
            _state.Session.SimHours = _clock.SimHours;
            _state.RngState = _random.State;
        }

        /// <summary>
        /// Adds elapsed real time to the run's playtime counter.
        /// </summary>
        /// <param name="realSeconds">Real seconds. Non-positive and non-finite values are ignored.</param>
        public void AddPlaytime(double realSeconds)
        {
            if (!_hasRun || !(realSeconds > 0d) || double.IsInfinity(realSeconds))
            {
                return;
            }

            _state.Session.PlaytimeSeconds += realSeconds;
        }

        /// <summary>
        /// Records that the player is now in <paramref name="zoneId"/> and announces it.
        /// </summary>
        /// <remarks>
        /// Called after a zone load reports success, never before. Announcing on request rather than on
        /// completion would have listeners binding to a zone that then failed to load.
        /// </remarks>
        public void SetZone(string zoneId)
        {
            var next = zoneId ?? string.Empty;
            if (string.Equals(_state.Session.ZoneId, next, StringComparison.Ordinal))
            {
                return;
            }

            _state.Session.ZoneId = next;
            PublishZoneChanged();
        }

        /// <summary>
        /// Records the player's pose.
        /// </summary>
        /// <remarks>
        /// Written on a cadence by <c>PlayerRig</c>, not every frame: the value only has to be correct
        /// when a save is taken, and a per-frame write would put a domain mutation on the hottest path
        /// in the game for no gain. Non-finite components are dropped rather than stored, because a NaN
        /// here would serialize as zero and silently teleport the player to the origin on reload.
        /// </remarks>
        public void SetPlayerPose(in Vec3 position, float yawDegrees)
        {
            if (!_hasRun)
            {
                return;
            }

            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z)
                || float.IsInfinity(position.X) || float.IsInfinity(position.Y) || float.IsInfinity(position.Z))
            {
                return;
            }

            if (float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees))
            {
                return;
            }

            _state.Player.Position = position;
            _state.Player.YawDegrees = yawDegrees;
        }

        /// <summary>Records the equipped tool lease. An empty or null id means "nothing equipped".</summary>
        public void SetEquippedTool(string instanceId)
        {
            if (!_hasRun)
            {
                return;
            }

            _state.Player.EquippedToolInstanceId = instanceId ?? string.Empty;
        }

        /// <summary>
        /// Records completion percentage for the save-slot card. Values outside 0..100 are clamped.
        /// </summary>
        public void SetRecordedPercent(int percent)
        {
            if (!_hasRun)
            {
                return;
            }

            if (percent < 0)
            {
                percent = 0;
            }
            else if (percent > 100)
            {
                percent = 100;
            }

            _state.Session.RecordedPercent = percent;
        }

        /// <summary>Writes the session section into <paramref name="doc"/>.</summary>
        public void Capture(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            _state.RngState = _random.State;

            var builder = new StringBuilder(224);
            builder.Append('{');
            JsonWriter.AppendMemberName(builder, KeySlot);
            JsonWriter.AppendInt(builder, _boundSlot);
            builder.Append(',');
            JsonWriter.AppendMemberName(builder, KeyActId);
            JsonWriter.AppendString(builder, ActId);
            builder.Append(',');
            JsonWriter.AppendMemberName(builder, KeyZoneId);
            JsonWriter.AppendString(builder, ZoneId);
            builder.Append(',');
            JsonWriter.AppendMemberName(builder, KeySimHours);
            JsonWriter.AppendDouble(builder, _state.Session.SimHours);
            builder.Append(',');
            JsonWriter.AppendMemberName(builder, KeyPlaytime);
            JsonWriter.AppendDouble(builder, _state.Session.PlaytimeSeconds);
            builder.Append(',');
            JsonWriter.AppendMemberName(builder, KeyRecordedPercent);
            JsonWriter.AppendInt(builder, _state.Session.RecordedPercent);
            builder.Append(',');

            // The RNG state is written as a STRING, not as a JSON number. JSON numbers parse back through
            // a double, which holds only 53 bits exactly; a 64-bit PCG state above 2^53 would come back
            // rounded and the run would diverge from the one that was saved. A decimal string round-trips
            // every value exactly.
            JsonWriter.AppendMemberName(builder, KeyRngState);
            JsonWriter.AppendString(builder, _state.RngState.ToString(CultureInfo.InvariantCulture));
            builder.Append('}');

            doc.PutSection(ParticipantId, builder.ToString());
        }

        /// <summary>Reads the session section back out of <paramref name="doc"/>.</summary>
        public void Restore(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            string payload;
            if (!doc.TryGetSection(ParticipantId, out payload) || string.IsNullOrEmpty(payload))
            {
                // A save written before this participant existed. Defaults stand; this is not an error.
                return;
            }

            JsonValue root;
            if (!JsonParser.TryParse(payload, out root) || !root.IsObject)
            {
                Warn(LogCode.SaveCorrupt, ParticipantId);
                return;
            }

            _boundSlot = root[KeySlot].AsInt(NoSlot);
            _state.Session.ActId = root[KeyActId].AsString(DefaultActId);
            _state.Session.ZoneId = root[KeyZoneId].AsString(string.Empty);
            _state.Session.SimHours = root[KeySimHours].AsDouble(0d);
            _state.Session.PlaytimeSeconds = root[KeyPlaytime].AsDouble(0d);
            _state.Session.RecordedPercent = root[KeyRecordedPercent].AsInt(0);

            ulong rngState;
            var rngText = root[KeyRngState].AsString(string.Empty);
            if (ulong.TryParse(rngText, NumberStyles.None, CultureInfo.InvariantCulture, out rngState) && rngState != 0UL)
            {
                _random.SetState(rngState);
                _state.RngState = rngState;
            }
            else
            {
                // A zero or unparseable state would leave the generator in a degenerate position. Keep the
                // stream we already have — a run that draws different numbers than the original beats a run
                // that draws the same number forever.
                Warn(LogCode.SaveCorrupt, ParticipantId + "." + KeyRngState);
                _state.RngState = _random.State;
            }

            _clock.SetSimHours(_state.Session.SimHours);
            _hasRun = true;

            PublishZoneChanged();
        }

        private void PublishZoneChanged()
        {
            if (_signals != null)
            {
                _signals.Publish(new ZoneChangedSignal(ZoneId));
            }
        }

        private void Warn(LogCode code, string detail)
        {
            if (_log != null)
            {
                _log.Warn(code, detail);
            }
        }

        /// <summary>
        /// The <c>player</c> save section, owned by the enclosing service so that the single-mutator
        /// rule survives having two sections.
        /// </summary>
        private sealed class PlayerParticipant : ISaveParticipant
        {
            private readonly SessionService _owner;

            internal PlayerParticipant(SessionService owner)
            {
                _owner = owner;
            }

            public string ParticipantId => SaveSections.Player;

            public void Capture(SaveDocument doc)
            {
                if (doc == null)
                {
                    return;
                }

                var player = _owner._state.Player;
                var builder = new StringBuilder(160);
                builder.Append('{');
                JsonWriter.AppendMemberName(builder, KeyPositionX);
                JsonWriter.AppendFloat(builder, player.Position.X);
                builder.Append(',');
                JsonWriter.AppendMemberName(builder, KeyPositionY);
                JsonWriter.AppendFloat(builder, player.Position.Y);
                builder.Append(',');
                JsonWriter.AppendMemberName(builder, KeyPositionZ);
                JsonWriter.AppendFloat(builder, player.Position.Z);
                builder.Append(',');
                JsonWriter.AppendMemberName(builder, KeyYaw);
                JsonWriter.AppendFloat(builder, player.YawDegrees);
                builder.Append(',');
                JsonWriter.AppendMemberName(builder, KeyEquippedTool);
                JsonWriter.AppendString(builder, player.EquippedToolInstanceId ?? string.Empty);
                builder.Append('}');

                doc.PutSection(ParticipantId, builder.ToString());
            }

            public void Restore(SaveDocument doc)
            {
                if (doc == null)
                {
                    return;
                }

                string payload;
                if (!doc.TryGetSection(ParticipantId, out payload) || string.IsNullOrEmpty(payload))
                {
                    return;
                }

                JsonValue root;
                if (!JsonParser.TryParse(payload, out root) || !root.IsObject)
                {
                    _owner.Warn(LogCode.SaveCorrupt, ParticipantId);
                    return;
                }

                var position = new Vec3(
                    root[KeyPositionX].AsFloat(0f),
                    root[KeyPositionY].AsFloat(0f),
                    root[KeyPositionZ].AsFloat(0f));

                _owner._state.Player.Position = position;
                _owner._state.Player.YawDegrees = root[KeyYaw].AsFloat(0f);
                _owner._state.Player.EquippedToolInstanceId = root[KeyEquippedTool].AsString(string.Empty);
            }
        }
    }
}

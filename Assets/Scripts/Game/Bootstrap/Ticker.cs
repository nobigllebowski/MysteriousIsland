// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Time/GameRunner.cs.
// Adapted for Vardholm: the GameContext.Current lookup is replaced by an Initialize() injection (ADR-0012);
// Nation's GameSpeed enum becomes a continuous world-to-real time ratio; the scheduler's whole-tick output is
// applied here rather than inside a session object, and each tick publishes TickCompletedSignal; a real-delta
// clamp and an explicit accumulator reset on resume were added because Nation had no backgrounding story.

using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Session;
using UnityEngine;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// The single per-frame hook in the project. Converts real time into whole simulation ticks and
    /// applies them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY exactly one <c>Update</c>: Unity's per-behaviour message dispatch is a managed-to-native
    /// call per component per frame, and — more importantly — a project with many <c>Update</c>
    /// methods has no defined order between them, so "the simulation advanced" and "something read
    /// the simulation" interleave differently on different devices. One entry point makes the frame
    /// an ordered sequence: advance time, then let everything else observe the result.
    /// </para>
    /// <para>
    /// WHY it is silent outside <see cref="GameStateId.InGame"/>: in the menu there is no run to
    /// advance, during a load the world is half-swapped, and while paused the player has explicitly
    /// asked for time to stop. The scheduler is not merely ignored in those modes — the accumulator is
    /// left untouched, so unpausing resumes mid-tick instead of discarding the fraction of a tick that
    /// had already elapsed.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Ticker : MonoBehaviour
    {
        /// <summary>Simulation ticks per real second at the default time ratio.</summary>
        public const double TargetTicksPerRealSecond = 10.0;

        /// <summary>
        /// In-fiction seconds that pass per real second by default: one real second is one in-fiction
        /// minute, so an in-fiction day takes twenty-four real minutes.
        /// </summary>
        /// <remarks>
        /// ⚠ VERIFY — open item O-6 in the ADR set records that the world clock scale is stated as two
        /// different values across the design documents and must be pinned to one and asserted by a
        /// test. This constant is that one place; changing it changes nothing else.
        /// </remarks>
        public const double DefaultWorldSecondsPerRealSecond = 60.0;

        /// <summary>Real seconds in an hour, for converting the ratio into hours per tick.</summary>
        private const double SecondsPerHour = 3600.0;

        /// <summary>
        /// Upper bound on the real delta fed to the scheduler in one frame.
        /// </summary>
        /// <remarks>
        /// A frame that reports two seconds of delta is not two seconds of gameplay — it is a hitch, an
        /// editor breakpoint, or the first frame after the app was backgrounded. Clamping here, on top
        /// of the scheduler's own tick cap, means the simulation never tries to catch up across a stall
        /// the player already experienced as a stall.
        /// </remarks>
        public const float MaxRealDeltaSeconds = 0.25f;

        private TickScheduler _scheduler;
        private SessionService _session;
        private GameStateMachine _states;
        private SignalBus _signals;
        private double _simHoursPerTick;
        private bool _initialized;

        /// <summary>
        /// In-fiction seconds per real second. Settable so a later phase can offer a time-skip without
        /// rebuilding the scheduler; zero or less freezes the simulation without disturbing the
        /// accumulator.
        /// </summary>
        public double WorldSecondsPerRealSecond { get; set; } = DefaultWorldSecondsPerRealSecond;

        /// <summary>In-fiction hours one tick advances. Fixed at initialization.</summary>
        public double SimHoursPerTick => _simHoursPerTick;

        /// <summary>Total ticks applied since initialization. Read by the development overlay.</summary>
        public long TickCount { get; private set; }

        /// <summary>True once <see cref="Initialize"/> has run. Before that the ticker does nothing.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Injects the graph this ticker drives.
        /// </summary>
        /// <remarks>
        /// A <c>MonoBehaviour</c> cannot take constructor arguments, so this is the constructor in all
        /// but name — called once by <see cref="AppBootstrap"/> immediately after
        /// <c>AddComponent</c>, before the first <c>Update</c> can run. Calling it twice replaces the
        /// graph and resets the accumulator rather than throwing, because the only realistic way it
        /// happens is a domain reload in the editor.
        /// </remarks>
        /// <param name="session">The run to advance. Required.</param>
        /// <param name="states">The mode machine, consulted every frame.</param>
        /// <param name="signals">Bus for <see cref="TickCompletedSignal"/>.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public void Initialize(SessionService session, GameStateMachine states, SignalBus signals, ICoreLog log)
        {
            if (session == null || states == null)
            {
                // Refusing to initialize leaves IsInitialized false and Update inert, which is a visibly
                // dead game rather than a null dereference once a frame forever.
                if (log != null)
                {
                    log.Warn(LogCode.CatalogMissing, nameof(Ticker));
                }

                return;
            }

            _session = session;
            _states = states;
            _signals = signals;

            _simHoursPerTick = DefaultWorldSecondsPerRealSecond / (TargetTicksPerRealSecond * SecondsPerHour);
            _scheduler = new TickScheduler(_simHoursPerTick);
            _scheduler.MaxTicksPerAdvance = TickScheduler.DefaultMaxTicksPerAdvance;
            TickCount = 0;
            _initialized = true;
        }

        /// <summary>
        /// Discards the fractional tick carried over from before a discontinuity.
        /// </summary>
        /// <remarks>
        /// Call after anything that decouples real time from simulation time — a scene load, a resume
        /// from background, a save being loaded — so the first frame afterwards does not immediately
        /// cash in a partial tick that belonged to a different moment.
        /// </remarks>
        public void ResetAccumulator()
        {
            if (_scheduler != null)
            {
                _scheduler.Reset();
            }
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return;
            }

            var delta = UnityEngine.Time.unscaledDeltaTime;
            if (delta > MaxRealDeltaSeconds)
            {
                delta = MaxRealDeltaSeconds;
            }

            if (delta <= 0f)
            {
                return;
            }

            // Playtime tracks real seconds the player spent playing, so it takes the clamped delta too:
            // the seconds lost to a hitch were not seconds of play.
            _session.AddPlaytime(delta);

            var ticks = _scheduler.Advance(delta, WorldSecondsPerRealSecond);
            for (var i = 0; i < ticks; i++)
            {
                _session.AdvanceTick(_simHoursPerTick);
                TickCount++;

                if (_signals != null)
                {
                    _signals.Publish(new TickCompletedSignal(_session.SimHours));
                }
            }
        }

        /// <summary>
        /// Drops the accumulator when the app returns from the background.
        /// </summary>
        /// <remarks>
        /// On resume the first frame's unscaled delta reports the whole time the app spent suspended.
        /// The clamp in <see cref="Update"/> already bounds that, but the accumulator may also hold a
        /// fraction from the frame before suspension, minutes or hours of wall-clock time ago. Dropping
        /// it means the world resumes where it stopped rather than lurching forward on frame one.
        /// </remarks>
        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                ResetAccumulator();
            }
        }

        /// <summary>
        /// Same reasoning as <see cref="OnApplicationPause"/>, for the desktop and editor path where
        /// focus loss rather than suspension is what stops the frame loop.
        /// </summary>
        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                ResetAccumulator();
            }
        }
    }
}

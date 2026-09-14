// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Models/GameDate.cs.
// Adapted for Vardholm: the real-calendar day count backed by DateTime is replaced by a continuous
// sim-hour scalar, because Vardholm's fiction has no months or years — only days, hours and minutes on
// an island with no calendar. The type also becomes a mutable class implementing IClock, since exactly
// one instance is owned by the session and its position is saved.

using System;
using System.Globalization;

namespace ForgottenIsle.Core.Time
{
    /// <summary>
    /// The island's clock: a continuous count of in-fiction hours, presented as day, hour and minute.
    /// </summary>
    /// <remarks>
    /// WHY a single scalar rather than separate day/hour/minute fields: every consumer that schedules
    /// something ("in six hours", "at dawn tomorrow") wants arithmetic, and split fields force carry
    /// handling into every one of those call sites. Day/hour/minute are derived on read instead, which is
    /// a handful of divisions and makes an invalid combination unrepresentable.
    /// <para>
    /// A day is exactly <see cref="HoursPerDay"/> hours. There is no daylight saving, no leap anything,
    /// and no timezone — this is not a real calendar and must never be backed by <see cref="DateTime"/>.
    /// </para>
    /// </remarks>
    public sealed class IslandClock : IClock
    {
        /// <summary>Hours in one island day.</summary>
        public const double HoursPerDay = 24.0;

        /// <summary>Minutes in one island hour.</summary>
        public const double MinutesPerHour = 60.0;

        private double _simHours;

        /// <param name="startSimHours">
        /// Hours already elapsed when the clock starts — 0 for a new game, the saved value on load.
        /// Negative input is clamped to 0: time before the shipwreck does not exist.
        /// </param>
        public IslandClock(double startSimHours = 0)
        {
            _simHours = startSimHours > 0 ? startSimHours : 0;
        }

        /// <inheritdoc />
        public double SimHours => _simHours;

        /// <summary>The current island day, 1-based: the shipwreck happens on Day 1.</summary>
        public int Day => (int)(_simHours / HoursPerDay) + 1;

        /// <summary>Hour of the current day, 0 through 23.</summary>
        public int Hour
        {
            get
            {
                var hourOfDay = (int)(_simHours % HoursPerDay);

                // Guard against the boundary case where floating point rounding lands exactly on 24.
                return hourOfDay >= 24 ? 23 : hourOfDay;
            }
        }

        /// <summary>Minute of the current hour, 0 through 59. Truncated, never rounded up.</summary>
        /// <remarks>
        /// Truncation matters: rounding could show 20:60, and it would let a displayed minute run ahead of
        /// the sim time that gates a timed event.
        /// </remarks>
        public int Minute
        {
            get
            {
                var fractionalHour = _simHours - Math.Floor(_simHours);
                var minute = (int)(fractionalHour * MinutesPerHour);
                return minute >= 60 ? 59 : minute;
            }
        }

        /// <summary>
        /// Moves the clock forward.
        /// </summary>
        /// <param name="simHours">
        /// Hours to add. Zero and negative values are ignored rather than throwing: callers derive this
        /// from a real frame delta, which can legitimately be zero on a paused or stalled frame, and the
        /// clock must never run backwards.
        /// </param>
        public void Advance(double simHours)
        {
            if (simHours <= 0 || double.IsNaN(simHours))
            {
                return;
            }

            _simHours += simHours;
        }

        /// <summary>
        /// Jumps the clock to an absolute position. Used on load and by debug tooling only.
        /// </summary>
        /// <remarks>
        /// This is the one operation that may move time backwards, which is why it is deliberately not on
        /// <see cref="IClock"/>: restoring a save is the only legitimate caller, and it holds the concrete
        /// clock. NaN and negative input clamp to 0 so a corrupt save cannot poison every later division.
        /// </remarks>
        public void SetSimHours(double simHours)
        {
            _simHours = simHours > 0 && !double.IsNaN(simHours) ? simHours : 0;
        }

        /// <summary>
        /// Diagnostic rendering, for example <c>Day 1, 19:21</c>.
        /// </summary>
        /// <remarks>
        /// DEVELOPER-FACING ONLY — logs, the debug overlay, save-file inspection. The literal "Day" here
        /// is not a translation escape hatch: player-visible time must be composed by the UI layer from a
        /// LocKey format string fed <see cref="Day"/>, <see cref="Hour"/> and <see cref="Minute"/>, so it
        /// re-renders correctly when the locale changes. Invariant culture keeps log output stable across
        /// devices.
        /// </remarks>
        public string ToDisplayString()
        {
            return "Day " + Day.ToString(CultureInfo.InvariantCulture)
                   + ", " + Hour.ToString("D2", CultureInfo.InvariantCulture)
                   + ":" + Minute.ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc cref="ToDisplayString" />
        public override string ToString()
        {
            return ToDisplayString();
        }
    }
}

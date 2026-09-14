namespace ForgottenIsle.Core.Time
{
    /// <summary>
    /// Read-only access to in-fiction time.
    /// </summary>
    /// <remarks>
    /// WHY consumers depend on this instead of on <see cref="IslandClock"/>: the interface exposes no way
    /// to advance or set time, which keeps the "only the session mutates state" rule enforceable by the
    /// type system rather than by convention. A test can also hand a subsystem a frozen clock and get a
    /// deterministic result with no scheduler involved.
    /// </remarks>
    public interface IClock
    {
        /// <summary>
        /// In-fiction hours elapsed since the game began. Monotonically non-decreasing during play.
        /// </summary>
        /// <remarks>
        /// A double, not a float: at 24 hours a day over a long playthrough a float's 24-bit mantissa
        /// starts losing sub-minute precision, which would make tick scheduling drift visibly.
        /// </remarks>
        double SimHours { get; }
    }
}

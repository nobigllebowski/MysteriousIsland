namespace ForgottenIsle.Core.Logging
{
    /// <summary>
    /// The only channel through which Core reports a diagnostic to the outside world.
    /// </summary>
    /// <remarks>
    /// WHY this is an interface Core consumes rather than a static logger Core owns: Core is engine-free
    /// and must be exercisable from a plain test runner. An injected sink lets tests assert "this load
    /// logged exactly one SaveMigrationApplied" instead of scraping a console, and lets the shipping build
    /// route codes to telemetry while the editor build routes them to the Unity console.
    /// <para>
    /// Implementations MUST NOT throw and MUST tolerate being called from a tight loop — callers treat
    /// logging as free and will not guard it.
    /// </para>
    /// </remarks>
    public interface ICoreLog
    {
        /// <summary>Reports a condition identified solely by its code.</summary>
        void Warn(LogCode code);

        /// <summary>
        /// Reports a condition with context.
        /// </summary>
        /// <param name="detail">
        /// A short, non-localized identifier that narrows the code down — a loc key, a scene name, a slot
        /// number. Developer-facing only: never build player-visible text here.
        /// </param>
        void Warn(LogCode code, string detail);
    }
}

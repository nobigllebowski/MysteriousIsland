namespace ForgottenIsle.Core.Logging
{
    /// <summary>
    /// Identifies a diagnostic condition Core wants to report without owning a logging backend.
    /// </summary>
    /// <remarks>
    /// WHY codes rather than message strings: Core cannot reference UnityEngine, so it cannot call
    /// <c>Debug.LogWarning</c>, and it must not carry player-facing or even developer-facing prose that
    /// would need translating or would bloat the IL2CPP string table. A code is two bytes, is greppable,
    /// and lets the Game layer decide whether a condition is a console warning, a telemetry event, or
    /// silence in a shipping build.
    /// <para>NUMBERS ARE STABLE — they appear in logs and telemetry. Append, never renumber.</para>
    /// </remarks>
    public enum LogCode : ushort
    {
        /// <summary>Unset. Never passed to <c>ICoreLog</c> deliberately.</summary>
        None = 0,

        /// <summary>A localization key had no entry in the active locale or the fallback locale.</summary>
        MissingLocKey = 1,

        /// <summary>A command was dispatched that has no registered handler.</summary>
        UnknownCommand = 2,

        /// <summary>A state transition was requested that the legal transition table forbids.</summary>
        IllegalTransition = 3,

        /// <summary>A save document failed its checksum or could not be parsed.</summary>
        SaveCorrupt = 4,

        /// <summary>A save was upgraded from an older schema version on load.</summary>
        SaveMigrationApplied = 5,

        /// <summary>A scene load exceeded its soft budget but had not yet hit the watchdog timeout.</summary>
        SceneLoadSlow = 6,

        /// <summary>A data catalog was requested before it had been populated, or is missing entirely.</summary>
        CatalogMissing = 7
    }
}

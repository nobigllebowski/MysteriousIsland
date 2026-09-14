namespace ForgottenIsle.Core.Primitives
{
    /// <summary>
    /// The outcome of an operation that is allowed to fail for an expected, in-game reason.
    /// </summary>
    /// <remarks>
    /// WHY an enum instead of exceptions: every value here describes a situation the player can reach by
    /// playing normally — a save slot is empty, a zone is still loading, a command arrived in the wrong
    /// state. Those are control flow, not faults, and throwing on them on IL2CPP costs far more than a
    /// return value. Exceptions remain reserved for programmer error (null handler, bad argument contract).
    /// <para>
    /// The backing type is <see cref="ushort"/> because codes are written into telemetry and diagnostic
    /// payloads where two bytes per code matters.
    /// </para>
    /// <para>
    /// NUMBERS ARE STABLE. Codes are recorded in logs and crash reports, so never renumber an existing
    /// member; append new ones.
    /// </para>
    /// </remarks>
    public enum ResultCode : ushort
    {
        /// <summary>Success. The only non-failure value; test with <c>== ResultCode.Ok</c>.</summary>
        Ok = 0,

        /// <summary>No command type matching the request is known to the dispatcher.</summary>
        UnknownCommand = 1,

        /// <summary>The requested game state transition is not on the legal transition list.</summary>
        IllegalStateTransition = 2,

        /// <summary>The command type is known but nothing has registered a handler for it.</summary>
        NoHandler = 3,

        /// <summary>A command field failed validation, for example a slot index out of range.</summary>
        InvalidArgument = 4,

        /// <summary>A referenced identifier does not exist in the catalog it was looked up in.</summary>
        NotFound = 5,

        /// <summary>The save file could not be written or flushed to disk.</summary>
        SaveWriteFailed = 6,

        /// <summary>The save file parsed but failed its checksum, or was structurally invalid.</summary>
        SaveCorrupt = 7,

        /// <summary>The save was written by a newer build; migration only ever runs forwards.</summary>
        SaveVersionTooNew = 8,

        /// <summary>The requested scene name is not in <c>SceneKeys.All</c>.</summary>
        SceneNotFound = 9,

        /// <summary>A load was requested while another load was still in flight.</summary>
        AlreadyLoading = 10,

        /// <summary>The scene load watchdog expired before the load completed.</summary>
        LoadTimedOut = 11,

        /// <summary>The save slot exists but holds no game.</summary>
        SlotEmpty = 12,

        /// <summary>The operation is valid in principle but forbidden in the current game state.</summary>
        NotAllowedInState = 13
    }
}

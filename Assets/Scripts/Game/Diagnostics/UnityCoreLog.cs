using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ForgottenIsle.Core.Logging;
using UnityEngine;

namespace ForgottenIsle.Game.Diagnostics
{
    /// <summary>
    /// The engine-side implementation of Core's diagnostics sink, and THE ONLY PLACE in the codebase where
    /// a <see cref="LogCode"/> becomes words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY Core logs codes instead of strings, and why the string is only assembled here: a code is an
    /// enum member that a test can assert on, a telemetry pipeline can count, and a localizer never
    /// has to see. The moment Core formatted a message it would be building player-adjacent text in
    /// the layer that is forbidden to, and every log site would allocate a string on paths that run
    /// per frame. Assembly happens at the boundary, once, and only when something actually went
    /// wrong.
    /// </para>
    /// <para>
    /// WHY THE MESSAGE TABLE LIVES HERE AND NOWHERE ELSE. A code means nothing on its own, so exactly one
    /// adapter is allowed to give it words. One adapter means one table to keep in step with the enum, and
    /// it means a grep for a message string lands on the code that produces it. A second translation of
    /// these codes — a telemetry uploader, say — belongs beside this class as another
    /// <see cref="ICoreLog"/> implementation sharing <see cref="Describe"/>, never as a second copy of the
    /// table.
    /// </para>
    /// <para>
    /// These strings are DEVELOPER-FACING and are deliberately not <c>LocKey</c>s. The no-user-facing-
    /// literals rule protects text a player can read; console diagnostics are read by whoever is holding
    /// the device logs, and routing them through the localization system would create the delightful
    /// failure mode of a missing-key warning that cannot report itself because the key is missing.
    /// </para>
    /// <para>
    /// WHY everything is a warning rather than an error: every condition Core reports is one it
    /// already recovered from — a missing key rendered as a marker, a rejected transition that changed
    /// nothing. Logging those as errors would train the team to ignore errors, which is how a real one
    /// gets missed.
    /// </para>
    /// <para>
    /// WHY REPEATS ARE THROTTLED: <see cref="ICoreLog"/> promises callers that logging is free and may be
    /// called from a tight loop, and callers take that promise literally. A condition that recurs every
    /// frame — a catalog that never arrives, a scene that keeps missing — would otherwise fill the log with
    /// thousands of identical lines a second, burying the one line that matters and costing real frame time
    /// with a profiler or telemetry attached. Each distinct message gets a small burst, then one notice
    /// that further repeats are suppressed.
    /// </para>
    /// </remarks>
    public sealed class UnityCoreLog : ICoreLog
    {
        /// <summary>Prefix that makes these lines filterable in the console search box.</summary>
        public const string Prefix = "[Isle] ";

        /// <summary>
        /// Identical messages logged before suppression kicks in. Small: the first occurrence carries the
        /// information, and a handful more establish that it is recurring rather than a one-off.
        /// </summary>
        public const int RepeatThreshold = 8;

        /// <summary>
        /// Cap on how many distinct messages are tracked for throttling.
        /// </summary>
        /// <remarks>
        /// A bound is necessary because the detail string carries unbounded variety — one per loc key, one
        /// per scene name — and an unbounded dictionary inside a logger is a slow leak that only shows up
        /// in a long play session. Past the cap, throttling stops and everything is logged: degrading to
        /// noise is acceptable, degrading to memory growth is not.
        /// </remarks>
        public const int MaxTrackedMessages = 512;

        private readonly StringBuilder _builder = new StringBuilder(96);
        private readonly Dictionary<string, int> _repeats = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Number of warnings reported since this log was created. Shown by the overlay.</summary>
        public int WarningCount { get; private set; }

        /// <summary>The most recent warning line, or an empty string. Shown by the overlay.</summary>
        public string LastWarning { get; private set; } = string.Empty;

        /// <summary>
        /// When true, nothing reaches the console. Intended for tests and for a shipping build that routes
        /// diagnostics elsewhere; <see cref="WarningCount"/> and <see cref="LastWarning"/> keep updating so
        /// the overlay stays truthful and switching it back on is not misleading.
        /// </summary>
        public bool Muted { get; set; }

        /// <inheritdoc />
        public void Warn(LogCode code)
        {
            Emit(code, null);
        }

        /// <inheritdoc />
        public void Warn(LogCode code, string detail)
        {
            Emit(code, detail);
        }

        /// <summary>
        /// Forgets every repeat count, so a condition that was fixed and recurs reports afresh. Called on
        /// scene transitions, where the previous scene's noise is no longer relevant.
        /// </summary>
        public void ResetThrottle()
        {
            _repeats.Clear();
        }

        /// <summary>
        /// Turns a code and its detail into the line that reaches the console.
        /// </summary>
        /// <remarks>
        /// Swallows everything. An <see cref="ICoreLog"/> that can throw turns a diagnostic into the
        /// failure it was reporting, and the call sites in Core do not guard it.
        /// </remarks>
        private void Emit(LogCode code, string detail)
        {
            try
            {
                WarningCount++;

                _builder.Length = 0;
                _builder.Append(Prefix).Append(Describe(code));
                if (!string.IsNullOrEmpty(detail))
                {
                    _builder.Append(" — ").Append(detail);
                }

                LastWarning = _builder.ToString();

                if (!ShouldEmit(LastWarning))
                {
                    return;
                }

                if (!Muted)
                {
                    Debug.LogWarning(LastWarning);
                }
            }
            catch (Exception)
            {
                // Deliberately empty. There is nowhere left to report to.
            }
        }

        /// <summary>
        /// THE TABLE. One arm per <see cref="LogCode"/> member.
        /// </summary>
        /// <remarks>
        /// A switch rather than a dictionary: a code added to the enum and forgotten here still produces a
        /// traceable line carrying its raw number, and a switch over an enum is the closest thing available
        /// to a compiler reminder. Each message names the condition AND what it means for the player,
        /// because the person reading it is usually triaging a bug report rather than working on that
        /// system.
        /// </remarks>
        private static string Describe(LogCode code)
        {
            switch (code)
            {
                case LogCode.None:
                    return "Unspecified condition reported with no code.";
                case LogCode.MissingLocKey:
                    return "Localization key has no entry in the active or fallback locale; the UI is showing a #key# placeholder.";
                case LogCode.UnknownCommand:
                    return "A command was dispatched with no registered handler; the action silently did nothing.";
                case LogCode.IllegalTransition:
                    return "A game state transition was refused by the transition table; the game stayed in its current state.";
                case LogCode.SaveCorrupt:
                    return "A save could not be read, verified or written; the slot is unusable and may fall back to its backup.";
                case LogCode.SaveMigrationApplied:
                    return "A save written by an older build was upgraded on load.";
                case LogCode.SceneLoadSlow:
                    return "A scene load exceeded its soft budget; the loading screen is visible longer than intended.";
                case LogCode.CatalogMissing:
                    return "A data catalog was requested before it was populated; dependent content will be missing.";
                default:
                    return "Unrecognised log code " + ((ushort)code).ToString(CultureInfo.InvariantCulture) + ".";
            }
        }

        /// <summary>
        /// Counts this message and decides whether it still deserves the console.
        /// </summary>
        /// <returns>
        /// True for the first <see cref="RepeatThreshold"/> occurrences; false thereafter, with one
        /// suppression notice emitted in place of the first silenced repeat.
        /// </returns>
        private bool ShouldEmit(string message)
        {
            if (_repeats.Count >= MaxTrackedMessages && !_repeats.ContainsKey(message))
            {
                return true;
            }

            int seen;
            _repeats.TryGetValue(message, out seen);
            seen++;
            _repeats[message] = seen;

            if (seen <= RepeatThreshold)
            {
                return true;
            }

            if (seen == RepeatThreshold + 1 && !Muted)
            {
                Debug.LogWarning(Prefix + "Further repeats of the previous warning are suppressed.");
            }

            return false;
        }
    }
}

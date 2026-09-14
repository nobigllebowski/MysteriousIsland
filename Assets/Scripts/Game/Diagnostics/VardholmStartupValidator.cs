using System.Collections.Generic;
using System.Text;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Game.Diagnostics
{
    /// <summary>
    /// Development-only startup self-check. Confirms that the things the game silently depends on
    /// actually exist, and says so loudly when they do not.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS: the project is written in an environment with no Unity, so nothing here has
    /// been compiled or run. The first person to press Play is the first real test, and the failure
    /// modes that matter — a scene missing from Build Settings, the localization TextAsset not in a
    /// Resources folder, a service that came back null — all present as *quiet* wrongness: a blank
    /// menu, a load that never completes, labels reading <c>#ui.menu.continue#</c>. Each of those
    /// costs an hour to trace back to its cause. This turns them into one report in the first frame.
    /// <para>
    /// It never throws and never blocks boot. A validator that stops the game cannot be used to
    /// diagnose the game. It reports, and lets the developer decide.
    /// </para>
    /// </remarks>
    public static class VardholmStartupValidator
    {
        /// <summary>Outcome of a single check.</summary>
        public enum Severity
        {
            /// <summary>Working as intended.</summary>
            Pass = 0,

            /// <summary>Degraded but playable. Worth knowing.</summary>
            Warn = 1,

            /// <summary>The game will not work correctly. Fix before continuing.</summary>
            Fail = 2
        }

        /// <summary>One line of the report.</summary>
        public readonly struct Line
        {
            /// <summary>How bad it is.</summary>
            public readonly Severity Severity;

            /// <summary>What was checked, and what to do about it when it failed.</summary>
            public readonly string Message;

            /// <param name="severity">How bad it is.</param>
            /// <param name="message">What was checked.</param>
            public Line(Severity severity, string message)
            {
                Severity = severity;
                Message = message;
            }
        }

        /// <summary>
        /// Runs every check against a built context and returns the report.
        /// </summary>
        /// <remarks>
        /// Separated from <see cref="RunAndLog"/> so tests can assert on the lines without capturing
        /// the Unity console.
        /// </remarks>
        /// <param name="context">The composed graph. Null is itself a failure, not an exception.</param>
        /// <returns>Every check performed, in report order.</returns>
        public static List<Line> Run(GameContext context)
        {
            var lines = new List<Line>(24);

            if (context == null)
            {
                lines.Add(new Line(Severity.Fail,
                    "composition root produced no GameContext — nothing else could be checked"));
                return lines;
            }

            CheckServices(context, lines);
            CheckScenes(lines);
            CheckLocalization(context, lines);
            CheckSingletons(lines);

            return lines;
        }

        /// <summary>Runs the checks and writes one block to the console.</summary>
        /// <param name="context">The composed graph.</param>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void RunAndLog(GameContext context)
        {
            var lines = Run(context);
            var worst = Severity.Pass;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Severity > worst)
                {
                    worst = lines[i].Severity;
                }
            }

            var text = Format(lines);
            if (worst == Severity.Fail)
            {
                Debug.LogError(text);
            }
            else if (worst == Severity.Warn)
            {
                Debug.LogWarning(text);
            }
            else
            {
                Debug.Log(text);
            }
        }

        /// <summary>Renders the report as the PASS / WARN / FAIL block.</summary>
        /// <param name="lines">Checks to render.</param>
        /// <returns>A printable report.</returns>
        public static string Format(List<Line> lines)
        {
            var builder = new StringBuilder(512);
            builder.AppendLine("VARDHOLM STARTUP CHECK");
            AppendSection(builder, lines, Severity.Pass, "PASS");
            AppendSection(builder, lines, Severity.Warn, "WARN");
            AppendSection(builder, lines, Severity.Fail, "FAIL");
            return builder.ToString();
        }

        private static void AppendSection(StringBuilder builder, List<Line> lines, Severity severity, string label)
        {
            var any = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Severity != severity)
                {
                    continue;
                }

                if (!any)
                {
                    builder.AppendLine();
                    builder.AppendLine(label + ":");
                    any = true;
                }

                builder.AppendLine("  " + lines[i].Message);
            }
        }

        private static void CheckServices(GameContext context, List<Line> lines)
        {
            Require(lines, context.Log != null, "log sink constructed");
            Require(lines, context.Signals != null, "signal bus constructed");
            Require(lines, context.Clock != null, "island clock constructed");
            Require(lines, context.States != null, "state machine constructed");
            Require(lines, context.Commands != null, "command dispatcher constructed");
            Require(lines, context.Session != null, "session service constructed");
            Require(lines, context.SceneLoader != null, "scene loader constructed");
            Require(lines, context.Zones != null, "zone registry constructed");
            Require(lines, context.Slots != null, "save slot service constructed");
            Require(lines, context.Input != null, "input router constructed");
            Require(lines, context.Localization != null, "localization service constructed");

            if (context.SaveParticipants == null || context.SaveParticipants.Count == 0)
            {
                lines.Add(new Line(Severity.Fail,
                    "no save participants registered — saving would write an empty document"));
            }
            else
            {
                lines.Add(new Line(Severity.Pass,
                    "save participants registered: " + context.SaveParticipants.Count));
            }

            if (context.Zones != null && context.Zones.ResidentCount > SceneKeys.Zones.Count)
            {
                lines.Add(new Line(Severity.Fail,
                    "zone registry reports more resident zones than exist"));
            }
        }

        private static void CheckScenes(List<Line> lines)
        {
            // sceneCountInBuildSettings is the runtime view of the build list, so this catches the
            // "scene exists on disk but was never added to Build Settings" case, which otherwise
            // surfaces only as a load that silently never completes.
            var inBuild = SceneManager.sceneCountInBuildSettings;
            if (inBuild == 0)
            {
                lines.Add(new Line(Severity.Fail,
                    "Build Settings has no scenes — run Vardholm > Setup Project"));
                return;
            }

            var found = new HashSet<string>();
            for (var i = 0; i < inBuild; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                found.Add(NameFromPath(path));
            }

            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var key = SceneKeys.All[i];
                if (found.Contains(key))
                {
                    lines.Add(new Line(Severity.Pass, "scene in build: " + key));
                }
                else
                {
                    lines.Add(new Line(Severity.Fail,
                        "scene NOT in Build Settings: " + key + " — run Vardholm > Setup Project"));
                }
            }

            var firstScene = NameFromPath(SceneUtility.GetScenePathByBuildIndex(0));
            if (firstScene == SceneKeys.Bootstrap)
            {
                lines.Add(new Line(Severity.Pass, "build index 0 is " + SceneKeys.Bootstrap));
            }
            else
            {
                lines.Add(new Line(Severity.Warn,
                    "build index 0 is '" + firstScene + "', expected " + SceneKeys.Bootstrap));
            }
        }

        private static void CheckLocalization(GameContext context, List<Line> lines)
        {
            if (context.Localization == null)
            {
                return;
            }

            // A probe key rather than a table count: what matters is not "did a file load" but "does
            // a lookup a screen actually performs return real text". A missing table and a present
            // but empty table both fail here, and both would ship visible #key# markers.
            var probe = new LocKey("ui.menu.continue");
            var value = context.Localization.Get(probe);

            if (string.IsNullOrEmpty(value))
            {
                lines.Add(new Line(Severity.Fail, "localization returned empty for " + probe.Value));
            }
            else if (value.StartsWith("#") && value.EndsWith("#"))
            {
                lines.Add(new Line(Severity.Fail,
                    "localization table did not load — every label will render as " + value +
                    ". Check Assets/Resources/Localization/en.csv exists."));
            }
            else
            {
                lines.Add(new Line(Severity.Pass, "localization loaded (" + probe.Value + " resolves)"));
            }
        }

        private static void CheckSingletons(List<Line> lines)
        {
            var bootstraps = Object.FindObjectsByType<AppBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (bootstraps.Length == 1)
            {
                lines.Add(new Line(Severity.Pass, "exactly one AppBootstrap host"));
            }
            else
            {
                lines.Add(new Line(Severity.Fail,
                    bootstraps.Length + " AppBootstrap hosts found — expected exactly 1. " +
                    "Two hosts means two service graphs and two tickers."));
            }

            var tickers = Object.FindObjectsByType<Ticker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (tickers.Length == 1)
            {
                lines.Add(new Line(Severity.Pass, "exactly one Ticker"));
            }
            else
            {
                lines.Add(new Line(Severity.Fail,
                    tickers.Length + " Tickers found — expected exactly 1."));
            }

            var listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (listeners.Length > 1)
            {
                lines.Add(new Line(Severity.Warn,
                    listeners.Length + " active AudioListeners — Unity warns and uses an arbitrary one."));
            }
        }

        private static void Require(List<Line> lines, bool condition, string what)
        {
            lines.Add(condition
                ? new Line(Severity.Pass, what)
                : new Line(Severity.Fail, "MISSING: " + what));
        }

        private static string NameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var slash = path.LastIndexOf('/');
            var dot = path.LastIndexOf('.');
            var start = slash + 1;
            var length = dot > start ? dot - start : path.Length - start;
            return path.Substring(start, length);
        }
    }
}

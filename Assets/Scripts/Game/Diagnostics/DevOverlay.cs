// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Performance/PerformanceMonitor.cs.
// Adapted for Vardholm: the GameContext.Current lookup and the country/map/session-engine readouts are gone; the
// panel now reports the GameStateId, the active zone and the resident scene count, which are the three facts
// Phase 1's acceptance criteria are written against; sampling moved off Update (Ticker owns the only one) and
// drawing moved from the UI document to IMGUI because ForgottenIsle.Game does not reference ForgottenIsle.UI;
// the whole file is compiled out of release builds.

#if DEBUG || UNITY_EDITOR

using System.Text;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ForgottenIsle.Game.Diagnostics
{
    /// <summary>
    /// Development-only heads-up panel: frame timing plus the three pieces of state Phase 1 is judged
    /// on — the current mode, the current zone, and how many scenes are resident.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY those three and not more: ADR-0004's two-zone cap and the state machine's legal transitions
    /// are both invariants that hold silently when they work and fail invisibly when they do not. A
    /// number on the screen turns "the cap is enforced" from something a test asserts in CI into
    /// something anyone holding the device can see is still true. The acceptance criteria and the
    /// demo script both read off this panel.
    /// </para>
    /// <para>
    /// WHY the entire file is inside <c>#if</c>: the alternative — shipping the class and guarding its
    /// body — still costs a component, its message dispatch and its string building in every release
    /// frame. Compiling it out means the release build cannot pay for a debug tool even by accident.
    /// <see cref="AppBootstrap"/> guards its <c>AddComponent</c> with the same condition.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DevOverlay : MonoBehaviour
    {
        /// <summary>Seconds of frames folded into each timing sample.</summary>
        public const float SampleIntervalSeconds = 0.5f;

        /// <summary>Margin inside the safe area, in points.</summary>
        private const float Margin = 8f;

        /// <summary>Panel width, in points. Wide enough for the longest line at font size 12.</summary>
        private const float PanelWidth = 340f;

        /// <summary>Panel height, in points. Six lines at font size 12 plus the padding.</summary>
        private const float PanelHeight = 108f;

        private readonly StringBuilder _builder = new StringBuilder(256);

        private GameContext _context;
        private Ticker _ticker;
        private UnityCoreLog _log;

        private int _frames;
        private float _elapsed;
        private float _worstFrameSeconds;
        private string _panelText = string.Empty;
        private GUIStyle _style;
        private bool _initialized;

        /// <summary>Frames per second over the last sample window.</summary>
        public float Fps { get; private set; }

        /// <summary>Mean frame time over the last sample window, in milliseconds.</summary>
        public float AverageFrameMs { get; private set; }

        /// <summary>Worst single frame in the last sample window, in milliseconds.</summary>
        public float WorstFrameMs { get; private set; }

        /// <summary>Whether the panel is currently drawn. Toggled with F3.</summary>
        public bool OverlayVisible { get; private set; } = true;

        /// <summary>
        /// Binds the overlay to the graph it reports on.
        /// </summary>
        /// <param name="context">The composed graph. Required.</param>
        /// <param name="ticker">The ticker, for the simulation tick count. Optional.</param>
        public void Initialize(GameContext context, Ticker ticker)
        {
            _context = context;
            _ticker = ticker;
            _log = context != null ? context.Log as UnityCoreLog : null;
            _initialized = context != null;
        }

        /// <summary>Shows or hides the panel.</summary>
        public void Toggle()
        {
            OverlayVisible = !OverlayVisible;
        }

        /// <summary>
        /// Samples frame timing and rebuilds the panel text.
        /// </summary>
        /// <remarks>
        /// In <c>LateUpdate</c> rather than <c>Update</c> for the same reason as <c>PlayerRig</c>:
        /// <see cref="Ticker"/> owns the frame's single simulation entry point, and a diagnostic must
        /// read the frame's final state rather than race it.
        /// </remarks>
        private void LateUpdate()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    Toggle();
                }

                ReadDevelopmentKeys(keyboard);
            }

            var dt = UnityEngine.Time.unscaledDeltaTime;
            _frames++;
            _elapsed += dt;
            if (dt > _worstFrameSeconds)
            {
                _worstFrameSeconds = dt;
            }

            if (_elapsed < SampleIntervalSeconds)
            {
                return;
            }

            Fps = _frames / _elapsed;
            AverageFrameMs = _elapsed / _frames * 1000f;
            WorstFrameMs = _worstFrameSeconds * 1000f;
            _frames = 0;
            _elapsed = 0f;
            _worstFrameSeconds = 0f;

            // Rebuilt on the sample boundary, not in OnGUI: IMGUI runs its callback several times per
            // frame, and building this string in there would allocate a few kilobytes a second while
            // measuring how much the game allocates.
            RebuildPanelText();
        }

        /// <summary>
        /// Development shortcuts: jump between zones, open everything, wipe progress.
        /// </summary>
        /// <remarks>
        /// WHY these three: the Phase 2 slice is a chain — read a marker, take a tag, cross into the
        /// second zone — and testing the far end of it by playing the near end every time is how a
        /// bug at the far end stops getting looked at. Each shortcut goes through the same command or
        /// service the game itself uses, so a key that works here is evidence the real path works,
        /// not a back door around it. They live inside the same <c>#if</c> as the rest of the file and
        /// exist in no release build.
        /// <para>
        /// F5 travels to the other zone · F6 takes the brass tag (opening Fernmaw) · F7 resets progression in place.
        /// </para>
        /// </remarks>
        private void ReadDevelopmentKeys(Keyboard keyboard)
        {
            if (!_initialized || _context.States.Current != GameStateId.InGame)
            {
                return;
            }

            if (keyboard.f5Key.wasPressedThisFrame)
            {
                var here = _context.Session.ZoneId;
                var there = here == ContentIds.ZoneFernmaw
                    ? ContentIds.ZoneRibcage
                    : ContentIds.ZoneFernmaw;

                var result = _context.Commands.Dispatch(new TravelToZoneCommand(there));
                if (!result.Success)
                {
                    // Most often the zone is still locked. Reported rather than silently ignored, so
                    // the key never looks broken when it is in fact enforcing the rule.
                    Debug.LogWarning("[dev] travel to " + there + " refused: " + result.Code);
                }
            }

            if (keyboard.f6Key.wasPressedThisFrame)
            {
                // Through the same command the pickup issues, not by poking WorldProgress directly:
                // a direct unlock would skip the signals, leaving the HUD showing an objective the
                // state no longer implies — a dev shortcut that manufactures a bug is worse than none.
                var result = _context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));
                Debug.Log("[dev] brass tag: " + (result.Success ? "taken, Fernmaw open" : result.Code.ToString()));
            }

            if (keyboard.f7Key.wasPressedThisFrame)
            {
                _context.Progress.ResetForNewRun();
                Debug.Log("[dev] progression reset (re-enter the zone to rebuild its content)");
            }

            RebuildPanelText();
        }

        private void RebuildPanelText()
        {
            if (!_initialized)
            {
                return;
            }

            var session = _context.Session;
            var zone = string.IsNullOrEmpty(session.ZoneId) ? "-" : session.ZoneId;

            _builder.Length = 0;
            _builder.Append(_context.States.Current.ToString())
                    .Append("  ·  zone ").Append(zone)
                    .Append("  ·  scenes ").Append(_context.Zones.ResidentCount)
                    .Append('/').Append(_context.Zones.MaxResident)
                    .Append('\n');

            _builder.Append(Fps.ToString("F1")).Append(" fps  ·  ")
                    .Append(AverageFrameMs.ToString("F1")).Append(" ms avg  ·  ")
                    .Append(WorstFrameMs.ToString("F1")).Append(" ms worst")
                    .Append('\n');

            // Phase 2 line: where the player is standing, what they are being asked to do, and how
            // much they have found. These are the three questions a gameplay bug starts with.
            var pos = session.PlayerPosition;
            _builder.Append("xyz ")
                    .Append(pos.X.ToString("F1")).Append(' ')
                    .Append(pos.Y.ToString("F1")).Append(' ')
                    .Append(pos.Z.ToString("F1"))
                    .Append("  ·  found ").Append(_context.Progress.Progress.CollectedCount)
                    .Append("  ·  near ").Append(_context.Interactions.RegisteredCount)
                    .AppendLine();

            var objective = _context.Progress.ObjectiveKey;
            _builder.Append("obj ")
                    .Append(string.IsNullOrEmpty(objective) ? "-" : objective)
                    .AppendLine();

            // The puzzles' state in one line: the set, the fire, and how many hint rungs have
            // been said. A "why did she say that" bug starts here.
            _builder.Append("radio ")
                    .Append(_context.Radio.IsWorking ? (_context.Radio.TransmissionReceived ? "heard" : "working") : "dead")
                    .Append("  ·  fire ")
                    .Append(_context.Fire.IsLit ? "lit" : (_context.Fire.Sparked ? "sparked" : "cold"))
                    .Append("  ·  hints ").Append(_context.Hints != null ? _context.Hints.Said : 0)
                    .AppendLine();

            _builder.Append(_context.Clock.ToDisplayString())
                    .Append("  ·  ticks ").Append(_ticker != null ? _ticker.TickCount : 0L)
                    .Append("  ·  slot ").Append(session.BoundSlot)
                    .Append('\n');

            if (_context.SceneLoader.IsLoading)
            {
                _builder.Append("loading ").Append(_context.SceneLoader.PendingKey)
                        .Append(' ').Append((_context.SceneLoader.Progress * 100f).ToString("F0")).Append('%');
            }
            else if (_log != null && _log.WarningCount > 0)
            {
                _builder.Append("warn ").Append(_log.WarningCount).Append("  ·  ").Append(_log.LastWarning);
            }
            else
            {
                _builder.Append("no warnings");
            }

            _panelText = _builder.ToString();
        }

        private void OnGUI()
        {
            if (!_initialized || !OverlayVisible || string.IsNullOrEmpty(_panelText))
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = 12;
                _style.alignment = TextAnchor.UpperLeft;
                _style.wordWrap = false;
                _style.normal.textColor = Color.white;
                _style.padding = new RectOffset(6, 6, 4, 4);
            }

            // Anchored inside the safe area so the panel is not under a notch or a home indicator on the
            // devices the performance numbers actually matter on.
            var safe = Screen.safeArea;
            var x = safe.x + Margin;
            var y = Screen.height - safe.yMax + Margin;
            var rect = new Rect(x, y, PanelWidth, PanelHeight);

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(rect, _panelText, _style);
        }
    }
}

#endif

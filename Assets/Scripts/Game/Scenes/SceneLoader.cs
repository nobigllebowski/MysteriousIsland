// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Scenes/SceneNavigator.cs.
// Adapted for Vardholm: rewritten from single-scene LoadSceneMode.Single navigation to additive load/unload with
// a normalised progress readout, an explicit activation gate and a 20-second watchdog that routes to
// ResultCode.LoadTimedOut instead of hanging. Nation's Debug.Log diagnostics become ICoreLog codes.

using System;
using System.Collections;
using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Game.Scenes
{
    /// <summary>
    /// Loads and unloads scenes additively, one operation at a time, and refuses to hang.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY one operation at a time: overlapping additive loads make <see cref="Progress"/> meaningless
    /// (there is no single number to show behind the curtain) and make the watchdog ambiguous about
    /// which load it is timing. Zone travel is serial by design under ADR-0004, so the restriction
    /// costs nothing and a second request is rejected with <see cref="ResultCode.AlreadyLoading"/>
    /// rather than queued behind a load the player may already have cancelled.
    /// </para>
    /// <para>
    /// WHY the watchdog is not optional: a stuck loading screen is the worst bug Phase 1 can ship,
    /// because the player has no way to recover from it and no way to report what they saw. After
    /// <see cref="WatchdogSeconds"/> the load is abandoned, the caller is told
    /// <see cref="ResultCode.LoadTimedOut"/>, and the state machine bounces to
    /// <c>GameStateId.LoadFailed</c> and from there back to the menu. A recoverable bounce beats an
    /// unrecoverable wait every time.
    /// </para>
    /// </remarks>
    public sealed class SceneLoader
    {
        /// <summary>
        /// The value <c>AsyncOperation.progress</c> stops at while activation is withheld.
        /// </summary>
        /// <remarks>
        /// ⚠ VERIFY (Phase 1 risk R2, editor spike, day 1 of task 10). With
        /// <c>allowSceneActivation = false</c> Unity is documented to hold <c>progress</c> at 0.9 and to
        /// leave <c>isDone</c> false until activation is permitted. Both halves matter here and both are
        /// version-sensitive: if <c>progress</c> actually caps at some other value the wait below never
        /// clears and the watchdog fires on every load; if <c>isDone</c> turned true without activation
        /// the second wait would exit before the scene existed. This constant and
        /// <see cref="WaitForOperation"/> are the only two places the assumption lives, so confirming it
        /// on the pinned 6000.6.0f1 build is a one-line change if it is wrong.
        /// </remarks>
        public const float ActivationThreshold = 0.9f;

        /// <summary>Real seconds after which a load or unload is abandoned as failed.</summary>
        public const float WatchdogSeconds = 20f;

        /// <summary>
        /// Real seconds after which a still-running operation is reported as slow.
        /// </summary>
        /// <remarks>
        /// Warning well before the watchdog is what turns "it timed out once on a tester's device" into
        /// a number we saw trending upward for a week beforehand.
        /// </remarks>
        public const float SlowLoadWarningSeconds = 6f;

        /// <summary>
        /// Progress is pinned just below completion until the scene is genuinely activated, so a curtain
        /// bound to this value never reads 100% over a frame in which the scene does not yet exist.
        /// </summary>
        private const float NearlyComplete = 0.99f;

        private readonly MonoBehaviour _host;
        private readonly ICoreLog _log;
        private readonly List<string> _resident;

        /// <summary>
        /// True from the instant an operation is committed to until <see cref="Finish"/> clears it.
        /// </summary>
        /// <remarks>
        /// ⚠ ORDERING RULE, AND WHY IT IS A FLAG RATHER THAN THE <c>Coroutine</c> HANDLE. This used to
        /// hold the value <c>StartCoroutine</c> returned, which meant the caller assigned the busy state
        /// AFTER the routine had already run. A routine that settles before its first <c>yield</c> — the
        /// synchronous <c>LoadSceneAsync</c>-returned-null path — reaches <see cref="Finish"/> inside
        /// <c>StartCoroutine</c>, clears the field, and then the caller's assignment writes a handle for
        /// an already-dead coroutine back into it: <see cref="IsLoading"/> latches true forever, every
        /// later request is refused with <see cref="ResultCode.AlreadyLoading"/>, and the curtain never
        /// lifts. The same shape bites when a completion callback starts the next operation (the registry
        /// unloads then loads) — the outer frame's assignment would clobber the inner operation's
        /// bookkeeping.
        /// <para>
        /// So the rule is: this field is set to true BEFORE <c>StartCoroutine</c>, and NOTHING touches
        /// loader state after that call returns. The routine owns its own bookkeeping from that point on,
        /// and there is no assignment left for it to race.
        /// </para>
        /// </remarks>
        private bool _busy;

        /// <summary>
        /// Counts committed operations, so a rollback can tell whether it is still the newest one.
        /// </summary>
        /// <remarks>
        /// Only <see cref="AbortStart"/> reads it, and only to refuse to clear a busy state that some
        /// nested operation may have established in the meantime.
        /// </remarks>
        private int _generation;

        private float _progress;
        private string _pendingKey;

        /// <summary>
        /// Creates a loader that runs its coroutines on <paramref name="host"/>.
        /// </summary>
        /// <param name="host">
        /// The persistent bootstrap behaviour. It must outlive every scene the loader touches — a host
        /// living in a scene being unloaded would stop mid-unload and strand <see cref="IsLoading"/>.
        /// </param>
        /// <param name="log">Diagnostics sink. Null is tolerated.</param>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
        public SceneLoader(MonoBehaviour host, ICoreLog log)
        {
            if (host == null)
            {
                // The one place a throw is right: without a host there is no coroutine driver, so every
                // later call would fail identically and silently. Fail at composition, not at travel time.
                throw new ArgumentNullException(nameof(host));
            }

            _host = host;
            _log = log;
            _resident = new List<string>(4);
        }

        /// <summary>True while a load or unload is in flight.</summary>
        public bool IsLoading => _busy;

        /// <summary>
        /// Normalised progress of the operation in flight, 0..1. Reads 0 when nothing is loading and 1
        /// for the single frame on which an operation completes.
        /// </summary>
        public float Progress => _progress;

        /// <summary>The scene key being loaded or unloaded, or an empty string when idle.</summary>
        public string PendingKey => _pendingKey ?? string.Empty;

        /// <summary>Number of scenes this loader currently believes are resident.</summary>
        public int ResidentCount => _resident.Count;

        /// <summary>The resident scene keys, oldest first.</summary>
        public IReadOnlyList<string> Resident => _resident;

        /// <summary>Raised when an operation fails, with the scene key and the reason.</summary>
        public event Action<string, ResultCode> LoadFailed;

        /// <summary>Raised after a scene has finished loading and activating.</summary>
        public event Action<string> Loaded;

        /// <summary>Raised after a scene has finished unloading.</summary>
        public event Action<string> Unloaded;

        /// <summary>True when <paramref name="sceneKey"/> is recorded as resident.</summary>
        public bool IsResident(string sceneKey)
        {
            return IndexOfResident(sceneKey) >= 0;
        }

        /// <summary>
        /// Records a scene that is already open as resident without loading it.
        /// </summary>
        /// <remarks>
        /// WHY this exists: pressing Play directly on a zone scene in the editor leaves that scene open
        /// before the bootstrap ever runs. Without this the loader would believe nothing is resident,
        /// the registry's two-zone cap would be computed against the wrong set, and the first travel
        /// would leave the hand-opened scene loaded forever. Adopting it makes the editor path converge
        /// on the same state a real boot produces. Not for general use: it asserts a fact about the
        /// engine rather than establishing one.
        /// </remarks>
        public void AdoptResident(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey) || IsResident(sceneKey))
            {
                return;
            }

            _resident.Add(sceneKey);
        }

        /// <summary>
        /// Loads <paramref name="sceneKey"/> additively.
        /// </summary>
        /// <param name="sceneKey">A key from <see cref="SceneKeys"/>.</param>
        /// <param name="onComplete">
        /// Invoked exactly once with the outcome, on the frame the operation settles. Optional, because
        /// fire-and-forget is legitimate for a scene nothing is waiting on; every caller that gates a
        /// state transition on the result passes one.
        /// </param>
        public void LoadAdditive(string sceneKey, Action<ResultCode> onComplete = null)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                Settle(sceneKey, ResultCode.InvalidArgument, onComplete);
                return;
            }

            if (!SceneKeys.IsKnown(sceneKey))
            {
                Settle(sceneKey, ResultCode.SceneNotFound, onComplete);
                return;
            }

            if (IsLoading)
            {
                Settle(sceneKey, ResultCode.AlreadyLoading, onComplete);
                return;
            }

            if (IsResident(sceneKey))
            {
                // Already open. Reporting success is correct rather than lenient: the caller's
                // postcondition — "this scene is loaded" — holds.
                InvokeComplete(onComplete, ResultCode.Ok);
                return;
            }

            if (!CanRunCoroutine())
            {
                Settle(sceneKey, ResultCode.LoadTimedOut, onComplete);
                return;
            }

            // Committed. The busy state is established BEFORE the routine can run, and nothing below
            // touches loader state afterwards — see the remarks on _busy.
            _pendingKey = sceneKey;
            _progress = 0f;
            _busy = true;
            var generation = NextGeneration();

            if (!TryStartCoroutine(LoadRoutine(sceneKey, onComplete)))
            {
                AbortStart(generation, sceneKey, ResultCode.LoadTimedOut, onComplete);
            }
        }

        /// <summary>
        /// Unloads <paramref name="sceneKey"/> and the objects it owns.
        /// </summary>
        /// <param name="sceneKey">A key from <see cref="SceneKeys"/>.</param>
        /// <param name="onComplete">Invoked exactly once with the outcome. Optional.</param>
        public void UnloadAsync(string sceneKey, Action<ResultCode> onComplete = null)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                Settle(sceneKey, ResultCode.InvalidArgument, onComplete);
                return;
            }

            if (IsLoading)
            {
                Settle(sceneKey, ResultCode.AlreadyLoading, onComplete);
                return;
            }

            if (!IsResident(sceneKey))
            {
                // Nothing to do, and the caller's postcondition already holds. Same reasoning as the
                // already-resident case in LoadAdditive.
                InvokeComplete(onComplete, ResultCode.Ok);
                return;
            }

            if (!CanRunCoroutine())
            {
                Settle(sceneKey, ResultCode.LoadTimedOut, onComplete);
                return;
            }

            _pendingKey = sceneKey;
            _progress = 0f;
            _busy = true;
            var generation = NextGeneration();

            if (!TryStartCoroutine(UnloadRoutine(sceneKey, onComplete)))
            {
                AbortStart(generation, sceneKey, ResultCode.LoadTimedOut, onComplete);
            }
        }

        private IEnumerator LoadRoutine(string sceneKey, Action<ResultCode> onComplete)
        {
            AsyncOperation op = null;
            try
            {
                op = SceneManager.LoadSceneAsync(sceneKey, LoadSceneMode.Additive);
            }
            catch (Exception)
            {
                // LoadSceneAsync throws rather than returning null when the scene is absent from the
                // build. Both outcomes mean the same thing to a caller, so they collapse to one code.
                op = null;
            }

            if (op == null)
            {
                Finish(sceneKey, ResultCode.SceneNotFound, onComplete);
                yield break;
            }

            // Withhold activation so the expensive part of the load runs while the curtain is still up,
            // and the visible swap happens on a frame we choose.
            op.allowSceneActivation = false;

            var elapsed = 0f;
            var warned = false;

            while (op.progress < ActivationThreshold)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                _progress = Mathf.Min(NearlyComplete, Mathf.Clamp01(op.progress / ActivationThreshold));

                if (!warned && elapsed >= SlowLoadWarningSeconds)
                {
                    warned = true;
                    Warn(LogCode.SceneLoadSlow, sceneKey);
                }

                if (elapsed >= WatchdogSeconds)
                {
                    // A Unity scene load cannot be cancelled. Releasing activation lets the engine finish
                    // and hand us a real scene, which we then unload — abandoning it without activating
                    // would leak the load into the next frame's scene list with nothing tracking it.
                    op.allowSceneActivation = true;
                    TryStartCoroutine(AbandonRoutine(op, sceneKey));
                    Finish(sceneKey, ResultCode.LoadTimedOut, onComplete);
                    yield break;
                }

                yield return null;
            }

            op.allowSceneActivation = true;

            while (!op.isDone)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                _progress = NearlyComplete;

                if (elapsed >= WatchdogSeconds)
                {
                    TryStartCoroutine(AbandonRoutine(op, sceneKey));
                    Finish(sceneKey, ResultCode.LoadTimedOut, onComplete);
                    yield break;
                }

                yield return null;
            }

            _progress = 1f;
            if (!IsResident(sceneKey))
            {
                _resident.Add(sceneKey);
            }

            Finish(sceneKey, ResultCode.Ok, onComplete);
        }

        private IEnumerator UnloadRoutine(string sceneKey, Action<ResultCode> onComplete)
        {
            AsyncOperation op = null;
            try
            {
                op = SceneManager.UnloadSceneAsync(sceneKey);
            }
            catch (Exception)
            {
                op = null;
            }

            if (op == null)
            {
                // The engine disagrees with our bookkeeping about what is loaded. Believe the engine:
                // drop the record so the two-zone cap is computed against reality from here on.
                RemoveResident(sceneKey);
                Finish(sceneKey, ResultCode.NotFound, onComplete);
                yield break;
            }

            var elapsed = 0f;
            while (!op.isDone)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                _progress = Mathf.Clamp01(op.progress);

                if (elapsed >= WatchdogSeconds)
                {
                    // Drop the record even on timeout. A scene we can neither unload nor account for must
                    // not also occupy one of the two resident slots forever.
                    RemoveResident(sceneKey);
                    Finish(sceneKey, ResultCode.LoadTimedOut, onComplete);
                    yield break;
                }

                yield return null;
            }

            _progress = 1f;
            RemoveResident(sceneKey);
            Finish(sceneKey, ResultCode.Ok, onComplete);
        }

        /// <summary>
        /// Drives a load that already timed out to completion and throws the resulting scene away.
        /// </summary>
        /// <remarks>
        /// Runs detached from <see cref="IsLoading"/> on purpose: the player has already been bounced to
        /// the failure path and must not be blocked from retrying while this cleans up behind them.
        /// </remarks>
        private IEnumerator AbandonRoutine(AsyncOperation op, string sceneKey)
        {
            while (!op.isDone)
            {
                yield return null;
            }

            AsyncOperation unload = null;
            try
            {
                unload = SceneManager.UnloadSceneAsync(sceneKey);
            }
            catch (Exception)
            {
                unload = null;
            }

            if (unload == null)
            {
                yield break;
            }

            while (!unload.isDone)
            {
                yield return null;
            }

            RemoveResident(sceneKey);
        }

        /// <summary>
        /// Clears the in-flight state, then reports the outcome.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The order is load-bearing. Callbacks routinely start the next operation — the registry unloads
        /// the outgoing zone and immediately loads the new one — and a callback fired while
        /// <see cref="IsLoading"/> was still true would be rejected with
        /// <see cref="ResultCode.AlreadyLoading"/> by the loader it is running inside.
        /// </para>
        /// <para>
        /// This is also the ONLY place the busy state is cleared, which is what makes the ordering rule on
        /// <see cref="_busy"/> hold: the routine sets nothing on the way in and the caller sets nothing on
        /// the way out, so there is no assignment left that could resurrect a finished operation.
        /// </para>
        /// </remarks>
        private void Finish(string sceneKey, ResultCode code, Action<ResultCode> onComplete)
        {
            _busy = false;
            _pendingKey = null;
            if (code != ResultCode.Ok)
            {
                _progress = 0f;
            }

            if (code == ResultCode.Ok)
            {
                var loaded = Loaded;
                var unloaded = Unloaded;
                if (IsResident(sceneKey))
                {
                    if (loaded != null)
                    {
                        loaded(sceneKey);
                    }
                }
                else if (unloaded != null)
                {
                    unloaded(sceneKey);
                }
            }
            else
            {
                var failed = LoadFailed;
                if (failed != null)
                {
                    failed(sceneKey, code);
                }
            }

            InvokeComplete(onComplete, code);
        }

        private void Settle(string sceneKey, ResultCode code, Action<ResultCode> onComplete)
        {
            var failed = LoadFailed;
            if (failed != null)
            {
                failed(sceneKey ?? string.Empty, code);
            }

            InvokeComplete(onComplete, code);
        }

        private static void InvokeComplete(Action<ResultCode> onComplete, ResultCode code)
        {
            if (onComplete != null)
            {
                onComplete(code);
            }
        }

        /// <summary>Stamps the operation about to be committed and returns its generation.</summary>
        private int NextGeneration()
        {
            unchecked
            {
                _generation++;
            }

            return _generation;
        }

        /// <summary>
        /// Hands <paramref name="routine"/> to the host and reports whether it actually started.
        /// </summary>
        /// <remarks>
        /// <see cref="CanRunCoroutine"/> has already established that the host is active, so the throw is
        /// not expected. It is caught anyway because the alternative — an exception escaping through
        /// <see cref="LoadAdditive"/> with <see cref="_busy"/> already true, or out of a routine before it
        /// reached <see cref="Finish"/> — is exactly the permanently wedged loader this design exists to
        /// make impossible.
        /// </remarks>
        private bool TryStartCoroutine(IEnumerator routine)
        {
            try
            {
                _host.StartCoroutine(routine);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Rolls back the bookkeeping of an operation whose coroutine never started, then reports it.
        /// </summary>
        /// <remarks>
        /// Guarded by <paramref name="generation"/> rather than clearing unconditionally: a routine that
        /// did start may already have settled and handed a nested operation the busy state, and clearing
        /// it from here would leave two operations believing they own the loader.
        /// </remarks>
        private void AbortStart(int generation, string sceneKey, ResultCode code, Action<ResultCode> onComplete)
        {
            if (_generation == generation)
            {
                _busy = false;
                _pendingKey = null;
                _progress = 0f;
            }

            Settle(sceneKey, code, onComplete);
        }

        private bool CanRunCoroutine()
        {
            return _host != null && _host.isActiveAndEnabled;
        }

        private int IndexOfResident(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                return -1;
            }

            for (var i = 0; i < _resident.Count; i++)
            {
                if (string.Equals(_resident[i], sceneKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveResident(string sceneKey)
        {
            var index = IndexOfResident(sceneKey);
            if (index >= 0)
            {
                _resident.RemoveAt(index);
            }
        }

        private void Warn(LogCode code, string detail)
        {
            if (_log != null)
            {
                _log.Warn(code, detail);
            }
        }
    }
}

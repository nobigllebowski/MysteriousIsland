using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Time;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="CommandDispatcher"/>'s routing, its validate-before-execute ordering, and its
    /// promise never to throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guarantee that matters is that a command which fails validation leaves the world
    /// byte-for-byte unchanged. That is what lets UI dispatch optimistically and react to the result,
    /// and it is invisible from the outside: a dispatcher that ran <c>Execute</c> and then returned
    /// the validation code would look identical at every call site. The spy handler below is the only
    /// way to see the difference, so it counts both calls separately rather than merely recording that
    /// it was touched.
    /// </para>
    /// <para>
    /// The no-throw promise matters for the same reason: dispatch happens from UI callbacks and from
    /// async load completions, where an exception becomes a lost session on a player's phone rather
    /// than a stack trace anybody reads.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class CommandDispatchTests
    {
        private FakeCoreLog _log;
        private FakeClock _clock;
        private CommandDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeCoreLog();
            _clock = new FakeClock(12.5d);
            _dispatcher = new CommandDispatcher(_log);
        }

        /// <summary>
        /// Nothing registered is a routine runtime condition — a screen dispatching before wiring
        /// finished — so it reports <see cref="ResultCode.NoHandler"/> and logs, and does not throw.
        /// </summary>
        [Test]
        public void Dispatcher_UnregisteredCommand_ReturnsNoHandlerAndDoesNotThrow()
        {
            CommandResult result = CommandResult.Ok;

            Assert.DoesNotThrow(() => result = _dispatcher.Dispatch(new SaveGameCommand(1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.NoHandler, result.Code);
            Assert.IsFalse(_dispatcher.HasHandler<SaveGameCommand>());
            Assert.AreEqual(1, _log.CountOf(LogCode.UnknownCommand), "An unroutable command was not reported.");
        }

        /// <summary>
        /// Registering one command type must not make a different one dispatchable. The registry is
        /// keyed per type, and a lookup that fell back to "any handler" would route a save into travel.
        /// </summary>
        [Test]
        public void Dispatcher_DifferentCommandType_IsStillUnroutable()
        {
            _dispatcher.Register(new SpySaveHandler(_clock, ResultCode.Ok));

            var result = _dispatcher.Dispatch(new QuitToMenuCommand());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.NoHandler, result.Code);
            Assert.IsTrue(_dispatcher.HasHandler<SaveGameCommand>());
            Assert.IsFalse(_dispatcher.HasHandler<QuitToMenuCommand>());
        }

        /// <summary>
        /// The central claim: a rejected command never reaches <c>Execute</c>. Proven with a spy that
        /// counts the two calls independently and stamps the clock when it runs, so "did not execute"
        /// is observable from two directions rather than inferred.
        /// </summary>
        [Test]
        public void Dispatcher_InvalidCommand_DoesNotExecute()
        {
            var handler = new SpySaveHandler(_clock, ResultCode.NotAllowedInState);
            _dispatcher.Register(handler);

            var result = _dispatcher.Dispatch(new SaveGameCommand(2));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.NotAllowedInState, result.Code, "The handler's own rejection code was not passed through.");
            Assert.AreEqual(1, handler.ValidateCallCount, "Validate should run exactly once.");
            Assert.AreEqual(0, handler.ExecuteCallCount, "Execute ran on a command that failed validation.");
            Assert.AreEqual(SpySaveHandler.NeverExecuted, handler.ExecutedAtSimHours, "Execute left a side effect behind.");
        }

        /// <summary>
        /// Every failure code a handler can return is passed through untouched. Callers map the code to
        /// a player-facing reason, so a dispatcher that collapsed them to one generic failure would
        /// make every rejection say the same wrong thing.
        /// </summary>
        [TestCase(ResultCode.InvalidArgument)]
        [TestCase(ResultCode.NotAllowedInState)]
        [TestCase(ResultCode.SlotEmpty)]
        [TestCase(ResultCode.NotFound)]
        [TestCase(ResultCode.AlreadyLoading)]
        public void Dispatcher_ValidationFailure_ReturnsTheHandlersExactCode(ResultCode code)
        {
            var handler = new SpySaveHandler(_clock, code);
            _dispatcher.Register(handler);

            var result = _dispatcher.Dispatch(new SaveGameCommand(0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(code, result.Code);
            Assert.AreEqual(0, handler.ExecuteCallCount);
        }

        [Test]
        public void Dispatcher_ValidCommand_ExecutesExactlyOnce()
        {
            var handler = new SpySaveHandler(_clock, ResultCode.Ok);
            _dispatcher.Register(handler);

            var result = _dispatcher.Dispatch(new SaveGameCommand(2));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(ResultCode.Ok, result.Code);
            Assert.AreEqual(1, handler.ValidateCallCount);
            Assert.AreEqual(1, handler.ExecuteCallCount, "Execute did not run exactly once.");
            Assert.AreEqual(12.5d, handler.ExecutedAtSimHours, "Execute did not observe the clock it was given.");
            Assert.AreEqual(0, _log.WarningCount, "A successful dispatch should be silent.");
        }

        /// <summary>
        /// The command value must survive the <c>in</c> hand-off intact — this is the payload the
        /// handler acts on, and a dispatcher that lost it would write the wrong save slot.
        /// </summary>
        [Test]
        public void Dispatcher_ValidCommand_DeliversThePayloadToBothValidateAndExecute()
        {
            var handler = new SpySaveHandler(_clock, ResultCode.Ok);
            _dispatcher.Register(handler);

            _dispatcher.Dispatch(new SaveGameCommand(2));

            Assert.AreEqual(2, handler.LastValidatedSlot);
            Assert.AreEqual(2, handler.LastExecutedSlot);
        }

        [Test]
        public void Dispatcher_RepeatedDispatch_RunsHandlerOncePerCall()
        {
            var handler = new SpySaveHandler(_clock, ResultCode.Ok);
            _dispatcher.Register(handler);

            for (var slot = 0; slot < 3; slot++)
            {
                Assert.IsTrue(_dispatcher.Dispatch(new SaveGameCommand(slot)).Success);
            }

            Assert.AreEqual(3, handler.ValidateCallCount);
            Assert.AreEqual(3, handler.ExecuteCallCount);
            Assert.AreEqual(2, handler.LastExecutedSlot);
        }

        /// <summary>
        /// A second registration replaces the first, so a test or debug tool can swap a handler without
        /// tearing the dispatcher down. The displaced handler must go completely silent.
        /// </summary>
        [Test]
        public void Dispatcher_ReRegistration_ReplacesThePreviousHandler()
        {
            var first = new SpySaveHandler(_clock, ResultCode.Ok);
            var second = new SpySaveHandler(_clock, ResultCode.Ok);

            _dispatcher.Register(first);
            _dispatcher.Register(second);
            _dispatcher.Dispatch(new SaveGameCommand(1));

            Assert.AreEqual(0, first.ValidateCallCount, "The displaced handler was still consulted.");
            Assert.AreEqual(0, first.ExecuteCallCount);
            Assert.AreEqual(1, second.ExecuteCallCount);
        }

        /// <summary>
        /// A null handler is refused rather than stored. Storing it would turn every later dispatch
        /// into a silent no-op that reports success — the worst possible outcome for a save command.
        /// </summary>
        [Test]
        public void Dispatcher_NullHandlerRegistration_IsRefusedAndLogged()
        {
            Assert.DoesNotThrow(() => _dispatcher.Register<SaveGameCommand>(null));

            Assert.IsFalse(_dispatcher.HasHandler<SaveGameCommand>(), "A null handler was stored.");
            Assert.AreEqual(1, _log.CountOf(LogCode.UnknownCommand));

            var result = _dispatcher.Dispatch(new SaveGameCommand(0));

            Assert.IsFalse(result.Success, "A dispatch against a refused registration reported success.");
            Assert.AreEqual(ResultCode.NoHandler, result.Code);
        }

        /// <summary>
        /// A null log is documented as tolerated. Exercise the path that would dereference it — the
        /// unroutable dispatch — rather than only construction.
        /// </summary>
        [Test]
        public void Dispatcher_NullLog_DispatchesWithoutThrowing()
        {
            var dispatcher = new CommandDispatcher(null);
            CommandResult result = CommandResult.Ok;

            Assert.DoesNotThrow(() => result = dispatcher.Dispatch(new TravelToZoneCommand("ZoneRibcage")));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.NoHandler, result.Code);
        }

        /// <summary>
        /// A handler that throws is a programmer error, not a routine rejection, so the dispatcher does
        /// not swallow it. Pinning that down stops a later "make it never throw" change from converting
        /// a real bug into a silent failure.
        /// </summary>
        [Test]
        public void Dispatcher_HandlerThatThrows_PropagatesRatherThanReportingSuccess()
        {
            _dispatcher.Register(new ThrowingTravelHandler());

            Assert.Throws<System.InvalidOperationException>(
                () => _dispatcher.Dispatch(new TravelToZoneCommand("ZoneFernmaw")));
        }

        /// <summary>
        /// <see cref="CommandResult.Fail"/> refuses to build a failure that reports success. A result
        /// claiming both would survive review and mislead every reader afterwards.
        /// </summary>
        [Test]
        public void CommandResult_FailWithOkCode_IsCoercedToInvalidArgument()
        {
            var result = CommandResult.Fail(ResultCode.Ok);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.InvalidArgument, result.Code);
        }

        [Test]
        public void CommandResult_Ok_ReportsSuccessWithOkCode()
        {
            var result = CommandResult.Ok;

            Assert.IsTrue(result.Success);
            Assert.AreEqual(ResultCode.Ok, result.Code);
        }

        /// <summary>
        /// "Continue" is a distinct command type, not a flag on <see cref="StartNewGameCommand"/>, so the
        /// registry has to key it separately. Registering the new-game handler must leave resume
        /// unroutable — a fallback that routed resume into new-game would answer the player's "continue"
        /// by silently erasing the save they asked to continue from.
        /// </summary>
        [Test]
        public void Dispatcher_ResumeSavedRun_IsNotServedByTheNewGameHandler()
        {
            _dispatcher.Register(new SpyResumeHandler(ResultCode.Ok));

            var resume = _dispatcher.Dispatch(new ResumeSavedRunCommand(1));
            var newGame = _dispatcher.Dispatch(new StartNewGameCommand(1));

            Assert.IsTrue(resume.Success, "The resume handler did not serve its own command type.");
            Assert.IsFalse(newGame.Success, "A new-game command was routed to the resume handler.");
            Assert.AreEqual(ResultCode.NoHandler, newGame.Code);
            Assert.IsTrue(_dispatcher.HasHandler<ResumeSavedRunCommand>());
            Assert.IsFalse(_dispatcher.HasHandler<StartNewGameCommand>());
        }

        /// <summary>
        /// The rejection that the Continue button exists to surface: an empty slot.
        /// </summary>
        /// <remarks>
        /// This is the case where validate-before-execute earns its keep. <c>ResumeSavedRunHandler</c>
        /// executes by raising the loading curtain and restoring participants from the slot; if a
        /// rejected command still reached <c>Execute</c>, pressing Continue on an empty slot would drop
        /// the player behind a curtain into a world with no run in it. The assertion is therefore not
        /// only on the code but on <c>Execute</c> never having run at all.
        /// </remarks>
        [Test]
        public void Dispatcher_ResumeSavedRunOnEmptySlot_ReturnsSlotEmptyAndNeverExecutes()
        {
            var handler = new SpyResumeHandler(ResultCode.SlotEmpty);
            _dispatcher.Register(handler);

            var result = _dispatcher.Dispatch(new ResumeSavedRunCommand(2));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.SlotEmpty, result.Code, "The empty-slot rejection was not passed through.");
            Assert.AreEqual(1, handler.ValidateCallCount);
            Assert.AreEqual(0, handler.ExecuteCallCount, "Resume executed against a slot it had just rejected as empty.");
            Assert.AreEqual(SpyResumeHandler.NeverExecuted, handler.LastExecutedSlot);
        }

        /// <summary>
        /// Every code the resume handler's own validation can produce, passed through untouched: an
        /// out-of-range slot index is a caller bug and an empty slot is a player-facing dead end, and
        /// the menu writes a different message for each.
        /// </summary>
        [TestCase(ResultCode.SlotEmpty)]
        [TestCase(ResultCode.InvalidArgument)]
        [TestCase(ResultCode.NotAllowedInState)]
        public void Dispatcher_ResumeSavedRunRejection_ReturnsTheHandlersExactCode(ResultCode code)
        {
            var handler = new SpyResumeHandler(code);
            _dispatcher.Register(handler);

            var result = _dispatcher.Dispatch(new ResumeSavedRunCommand(0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(code, result.Code);
            Assert.AreEqual(0, handler.ExecuteCallCount);
        }

        /// <summary>
        /// The slot has to survive the <c>in</c> hand-off to both halves. It is the only payload the
        /// command carries and it selects which save is read.
        /// </summary>
        [Test]
        public void Dispatcher_ResumeSavedRun_DeliversTheSlotToBothValidateAndExecute()
        {
            var handler = new SpyResumeHandler(ResultCode.Ok);
            _dispatcher.Register(handler);

            var result = _dispatcher.Dispatch(new ResumeSavedRunCommand(2));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(ResultCode.Ok, result.Code);
            Assert.AreEqual(2, handler.LastValidatedSlot);
            Assert.AreEqual(2, handler.LastExecutedSlot);
            Assert.AreEqual(1, handler.ExecuteCallCount);
            Assert.AreEqual(0, _log.WarningCount, "A successful resume dispatch should be silent.");
        }

        /// <summary>
        /// An autosave ring slot is a legitimate resume source — after a crash it is usually the newest
        /// save there is — so the payload must carry indices above the manual range intact rather than
        /// being clamped somewhere in the hand-off.
        /// </summary>
        [Test]
        public void Dispatcher_ResumeSavedRunFromAutosaveSlot_DeliversTheRingIndexIntact()
        {
            var handler = new SpyResumeHandler(ResultCode.Ok);
            _dispatcher.Register(handler);

            var autosaveSlot = ForgottenIsle.Game.Saves.SaveSlotService.AutosaveSlot(1);
            Assert.IsTrue(_dispatcher.Dispatch(new ResumeSavedRunCommand(autosaveSlot)).Success);

            Assert.AreEqual(autosaveSlot, handler.LastValidatedSlot);
            Assert.AreEqual(autosaveSlot, handler.LastExecutedSlot);
        }

        [Test]
        public void Dispatcher_UnregisteredResumeSavedRun_ReturnsNoHandler()
        {
            CommandResult result = CommandResult.Ok;

            Assert.DoesNotThrow(() => result = _dispatcher.Dispatch(new ResumeSavedRunCommand(0)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.NoHandler, result.Code);
            Assert.AreEqual(1, _log.CountOf(LogCode.UnknownCommand));
        }

        /// <summary>
        /// Records what it was asked and when, and returns a validation verdict chosen by the test.
        /// </summary>
        /// <remarks>
        /// It stamps <see cref="ExecutedAtSimHours"/> from an injected <see cref="IClock"/> rather than
        /// only bumping a counter, so "Execute never ran" is backed by the absence of a side effect the
        /// handler could only have produced by running.
        /// </remarks>
        private sealed class SpySaveHandler : ICommandHandler<SaveGameCommand>
        {
            /// <summary>Sentinel <see cref="ExecutedAtSimHours"/> holds while Execute has not run.</summary>
            public const double NeverExecuted = -1d;

            private readonly IClock _clock;
            private readonly ResultCode _validationVerdict;

            public SpySaveHandler(IClock clock, ResultCode validationVerdict)
            {
                _clock = clock;
                _validationVerdict = validationVerdict;
                ExecutedAtSimHours = NeverExecuted;
                LastValidatedSlot = int.MinValue;
                LastExecutedSlot = int.MinValue;
            }

            public int ValidateCallCount { get; private set; }

            public int ExecuteCallCount { get; private set; }

            public double ExecutedAtSimHours { get; private set; }

            public int LastValidatedSlot { get; private set; }

            public int LastExecutedSlot { get; private set; }

            public ResultCode Validate(in SaveGameCommand command)
            {
                ValidateCallCount++;
                LastValidatedSlot = command.Slot;
                return _validationVerdict;
            }

            public void Execute(in SaveGameCommand command)
            {
                ExecuteCallCount++;
                LastExecutedSlot = command.Slot;
                ExecutedAtSimHours = _clock.SimHours;
            }
        }

        /// <summary>
        /// The resume equivalent of <see cref="SpySaveHandler"/>: counts both halves separately and
        /// records the slot each was given.
        /// </summary>
        /// <remarks>
        /// A spy rather than the real <c>ResumeSavedRunHandler</c> because that handler needs a
        /// <c>ZoneRegistry</c>, which needs a <c>SceneLoader</c>, which needs a live
        /// <c>MonoBehaviour</c> coroutine driver — none of which exists in EditMode, and its zone load
        /// would never complete if it did. What is under test here is the dispatcher's contract around
        /// the command: routing by type, verdict pass-through, payload fidelity, and no execution after
        /// a rejection. The handler's own restore-and-enter behaviour belongs in a PlayMode test.
        /// </remarks>
        private sealed class SpyResumeHandler : ICommandHandler<ResumeSavedRunCommand>
        {
            /// <summary>Sentinel the recorded slots hold while the matching half has not run.</summary>
            public const int NeverExecuted = int.MinValue;

            private readonly ResultCode _validationVerdict;

            public SpyResumeHandler(ResultCode validationVerdict)
            {
                _validationVerdict = validationVerdict;
                LastValidatedSlot = NeverExecuted;
                LastExecutedSlot = NeverExecuted;
            }

            public int ValidateCallCount { get; private set; }

            public int ExecuteCallCount { get; private set; }

            public int LastValidatedSlot { get; private set; }

            public int LastExecutedSlot { get; private set; }

            public ResultCode Validate(in ResumeSavedRunCommand command)
            {
                ValidateCallCount++;
                LastValidatedSlot = command.Slot;
                return _validationVerdict;
            }

            public void Execute(in ResumeSavedRunCommand command)
            {
                ExecuteCallCount++;
                LastExecutedSlot = command.Slot;
            }
        }

        /// <summary>Validates cleanly and then fails the way a genuine programmer error would.</summary>
        private sealed class ThrowingTravelHandler : ICommandHandler<TravelToZoneCommand>
        {
            public ResultCode Validate(in TravelToZoneCommand command)
            {
                return ResultCode.Ok;
            }

            public void Execute(in TravelToZoneCommand command)
            {
                throw new System.InvalidOperationException("deliberate handler fault");
            }
        }
    }
}

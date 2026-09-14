using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Progress;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// UNITY EDITMODE TIER.
    /// The interaction command layer: what may be inspected or collected, and when.
    /// </summary>
    /// <remarks>
    /// Covers the handlers rather than the MonoBehaviours. Proximity, prompts and the actual
    /// clicking belong to PlayMode, because they need a live scene and a player standing in it;
    /// the rules about what is legal are pure and belong here, where they run in milliseconds.
    /// </remarks>
    [TestFixture]
    public sealed class InteractionTests
    {
        private GameStateMachine _states;
        private ProgressService _progress;
        private CommandDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            var signals = new SignalBus();
            _states = new GameStateMachine(null, signals);
            _progress = new ProgressService(signals, null);
            _dispatcher = new CommandDispatcher(null);
            _dispatcher.Register<InspectCommand>(new InspectHandler(_states, _progress, signals));
            _dispatcher.Register<CollectCommand>(new CollectHandler(_states, _progress, signals));
        }

        private void EnterGameplay()
        {
            _states.TryTransition(GameStateId.MainMenu);
            _states.TryTransition(GameStateId.Loading);
            _states.TryTransition(GameStateId.InGame);
            Assert.That(_states.Current, Is.EqualTo(GameStateId.InGame), "Test setup failed to reach InGame.");
        }

        [Test]
        public void Inspect_InGame_RecordsTheMarker()
        {
            EnterGameplay();

            var result = _dispatcher.Dispatch(new InspectCommand(ContentIds.MarkerRibStone));

            Assert.That(result.Success, Is.True, "Inspect was refused: " + result.Code);
            Assert.That(_progress.HasInspected(ContentIds.MarkerRibStone), Is.True);
        }

        [Test]
        public void Inspect_OutsideGameplay_IsRefused()
        {
            // Still in Boot. Interacting with the world from a menu must not be possible, and the
            // command layer is where that is enforced -- not the view that happens not to show it.
            var result = _dispatcher.Dispatch(new InspectCommand(ContentIds.MarkerRibStone));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(ResultCode.NotAllowedInState));
            Assert.That(_progress.HasInspected(ContentIds.MarkerRibStone), Is.False);
        }

        [Test]
        public void Inspect_EmptyId_IsRefusedAsInvalidArgument()
        {
            EnterGameplay();

            var result = _dispatcher.Dispatch(new InspectCommand(string.Empty));

            Assert.That(result.Code, Is.EqualTo(ResultCode.InvalidArgument));
        }

        [Test]
        public void Inspect_TheSameMarkerTwice_IsAllowed()
        {
            EnterGameplay();

            _dispatcher.Dispatch(new InspectCommand(ContentIds.MarkerRibStone));
            var second = _dispatcher.Dispatch(new InspectCommand(ContentIds.MarkerRibStone));

            Assert.That(second.Success, Is.True,
                "Markers are re-readable: a story beat missed while walking away must not be lost.");
        }

        [Test]
        public void Collect_InGame_RecordsAndUnlocks()
        {
            EnterGameplay();

            var result = _dispatcher.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));

            Assert.That(result.Success, Is.True, "Collect was refused: " + result.Code);
            Assert.That(_progress.HasCollected(ContentIds.DiscoveryBrassTag), Is.True);
            Assert.That(_progress.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True);
        }

        [Test]
        public void Collect_TheSameDiscoveryTwice_IsRefused()
        {
            EnterGameplay();
            _dispatcher.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));

            var second = _dispatcher.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));

            Assert.That(second.Success, Is.False,
                "A double tap must be refused with a reason, not silently no-op.");
            Assert.That(second.Code, Is.EqualTo(ResultCode.NotAllowedInState));
        }

        [Test]
        public void InteractionSystem_ExposesProgressAsReadOnlyServices()
        {
            EnterGameplay();
            var system = new InteractionSystem(_progress, _dispatcher, new SignalBus(), null);

            Assert.That(system.HasCollected(ContentIds.DiscoveryBrassTag), Is.False);

            _dispatcher.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));

            Assert.That(system.HasCollected(ContentIds.DiscoveryBrassTag), Is.True,
                "The services view must reflect live progression, not a snapshot taken at construction.");
            Assert.That(system.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True);
        }

        [Test]
        public void InteractionSystem_ActivateWithNoTarget_DoesNothingAndDoesNotThrow()
        {
            var system = new InteractionSystem(_progress, _dispatcher, new SignalBus(), null);

            Assert.DoesNotThrow(() => system.Activate());
            Assert.That(system.Activate(), Is.False);
        }

        [Test]
        public void InteractionSystem_Clear_EmptiesTheRegistry()
        {
            var system = new InteractionSystem(_progress, _dispatcher, new SignalBus(), null);

            system.Clear();

            Assert.That(system.RegisteredCount, Is.Zero);
            Assert.That(system.Current, Is.Null);
        }
    }
}

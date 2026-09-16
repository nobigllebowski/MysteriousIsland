using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Items;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// CORE TIER (engine-free logic, exercised through the Game-side service).
    /// Carrying, combining, and the one rule that makes an adventure game winnable.
    /// </summary>
    /// <remarks>
    /// The rule: a failed combination must cost nothing. In a game with no shop and no respawn,
    /// consuming the inputs on a wrong guess is a soft lock the player cannot see coming and cannot
    /// undo, and it is the single most common way this genre breaks itself. It gets its own test.
    /// </remarks>
    [TestFixture]
    public sealed class InventoryTests
    {
        private InventoryService NewService()
        {
            return new InventoryService(new SignalBus(), null);
        }

        // --- carrying -----------------------------------------------------------------------

        [Test]
        public void Take_PutsTheItemInHand()
        {
            var service = NewService();

            Assert.That(service.Take(ItemIds.DrySpindle), Is.True);
            Assert.That(service.Has(ItemIds.DrySpindle), Is.True);
            Assert.That(service.Inventory.Count, Is.EqualTo(1));
        }

        [Test]
        public void Take_TheSameItemTwice_IsRecordedOnce()
        {
            var service = NewService();
            service.Take(ItemIds.DrySpindle);

            Assert.That(service.Take(ItemIds.DrySpindle), Is.False,
                "Every item in this game is one specific object; two of it is not a state the "
                + "fiction can produce.");
            Assert.That(service.Inventory.Count, Is.EqualTo(1));
        }

        [Test]
        public void Items_KeepTheOrderTheyWereFoundIn()
        {
            var service = NewService();
            service.Take(ItemIds.SluiceKey);
            service.Take(ItemIds.DrySpindle);

            Assert.That(service.Inventory.Items[0], Is.EqualTo(ItemIds.SluiceKey));
            Assert.That(service.Inventory.Items[1], Is.EqualTo(ItemIds.DrySpindle),
                "The panel shows this list; a set that reshuffles itself is unreadable.");
        }

        // --- combining ----------------------------------------------------------------------

        [Test]
        public void Combine_TheReelAndTheSpindle_MakesAReboundReel()
        {
            var service = NewService();
            service.Take(ItemIds.WaterloggedReel);
            service.Take(ItemIds.DrySpindle);

            string result;
            Assert.That(service.Combine(ItemIds.WaterloggedReel, ItemIds.DrySpindle, out result), Is.True);

            Assert.That(result, Is.EqualTo(ItemIds.ReboundReel));
            Assert.That(service.Has(ItemIds.ReboundReel), Is.True);
            Assert.That(service.Has(ItemIds.WaterloggedReel), Is.False, "Both inputs are consumed.");
            Assert.That(service.Has(ItemIds.DrySpindle), Is.False);
        }

        [Test]
        public void Combine_WorksInEitherOrder()
        {
            var service = NewService();
            service.Take(ItemIds.WaterloggedReel);
            service.Take(ItemIds.DrySpindle);

            string result;
            Assert.That(service.Combine(ItemIds.DrySpindle, ItemIds.WaterloggedReel, out result), Is.True,
                "A player who tries it the other way round has not made a mistake.");
            Assert.That(result, Is.EqualTo(ItemIds.ReboundReel));
        }

        [Test]
        public void Combine_ThatDoesNotWork_CostsThePlayerNothing()
        {
            // THE SOFT-LOCK TEST. Consuming inputs on a wrong guess is unrecoverable in a game with
            // no way to get an item back, and the player has no warning it is about to happen.
            var service = NewService();
            service.Take(ItemIds.SluiceKey);
            service.Take(ItemIds.DrySpindle);

            string result;
            Assert.That(service.Combine(ItemIds.SluiceKey, ItemIds.DrySpindle, out result), Is.False);

            Assert.That(service.Has(ItemIds.SluiceKey), Is.True, "A wrong guess must not eat an item.");
            Assert.That(service.Has(ItemIds.DrySpindle), Is.True);
            Assert.That(service.Inventory.Count, Is.EqualTo(2));
        }

        [Test]
        public void Combine_WithAnItemNotCarried_ChangesNothing()
        {
            var service = NewService();
            service.Take(ItemIds.DrySpindle);

            string result;
            Assert.That(service.Combine(ItemIds.WaterloggedReel, ItemIds.DrySpindle, out result), Is.False);
            Assert.That(service.Has(ItemIds.DrySpindle), Is.True);
        }

        [Test]
        public void Combine_AnItemWithItself_IsRefused()
        {
            var service = NewService();
            service.Take(ItemIds.DrySpindle);

            string result;
            Assert.That(service.Combine(ItemIds.DrySpindle, ItemIds.DrySpindle, out result), Is.False);
        }

        [Test]
        public void RecipeTable_IsNotEmpty()
        {
            // A guard against the table being silently emptied by a refactor: with no recipes the
            // game still runs, still saves, and is unwinnable.
            Assert.That(Combinations.RecipeCount, Is.GreaterThan(0));
        }

        // --- save round trip ------------------------------------------------------------------

        [Test]
        public void Inventory_SurvivesACaptureAndRestore()
        {
            var source = NewService();
            source.Take(ItemIds.SluiceKey);
            source.Take(ItemIds.DrySpindle);

            var doc = new SaveDocument();
            source.Capture(doc);

            var target = NewService();
            target.Restore(doc);

            Assert.That(target.Has(ItemIds.SluiceKey), Is.True);
            Assert.That(target.Has(ItemIds.DrySpindle), Is.True);
            Assert.That(target.Inventory.Count, Is.EqualTo(2));
        }

        [Test]
        public void Restore_FromASaveWithNoInventorySection_StartsEmpty()
        {
            var target = NewService();
            target.Take(ItemIds.SluiceKey);

            target.Restore(new SaveDocument());

            Assert.That(target.Inventory.Count, Is.Zero,
                "A save written before the inventory existed is an older run, not corruption.");
        }

        [Test]
        public void ResetForNewRun_EmptiesTheHands()
        {
            var service = NewService();
            service.Take(ItemIds.SluiceKey);

            service.ResetForNewRun();

            Assert.That(service.Inventory.Count, Is.Zero);
        }

        // --- holding ------------------------------------------------------------------------------

        [Test]
        public void Holding_RequiresCarrying_AndConsumingPutsItDown()
        {
            var service = NewService();
            Assert.That(service.Hold(ItemIds.DrySpindle), Is.False, "Not carried.");

            service.Take(ItemIds.DrySpindle);
            Assert.That(service.Hold(ItemIds.DrySpindle), Is.True);
            Assert.That(service.Held, Is.EqualTo(ItemIds.DrySpindle));

            service.Consume(ItemIds.DrySpindle);
            Assert.That(service.Held, Is.Null, "A spent item cannot stay held.");
        }

        [Test]
        public void Held_IsTransient_ANewRunOrALoadPutsItDown()
        {
            var service = NewService();
            service.Take(ItemIds.SluiceKey);
            service.Hold(ItemIds.SluiceKey);

            var doc = new SaveDocument();
            service.Capture(doc);
            service.Restore(doc);
            Assert.That(service.Held, Is.Null);
            Assert.That(service.Has(ItemIds.SluiceKey), Is.True, "The item is saved; the holding is not.");

            service.Hold(ItemIds.SluiceKey);
            service.ResetForNewRun();
            Assert.That(service.Held, Is.Null);
        }

        [Test]
        public void HoldItem_IsACommand_AndAnEmptyIdPutsDown()
        {
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var inventory = new InventoryService(signals, null);
            inventory.Take(ItemIds.SluiceKey);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<HoldItemCommand>(new HoldItemHandler(states, inventory));
            states.TryTransition(GameStateId.MainMenu);
            states.TryTransition(GameStateId.Loading);
            states.TryTransition(GameStateId.InGame);

            Assert.That(dispatcher.Dispatch(new HoldItemCommand(ItemIds.DrySpindle)).Code, Is.EqualTo(ResultCode.InvalidArgument), "Not carried.");
            Assert.That(dispatcher.Dispatch(new HoldItemCommand(ItemIds.SluiceKey)).Success, Is.True);
            Assert.That(inventory.Held, Is.EqualTo(ItemIds.SluiceKey));
            Assert.That(dispatcher.Dispatch(new HoldItemCommand(string.Empty)).Success, Is.True);
            Assert.That(inventory.Held, Is.Null);
        }

        // --- the command layer ------------------------------------------------------------------

        [Test]
        public void TakeItem_OutsideGameplay_IsRefused()
        {
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var inventory = new InventoryService(signals, null);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<TakeItemCommand>(new TakeItemHandler(states, inventory, signals));

            // Still in Boot.
            var result = dispatcher.Dispatch(new TakeItemCommand(ItemIds.SluiceKey));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(ResultCode.NotAllowedInState));
            Assert.That(inventory.Has(ItemIds.SluiceKey), Is.False);
        }

        [Test]
        public void TakeItem_InGameplay_PutsItInHand()
        {
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var inventory = new InventoryService(signals, null);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<TakeItemCommand>(new TakeItemHandler(states, inventory, signals));

            states.TryTransition(GameStateId.MainMenu);
            states.TryTransition(GameStateId.Loading);
            states.TryTransition(GameStateId.InGame);

            var result = dispatcher.Dispatch(new TakeItemCommand(ItemIds.SluiceKey));

            Assert.That(result.Success, Is.True, "Take was refused: " + result.Code);
            Assert.That(inventory.Has(ItemIds.SluiceKey), Is.True);
        }
    }
}

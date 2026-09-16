using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Fire;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Items;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>CORE + GAME TIER. The dry-fire problem: spark, tinder, shelter, and five honest failures.</summary>
    [TestFixture]
    public sealed class FireTests
    {
        // --- The rules, as the design's table (§7:10, §4.2) --------------------------------------

        [Test]
        public void Grass_Flares_AndIsSpent_AndNothingElseIsLost()
        {
            var site = new FireSiteState("open", false);
            Assert.That(site.Apply(ItemIds.DryGrass, true), Is.EqualTo(FireAct.GrassLaid));
            Assert.That(site.Apply(ItemIds.DriftwoodDry, true), Is.EqualTo(FireAct.WoodStacked));

            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.GrassFlared));

            Assert.That(site.Tinder, Is.EqualTo(Tinder.None), "The grass is gone.");
            Assert.That(site.HasWood, Is.True, "The wood is not.");
            Assert.That(site.IsLit, Is.False);
        }

        [Test]
        public void Fibre_WithNoWood_HoldsAnEmberAndStarves_KeepingTheFibre()
        {
            var site = new FireSiteState("lee", true);
            site.Apply(ItemIds.PolyFibre, true);

            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.EmberStarved));
            Assert.That(site.Tinder, Is.EqualTo(Tinder.Fibre), "Failing costs nothing (ADR-0019).");
        }

        [Test]
        public void FibreAndWood_InTheOpen_BlowOut_AndTheKitStays()
        {
            var site = new FireSiteState("open", false);
            site.Apply(ItemIds.PolyFibre, true);
            site.Apply(ItemIds.DriftwoodDry, true);

            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.BlewOut));
            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.BlewOut));

            Assert.That(site.BlowOuts, Is.EqualTo(2));
            Assert.That(site.Tinder, Is.EqualTo(Tinder.Fibre));
            Assert.That(site.HasWood, Is.True);
            Assert.That(site.IsLit, Is.False);
        }

        [Test]
        public void FibreAndWood_InTheLee_Light()
        {
            var site = new FireSiteState("lee", true);
            site.Apply(ItemIds.PolyFibre, true);
            site.Apply(ItemIds.DriftwoodDry, true);

            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.Lit));
            Assert.That(site.IsLit, Is.True);
            Assert.That(site.Apply(ItemIds.PolyFibre, true), Is.EqualTo(FireAct.AlreadyLit));
            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.AlreadyLit));
        }

        [Test]
        public void ThePanel_MakesOpenSandSheltered_AndIsPointlessInTheLee()
        {
            var open = new FireSiteState("open", false);
            Assert.That(open.Apply(ItemIds.FibreglassPanel, true), Is.EqualTo(FireAct.PanelPlaced));
            Assert.That(open.Sheltered, Is.True);
            open.Apply(ItemIds.PolyFibre, true);
            open.Apply(ItemIds.DriftwoodDry, true);
            Assert.That(open.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.Lit), "Two solutions, both authored.");

            var lee = new FireSiteState("lee", true);
            Assert.That(lee.Apply(ItemIds.FibreglassPanel, true), Is.EqualTo(FireAct.PanelPointless));
            Assert.That(lee.PanelPlaced, Is.False);
        }

        [Test]
        public void WetWood_IsRefusedUnlit_AndDriesByAFire()
        {
            var site = new FireSiteState("lee", true);
            Assert.That(site.Apply(ItemIds.DriftwoodWet, true), Is.EqualTo(FireAct.WetWoodRefused));

            site.Apply(ItemIds.PolyFibre, true);
            site.Apply(ItemIds.DriftwoodDry, true);
            site.Apply(ItemIds.ChertNodule, true);

            Assert.That(site.Apply(ItemIds.DriftwoodWet, true), Is.EqualTo(FireAct.WoodDrying));
        }

        [Test]
        public void TheChert_NeedsTheSpine_AndSparksOnSandCostNothing()
        {
            var site = new FireSiteState("lee", true);
            Assert.That(site.Apply(ItemIds.ChertNodule, false), Is.EqualTo(FireAct.NoSpine));
            Assert.That(site.Apply(ItemIds.ChertNodule, true), Is.EqualTo(FireAct.SparksOnSand));
            Assert.That(site.Apply(ItemIds.Kelp, true), Is.EqualTo(FireAct.KelpRefused));
            Assert.That(site.Apply(ItemIds.BrassTag, true), Is.EqualTo(FireAct.Nothing));
        }

        [Test]
        public void WhatIsConsumed_IsWhatWasLaid()
        {
            Assert.That(FireRules.Consumes(FireAct.FibreLaid), Is.True);
            Assert.That(FireRules.Consumes(FireAct.WoodStacked), Is.True);
            Assert.That(FireRules.Consumes(FireAct.PanelPlaced), Is.True);
            Assert.That(FireRules.Consumes(FireAct.WoodDrying), Is.True);
            Assert.That(FireRules.Consumes(FireAct.Lit), Is.False, "The chert is not consumed by striking.");
            Assert.That(FireRules.Consumes(FireAct.BlewOut), Is.False);
            Assert.That(FireRules.Consumes(FireAct.EmberStarved), Is.False);
            Assert.That(FireRules.IsRefusal(FireAct.WetWoodRefused), Is.True);
            Assert.That(FireRules.IsRefusal(FireAct.BlewOut), Is.False, "A blow-out happened; it is not a refusal.");
            Assert.That(FireRules.NarrationKey(FireAct.Nothing), Is.Null);
            Assert.That(FireRules.NarrationKey(FireAct.Lit), Is.EqualTo("narration.fire.lit"));
        }

        [Test]
        public void SiteState_SurvivesACaptureAndRestore()
        {
            var site = new FireSiteState("open", false);
            site.Apply(ItemIds.FibreglassPanel, true);
            site.Apply(ItemIds.PolyFibre, true);
            site.Apply(ItemIds.DriftwoodDry, true);

            var copy = new FireSiteState("open", false);
            copy.Restore(site.Capture());
            Assert.That(copy.PanelPlaced, Is.True);
            Assert.That(copy.Tinder, Is.EqualTo(Tinder.Fibre));
            Assert.That(copy.HasWood, Is.True);
            Assert.That(copy.IsLit, Is.False);

            var open = new FireSiteState("open2", false);
            open.Apply(ItemIds.PolyFibre, true);
            open.Apply(ItemIds.DriftwoodDry, true);
            open.Apply(ItemIds.ChertNodule, true);
            open.Apply(ItemIds.ChertNodule, true);
            open.Apply(ItemIds.ChertNodule, true);
            var again = new FireSiteState("open2", false);
            again.Restore(open.Capture());
            Assert.That(again.BlowOuts, Is.EqualTo(3));
        }

        // --- The service --------------------------------------------------------------------------

        private static FireService NewService(out InventoryService inventory, out SignalBus signals)
        {
            signals = new SignalBus();
            inventory = new InventoryService(signals, null);
            return new FireService(signals, inventory, null);
        }

        [Test]
        public void FirstSparks_AreAnnouncedOnce_WhateverTheyLandIn()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            var firsts = 0;
            signals.Subscribe<FireChangedSignal>(s => { if (s.Kind == FireChangeKind.FirstSparks) firsts++; });

            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);

            Assert.That(fire.Sparked, Is.True);
            Assert.That(firsts, Is.EqualTo(1));
        }

        [Test]
        public void WetWood_ByTheFire_ComesBackDry_AfterNinetySeconds()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.PolyFibre, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodDry, true);
            Assert.That(fire.Apply(ContentIds.FireSiteLee, ItemIds.ChertNodule, true), Is.EqualTo(FireAct.Lit));
            Assert.That(fire.IsLit, Is.True);

            Assert.That(fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodWet, true), Is.EqualTo(FireAct.WoodDrying));
            Assert.That(fire.DryingRemaining, Is.EqualTo(FireRules.WoodDryingSeconds));

            fire.Advance(89d);
            Assert.That(inventory.Has(ItemIds.DriftwoodDry), Is.False);
            fire.Advance(1.5d);
            Assert.That(inventory.Has(ItemIds.DriftwoodDry), Is.True, "Dry now, and in the bag.");
            Assert.That(fire.DryingRemaining, Is.Zero);
        }

        [Test]
        public void Ticks_DriveTheWarmZone_AtTenASecond()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.PolyFibre, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodDry, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.ChertNodule, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodWet, true);

            for (var i = 0; i < 895; i++)
            {
                signals.Publish(new TickCompletedSignal(0d));
            }

            Assert.That(inventory.Has(ItemIds.DriftwoodDry), Is.False, "89.5 s.");
            for (var i = 0; i < 10; i++)
            {
                signals.Publish(new TickCompletedSignal(0d));
            }

            Assert.That(inventory.Has(ItemIds.DriftwoodDry), Is.True);
        }

        [Test]
        public void CarryingTheKit_MovesWhatIsLaidInTheOpen_IntoTheLee()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.PolyFibre, true);
            fire.Apply(ContentIds.FireSiteOpenB, ItemIds.DriftwoodDry, true);

            Assert.That(fire.CarryKitToLee(), Is.True);

            var lee = fire.Site(ContentIds.FireSiteLee);
            Assert.That(lee.Tinder, Is.EqualTo(Tinder.Fibre));
            Assert.That(lee.HasWood, Is.True);
            Assert.That(fire.Site(ContentIds.FireSiteOpenA).HasKit, Is.False);
            Assert.That(fire.Site(ContentIds.FireSiteOpenB).HasKit, Is.False);
            Assert.That(fire.Apply(ContentIds.FireSiteLee, ItemIds.ChertNodule, true), Is.EqualTo(FireAct.Lit), "The strike is still the player's.");
            Assert.That(fire.CarryKitToLee(), Is.False, "Nothing left to carry, and the lee burns.");
        }

        [Test]
        public void TheService_SurvivesACaptureAndRestore()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.PolyFibre, true);
            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.DriftwoodDry, true);
            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.PolyFibre, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodDry, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.ChertNodule, true);
            fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodWet, true);
            fire.Advance(30d);

            var doc = new SaveDocument();
            fire.Capture(doc);

            InventoryService other;
            SignalBus otherSignals;
            var restored = NewService(out other, out otherSignals);
            restored.Restore(doc);

            Assert.That(restored.IsLit, Is.True);
            Assert.That(restored.Site(ContentIds.FireSiteLee).IsLit, Is.True);
            Assert.That(restored.Site(ContentIds.FireSiteOpenA).BlowOuts, Is.EqualTo(1));
            Assert.That(restored.Site(ContentIds.FireSiteOpenA).HasKit, Is.True);
            Assert.That(restored.BlowOuts, Is.EqualTo(1));
            Assert.That(restored.Sparked, Is.True);
            Assert.That(restored.DryingRemaining, Is.EqualTo(60d).Within(0.001d));

            restored.Advance(61d);
            Assert.That(other.Has(ItemIds.DriftwoodDry), Is.True, "The warm zone's clock survives a save.");
        }

        [Test]
        public void AnOlderSave_ReadsAsBareSand()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            fire.Restore(new SaveDocument());

            Assert.That(fire.IsLit, Is.False);
            Assert.That(fire.Sparked, Is.False);
            Assert.That(fire.Site(ContentIds.FireSiteLee).HasKit, Is.False);
        }

        // --- The command layer --------------------------------------------------------------------

        [Test]
        public void CarryFireKit_IsRefused_WithNothingInTheOpen_OrWithAFireBurning()
        {
            InventoryService inventory;
            SignalBus signals;
            var fire = NewService(out inventory, out signals);
            var states = new GameStateMachine(null, signals);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<CarryFireKitCommand>(new CarryFireKitHandler(states, fire));
            states.TryTransition(GameStateId.MainMenu);
            states.TryTransition(GameStateId.Loading);
            states.TryTransition(GameStateId.InGame);

            Assert.That(dispatcher.Dispatch(new CarryFireKitCommand()).Success, Is.False, "Nothing laid.");

            fire.Apply(ContentIds.FireSiteOpenA, ItemIds.PolyFibre, true);
            Assert.That(dispatcher.Dispatch(new CarryFireKitCommand()).Success, Is.True);
            Assert.That(fire.Site(ContentIds.FireSiteLee).Tinder, Is.EqualTo(Tinder.Fibre));
        }

        // --- The record ----------------------------------------------------------------------------

        [Test]
        public void TheFire_IsRecordedWhenLit_AndIsTheNotebooksEntry()
        {
            var progress = new WorldProgress();
            Assert.That(ContentIds.KindOf(ContentIds.MechanismFire), Is.EqualTo(ContentKind.Mechanism));
            progress.Solve(ContentIds.MechanismFire);

            var contents = Slate.Build(progress, new SlateFacts(false, false, false, false, false, false, false));
            Assert.That(contents.Observed[0].TitleKey, Is.EqualTo("slate.observed." + ContentIds.MechanismFire));
            Assert.That(System.Array.IndexOf(Recorded.Recordable, ContentIds.MechanismFire), Is.GreaterThanOrEqualTo(0));
        }
    }
}

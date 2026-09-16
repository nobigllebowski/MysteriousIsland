using System.Collections.Generic;
using ForgottenIsle.UI.Controllers;
using ForgottenIsle.UI.Hud;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// UI TIER. The tray's one rule -- hold, put down, or combine -- and the tray as a reporter.
    /// </summary>
    /// <remarks>
    /// Holding an item is game state (HoldItemCommand), so the decision lives in the controller as
    /// a pure function and the panel only reports taps. The cancel is the case that matters: a
    /// two-step interaction needs a way out of the first step, and in this game combinations are
    /// not reversible.
    /// </remarks>
    [TestFixture]
    public sealed class InventoryPanelTests
    {
        private const string Spindle = "item.dry_spindle";
        private const string Reel = "item.waterlogged_reel";

        [Test]
        public void NothingHeld_ATapHolds()
        {
            Assert.That(HudController.Decide(null, Spindle), Is.EqualTo(HudController.TapMeaning.Hold));
            Assert.That(HudController.Decide(string.Empty, Spindle), Is.EqualTo(HudController.TapMeaning.Hold));
        }

        [Test]
        public void TheHeldItemTapped_PutsItDown()
        {
            // THE WAY OUT.
            Assert.That(HudController.Decide(Spindle, Spindle), Is.EqualTo(HudController.TapMeaning.Release));
        }

        [Test]
        public void AnotherItemTapped_Combines()
        {
            Assert.That(HudController.Decide(Spindle, Reel), Is.EqualTo(HudController.TapMeaning.Combine));
        }

        [Test]
        public void ThePanel_ReportsTaps_AndIgnoresEmptyOnes()
        {
            var panel = new InventoryPanel(new VisualElement(), "Items", "CARRYING", "Nothing yet.", "Tap two.");
            panel.SetItems(new List<InventoryItemView> { new InventoryItemView(Spindle, Spindle) });

            var tapped = new List<string>();
            panel.ItemTapped += id => tapped.Add(id);

            panel.TapItem(Spindle);
            panel.TapItem(null);
            panel.TapItem(string.Empty);

            Assert.That(tapped, Is.EqualTo(new[] { Spindle }));
        }

        [Test]
        public void ThePanel_DrawsWhatItIsToldIsHeld_AndKeepsItAcrossARebuild()
        {
            var panel = new InventoryPanel(new VisualElement(), "Items", "CARRYING", "Nothing yet.", "Tap two.");
            panel.SetItems(new List<InventoryItemView> { new InventoryItemView(Spindle, Spindle), new InventoryItemView(Reel, Reel) });

            panel.SetHeld(Reel);
            Assert.That(panel.HeldItem, Is.EqualTo(Reel));

            panel.SetItems(new List<InventoryItemView> { new InventoryItemView(Reel, Reel) });
            Assert.That(panel.HeldItem, Is.EqualTo(Reel), "Still carried, still held.");

            panel.SetHeld(null);
            Assert.That(panel.HeldItem, Is.Null);
        }

        [Test]
        public void AnEmptyInventory_IsNotAnError()
        {
            var panel = new InventoryPanel(new VisualElement(), "Items", "CARRYING", "Nothing yet.", "Tap two.");
            Assert.DoesNotThrow(() => panel.SetItems(null));
            Assert.DoesNotThrow(() => panel.SetHeld("item.nothing"));
        }
    }
}

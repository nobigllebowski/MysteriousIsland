using System.Collections.Generic;
using ForgottenIsle.UI.Hud;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// UI TIER. The selection rule in the inventory tray — the part a player can get stuck in.
    /// </summary>
    /// <remarks>
    /// Worth its own fixture because the tray is the only place in the game where an interaction
    /// has two steps. Every two-step interaction needs a way out of the first step, and "tap it
    /// again" is only a way out if it actually clears the selection — otherwise the player's next
    /// tap combines something they did not mean to combine, and in this game combinations are not
    /// reversible.
    /// <para>
    /// No panel, no event system and no frame: the tap entry point is a method, so the rule can be
    /// exercised directly. That is the reason it is a method.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class InventoryPanelTests
    {
        private const string Spindle = "item.dry_spindle";
        private const string Reel = "item.waterlogged_reel";
        private const string Key = "item.sluice_key";

        private InventoryPanel NewPanel()
        {
            // A bare VisualElement is a legitimate parent: nothing here needs a panel to be
            // attached, because nothing here draws.
            return new InventoryPanel(new VisualElement(), "Items", "CARRYING", "Nothing yet.", "Tap two.");
        }

        private static IReadOnlyList<InventoryItemView> Carrying(params string[] ids)
        {
            var views = new List<InventoryItemView>(ids.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                views.Add(new InventoryItemView(ids[i], ids[i]));
            }

            return views;
        }

        [Test]
        public void TappingTwoDifferentItems_RequestsThatCombination()
        {
            var panel = NewPanel();
            panel.SetItems(Carrying(Spindle, Reel));

            string first = null;
            string second = null;
            panel.Combine += (a, b) =>
            {
                first = a;
                second = b;
            };

            panel.TapItem(Spindle);
            panel.TapItem(Reel);

            Assert.That(first, Is.EqualTo(Spindle));
            Assert.That(second, Is.EqualTo(Reel));
        }

        [Test]
        public void TappingOneItem_RequestsNothing()
        {
            var panel = NewPanel();
            panel.SetItems(Carrying(Spindle, Reel));

            var raised = false;
            panel.Combine += (a, b) => raised = true;

            panel.TapItem(Spindle);

            Assert.That(raised, Is.False, "One tap is a selection, not a combination.");
        }

        [Test]
        public void TappingTheSameItemTwice_CancelsInsteadOfCombining()
        {
            // THE WAY OUT. Without this the player's first tap is a commitment they cannot take
            // back, on an action that consumes items and cannot be undone.
            var panel = NewPanel();
            panel.SetItems(Carrying(Spindle, Reel));

            var raised = 0;
            panel.Combine += (a, b) => raised++;

            panel.TapItem(Spindle);
            panel.TapItem(Spindle);

            Assert.That(raised, Is.Zero, "Tapping the selected item again must put it back down.");

            // And the selection really is cleared: the next single tap starts a new pair rather
            // than completing the old one.
            panel.TapItem(Reel);
            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void AfterACombination_TheSelectionIsCleared()
        {
            var panel = NewPanel();
            panel.SetItems(Carrying(Spindle, Reel, Key));

            var pairs = new List<string>();
            panel.Combine += (a, b) => pairs.Add(a + "+" + b);

            panel.TapItem(Spindle);
            panel.TapItem(Reel);
            panel.TapItem(Key);

            Assert.That(pairs.Count, Is.EqualTo(1),
                "The third tap starts a new selection; it does not pair with the last result.");
        }

        [Test]
        public void ReplacingTheItems_ClearsAnyPendingSelection()
        {
            // The inventory changes underneath the tray whenever anything is taken or consumed,
            // and a selection left pointing at an item that is no longer carried would send the
            // handler a pair it has to refuse.
            var panel = NewPanel();
            panel.SetItems(Carrying(Spindle, Reel));

            var raised = 0;
            panel.Combine += (a, b) => raised++;

            panel.TapItem(Spindle);
            panel.SetItems(Carrying(Reel, Key));
            panel.TapItem(Reel);

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void TappingNothing_IsIgnored()
        {
            var panel = NewPanel();
            panel.SetItems(Carrying(Spindle));

            var raised = 0;
            panel.Combine += (a, b) => raised++;

            panel.TapItem(null);
            panel.TapItem(string.Empty);
            panel.TapItem(Spindle);

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void AnEmptyInventory_IsNotAnError()
        {
            var panel = NewPanel();

            Assert.DoesNotThrow(() => panel.SetItems(null));
            Assert.DoesNotThrow(() => panel.SetItems(Carrying()));
            Assert.DoesNotThrow(() => panel.TapItem(Spindle));
        }
    }
}

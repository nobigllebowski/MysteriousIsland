using System;
using System.Collections.Generic;
using ForgottenIsle.UI.Core;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Hud
{
    /// <summary>
    /// What the player is carrying, and the only place two things can be put together.
    /// </summary>
    /// <remarks>
    /// A tray of chips along the bottom of the screen, opened by a tab, not a full-screen bag.
    /// The distinction matters: this game's inventory holds five or six specific objects, not an
    /// economy, and a screen-filling grid of slots would be a promise of a game about collecting
    /// that Vardholm has no intention of keeping.
    /// <para>
    /// COMBINING IS TAP, THEN TAP. The first tap selects; the second either raises
    /// <see cref="Combine"/> for that pair or deselects if it is the same chip again. No drag, no
    /// long press, no separate "combine" mode — one thumb, no gesture to learn, and nothing that a
    /// player can start and be unable to cancel. The second tap on the same chip is the cancel.
    /// </para>
    /// <para>
    /// It renders strings it was given and reports taps by id. It holds no service, looks nothing
    /// up, and cannot change what the player is carrying: the id it hands back is an opaque token
    /// as far as this class is concerned, and the controller decides what it means (ADR-0002).
    /// </para>
    /// </remarks>
    public sealed class InventoryPanel
    {
        private readonly VisualElement _root;
        private readonly VisualElement _tray;
        private readonly VisualElement _chipRow;
        private readonly Label _emptyLabel;
        private readonly Label _hintLabel;
        private readonly Button _tab;
        private readonly List<Chip> _chips = new List<Chip>(8);

        private bool _open;
        private string _selectedId;

        /// <summary>
        /// Builds the panel into <paramref name="parent"/>, hidden and empty.
        /// </summary>
        /// <param name="parent">The HUD root.</param>
        /// <param name="tabText">Already-localized label for the tab that opens the tray.</param>
        /// <param name="headingText">Already-localized heading over the chips.</param>
        /// <param name="emptyText">Already-localized line shown while nothing is carried.</param>
        /// <param name="hintText">Already-localized line explaining how to combine.</param>
        public InventoryPanel(
            VisualElement parent,
            string tabText,
            string headingText,
            string emptyText,
            string hintText)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            _root = new VisualElement { name = "inventory" };
            _root.style.position = Position.Absolute;
            _root.style.left = Theme.Space16;
            _root.style.right = Theme.Space16;

            // TOP of the screen, under the pause button, and not the bottom where an inventory
            // tray belongs on every desktop game ever made. Both of this game's thumbs live along
            // the bottom edge -- the movement stick on the left, the look pad on the right -- and
            // an opened tray down there is an opaque sheet over the controls the player is holding.
            // The top strip is the only part of a portrait screen a thumb never rests on.
            _root.style.top = 72f + 48f + Theme.Space8 + 48f + Theme.Space8;
            _root.pickingMode = PickingMode.Ignore;
            _root.style.alignItems = Align.FlexEnd;
            parent.Add(_root);

            _tab = Buttons.Ghost(tabText, Toggle);
            _tab.name = "inventory-tab";
            _tab.style.marginBottom = Theme.Space8;
            _tab.style.minWidth = 48f;
            _tab.style.minHeight = 48f;
            _root.Add(_tab);

            _tray = new VisualElement { name = "inventory-tray" };
            _tray.style.display = DisplayStyle.None;
            _tray.style.backgroundColor = Theme.Surface;
            _tray.style.paddingLeft = Theme.Space8;
            _tray.style.paddingRight = Theme.Space8;
            _tray.style.paddingTop = Theme.Space8;
            _tray.style.paddingBottom = Theme.Space16;
            _tray.style.alignSelf = Align.Stretch;
            _tray.style.borderTopLeftRadius = Theme.RadiusMd;
            _tray.style.borderTopRightRadius = Theme.RadiusMd;
            _tray.style.borderBottomLeftRadius = Theme.RadiusMd;
            _tray.style.borderBottomRightRadius = Theme.RadiusMd;
            _root.Add(_tray);

            var heading = Typography.Caption(headingText);
            heading.style.color = Theme.TextMuted;
            heading.style.letterSpacing = 2f;
            heading.style.marginLeft = Theme.Space8;
            heading.pickingMode = PickingMode.Ignore;
            _tray.Add(heading);

            _chipRow = new VisualElement { name = "inventory-chips" };
            _chipRow.style.flexDirection = FlexDirection.Row;
            _chipRow.style.flexWrap = Wrap.Wrap;
            _tray.Add(_chipRow);

            _emptyLabel = Typography.Caption(emptyText);
            _emptyLabel.style.color = Theme.TextMuted;
            _emptyLabel.style.marginLeft = Theme.Space8;
            _emptyLabel.pickingMode = PickingMode.Ignore;
            _tray.Add(_emptyLabel);

            // The hint appears only once there are two things to put together. Shown before that
            // it is an instruction for something the player cannot do yet, which teaches them the
            // HUD talks to them about nothing.
            _hintLabel = Typography.Caption(hintText);
            _hintLabel.style.color = Theme.TextMuted;
            _hintLabel.style.marginLeft = Theme.Space8;
            _hintLabel.style.marginTop = Theme.Space8;
            _hintLabel.style.display = DisplayStyle.None;
            _hintLabel.pickingMode = PickingMode.Ignore;
            _tray.Add(_hintLabel);
        }

        /// <summary>
        /// Raised when the player taps two different items in a row.
        /// </summary>
        /// <remarks>
        /// The panel does not know or care whether the pair goes together. It reports the intent;
        /// validating it is the handler's job, and refusing it is the handler's answer.
        /// </remarks>
        public event Action<string, string> Combine;

        /// <summary>Replaces the tray's contents.</summary>
        /// <param name="items">Item ids paired with their already-localized names.</param>
        public void SetItems(IReadOnlyList<InventoryItemView> items)
        {
            ClearSelection();

            for (var i = 0; i < _chips.Count; i++)
            {
                _chips[i].Root.RemoveFromHierarchy();
            }

            _chips.Clear();

            var count = items != null ? items.Count : 0;
            _emptyLabel.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _hintLabel.style.display = count >= 2 ? DisplayStyle.Flex : DisplayStyle.None;

            for (var i = 0; i < count; i++)
            {
                var chip = new Chip(items[i].Id, items[i].Name, TapItem);
                _chips.Add(chip);
                _chipRow.Add(chip.Root);
            }

            // Opening on its own the first time something is picked up: the tray is the only place
            // the player ever sees what they are holding, and a tab that has to be discovered is a
            // puzzle nobody wrote. It stays open or closed by choice after that.
            if (count > 0 && !_open)
            {
                SetOpen(true);
            }
        }

        /// <summary>Shows or hides the whole panel.</summary>
        /// <param name="visible">False while paused, loading, or in a menu.</param>
        public void SetVisible(bool visible)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible)
            {
                ClearSelection();
            }
        }

        private void Toggle()
        {
            SetOpen(!_open);
        }

        private void SetOpen(bool open)
        {
            _open = open;
            _tray.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (!open)
            {
                ClearSelection();
            }
        }

        /// <summary>
        /// Registers a tap on a carried item: selects it, deselects it, or completes a pair.
        /// </summary>
        /// <remarks>
        /// Public because it is the panel's input entry point, not because a test needed a door.
        /// A chip's button routes straight here and does nothing else, which puts the selection
        /// rule — the part with the cancel in it, and the part that can get a player stuck — in one
        /// method that can be exercised without a panel, an event system or a frame.
        /// </remarks>
        /// <param name="id">The tapped item's id. Empty is ignored.</param>
        public void TapItem(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (_selectedId == null)
            {
                Select(id);
                return;
            }

            if (_selectedId == id)
            {
                // The cancel. Tapping the selected chip again puts it back down, which is the one
                // interaction a player reaches for first and the one most inventories do not have.
                ClearSelection();
                return;
            }

            var first = _selectedId;
            ClearSelection();

            var handler = Combine;
            if (handler != null)
            {
                handler(first, id);
            }
        }

        private void Select(string id)
        {
            _selectedId = id;
            for (var i = 0; i < _chips.Count; i++)
            {
                _chips[i].SetSelected(_chips[i].Id == id);
            }
        }

        private void ClearSelection()
        {
            _selectedId = null;
            for (var i = 0; i < _chips.Count; i++)
            {
                _chips[i].SetSelected(false);
            }
        }

        /// <summary>One carried item, as the tray draws it.</summary>
        private sealed class Chip
        {
            private readonly Button _button;

            internal Chip(string id, string name, Action<string> onTapped)
            {
                Id = id;

                _button = Buttons.Secondary(name, () => onTapped(id));
                _button.style.marginLeft = Theme.Space8;
                _button.style.marginTop = Theme.Space8;

                // 48dp again. A chip is a small word on a dark strip; the target it offers the
                // thumb is not allowed to be that small.
                _button.style.minHeight = 48f;
            }

            internal string Id { get; private set; }

            internal VisualElement Root
            {
                get { return _button; }
            }

            internal void SetSelected(bool selected)
            {
                // Border rather than a fill change: the chip has to stay readable while selected,
                // and the tray's background is already translucent over a moving world.
                // 1 dp when not selected, not 0: Buttons.Secondary draws a 1 dp outline, and
                // zeroing it here meant a chip lost its edge the first time it was deselected.
                _button.style.borderLeftWidth = selected ? 2f : 1f;
                _button.style.borderRightWidth = selected ? 2f : 1f;
                _button.style.borderTopWidth = selected ? 2f : 1f;
                _button.style.borderBottomWidth = selected ? 2f : 1f;

                var color = selected ? Theme.Gold : Theme.Border;
                _button.style.borderLeftColor = color;
                _button.style.borderRightColor = color;
                _button.style.borderTopColor = color;
                _button.style.borderBottomColor = color;
            }
        }
    }

    /// <summary>One row of the inventory tray: an opaque id and the text to draw for it.</summary>
    /// <remarks>
    /// The id travels through the UI without the UI ever interpreting it. The panel needs something
    /// to hand back when a chip is tapped, and the alternative — handing back the display name and
    /// having the controller map text to items — breaks the moment two items share a name or the
    /// game is translated.
    /// </remarks>
    public readonly struct InventoryItemView
    {
        /// <summary>The item's id, carried through unchanged.</summary>
        public readonly string Id;

        /// <summary>Already-localized display name.</summary>
        public readonly string Name;

        /// <param name="id">The item's id.</param>
        /// <param name="name">Already-localized display name.</param>
        public InventoryItemView(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}

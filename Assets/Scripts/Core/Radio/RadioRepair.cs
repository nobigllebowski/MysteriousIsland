using System;
using ForgottenIsle.Core.Items;

namespace ForgottenIsle.Core.Radio
{
    /// <summary>The three things wrong with the set.</summary>
    [Flags]
    public enum RadioFault : byte
    {
        /// <summary>Nothing wrong. The set works.</summary>
        None = 0,

        /// <summary>Battery bay empty and clean — emptied deliberately, for storage.</summary>
        Power = 1,

        /// <summary>Green bloom on the spring terminals. Not dirt: a bad connection.</summary>
        Contacts = 2,

        /// <summary>Fuse holder empty. Somebody removed a dead one and never had a spare.</summary>
        Fuse = 4,

        /// <summary>All three.</summary>
        All = Power | Contacts | Fuse
    }

    /// <summary>
    /// "Three things wrong with it, and you have three things."
    /// </summary>
    /// <remarks>
    /// Diagnostic repair, the prologue's dress rehearsal for the Fold Camp power restoration: look
    /// at an object, find what is wrong, fix what is wrong. No minigame, no timing bar. The
    /// pleasure is in the diagnosis, so this class does not tell the player what is wrong — the
    /// inspection lines do, one fault at a time, and the fixes can be made in any order.
    /// <para>
    /// The one real decision in the prologue lives here and is never flagged as one: the set can be
    /// powered from the dead torch's cells or from the field recorder's own, and the second choice
    /// costs the recorder its nine per cent. <see cref="UsedRecorderCells"/> remembers which, and a
    /// later beat reads it.
    /// </para>
    /// </remarks>
    public sealed class RadioRepair
    {
        private RadioFault _outstanding = RadioFault.All;
        private bool _usedRecorderCells;

        /// <summary>Faults still present.</summary>
        public RadioFault Outstanding => _outstanding;

        /// <summary>True once every fault is cleared.</summary>
        public bool IsWorking => _outstanding == RadioFault.None;

        /// <summary>True when the recorder's cells went into the set. The unflagged decision.</summary>
        public bool UsedRecorderCells => _usedRecorderCells;

        /// <summary>
        /// True when the fuse was bypassed with copper stripped from the hand-mic's cord rather
        /// than the torch's spring. The design's other valid answer (§4.2).
        /// </summary>
        public bool FuseFromCord => _fuseFromCord;

        private bool _fuseFromCord;

        /// <summary>Which fault an item addresses, if any.</summary>
        /// <remarks>
        /// The table is here rather than on the items because it is the radio that knows what fits
        /// it — the same rule <c>Mechanism</c> follows. A fault can have more than one answer; the
        /// fuse takes any conductor, and the torch's copper spring is the one most players find.
        /// </remarks>
        public static RadioFault FaultAddressedBy(string itemId)
        {
            switch (itemId)
            {
                case ItemIds.DeadTorch:
                case ItemIds.FieldRecorder:
                    return RadioFault.Power;
                case ItemIds.Multitool:
                    return RadioFault.Contacts;
                case ItemIds.CopperSpring:
                    return RadioFault.Fuse;
                default:
                    return RadioFault.None;
            }
        }

        /// <summary>
        /// Which fault the item would address on THIS set, given what is still wrong with it.
        /// </summary>
        /// <remarks>
        /// The multitool answers two faults in order: the scraper cleans the contacts, and once
        /// they are clean the blade strips a loop of the mic cord for the fuse. So the same tool
        /// offered twice does two different things, and the second is the alternate fuse fix a
        /// player without the spring can still find.
        /// </remarks>
        public RadioFault FaultFor(string itemId)
        {
            if (itemId == ItemIds.Multitool && (_outstanding & RadioFault.Contacts) == 0 && (_outstanding & RadioFault.Fuse) != 0)
            {
                return RadioFault.Fuse;
            }

            var fault = FaultAddressedBy(itemId);
            return (_outstanding & fault) != 0 ? fault : RadioFault.None;
        }

        /// <summary>Whether the item is spent by the repair.</summary>
        /// <remarks>
        /// The torch is taken apart for its cells, and stays apart. The spring is bent across the
        /// fuse holder and stays there. The recorder keeps its body and loses its charge — it
        /// remains carried, dead. The multitool is a tool.
        /// </remarks>
        public static bool ConsumesItem(string itemId)
        {
            return itemId == ItemIds.DeadTorch || itemId == ItemIds.CopperSpring;
        }

        /// <summary>True once the torch has been taken apart. The nail row does not grow a new one.</summary>
        public bool TorchTakenApart => _torchTakenApart;

        private bool _torchTakenApart;

        /// <summary>
        /// Applies an item to the set.
        /// </summary>
        /// <param name="itemId">The carried item.</param>
        /// <param name="cleared">The fault it fixed, or <see cref="RadioFault.None"/>.</param>
        /// <returns>True when a fault was cleared by this call.</returns>
        public bool TryApply(string itemId, out RadioFault cleared)
        {
            cleared = RadioFault.None;
            var fault = FaultFor(itemId);
            if (fault == RadioFault.None)
            {
                return false;
            }

            _outstanding &= ~fault;
            cleared = fault;

            if (itemId == ItemIds.FieldRecorder)
            {
                _usedRecorderCells = true;
            }

            if (itemId == ItemIds.Multitool && fault == RadioFault.Fuse)
            {
                _fuseFromCord = true;
            }

            if (itemId == ItemIds.DeadTorch)
            {
                _torchTakenApart = true;
            }

            return true;
        }

        /// <summary>The first outstanding fault, in the order a person opening the set would meet them.</summary>
        public RadioFault FirstOutstanding()
        {
            if ((_outstanding & RadioFault.Power) != 0)
            {
                return RadioFault.Power;
            }

            if ((_outstanding & RadioFault.Contacts) != 0)
            {
                return RadioFault.Contacts;
            }

            if ((_outstanding & RadioFault.Fuse) != 0)
            {
                return RadioFault.Fuse;
            }

            return RadioFault.None;
        }

        /// <summary>Puts every fault back. A new run.</summary>
        public void Reset()
        {
            _outstanding = RadioFault.All;
            _usedRecorderCells = false;
            _torchTakenApart = false;
            _fuseFromCord = false;
        }

        /// <summary>Packs the state for a save. Three bits of faults, three flags.</summary>
        public int Capture()
        {
            return (int)_outstanding | (_usedRecorderCells ? 8 : 0) | (_torchTakenApart ? 16 : 0) | (_fuseFromCord ? 32 : 0);
        }

        /// <summary>Restores from <see cref="Capture"/>. A save without the cord bit reads as the spring.</summary>
        public void Restore(int packed)
        {
            _outstanding = (RadioFault)(packed & (int)RadioFault.All);
            _usedRecorderCells = (packed & 8) != 0;
            _torchTakenApart = (packed & 16) != 0;
            _fuseFromCord = (packed & 32) != 0;
        }
    }
}

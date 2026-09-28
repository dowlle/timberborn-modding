using System;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Holds at most one of the game's input blocks for one text field (#12).
    /// Focus events can repeat or arrive unpaired (the panel is hidden while a field
    /// has focus, the scene unloads), and the game's InputBlocker is a plain counter,
    /// so the latch keeps every Block matched by exactly one Unblock.
    /// </summary>
    public sealed class FocusInputLatch
    {
        private readonly Action _block;
        private readonly Action _unblock;

        public bool Holding { get; private set; }

        public FocusInputLatch(Action block, Action unblock)
        {
            _block = block ?? throw new ArgumentNullException(nameof(block));
            _unblock = unblock ?? throw new ArgumentNullException(nameof(unblock));
        }

        /// <summary>Field gained focus: block the game's hotkeys once.</summary>
        public void Acquire()
        {
            if (Holding) return;
            Holding = true;
            _block();
        }

        /// <summary>Field lost focus or went away: release the block if this latch holds it.</summary>
        public void Release()
        {
            if (!Holding) return;
            Holding = false;
            _unblock();
        }
    }
}

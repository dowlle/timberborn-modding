using System.Collections.Generic;
using Timberborn.InputSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Stops Timberborn's hotkeys (pause, speed, tools, camera, Escape) while an
    /// Archipelago text field has focus (#12).
    ///
    /// Uses the game's own mechanism: InputService skips every input processor while
    /// InputBlocker is blocked, and the game blocks it for its own text fields in
    /// TextElementInitializer. That initializer listens on the field's first
    /// TextElement, which for a labelled TextField is the label, so it never fires
    /// for our Host/Port/Slot/Password fields. Here the focus events are taken on the
    /// field itself (they bubble up from the inner text input).
    ///
    /// Escape and Enter leave the field. The game's UI confirm and cancel keys are
    /// flushed then, so the same key press does not also close the panel.
    /// </summary>
    internal sealed class ApTextInputGuard
    {
        private readonly InputBlocker _inputBlocker;
        private readonly InputService _inputService;
        private readonly List<FocusInputLatch> _latches = new();

        public ApTextInputGuard(InputBlocker inputBlocker, InputService inputService)
        {
            _inputBlocker = inputBlocker;
            _inputService = inputService;
        }

        public void Attach(TextField field)
        {
            if (field == null) return;
            var latch = new FocusInputLatch(_inputBlocker.Block, _inputBlocker.Unblock);
            _latches.Add(latch);
            field.RegisterCallback<FocusInEvent>(_ => latch.Acquire());
            field.RegisterCallback<FocusOutEvent>(_ => latch.Release());
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape && evt.keyCode != KeyCode.Return
                    && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                LeaveField(field, evt.target as Focusable);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Takes focus away from a field inside <paramref name="root"/>, if any.</summary>
        public void BlurWithin(VisualElement root)
        {
            var focused = root?.focusController?.focusedElement as VisualElement;
            if (focused != null && (focused == root || root.Contains(focused)))
                focused.Blur();
        }

        /// <summary>Releases every block still held, e.g. when the panel hides or unloads.</summary>
        public void ReleaseAll()
        {
            foreach (var latch in _latches)
                latch.Release();
        }

        private void LeaveField(TextField field, Focusable target)
        {
            target?.Blur();
            field.Blur();
            _inputService.FlushUIInput();
        }
    }
}

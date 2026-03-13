using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Manipulators
{
    /// <summary>
    /// Handles click-to-select (single bar) and left-drag to rubber-band select
    /// a range of bars.  Uses pointer capture so the selection rectangle tracks
    /// correctly when the cursor leaves the element bounds.
    ///
    /// Alt+click / Alt+drag adds to the existing selection.
    /// </summary>
    internal sealed class SelectionManipulator : PointerManipulator
    {
        private readonly BarGraphElement _chart;

        private Vector2 _dragStart;
        private bool    _dragging;
        private bool    _additive;       // Alt held at drag start
        private int     _clickedBar;     // bar hit at PointerDown (-1 = none)

        internal SelectionManipulator(BarGraphElement chart)
        {
            _chart = chart;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.NoTrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerUpEvent>(OnUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;
            if (!_chart.Settings.EnableSelection) return;

            // Alt+left is reserved for PanManipulator.
            if (evt.altKey) return;

            _dragging    = false;
            _additive    = evt.ctrlKey;
            // evt.localPosition is Vector3 in Unity 6; cast to Vector2.
            _dragStart   = (Vector2)evt.localPosition;
            _clickedBar  = _chart.HitTestBar((Vector2)evt.localPosition);

            target.CapturePointer(evt.pointerId);
            target.Focus();
            evt.StopPropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (!target.HasPointerCapture(evt.pointerId)) return;

            // Cast Vector3 → Vector2 before distance check and rect building.
            Vector2 localPos = (Vector2)evt.localPosition;
            if (!_dragging && Vector2.Distance(localPos, _dragStart) > 3f)
                _dragging = true;

            if (_dragging)
            {
                var rect = MakeRect(_dragStart, localPos);
                _chart.InternalUpdateDragRect(rect);
                evt.StopPropagation();
            }
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (!target.HasPointerCapture(evt.pointerId)) return;

            Vector2 localPos = (Vector2)evt.localPosition;
            if (_dragging)
            {
                var finalRect = MakeRect(_dragStart, localPos);
                _chart.InternalCommitDragSelection(finalRect, _additive);
            }
            else
            {
                _chart.InternalSelectBar(_clickedBar, _additive);
            }

            _dragging = false;
            target.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            // Pointer capture lost externally – cancel drag
            if (_dragging)
                _chart.InternalCancelDrag();
            _dragging = false;
        }

        private static Rect MakeRect(Vector2 a, Vector2 b) => new Rect(
            Mathf.Min(a.x, b.x),
            Mathf.Min(a.y, b.y),
            Mathf.Abs(b.x - a.x),
            Mathf.Abs(b.y - a.y));
    }
}

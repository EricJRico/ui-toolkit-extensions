using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Manipulators
{
    /// <summary>
    /// Horizontal (and optionally vertical) panning via middle-click drag
    /// or Alt+left-drag.  Accumulates a fractional data-space offset so
    /// sub-pixel smooth scrolling is preserved end-to-end.
    /// </summary>
    internal sealed class PanManipulator : PointerManipulator
    {
        private readonly BarGraphElement _chart;
        private Vector2 _dragStart;
        private float   _startPanX;
        private float   _startPanY;

        internal PanManipulator(BarGraphElement chart)
        {
            _chart = chart;
            // Middle-click pan
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.MiddleMouse });
            // Alt + left-click pan (doesn't conflict with SelectionManipulator which checks Alt)
            activators.Add(new ManipulatorActivationFilter
            {
                button    = MouseButton.LeftMouse,
                modifiers = EventModifiers.Alt
            });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerUpEvent>(OnUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;
            if (!_chart.Settings.EnableMousePan) return;

            // evt.localPosition is Vector3 in Unity 6; store as Vector2.
            _dragStart = (Vector2)evt.localPosition;
            _startPanX = _chart.ViewState.PanX;
            _startPanY = _chart.ViewState.PanY;
            target.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (!target.HasPointerCapture(evt.pointerId)) return;

            // evt.localPosition is Vector3 in Unity 6; cast before arithmetic.
            Vector2 delta = (Vector2)evt.localPosition - _dragStart;

            // Convert pixel delta to data-space offset.
            // barStride = barWidth + gap; ZoomX scales everything.
            float barStride  = _chart.GetBarStride();
            float newPanX    = _startPanX - delta.x / barStride;

            float newPanY    = _startPanY;
            if (_chart.Settings.EnableYPan)
            {
                float plotH  = _chart.GetPlotHeight();
                newPanY      = _startPanY + delta.y / Mathf.Max(1f, plotH);
            }

            _chart.InternalSetPan(newPanX, newPanY);
            evt.StopPropagation();
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (target.HasPointerCapture(evt.pointerId))
                target.ReleasePointer(evt.pointerId);
        }
    }
}

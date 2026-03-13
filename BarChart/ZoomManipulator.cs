using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Manipulators
{
    /// <summary>
    /// Mouse-wheel zoom anchored to the cursor position so the bar under the
    /// cursor remains stationary.
    ///
    ///   • Scroll          → zoom X axis  (scroll up = zoom in, scroll down = zoom out)
    ///   • Ctrl + Scroll   → zoom Y axis  (Ctrl on Windows/Linux, ⌘ Command on macOS)
    /// </summary>
    internal sealed class ZoomManipulator : Manipulator
    {
        private readonly BarGraphElement _chart;

        internal ZoomManipulator(BarGraphElement chart) => _chart = chart;

        protected override void RegisterCallbacksOnTarget()
            => target.RegisterCallback<WheelEvent>(OnWheel);

        protected override void UnregisterCallbacksFromTarget()
            => target.UnregisterCallback<WheelEvent>(OnWheel);

        private void OnWheel(WheelEvent evt)
        {
            var s  = _chart.Settings;
            var vs = _chart.ViewState;

            // WheelEvent.delta.y is positive scrolling DOWN.
            // Scroll up (delta < 0) = zoom in  → factor > 1
            // Scroll down (delta > 0) = zoom out → factor < 1
            float factor = evt.delta.y > 0f ? 1f - s.ZoomSpeed : 1f + s.ZoomSpeed;

            if (evt.ctrlKey && s.EnableMouseZoomY)
            {
                // Y-axis zoom — Ctrl+Scroll (⌘+Scroll on macOS)
                float newZoom = Mathf.Clamp(vs.ZoomY * factor, vs.MinZoomY, vs.MaxZoomY);

                // Anchor the data value under the cursor so it stays stationary.
                //
                // The value at screen fraction t (0=bottom, 1=top of plot) is:
                //   V = maxY * (PanY + t) / ZoomY
                //
                // Keeping V constant before/after zoom:
                //   (PanY + t) / ZoomY = (newPanY + t) / newZoom
                //   newPanY = newZoom * (PanY + t) / ZoomY - t
                float plotH = _chart.GetPlotHeight();
                float plotY2 = _chart.contentRect.height - _chart.Settings.PaddingBottom;
                float t = Mathf.Clamp01((plotY2 - evt.localMousePosition.y) / Mathf.Max(1f, plotH));
                float newPanY = newZoom * (vs.PanY + t) / vs.ZoomY - t;

                _chart.InternalSetZoom(vs.ZoomX, newZoom, vs.PanX, newPanY);
            }
            else if (!evt.ctrlKey && s.EnableMouseZoomX)
            {
                // X-axis zoom — plain Scroll
                float newZoom = Mathf.Clamp(vs.ZoomX * factor, vs.MinZoomX, vs.MaxZoomX);

                // Anchor the bar column under the cursor so it stays stationary.
                float barStride = _chart.GetBarStrideBase();   // px per bar at zoom = 1
                float localX    = evt.localMousePosition.x - _chart.Settings.PaddingLeft;
                float dataX     = vs.PanX + localX / (barStride * vs.ZoomX);
                float newPanX   = dataX - localX / (barStride * newZoom);

                _chart.InternalSetZoom(newZoom, vs.ZoomY, newPanX, vs.PanY);
            }

            evt.StopPropagation();
        }
    }
}

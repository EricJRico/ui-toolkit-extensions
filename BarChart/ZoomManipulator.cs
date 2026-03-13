using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Manipulators
{
    /// <summary>
    /// Mouse-wheel zoom anchored to the cursor position so the bar under the
    /// cursor remains stationary.
    ///
    ///   • Scroll         → zoom X axis
    ///   • Shift + Scroll → zoom Y axis (if <see cref="BarGraphSettings.EnableMouseZoomY"/> is true)
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
            var   s     = _chart.Settings;
            var   vs    = _chart.ViewState;
            float delta = evt.delta.y;

            if (evt.shiftKey && s.EnableMouseZoomY)
            {
                // Y-axis zoom
                float factor  = delta > 0f ? 1f + s.ZoomSpeed : 1f - s.ZoomSpeed;
                float newZoom = Mathf.Clamp(vs.ZoomY * factor, vs.MinZoomY, vs.MaxZoomY);

                // Anchor the value under the cursor
                float plotH   = _chart.GetPlotHeight();
                float localY  = _chart.contentRect.height - _chart.Settings.PaddingBottom - evt.localMousePosition.y;
                float normY   = Mathf.Clamp01(localY / Mathf.Max(1f, plotH));
                float dataY   = vs.PanY + normY / vs.ZoomY;
                float newPanY = dataY - normY / newZoom;

                _chart.InternalSetZoom(vs.ZoomX, newZoom, vs.PanX, newPanY);
            }
            else if (s.EnableMouseZoomX)
            {
                // X-axis zoom (default)
                float factor  = delta > 0f ? 1f + s.ZoomSpeed : 1f - s.ZoomSpeed;
                float newZoom = Mathf.Clamp(vs.ZoomX * factor, vs.MinZoomX, vs.MaxZoomX);

                // Anchor the bar column under the cursor
                float barStride  = _chart.GetBarStrideBase();     // in px at zoom=1
                float localX     = evt.localMousePosition.x - _chart.Settings.PaddingLeft;
                float dataX      = vs.PanX + localX / (barStride * vs.ZoomX);
                float newPanX    = dataX - localX / (barStride * newZoom);

                _chart.InternalSetZoom(newZoom, vs.ZoomY, newPanX, vs.PanY);
            }

            evt.StopPropagation();
        }
    }
}

using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class MousePanPanel : ControlsDemoPanel
    {
        public MousePanPanel() : base(
            "Mouse Pan",
            "Try: Middle-drag or Alt+Left-drag to pan around",
            new BarGraphSettings
            {
                EnableMouseZoomX = true,
                EnableMousePan   = true,
                EnableYPan       = true,
                EnableSelection  = false,
            })
        { }

        protected override void SetupHandlers()
        {
            Graph.AddHandler(new BarGraphZoomHandler());
            Graph.AddHandler(new BarGraphPanHandler());

            // Pre-zoom so there's room to pan
            Graph.RestoreViewSnapshot(new BarGraphViewSnapshot
            {
                ZoomX = 3f, ZoomY = 1f, PanX = 5f, PanY = 0f,
                FocusedBarIndex = -1, IsValid = true
            });
        }

        protected override void WireStatusEvents()
        {
            Graph.ViewChanged += _ =>
            {
                var vs = Graph.ViewState;
                SetStatus($"Pan X: {vs.PanX:F1}  Pan Y: {vs.PanY:F1}  Zoom: {vs.ZoomX:F1}x");
            };
        }
    }
}

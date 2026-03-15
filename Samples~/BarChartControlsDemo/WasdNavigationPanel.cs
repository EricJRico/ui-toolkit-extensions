using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class WasdNavigationPanel : ControlsDemoPanel
    {
        public WasdNavigationPanel() : base(
            "WASD Navigation",
            "Try: W/S to zoom in/out, A/D to pan left/right",
            new BarGraphSettings
            {
                EnableMouseZoomX = false,
                EnableMousePan   = false,
                EnableSelection  = false,
            })
        { }

        protected override void SetupHandlers()
        {
            Graph.AddHandler(new BarGraphKeyboardNavigationHandler());

            // Pre-zoom so A/D panning is immediately visible
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
                SetStatus($"Zoom: {vs.ZoomX:F1}x  Pan: {vs.PanX:F1}");
            };
        }
    }
}

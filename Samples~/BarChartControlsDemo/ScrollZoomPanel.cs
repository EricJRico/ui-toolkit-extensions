using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class ScrollZoomPanel : ControlsDemoPanel
    {
        public ScrollZoomPanel() : base(
            "Scroll Zoom",
            "Try: Scroll to zoom X, Ctrl+Scroll to zoom Y",
            new BarGraphSettings
            {
                EnableMouseZoomX = true,
                EnableMouseZoomY = true,
                EnableMousePan   = false,
                EnableSelection  = false,
            })
        { }

        protected override void SetupHandlers()
        {
            Graph.AddHandler(new BarGraphZoomHandler());
        }

        protected override void WireStatusEvents()
        {
            Graph.ViewChanged += _ =>
            {
                var vs = Graph.ViewState;
                SetStatus($"X Zoom: {vs.ZoomX:F1}x  Y Zoom: {vs.ZoomY:F1}x");
            };
        }
    }
}

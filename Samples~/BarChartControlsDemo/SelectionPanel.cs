using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class SelectionPanel : ControlsDemoPanel
    {
        public SelectionPanel() : base(
            "Click & Select",
            "Try: Click a bar, Ctrl+click to add, drag to rubber-band select",
            new BarGraphSettings
            {
                EnableMouseZoomX = false,
                EnableMousePan   = false,
                EnableSelection  = true,
            })
        { }

        protected override void SetupHandlers()
        {
            Graph.AddHandler(new BarGraphHoverHandler());
            Graph.AddHandler(new BarGraphSelectionHandler());
        }

        protected override void WireStatusEvents()
        {
            Graph.SelectionChanged += a =>
                SetStatus($"Selected: {a.SelectedDataIndices.Count} bars");

            Graph.BarClicked += a =>
                SetStatus($"Clicked bar {a.DataIndex} (val: {a.TotalValue:F0})");
        }
    }
}

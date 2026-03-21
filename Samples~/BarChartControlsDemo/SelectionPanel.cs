using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class SelectionPanel : ControlsDemoPanel
    {
        private BarGraphSelectionHandler _selHandler;

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
            Graph.AddToClassList("bar-graph--dim-selection");
            Graph.AddHandler(new BarGraphHoverHandler());
            _selHandler = new BarGraphSelectionHandler();
            Graph.AddHandler(_selHandler);
            Graph.AddHandler(new BarGraphKeyboardSelectionHandler());
        }

        protected override void WireStatusEvents()
        {
            Graph.SelectionChanged += a =>
                SetStatus($"Selected: {a.SelectedDataIndices.Count} bars");

            _selHandler.BarClicked += a =>
                SetStatus($"Clicked bar {a.DataIndex} (val: {a.TotalValue:F0})");
        }
    }
}

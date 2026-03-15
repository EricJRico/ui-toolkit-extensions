using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class KeyboardFocusPanel : ControlsDemoPanel
    {
        public KeyboardFocusPanel() : base(
            "Keyboard Focus",
            "Try: Click graph, then Arrow keys, Shift+Arrow to extend, Space to toggle",
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
            Graph.HoverChanged += a =>
            {
                if (a.DataIndex >= 0)
                    SetStatus($"Hover: bar {a.DataIndex}  Val: {a.TotalValue:F0}");
            };

            Graph.SelectionChanged += a =>
            {
                int focus = Graph.ViewState.FocusedBarIndex;
                string focusText = focus >= 0 ? $"Focus: bar {focus}" : "No focus";
                SetStatus($"{focusText}  Selected: {a.SelectedDataIndices.Count}");
            };
        }
    }
}

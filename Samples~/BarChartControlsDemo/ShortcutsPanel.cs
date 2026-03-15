using UnityEngine;
using BarGraph.Core;
using BarGraph.Input.Handlers;

namespace BarGraph.ControlsDemo
{
    sealed class ShortcutsPanel : ControlsDemoPanel
    {
        public ShortcutsPanel() : base(
            "Shortcuts",
            "Try: Ctrl+A (select all), Escape (clear), Ctrl+R (reset view)",
            new BarGraphSettings
            {
                EnableMouseZoomX = true,
                EnableMouseZoomY = true,
                EnableMousePan   = true,
                EnableYPan       = true,
                EnableSelection  = true,
                BarSpacingRatio  = 0.12f,
            })
        { }

        protected override void SetupHandlers()
        {
            Graph.AddHandler(new BarGraphHoverHandler());
            Graph.AddHandler(new BarGraphSelectionHandler());
            Graph.AddHandler(new BarGraphZoomHandler());
            Graph.AddHandler(new BarGraphPanHandler());
            Graph.AddHandler(new BarGraphKeyboardNavigationHandler());
        }

        protected override void WireStatusEvents()
        {
            Graph.SelectionChanged += a =>
                SetStatus($"Selected: {a.SelectedDataIndices.Count} bars");

            Graph.ViewChanged += _ =>
            {
                var vs = Graph.ViewState;
                bool isReset = Mathf.Approximately(vs.ZoomX, 1f) &&
                               Mathf.Approximately(vs.PanX, 0f);
                if (isReset)
                    SetStatus("View reset!");
            };
        }
    }
}

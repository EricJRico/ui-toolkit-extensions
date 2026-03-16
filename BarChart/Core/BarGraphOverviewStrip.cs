using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Events;

namespace BarGraph.Core
{
    /// <summary>
    /// Passive overview strip that shows the full dataset at zoom-1 with a viewport
    /// indicator rectangle reflecting the bound chart's current zoom/pan position.
    ///
    /// Usage
    /// ─────
    ///  1. Place above (or below) a <see cref="BarGraphElement"/> in a vertical layout.
    ///  2. Call <see cref="BindTo"/> to wire automatic viewport tracking.
    ///  3. Call the same <see cref="SetData"/> overload you use on the main chart.
    ///
    /// <code>
    ///   var overview = new BarGraphOverviewStrip();
    ///   overview.BindTo(mainChart);
    ///   container.Add(overview);
    ///   container.Add(mainChart);
    ///
    ///   mainChart.SetData(values, color);
    ///   overview.SetData(values, color);
    /// </code>
    /// </summary>
    public sealed class BarGraphOverviewStrip : VisualElement
    {
        // ── USS class names ──────────────────────────────────────────────────
        public const string UssClassName        = "bar-graph-overview-strip";
        public const string InnerChartClassName = "bar-graph--overview";
        public const string IndicatorClassName  = "bar-graph-overview-strip__indicator";

        // ── Custom style properties ──────────────────────────────────────────
        static readonly CustomStyleProperty<Color> s_indicatorFillColor
            = new("--overview-indicator-fill-color");
        static readonly CustomStyleProperty<Color> s_indicatorBorderColor
            = new("--overview-indicator-border-color");
        static readonly CustomStyleProperty<float> s_indicatorBorderWidth
            = new("--overview-indicator-border-width");

        // ── Resolved indicator visuals (cached) ──────────────────────────────
        private Color _indicatorFill        = new Color(1f, 1f, 1f, 0.12f);
        private Color _indicatorBorder      = new Color(0.4f, 0.7f, 1f, 0.7f);
        private float _indicatorBorderWidth = 1.5f;

        // ── Children ─────────────────────────────────────────────────────────
        private readonly BarGraphElement _innerChart;
        private readonly VisualElement   _indicator;

        // ── Bound source ─────────────────────────────────────────────────────
        private BarGraphElement _source;

        // ── Shared default stylesheet ────────────────────────────────────────
        private static StyleSheet s_defaultSheet;

        // ─────────────────────────────────────────────────────────────────────
        //  Construction
        // ─────────────────────────────────────────────────────────────────────

        public BarGraphOverviewStrip()
        {
            AddToClassList(UssClassName);

            if (s_defaultSheet == null)
                s_defaultSheet = Resources.Load<StyleSheet>("BarGraphOverviewStrip");
            if (s_defaultSheet != null)
                styleSheets.Add(s_defaultSheet);

            style.overflow = Overflow.Hidden;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;

            // ── Inner chart: full dataset, no chrome ─────────────────────────
            _innerChart = new BarGraphElement();
            _innerChart.AddToClassList(InnerChartClassName);
            _innerChart.style.position = Position.Absolute;
            _innerChart.style.left   = 0;
            _innerChart.style.top    = 0;
            _innerChart.style.right  = 0;
            _innerChart.style.bottom = 0;
            _innerChart.pickingMode  = PickingMode.Ignore;
            _innerChart.focusable    = false;
            // No SetInputSource, no AddHandler → purely visual
            _innerChart.UpdateSettings(new BarGraphSettings
            {
                ShowGrid         = false,
                ShowAxes         = false,
                EnableMouseZoomX = false,
                EnableMouseZoomY = false,
                EnableMousePan   = false,
                EnableSelection  = false,
                MaxXLabels       = 0,
                MaxYLabels       = 0,
            });
            Add(_innerChart);

            // ── Viewport indicator ───────────────────────────────────────────
            _indicator = new VisualElement();
            _indicator.AddToClassList(IndicatorClassName);
            _indicator.style.position = Position.Absolute;
            _indicator.style.top    = 0;
            _indicator.style.bottom = 0;
            _indicator.pickingMode  = PickingMode.Ignore;
            _indicator.style.display = DisplayStyle.None;
            ApplyIndicatorStyle();
            Add(_indicator);

            RegisterCallback<CustomStyleResolvedEvent>(_ => ResolveCustomStyles());
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_source != null)
                    SyncIndicator(_source.ViewState.ZoomX, _source.ViewState.PanX);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API – binding
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this overview strip to a main chart. Subscribes to
        /// <see cref="BarGraphElement.ViewChanged"/> for automatic viewport tracking.
        /// Call <see cref="Unbind"/> or bind to a different source to release.
        /// </summary>
        public void BindTo(BarGraphElement source)
        {
            Unbind();
            _source = source;
            if (_source == null) return;

            _source.ViewChanged += OnSourceViewChanged;
            SyncIndicator(_source.ViewState.ZoomX, _source.ViewState.PanX);
        }

        /// <summary>Detach from the currently bound chart.</summary>
        public void Unbind()
        {
            if (_source != null)
            {
                _source.ViewChanged -= OnSourceViewChanged;
                _source = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API – data  (forward to inner chart)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Full stacked-bar data.</summary>
        public void SetData(BarEntry[] bars, int barCount, BarSegment[] segments, int segCount)
            => _innerChart.SetData(bars, barCount, segments, segCount);

        /// <summary>Convenience: flat float values, single colour per bar.</summary>
        public void SetData(IList<float> values, Color? barColor = null)
            => _innerChart.SetData(values, barColor);

        /// <summary>Convenience: single-segment BarEntry list.</summary>
        public void SetData(IList<BarEntry> bars)
            => _innerChart.SetData(bars);

        /// <summary>Remove all bars and segments.</summary>
        public void ClearData() => _innerChart.ClearData();

        // ─────────────────────────────────────────────────────────────────────
        //  Viewport indicator sync
        // ─────────────────────────────────────────────────────────────────────

        private void OnSourceViewChanged(ViewChangedEventArgs args)
            => SyncIndicator(args.ZoomX, args.PanX);

        private void SyncIndicator(float zoomX, float panX)
        {
            bool visible = zoomX > 1f + float.Epsilon;
            _indicator.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;

            // Align to the inner chart's plot area, not the full strip width
            float padL  = _innerChart.VisPaddingLeft;
            float padR  = _innerChart.VisPaddingRight;
            float plotW = _innerChart.contentRect.width - padL - padR;
            if (plotW < 1f) return;

            int totalBars = _innerChart.BarCount;
            if (totalBars <= 0) return;

            // Same math as BarGraphScrollbarHandler.SyncX
            float visibleFraction = Mathf.Clamp01(1f / zoomX);
            float indicatorW     = plotW * visibleFraction;
            float scrollablePx   = plotW - indicatorW;
            float maxPanX        = totalBars * (1f - 1f / zoomX);
            float indicatorL     = padL + (maxPanX > 0f ? scrollablePx * (panX / maxPanX) : 0f);

            _indicator.style.width = indicatorW;
            _indicator.style.left  = indicatorL;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  USS custom style resolution
        // ─────────────────────────────────────────────────────────────────────

        private void ResolveCustomStyles()
        {
            var cs = customStyle;
            if (cs.TryGetValue(s_indicatorFillColor,   out var fill))   _indicatorFill = fill;
            if (cs.TryGetValue(s_indicatorBorderColor, out var border)) _indicatorBorder = border;
            if (cs.TryGetValue(s_indicatorBorderWidth, out var width))  _indicatorBorderWidth = width;
            ApplyIndicatorStyle();
        }

        private void ApplyIndicatorStyle()
        {
            _indicator.style.backgroundColor = _indicatorFill;

            _indicator.style.borderLeftColor   = _indicatorBorder;
            _indicator.style.borderRightColor  = _indicatorBorder;
            _indicator.style.borderTopColor    = _indicatorBorder;
            _indicator.style.borderBottomColor = _indicatorBorder;

            _indicator.style.borderLeftWidth   = _indicatorBorderWidth;
            _indicator.style.borderRightWidth  = _indicatorBorderWidth;
            _indicator.style.borderTopWidth    = _indicatorBorderWidth;
            _indicator.style.borderBottomWidth = _indicatorBorderWidth;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Manipulators;

namespace BarGraph
{
    /// <summary>
    /// High-performance stacked vertical bar graph for Unity UI Toolkit.
    ///
    /// ── Performance model ────────────────────────────────────────────────────
    ///  • All geometry is drawn via <c>Painter2D</c>.  No per-bar VisualElements.
    ///  • Bars sharing the same <see cref="Color32"/> are batched into a SINGLE
    ///    <c>BeginPath…Fill</c> call using struct-of-arrays <see cref="RectBatch"/>.
    ///    Draw-call count = O(unique colours across all visible segments).
    ///  • LOD path: when bar slot width &lt; 1 screen pixel the renderer switches
    ///    to pixel-column mode — render cost is bounded by panel width, not bar count.
    ///  • Backing arrays grow (double) but never shrink → zero GC in steady state.
    ///  • Label pool: fixed-size <see cref="Label"/> pool repositioned each repaint.
    ///  • Sort index is rebuilt lazily only when <see cref="ChartViewState.SortDirty"/> is set.
    ///
    /// ── Minimal usage ────────────────────────────────────────────────────────
    ///   var graph = new BarGraphElement();
    ///   graph.style.flexGrow = 1;
    ///   graph.SetData(new float[] { 12, 45, 7, 88, 33 });
    ///   rootElement.Add(graph);
    /// </summary>
    // [UxmlElement] is the Unity 6 replacement for the UxmlFactory/UxmlTraits pattern.
    // The old nested-class approach still compiles but emits CS0618 obsolete warnings;
    // using the attribute silences them and is forward-compatible.
    [UxmlElement]
    public sealed partial class BarGraphElement : VisualElement
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Core state
        // ─────────────────────────────────────────────────────────────────────

        private readonly ChartDataModel  _model    = new ChartDataModel();
        private readonly ChartViewState  _viewState = new ChartViewState();
        private          BarGraphSettings _settings  = new BarGraphSettings();

        // ── Formatters (pluggable, non-null defaulted) ────────────────────────
        private Func<float, string> _yFormatter = DefaultYFormatter;
        private Func<int,   string> _xFormatter = DefaultXFormatter;

        private static readonly Func<float, string> DefaultYFormatter = v =>
            Mathf.Abs(v) >= 1_000_000f ? $"{v / 1_000_000f:0.#}M" :
            Mathf.Abs(v) >= 1_000f     ? $"{v / 1_000f:0.#}K"      :
            Mathf.Abs(v) >= 100f       ? $"{v:0}"                   :
            Mathf.Abs(v) >= 10f        ? $"{v:0.#}"                 :
                                          $"{v:0.##}";

        private static readonly Func<int, string> DefaultXFormatter = i => i.ToString();

        // ── Derived bounds (recomputed after each data change) ────────────────
        private float _effectiveMaxY = 1f;

        // ─────────────────────────────────────────────────────────────────────
        //  Per-frame zero-GC render buffers
        // ─────────────────────────────────────────────────────────────────────

        // Per-colour rect batches (key = Color32 avoids float precision issues)
        private readonly Dictionary<Color32, RectBatch> _batches    = new Dictionary<Color32, RectBatch>(64);
        private readonly List<Color32>                  _batchOrder = new List<Color32>(64);

        // LOD pixel buffer (grows, never shrinks)
        private LodPixel[] _lodBuf = new LodPixel[2048];

        // ─────────────────────────────────────────────────────────────────────
        //  Label overlay pool
        // ─────────────────────────────────────────────────────────────────────

        private readonly VisualElement _labelRoot;
        private readonly List<Label>   _yLabels = new List<Label>();
        private readonly List<Label>   _xLabels = new List<Label>();

        // Cached plot-area coords written by OnGenerateVisualContent so that
        // PositionLabels can be called from the scheduled callback (outside the
        // repaint pass) rather than from inside generateVisualContent.
        private float _lblPlotX, _lblPlotW, _lblPlotH, _lblPlotY2;
        private bool  _labelsDirty;

        // ─────────────────────────────────────────────────────────────────────
        //  Public events (Action<T> → zero-alloc dispatch with stack-only args)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Fired when a bar is clicked or activated via keyboard.</summary>
        public event Action<BarClickedEventArgs>     BarClicked;

        /// <summary>Fired whenever the selection set changes.</summary>
        public event Action<SelectionChangedEventArgs> SelectionChanged;

        /// <summary>Fired when a rubber-band drag-select gesture completes.</summary>
        public event Action<DragCompletedEventArgs>  DragCompleted;

        /// <summary>Fired when the bar under the cursor changes (or cursor leaves).</summary>
        public event Action<HoverChangedEventArgs>   HoverChanged;

        /// <summary>Fired after zoom or pan changes.</summary>
        public event Action<ViewChangedEventArgs>    ViewChanged;

        // ─────────────────────────────────────────────────────────────────────
        //  Construction
        // ─────────────────────────────────────────────────────────────────────

        public BarGraphElement()
        {
            name           = "bar-graph";
            style.overflow = Overflow.Hidden;
            pickingMode    = PickingMode.Position;
            focusable      = true;
            tabIndex       = 0;

            // Invisible label layer on top of the canvas
            _labelRoot = new VisualElement { name = "bar-graph__labels", pickingMode = PickingMode.Ignore };
            _labelRoot.style.position = Position.Absolute;
            _labelRoot.style.left     = 0;
            _labelRoot.style.top      = 0;
            _labelRoot.style.right    = 0;
            _labelRoot.style.bottom   = 0;
            Add(_labelRoot);

            generateVisualContent += OnGenerateVisualContent;

            // Layout
            RegisterCallback<GeometryChangedEvent>(_ => { RebuildLabelPool(); MarkDirtyRepaint(); });

            // Start the label-position scheduler only once the element is attached
            // to a panel (schedule is unavailable before attachment).
            // Labels must be positioned OUTSIDE generateVisualContent — setting
            // style properties on child VisualElements from within a repaint
            // callback invalidates layout mid-pass and can cause repaint loops.
            RegisterCallback<AttachToPanelEvent>(_ =>
                schedule.Execute(FlushLabelPositions).Every(0));

            // Interaction manipulators (registered in priority order)
            this.AddManipulator(new HoverManipulator(this));
            this.AddManipulator(new SelectionManipulator(this));
            this.AddManipulator(new PanManipulator(this));
            this.AddManipulator(new ZoomManipulator(this));

            // Keyboard
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            // Data change → mark sort dirty and repaint
            _model.DataChanged += OnDataChanged;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public properties
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Read-only access to the live view state (for external inspection).</summary>
        public ChartViewState ViewState => _viewState;

        /// <summary>Current settings. Use <see cref="UpdateSettings"/> to apply changes.</summary>
        public BarGraphSettings Settings => _settings;

        /// <summary>
        /// Pluggable Y-axis label formatter.
        /// Signature: <c>float value → string label</c>.
        /// Defaults to SI-suffix formatting (1.2K, 3.4M, etc.).
        /// </summary>
        public Func<float, string> FormatYLabel
        {
            get => _yFormatter;
            set { _yFormatter = value ?? DefaultYFormatter; MarkDirtyRepaint(); }
        }

        /// <summary>
        /// Pluggable X-axis label formatter.
        /// Signature: <c>int barDataIndex → string label</c>.
        /// Defaults to the bar's own <see cref="BarEntry.Label"/> or the index.
        /// </summary>
        public Func<int, string> FormatXLabel
        {
            get => _xFormatter;
            set { _xFormatter = value ?? DefaultXFormatter; MarkDirtyRepaint(); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API – data
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Full stacked-bar data.  Both arrays are copied into pre-allocated
        /// internal storage – no heap allocation after warm-up.
        /// </summary>
        public void SetData(BarEntry[] bars, int barCount, BarSegment[] segments, int segCount,
                            BarGraphSettings settings = null)
        {
            if (settings != null) _settings = settings;
            _model.SetData(bars, barCount, segments, segCount);
            // DataChanged fires → OnDataChanged()
        }

        /// <summary>Convenience: flat float values, single colour per bar.</summary>
        public void SetData(IList<float> values, Color? barColor = null,
                            BarGraphSettings settings = null)
        {
            if (settings != null) _settings = settings;
            Color32 c = barColor.HasValue ? (Color32)barColor.Value : default;
            _model.SetData(values, c);
        }

        /// <summary>Convenience: single-segment BarEntry list.</summary>
        public void SetData(IList<BarEntry> bars, BarGraphSettings settings = null)
        {
            if (settings != null) _settings = settings;
            _model.SetData(bars);
        }

        /// <summary>Set the secondary comparison overlay dataset (flat floats).</summary>
        public void SetOverlay(IList<float> values, Color? overlayColor = null)
        {
            Color32 c = overlayColor.HasValue ? (Color32)overlayColor.Value : (Color32)_settings.OverlayTint;
            _model.SetOverlay(values, c);
        }

        /// <summary>Remove the overlay series.</summary>
        public void ClearOverlay() => _model.ClearOverlay();

        /// <summary>Append a single bar without rebuilding the dataset.</summary>
        public void AppendBar(float value, Color? color = null, string label = null)
        {
            Color32 c = color.HasValue ? (Color32)color.Value : default;
            _model.AppendBar(value, c, label);
        }

        /// <summary>Remove all bars and segments.</summary>
        public void ClearData() => _model.Clear();

        // ─────────────────────────────────────────────────────────────────────
        //  Public API – settings / sort / view
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Apply new visual / behaviour settings without touching data.</summary>
        public void UpdateSettings(BarGraphSettings s)
        {
            _settings = s ?? throw new ArgumentNullException(nameof(s));
            RecalcBounds();
            RebuildLabelPool();
            MarkDirtyRepaint();
        }

        /// <summary>Change the display sort order.</summary>
        public void SetSortMode(SortMode mode, bool descending = true)
        {
            _viewState.SortMode       = mode;
            _viewState.SortDescending = descending;
            _viewState.SortDirty      = true;
            MarkDirtyRepaint();
        }

        /// <summary>Reset zoom, pan, and sort to defaults.</summary>
        public void ResetView()
        {
            _viewState.ResetZoomPan();
            NotifyViewChanged();
            MarkDirtyRepaint();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Internal geometry helpers (used by manipulators)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Bar stride (slot width) in pixels at current zoom, including spacing.</summary>
        internal float GetBarStride()
        {
            float plotW = GetPlotWidth();
            int   total = _model.BarCount;
            if (total <= 0) return plotW;
            // At ZoomX=1 all bars fit in plotW; each bar takes plotW/total
            return plotW / total * _viewState.ZoomX;
        }

        /// <summary>Bar stride at zoom = 1 (used by ZoomManipulator for anchor maths).</summary>
        internal float GetBarStrideBase()
        {
            float plotW = GetPlotWidth();
            int   total = _model.BarCount;
            return total > 0 ? plotW / total : plotW;
        }

        internal float GetPlotWidth()  =>
            Mathf.Max(1f, contentRect.width  - _settings.PaddingLeft - _settings.PaddingRight);

        internal float GetPlotHeight() =>
            Mathf.Max(1f, contentRect.height - _settings.PaddingTop  - _settings.PaddingBottom);

        // ─────────────────────────────────────────────────────────────────────
        //  Internal state mutators (called by manipulators; each dirty-repaints)
        // ─────────────────────────────────────────────────────────────────────

        internal void InternalSetHover(int dataIndex)
        {
            if (_viewState.HoveredBarIndex == dataIndex) return;
            _viewState.HoveredBarIndex = dataIndex;
            float val = (dataIndex >= 0 && dataIndex < _model.BarCount)
                ? _model.Bars[dataIndex].TotalValue : 0f;
            HoverChanged?.Invoke(new HoverChangedEventArgs(dataIndex, val));
            MarkDirtyRepaint();
        }

        internal void InternalSetPan(float panX, float panY)
        {
            _viewState.PanX = panX;
            _viewState.PanY = panY;
            ClampViewState();
            NotifyViewChanged();
            MarkDirtyRepaint();
        }

        internal void InternalSetZoom(float zoomX, float zoomY, float panX, float panY)
        {
            _viewState.ZoomX = Mathf.Clamp(zoomX, _viewState.MinZoomX, _viewState.MaxZoomX);
            _viewState.ZoomY = Mathf.Clamp(zoomY, _viewState.MinZoomY, _viewState.MaxZoomY);
            _viewState.PanX  = panX;
            _viewState.PanY  = panY;
            ClampViewState();
            NotifyViewChanged();
            MarkDirtyRepaint();
        }

        internal void InternalSelectBar(int dataIndex, bool additive)
        {
            if (!_settings.EnableSelection) return;
            if (!additive) _viewState.SelectedBars.Clear();
            if (dataIndex >= 0)
            {
                if (!_viewState.SelectedBars.Add(dataIndex))
                    _viewState.SelectedBars.Remove(dataIndex); // toggle on re-click
                _viewState.FocusedBarIndex = dataIndex;
            }
            FireSelectionChanged();
            MarkDirtyRepaint();

            if (dataIndex >= 0)
            {
                int displayIdx = dataIndex < _viewState.DataToDisplay.Length
                    ? _viewState.DataToDisplay[dataIndex] : dataIndex;
                float val = _model.Bars[dataIndex].TotalValue;
                BarClicked?.Invoke(new BarClickedEventArgs(dataIndex, displayIdx, val, Vector2.zero));

                using var uiEvt = BarClickedUIEvent.GetPooled(dataIndex, val);
                uiEvt.target = this;
                SendEvent(uiEvt);
            }
        }

        internal void InternalUpdateDragRect(Rect rect)
        {
            _viewState.DragRect          = rect;
            _viewState.IsDragSelecting   = true;
            MarkDirtyRepaint();
        }

        internal void InternalCommitDragSelection(Rect rect, bool additive)
        {
            _viewState.IsDragSelecting = false;
            _viewState.DragRect        = Rect.zero;

            if (!additive) _viewState.SelectedBars.Clear();

            // Select all bars whose rendered rect intersects the drag rect
            CollectBarsInRect(rect, _viewState.SelectedBars);
            FireSelectionChanged();

            DragCompleted?.Invoke(new DragCompletedEventArgs(rect, _viewState.SelectedBars));
            MarkDirtyRepaint();
        }

        internal void InternalCancelDrag()
        {
            _viewState.IsDragSelecting = false;
            _viewState.DragRect        = Rect.zero;
            MarkDirtyRepaint();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Hit-testing (O(1) for uniformly-spaced bars)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the DATA index of the bar under <paramref name="localPos"/>,
        /// or -1 if none.
        /// </summary>
        internal int HitTestBar(Vector2 localPos)
        {
            if (_model.BarCount == 0) return -1;

            float plotX  = _settings.PaddingLeft;
            float plotY2 = contentRect.height - _settings.PaddingBottom;

            // Must be inside the plot area
            if (localPos.x < plotX || localPos.y > plotY2) return -1;

            float stride  = GetBarStride();   // pixels per slot at current zoom
            float gapPx   = stride * _settings.BarSpacingRatio;
            float barW    = Mathf.Max(_settings.MinBarWidthPx, stride - gapPx);

            float relX    = localPos.x - plotX + _viewState.PanX * stride;
            if (relX < 0f) return -1;

            int displayIdx = (int)(relX / stride);
            if (displayIdx < 0 || displayIdx >= _model.BarCount) return -1;

            // Confirm cursor is on the bar, not the gap
            float barStartX = plotX + (displayIdx - _viewState.PanX) * stride;
            if (localPos.x < barStartX || localPos.x > barStartX + barW) return -1;

            // Resolve display → data
            EnsureSortMap();
            return displayIdx < _viewState.DisplayToData.Length
                ? _viewState.DisplayToData[displayIdx] : displayIdx;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Keyboard navigation
        // ─────────────────────────────────────────────────────────────────────

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_model.BarCount == 0) return;

            switch (evt.keyCode)
            {
                case KeyCode.RightArrow:
                    MoveFocus(+1, evt.shiftKey);
                    evt.StopPropagation();
                    break;

                case KeyCode.LeftArrow:
                    MoveFocus(-1, evt.shiftKey);
                    evt.StopPropagation();
                    break;

                case KeyCode.Home:
                    SetFocus(0, evt.shiftKey);
                    evt.StopPropagation();
                    break;

                case KeyCode.End:
                    SetFocus(_model.BarCount - 1, evt.shiftKey);
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.Space:
                    if (_viewState.FocusedBarIndex >= 0)
                        ActivateFocusedBar();
                    evt.StopPropagation();
                    break;

                case KeyCode.A when evt.ctrlKey:
                    SelectAll();
                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    _viewState.SelectedBars.Clear();
                    _viewState.FocusedBarIndex = -1;
                    FireSelectionChanged();
                    MarkDirtyRepaint();
                    evt.StopPropagation();
                    break;

                case KeyCode.R when evt.ctrlKey:
                    ResetView();
                    evt.StopPropagation();
                    break;
            }
        }

        private void MoveFocus(int delta, bool extend)
        {
            int cur  = _viewState.FocusedBarIndex < 0 ? 0 : _viewState.FocusedBarIndex;
            SetFocus(Mathf.Clamp(cur + delta, 0, _model.BarCount - 1), extend);
        }

        private void SetFocus(int dataIdx, bool extend)
        {
            _viewState.FocusedBarIndex = dataIdx;
            if (!extend) _viewState.SelectedBars.Clear();
            _viewState.SelectedBars.Add(dataIdx);
            EnsureBarVisible(dataIdx);
            FireSelectionChanged();
            MarkDirtyRepaint();
        }

        private void ActivateFocusedBar()
        {
            int   dataIdx    = _viewState.FocusedBarIndex;
            int   displayIdx = dataIdx < _viewState.DataToDisplay.Length
                ? _viewState.DataToDisplay[dataIdx] : dataIdx;
            float val        = _model.Bars[dataIdx].TotalValue;

            BarClicked?.Invoke(new BarClickedEventArgs(dataIdx, displayIdx, val, Vector2.zero));

            using var uiEvt = BarClickedUIEvent.GetPooled(dataIdx, val);
            uiEvt.target = this;
            SendEvent(uiEvt);
        }

        private void SelectAll()
        {
            _viewState.SelectedBars.Clear();
            for (int i = 0; i < _model.BarCount; i++) _viewState.SelectedBars.Add(i);
            FireSelectionChanged();
            MarkDirtyRepaint();
        }

        /// <summary>Scrolls PanX so <paramref name="dataIdx"/> is within the visible window.</summary>
        private void EnsureBarVisible(int dataIdx)
        {
            EnsureSortMap();
            int displayIdx = dataIdx < _viewState.DataToDisplay.Length
                ? _viewState.DataToDisplay[dataIdx] : dataIdx;

            float plotW       = GetPlotWidth();
            float stride      = GetBarStrideBase();
            float visibleBars = plotW / (stride * _viewState.ZoomX);
            float barLeft     = displayIdx;
            float barRight    = displayIdx + 1f;

            if (barLeft < _viewState.PanX)
                _viewState.PanX = barLeft;
            else if (barRight > _viewState.PanX + visibleBars)
                _viewState.PanX = barRight - visibleBars;

            ClampViewState();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Core rendering  (generateVisualContent)
        // ─────────────────────────────────────────────────────────────────────

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Rect cr = contentRect;
            if (cr.width < 2f || cr.height < 2f) return;

            // Lazy sort rebuild
            EnsureSortMap();

            Painter2D p = mgc.painter2D;

            // 1 – Background
            FillRect(p, _settings.BackgroundColor, 0, 0, cr.width, cr.height);

            // 2 – Plot-area bounds
            float pL    = _settings.PaddingLeft;
            float pT    = _settings.PaddingTop;
            float plotX  = pL;
            float plotY  = pT;
            float plotW  = cr.width  - pL - _settings.PaddingRight;
            float plotH  = cr.height - pT - _settings.PaddingBottom;
            float plotX2 = plotX + plotW;
            float plotY2 = plotY + plotH;   // bottom edge (Y increases downward)

            if (plotW < 2f || plotH < 2f) return;

            // 3 – Horizontal grid lines (below bars)
            if (_settings.ShowGrid && _settings.GridLineCount > 0)
                DrawGrid(p, plotX, plotX2, plotY, plotY2, plotH);

            // 4 – Primary bars (stacked, colour-batched, LOD-aware)
            if (_model.BarCount > 0)
            {
                CalcViewSlice(plotW, out float stride, out float barW,
                              out int startDisp, out int endDisp);
                int viewCount = endDisp - startDisp;

                if (viewCount > 0)
                {
                    if (stride >= 1f)
                        DrawDirectBars(p, startDisp, endDisp, plotX, plotY2, stride, barW,
                                       plotH, _model, false, 1f);
                    else
                        DrawLodBars(p, startDisp, endDisp, plotX, plotY2, plotW, plotH,
                                    _model, 1f);
                }
            }

            // 5 – Overlay bars (same position mapping, tinted alpha)
            if (_model.HasOverlay && _model.BarCount > 0)
            {
                CalcViewSlice(plotW, out float stride, out float barW,
                              out int startDisp, out int endDisp);
                int viewCount = endDisp - startDisp;

                if (viewCount > 0)
                {
                    if (stride >= 1f)
                        DrawDirectBars(p, startDisp, endDisp, plotX, plotY2, stride, barW,
                                       plotH, _model, true, _settings.OverlayOpacity);
                    else
                        DrawLodBars(p, startDisp, endDisp, plotX, plotY2, plotW, plotH,
                                    _model, _settings.OverlayOpacity);
                }
            }

            // 6 – Selection & hover highlights
            DrawHighlights(p, plotX, plotY2, plotH);

            // 7 – Drag-select rectangle
            if (_viewState.IsDragSelecting && _viewState.DragRect.width > 1f)
                DrawDragRect(p, _viewState.DragRect);

            // 7b – Padding overdraw: re-fill the four padding strips with the
            //      background colour.  The bar clamp prevents bars from drawing
            //      above plotY (= PaddingTop), but the top Y-label is centred ON
            //      plotY so its lower half sits inside the plot area.  Overpainting
            //      the strips creates a clean frame that masks any bar tip that
            //      touches a boundary and keeps label backgrounds opaque.
            FillRect(p, _settings.BackgroundColor, 0,      0,      cr.width,           pT);
            FillRect(p, _settings.BackgroundColor, 0,      plotY2, cr.width,           cr.height - plotY2);
            FillRect(p, _settings.BackgroundColor, 0,      pT,     pL,                 plotH);
            FillRect(p, _settings.BackgroundColor, plotX2, pT,     cr.width - plotX2,  plotH);

            // 8 – Axes (on top of bars and overdraw)
            if (_settings.ShowAxes)
                DrawAxes(p, plotX, plotX2, plotY, plotY2);

            // 9 – Cache label layout params for the deferred FlushLabelPositions
            //     scheduler (must NOT modify VisualElement styles here).
            _lblPlotX    = plotX;
            _lblPlotW    = plotW;
            _lblPlotH    = plotH;
            _lblPlotY2   = plotY2;
            _labelsDirty = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Grid
        // ─────────────────────────────────────────────────────────────────────

        private void DrawGrid(Painter2D p,
            float plotX, float plotX2, float plotY, float plotY2, float plotH)
        {
            p.strokeColor = _settings.GridLineColor;
            p.lineWidth   = 1f;
            int lines = _settings.GridLineCount;
            for (int i = 0; i <= lines; i++)
            {
                float t = (float)i / lines;
                float y = plotY2 - t * plotH;
                p.BeginPath();
                p.MoveTo(new Vector2(plotX,  y));
                p.LineTo(new Vector2(plotX2, y));
                p.Stroke();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Direct bar rendering  (slot ≥ 1 px; supports stacked segments)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawDirectBars(
            Painter2D p,
            int startDisp, int endDisp,
            float plotX, float plotY2,
            float stride, float barW, float plotH,
            ChartDataModel model, bool useOverlay, float alpha)
        {
            // Clear batch buckets without reallocating list instances
            foreach (var kv in _batches) kv.Value.Clear();
            _batchOrder.Clear();

            BarEntry[]   bars     = useOverlay ? model.OverlayBars     : model.Bars;
            BarSegment[] segments = useOverlay ? model.OverlaySegments : model.Segments;
            int          barCount = useOverlay ? model.OverlayBarCount  : model.BarCount;

            // Effective Y scale: _effectiveMaxY applies Y zoom and pan
            float yScale = plotH / _effectiveMaxY * _viewState.ZoomY;
            // PanY shifts the bottom of the Y range
            float yOffset = _viewState.PanY * plotH;

            for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
            {
                int dataIdx = _viewState.DisplayToData[dispIdx];
                if (dataIdx >= barCount) continue;

                ref readonly BarEntry bar = ref bars[dataIdx];
                float x       = plotX + (dispIdx - _viewState.PanX) * stride;
                float yBottom = plotY2 + yOffset;   // +yOffset shifts range up when panned

                for (int s = 0; s < bar.SegmentCount; s++)
                {
                    ref readonly BarSegment seg = ref segments[bar.SegmentStart + s];
                    float segH = Mathf.Min(seg.Value * yScale, plotH);
                    if (segH < 0.5f) { yBottom -= segH; continue; }

                    float yTop = yBottom - segH;
                    if (yTop  >= plotY2) { yBottom = yTop; continue; }   // below visible
                    if (yBottom <= plotY2 - plotH) break;                // above visible

                    // Clamp to plot area
                    float drawTop = Mathf.Max(yTop, plotY2 - plotH);
                    float drawH   = Mathf.Min(yBottom, plotY2) - drawTop;
                    if (drawH < 0.5f) { yBottom = yTop; continue; }

                    Color32 c = ResolveSegmentColor(seg.Color, alpha);
                    BatchRect(c, x, drawTop, barW, drawH);
                    yBottom = yTop;
                }
            }

            FlushBatches(p);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LOD bar rendering  (slot < 1 px → pixel-column merging)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawLodBars(
            Painter2D p,
            int startDisp, int endDisp,
            float plotX, float plotY2,
            float plotW, float plotH,
            ChartDataModel model, float alpha)
        {
            int pixelCount = Mathf.Max(1, Mathf.FloorToInt(plotW));
            int viewCount  = endDisp - startDisp;

            if (_lodBuf.Length < pixelCount)
                _lodBuf = new LodPixel[Mathf.NextPowerOfTwo(pixelCount + 1)];

            for (int px = 0; px < pixelCount; px++) _lodBuf[px] = default;

            BarEntry[]   bars     = model.Bars;
            BarSegment[] segments = model.Segments;
            int          barCount = model.BarCount;

            for (int i = 0; i < viewCount; i++)
            {
                int dataIdx = _viewState.DisplayToData[startDisp + i];
                if (dataIdx >= barCount) continue;

                ref readonly BarEntry bar = ref bars[dataIdx];
                int px = Mathf.Clamp(
                    Mathf.FloorToInt((float)i / viewCount * pixelCount),
                    0, pixelCount - 1);

                // For LOD we just use the first segment colour at total value
                float total = bar.TotalValue;
                if (total > _lodBuf[px].MaxValue)
                {
                    Color32 c = bar.SegmentCount > 0
                        ? ResolveSegmentColor(segments[bar.SegmentStart].Color, alpha)
                        : ResolveSegmentColor(default, alpha);
                    _lodBuf[px] = new LodPixel { MaxValue = total, Color = c };
                }
            }

            foreach (var kv in _batches) kv.Value.Clear();
            _batchOrder.Clear();

            float yScale  = plotH / _effectiveMaxY * _viewState.ZoomY;
            float yOffset = _viewState.PanY * plotH;

            for (int px = 0; px < pixelCount; px++)
            {
                LodPixel lp = _lodBuf[px];
                if (lp.MaxValue <= 0f) continue;

                float barH = Mathf.Min(lp.MaxValue * yScale, plotH);
                if (barH < 0.5f) continue;

                float x = plotX + px;
                float y = plotY2 + yOffset - barH;

                BatchRect(lp.Color, x, y, 1f, barH);
            }

            FlushBatches(p);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Selection & hover highlights
        // ─────────────────────────────────────────────────────────────────────

        private void DrawHighlights(Painter2D p, float plotX, float plotY2, float plotH)
        {
            if (_model.BarCount == 0) return;

            float plotW = GetPlotWidth();

            // Compute the visible display-index range once.
            CalcViewSlice(plotW, out float stride, out float barW,
                          out int startDisp, out int endDisp);

            // ── LOD mode (stride < 1 px) ──────────────────────────────────────
            // Per-bar highlights are invisible at this zoom AND catastrophically
            // expensive: iterating a 10 000-element SelectedBars HashSet produces
            // 10 000 sub-paths, which stalls the Painter2D tessellator and freezes
            // the editor.  Draw a single aggregate rect instead (O(1)).
            bool lodMode = stride < 1f;

            if (_viewState.SelectedBars.Count > 0)
            {
                if (lodMode)
                {
                    // LOD mode: iterate pixel columns (O(plotWidth), bounded by screen),
                    // check which display index each column maps to, draw a 1 px
                    // highlight column only if that bar is selected.
                    // This is O(pixelCount) with O(1) HashSet.Contains per column —
                    // never O(selected), so stays fast even with 10 000 bars selected.
                    int pixelCount = Mathf.Max(1, Mathf.FloorToInt(plotW));
                    int viewCount  = endDisp - startDisp;

                    p.fillColor = _settings.SelectionFillColor;
                    p.BeginPath();
                    for (int px = 0; px < pixelCount; px++)
                    {
                        // Inverse of DrawLodBars mapping: px → representative display index.
                        int dispOffset = Mathf.FloorToInt((float)px * viewCount / pixelCount);
                        int dispIdx    = startDisp + Mathf.Clamp(dispOffset, 0, viewCount - 1);
                        int dataIdx    = dispIdx < _viewState.DisplayToData.Length
                            ? _viewState.DisplayToData[dispIdx] : dispIdx;

                        if (!_viewState.SelectedBars.Contains(dataIdx)) continue;

                        float x = plotX + px;
                        PathRect(p, x, plotY2 - plotH, 1f, plotH);
                    }
                    p.Fill(FillRule.OddEven);

                    // Rim: a single stroke around the entire selected pixel span would
                    // require tracking contiguous runs — a simple column fill is enough
                    // at LOD density; skip per-column stroke to keep call count O(1).
                }
                else
                {
                    // ── Normal mode: iterate VISIBLE bars only, O(visibleCount) ──
                    // We invert the iteration (display range → Contains check) so
                    // cost is bounded by screen width, not by selection size.

                    // Fill pass
                    p.fillColor = _settings.SelectionFillColor;
                    p.BeginPath();
                    for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
                    {
                        int dataIdx = dispIdx < _viewState.DisplayToData.Length
                            ? _viewState.DisplayToData[dispIdx] : dispIdx;
                        if (!_viewState.SelectedBars.Contains(dataIdx)) continue;
                        float x = plotX + (dispIdx - _viewState.PanX) * stride;
                        PathRect(p, x, plotY2 - plotH, barW, plotH);
                    }
                    p.Fill(FillRule.OddEven);

                    // Rim stroke pass
                    p.strokeColor = _settings.SelectionRimColor;
                    p.lineWidth   = 1.5f;
                    for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
                    {
                        int dataIdx = dispIdx < _viewState.DisplayToData.Length
                            ? _viewState.DisplayToData[dispIdx] : dispIdx;
                        if (!_viewState.SelectedBars.Contains(dataIdx)) continue;
                        float x = plotX + (dispIdx - _viewState.PanX) * stride;
                        p.BeginPath();
                        PathRect(p, x, plotY2 - plotH, barW, plotH);
                        p.Stroke();
                    }
                }
            }

            // ── Focused bar (keyboard focus ring) — always single-bar, always fast ──
            int focIdx = _viewState.FocusedBarIndex;
            if (!lodMode && focIdx >= 0 && focIdx < _model.BarCount)
            {
                int displayIdx = focIdx < _viewState.DataToDisplay.Length
                    ? _viewState.DataToDisplay[focIdx] : focIdx;
                float x = plotX + (displayIdx - _viewState.PanX) * stride;

                p.strokeColor = _settings.FocusRimColor;
                p.lineWidth   = 2f;
                p.BeginPath();
                PathRect(p, x - 1f, plotY2 - plotH - 1f, barW + 2f, plotH + 2f);
                p.Stroke();
            }

            // ── Hovered bar tint — always single-bar, always fast ──────────────
            int hovIdx = _viewState.HoveredBarIndex;
            if (hovIdx >= 0 && hovIdx < _model.BarCount)
            {
                int displayIdx = hovIdx < _viewState.DataToDisplay.Length
                    ? _viewState.DataToDisplay[hovIdx] : hovIdx;
                float x = plotX + (displayIdx - _viewState.PanX) * stride;

                // In LOD mode use a 1 px wide tint; in normal mode use the full barW.
                float hw = lodMode ? 1f : barW;

                p.fillColor = _settings.HoverTintColor;
                p.BeginPath();
                PathRect(p, x, plotY2 - plotH, hw, plotH);
                p.Fill();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Drag-select rectangle
        // ─────────────────────────────────────────────────────────────────────

        private void DrawDragRect(Painter2D p, Rect r)
        {
            p.fillColor = _settings.DragRectFillColor;
            p.BeginPath();
            PathRect(p, r.x, r.y, r.width, r.height);
            p.Fill();

            p.strokeColor = _settings.DragRectBorderColor;
            p.lineWidth   = 1f;
            p.BeginPath();
            PathRect(p, r.x, r.y, r.width, r.height);
            p.Stroke();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Axes
        // ─────────────────────────────────────────────────────────────────────

        private void DrawAxes(Painter2D p,
            float plotX, float plotX2, float plotY, float plotY2)
        {
            p.strokeColor = _settings.AxisColor;
            p.lineWidth   = 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(plotX, plotY));
            p.LineTo(new Vector2(plotX, plotY2));
            p.Stroke();

            p.BeginPath();
            p.MoveTo(new Vector2(plotX,  plotY2));
            p.LineTo(new Vector2(plotX2, plotY2));
            p.Stroke();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Label overlay
        // ─────────────────────────────────────────────────────────────────────

        private void RebuildLabelPool()
        {
            _labelRoot.Clear();
            _yLabels.Clear();
            _xLabels.Clear();

            int yCount = Mathf.Min(_settings.MaxYLabels + 1, _settings.GridLineCount + 1);
            for (int i = 0; i < yCount; i++) { var l = MakeLabel(); _yLabels.Add(l); _labelRoot.Add(l); }
            for (int i = 0; i < _settings.MaxXLabels; i++) { var l = MakeLabel(); _xLabels.Add(l); _labelRoot.Add(l); }
        }

        /// <summary>
        /// Called by the per-frame scheduler.  Applies cached label positions
        /// computed during the last repaint — outside <c>generateVisualContent</c>
        /// so that VisualElement style mutations don't invalidate the layout pass.
        /// </summary>
        private void FlushLabelPositions()
        {
            if (!_labelsDirty) return;
            _labelsDirty = false;
            PositionLabels(_lblPlotX, _lblPlotW, _lblPlotH, _lblPlotY2);
        }

        private void PositionLabels(float plotX, float plotW, float plotH, float plotY2)
        {
            // Y-axis value labels.
            // The visible Y range is determined by ZoomY and PanY — not just
            // [MinValue, _effectiveMaxY].  From DrawDirectBars:
            //   yScale  = plotH / _effectiveMaxY * ZoomY
            //   yBottom = plotY2 + PanY * plotH
            // → value at screen-y = (yBottom - y) / yScale
            // → bottom edge (y=plotY2) : PanY * _effectiveMaxY / ZoomY
            // → top    edge (y=plotY2-plotH) : (1+PanY) * _effectiveMaxY / ZoomY
            float zoomY      = Mathf.Max(0.001f, _viewState.ZoomY);
            float visibleMin = _viewState.PanY         * _effectiveMaxY / zoomY;
            float visibleMax = (1f + _viewState.PanY)  * _effectiveMaxY / zoomY;

            for (int i = 0; i < _yLabels.Count; i++)
            {
                Label lbl  = _yLabels[i];
                int   cnt  = _yLabels.Count;
                float t    = cnt > 1 ? (float)i / (cnt - 1) : 0f;
                float val  = Mathf.Lerp(visibleMin, visibleMax, t);
                float y    = plotY2 - t * plotH;
                lbl.text   = _yFormatter(val);
                lbl.style.left   = 0f;
                lbl.style.top    = y - 7f;
                lbl.style.width  = plotX - 4f;
                lbl.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleRight);
                lbl.visible = true;
            }

            // X-axis bar labels — only when bars are wide enough
            CalcViewSlice(plotW, out float stride, out _, out int startDisp, out int endDisp);
            int   viewCnt = endDisp - startDisp;
            bool  show    = stride >= 14f && viewCnt > 0;

            for (int j = 0; j < _xLabels.Count; j++)
            {
                Label lbl = _xLabels[j];
                if (!show) { lbl.visible = false; continue; }

                int   cnt    = _xLabels.Count;
                float step   = cnt > 1 ? (float)(viewCnt - 1) / (cnt - 1) : 0f;
                int   dispIdx = Mathf.Clamp(startDisp + Mathf.RoundToInt(j * step), startDisp, endDisp - 1);
                int   dataIdx = dispIdx < _viewState.DisplayToData.Length
                    ? _viewState.DisplayToData[dispIdx] : dispIdx;

                string text = null;
                if (dataIdx < _model.BarCount)
                {
                    text = _model.Bars[dataIdx].Label;
                    if (string.IsNullOrEmpty(text))
                        text = _xFormatter(dataIdx);
                }
                text ??= dispIdx.ToString();

                float x  = plotX + (dispIdx - _viewState.PanX) * stride + stride * 0.5f;
                lbl.text = text;
                lbl.style.left   = x - 20f;
                lbl.style.top    = plotY2 + 3f;
                lbl.style.width  = 40f;
                lbl.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.UpperCenter);
                lbl.visible = (x >= plotX && x <= plotX + plotW);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Sort index (lazy rebuild, O(N log N) only when dirty)
        // ─────────────────────────────────────────────────────────────────────

        private void EnsureSortMap()
        {
            if (!_viewState.SortDirty) return;
            int n = _model.BarCount;
            _viewState.EnsureSortCapacity(n);

            for (int i = 0; i < n; i++) _viewState.DisplayToData[i] = i;

            if (_viewState.SortMode == SortMode.ByValue)
            {
                bool desc = _viewState.SortDescending;
                Array.Sort(_viewState.DisplayToData, 0, n,
                    Comparer<int>.Create((a, b) =>
                    {
                        float va = _model.Bars[a].TotalValue;
                        float vb = _model.Bars[b].TotalValue;
                        return desc ? vb.CompareTo(va) : va.CompareTo(vb);
                    }));
            }

            for (int i = 0; i < n; i++)
                _viewState.DataToDisplay[_viewState.DisplayToData[i]] = i;

            _viewState.SortDirty = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  View-slice calculation
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes which display indices [startDisp, endDisp) are visible given
        /// current zoom and pan.  <paramref name="stride"/> is the pixel width
        /// per bar slot (includes spacing); <paramref name="barW"/> is the filled
        /// portion.  Fractional PanX is preserved for sub-pixel accuracy.
        /// </summary>
        private void CalcViewSlice(float plotW,
            out float stride, out float barW,
            out int startDisp, out int endDisp)
        {
            int total = _model.BarCount;
            if (total == 0)
            {
                stride = plotW; barW = plotW; startDisp = 0; endDisp = 0; return;
            }

            // stride = plotW / total at zoom=1, scaled by ZoomX
            float baseStride = plotW / total;
            stride = baseStride * _viewState.ZoomX;
            float gap = stride * _settings.BarSpacingRatio;
            barW = Mathf.Max(_settings.MinBarWidthPx, stride - gap);

            // PanX is in data-space (fractional bar units)
            // How many bars fit in the viewport?
            float visibleBars = plotW / stride;

            startDisp = Mathf.FloorToInt(_viewState.PanX);
            startDisp = Mathf.Clamp(startDisp, 0, total - 1);

            // Add one extra on each side to avoid visible pop-in at edges
            int visCount = Mathf.CeilToInt(visibleBars) + 2;
            endDisp = Mathf.Clamp(startDisp + visCount, 0, total);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Drag-select hit collection
        // ─────────────────────────────────────────────────────────────────────

        private void CollectBarsInRect(Rect rect, HashSet<int> result)
        {
            if (_model.BarCount == 0) return;

            float plotX  = _settings.PaddingLeft;
            float plotY2 = contentRect.height - _settings.PaddingBottom;
            float plotH  = GetPlotHeight();
            float plotW  = GetPlotWidth();

            CalcViewSlice(plotW, out float stride, out float barW,
                          out int startDisp, out int endDisp);

            EnsureSortMap();

            int viewCount = endDisp - startDisp;
            if (viewCount <= 0) return;

            if (stride < 1f)
            {
                // LOD mode: bars are denser than 1 px.
                // DrawLodBars maps display-index offset i → pixel column:
                //   px = Floor(i / viewCount * pixelCount)
                // Inverse: pixel column px → display-index offset start:
                //   i = Floor(px * viewCount / pixelCount)
                //
                // Convert the rect's x-range to a display-index range using the
                // same formula so only bars whose pixel columns fall inside the
                // drag rect get selected — not the entire visible set.
                int pixelCount = Mathf.Max(1, Mathf.FloorToInt(plotW));

                // Clamp rect to plot area, convert to pixel columns.
                float relXMin = Mathf.Clamp(rect.xMin - plotX, 0f, plotW);
                float relXMax = Mathf.Clamp(rect.xMax - plotX, 0f, plotW);

                // Pixel columns that the rect covers (inclusive).
                int pxMin = Mathf.FloorToInt(relXMin);
                int pxMax = Mathf.Min(Mathf.CeilToInt(relXMax), pixelCount - 1);

                if (pxMin > pxMax) return;

                // Map pixel column range → display-index range using inverse formula.
                int dispStart = startDisp + Mathf.FloorToInt((float)pxMin * viewCount / pixelCount);
                int dispEnd   = startDisp + Mathf.Min(
                    Mathf.CeilToInt((float)(pxMax + 1) * viewCount / pixelCount), viewCount);

                dispStart = Mathf.Clamp(dispStart, startDisp, endDisp);
                dispEnd   = Mathf.Clamp(dispEnd,   startDisp, endDisp);

                for (int dispIdx = dispStart; dispIdx < dispEnd; dispIdx++)
                {
                    int dataIdx = dispIdx < _viewState.DisplayToData.Length
                        ? _viewState.DisplayToData[dispIdx] : dispIdx;
                    if (dataIdx < _model.BarCount)
                        result.Add(dataIdx);
                }
                return;
            }

            // Normal mode: walk visible display indices, early-exit on x > rect.xMax.
            for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
            {
                float x = plotX + (dispIdx - _viewState.PanX) * stride;
                if (x + barW < rect.xMin) continue;
                if (x > rect.xMax)        break;

                int dataIdx = dispIdx < _viewState.DisplayToData.Length
                    ? _viewState.DisplayToData[dispIdx] : dispIdx;
                if (dataIdx >= _model.BarCount) continue;

                float barH    = Mathf.Min(_model.Bars[dataIdx].TotalValue /
                                          _effectiveMaxY * plotH * _viewState.ZoomY, plotH);
                float barTopY = plotY2 - barH;
                var   barRect = new Rect(x, barTopY, barW, barH);

                if (rect.Overlaps(barRect))
                    result.Add(dataIdx);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Internal state helpers
        // ─────────────────────────────────────────────────────────────────────

        private void OnDataChanged()
        {
            RecalcBounds();
            _viewState.SortDirty = true;
            RebuildLabelPool();
            MarkDirtyRepaint();
        }

        private void RecalcBounds()
        {
            _effectiveMaxY = _settings.MaxValue > 0f
                ? _settings.MaxValue
                : _model.MaxPrimaryY;
            if (_effectiveMaxY <= _settings.MinValue) _effectiveMaxY = _settings.MinValue + 1f;
        }

        private void ClampViewState()
        {
            float plotW       = GetPlotWidth();
            float stride      = GetBarStrideBase();
            float visibleBars = stride > 0f
                ? plotW / (stride * _viewState.ZoomX) : (float)_model.BarCount;

            _viewState.ClampPan(visibleBars, _model.BarCount, _viewState.ZoomY);
        }

        private void NotifyViewChanged()
        {
            ViewChanged?.Invoke(new ViewChangedEventArgs(
                _viewState.ZoomX, _viewState.ZoomY,
                _viewState.PanX,  _viewState.PanY));
        }

        private void FireSelectionChanged()
        {
            SelectionChanged?.Invoke(new SelectionChangedEventArgs(_viewState.SelectedBars));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Batch helpers (struct-of-arrays, zero GC)
        // ─────────────────────────────────────────────────────────────────────

        private void BatchRect(Color32 color, float x, float y, float w, float h)
        {
            if (!_batches.TryGetValue(color, out RectBatch batch))
            {
                // First time this colour is ever seen: create the batch.
                batch = new RectBatch();
                _batches[color] = batch;
            }

            // Add to draw-order the FIRST time this colour is used in the
            // current frame (Count == 0 after the per-frame Clear call).
            // We cannot rely on TryGetValue failing here: _batches retains its
            // keys across repaints — only Count is reset in DrawDirectBars.
            // Without this check, _batchOrder stays empty on every repaint
            // after the first, FlushBatches draws nothing, and all bars vanish.
            if (batch.Count == 0)
                _batchOrder.Add(color);

            batch.Add(x, y, w, h);
        }

        private void FlushBatches(Painter2D p)
        {
            foreach (Color32 c in _batchOrder)
            {
                p.fillColor = (Color)c;
                p.BeginPath();
                RectBatch b = _batches[c];
                for (int k = 0; k < b.Count; k++)
                    PathRect(p, b.Xs[k], b.Ys[k], b.Ws[k], b.Hs[k]);
                p.Fill(FillRule.OddEven);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Painter2D utilities
        // ─────────────────────────────────────────────────────────────────────

        private static void FillRect(Painter2D p, Color c, float x, float y, float w, float h)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x,     y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x,     y + h));
            p.ClosePath();
            p.Fill();
        }

        private static void PathRect(Painter2D p, float x, float y, float w, float h)
        {
            p.MoveTo(new Vector2(x,     y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x,     y + h));
            p.ClosePath();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Label helpers
        // ─────────────────────────────────────────────────────────────────────

        private Label MakeLabel() => new Label
        {
            pickingMode = PickingMode.Ignore,
            style =
            {
                position    = Position.Absolute,
                fontSize    = _settings.LabelFontSize,
                color       = _settings.LabelColor,
                overflow    = Overflow.Hidden,
                height      = 14f,
                paddingLeft  = 0, paddingRight  = 0,
                marginLeft   = 0, marginRight   = 0,
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Color resolve
        // ─────────────────────────────────────────────────────────────────────

        private Color32 ResolveSegmentColor(Color32 c, float alpha)
        {
            // default(Color32) == (0,0,0,0) → use default bar colour
            bool isDefault = c.r == 0 && c.g == 0 && c.b == 0 && c.a == 0;
            Color32 resolved = isDefault ? (Color32)_settings.DefaultBarColor : c;

            if (alpha < 0.999f)
            {
                resolved.a = (byte)Mathf.RoundToInt(resolved.a * alpha);
            }
            return resolved;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Private structs
        // ─────────────────────────────────────────────────────────────────────

        private struct LodPixel
        {
            public float   MaxValue;
            public Color32 Color;
        }

        /// <summary>
        /// Struct-of-arrays for batched bar rectangles.
        /// Four parallel arrays stay cache-friendly; no boxing; grows via doubling.
        /// One instance per unique colour, reused across frames.
        /// </summary>
        private sealed class RectBatch
        {
            public float[] Xs = new float[64];
            public float[] Ys = new float[64];
            public float[] Ws = new float[64];
            public float[] Hs = new float[64];
            public int Count;

            public void Clear() => Count = 0;

            public void Add(float x, float y, float w, float h)
            {
                if (Count == Xs.Length) Grow();
                Xs[Count] = x; Ys[Count] = y;
                Ws[Count] = w; Hs[Count] = h;
                Count++;
            }

            private void Grow()
            {
                int n = Xs.Length * 2;
                Array.Resize(ref Xs, n); Array.Resize(ref Ys, n);
                Array.Resize(ref Ws, n); Array.Resize(ref Hs, n);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Events;
using BarGraph.Input;

namespace BarGraph.Core
{
    /// <summary>
    /// High-performance stacked vertical bar graph for Unity UI Toolkit.
    ///
    /// ── Performance model ────────────────────────────────────────────────────
    ///  • Bar geometry bypasses <c>Painter2D</c> entirely.  Quads are written
    ///    directly via <c>MeshGenerationContext.Allocate()</c> in chunks of up to
    ///    16 383 quads (65 532 vertices), with unlimited chunks per repaint.
    ///    No tessellation overhead, no 65 535-vertex ceiling.
    ///  • Segment-level LOD: sub-pixel segments are merged in a single O(n) pass,
    ///    capping output to ~plotHeight rects per bar regardless of segment count.
    ///  • Bar-level LOD: when bar slot width &lt; 1 screen pixel the renderer
    ///    switches to pixel-column mode — cost bounded by panel width, not bar count.
    ///  • Chrome (grid, axes, highlights, drag rect) still uses <c>Painter2D</c>
    ///    since vertex counts are trivially bounded.
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

        // Bar quads written by DrawDirectBars / DrawLodBars, flushed via
        // MeshGenerationContext.Allocate().  Grows via doubling, never shrinks.
        private QuadData[] _quadBuf = new QuadData[4096];
        private int        _quadCount;

        // LOD pixel buffer (grows, never shrinks)
        private LodPixel[] _lodBuf = new LodPixel[2048];

        // Selection run buffer: pairs of (runStartDisp, runEndDispExclusive)
        private readonly List<int> _selectionRuns = new List<int>(32);

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

        // ── Input ─────────────────────────────────────────────────────────────

        private readonly BarGraphEventBus       _eventBus = new BarGraphEventBus();
        private          Manipulator            _inputSource;
        private readonly List<IBarGraphHandler> _handlers = new List<IBarGraphHandler>();

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

            _labelRoot = new VisualElement { name = "bar-graph__labels", pickingMode = PickingMode.Ignore };
            _labelRoot.style.position = Position.Absolute;
            _labelRoot.style.left     = 0;
            _labelRoot.style.top      = 0;
            _labelRoot.style.right    = 0;
            _labelRoot.style.bottom   = 0;
            Add(_labelRoot);

            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<GeometryChangedEvent>(_ => { EnsureLabelPool(); MarkDirtyRepaint(); });
            RegisterCallback<AttachToPanelEvent>(_ =>
                schedule.Execute(FlushLabelPositions).Every(0));

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            _model.DataChanged += OnDataChanged;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Input source and handler registration
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the input source that translates raw input into
        /// <see cref="BarGraphEventBus"/> calls.  Replaces any existing source.
        /// If the source implements <see cref="IBarGraphInputSource"/>, the
        /// internal event bus is injected automatically — callers never need
        /// to access it directly.
        /// </summary>
        public void SetInputSource(Manipulator inputSource)
        {
            if (_inputSource != null)
                this.RemoveManipulator(_inputSource);

            _inputSource = inputSource;

            if (_inputSource != null)
            {
                if (_inputSource is IBarGraphInputSource src)
                    src.Initialize(_eventBus);

                this.AddManipulator(_inputSource);
            }
        }

        /// <summary>
        /// Adds a behaviour handler and registers it against the internal event bus.
        /// If a handler of the same type is already registered it is replaced.
        /// </summary>
        public void AddHandler(IBarGraphHandler handler)
        {
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (_handlers[i].GetType() == handler.GetType())
                {
                    _handlers[i].Unregister(_eventBus);
                    _handlers[i] = handler;
                    handler.Register(_eventBus, this);
                    return;
                }
            }

            _handlers.Add(handler);
            handler.Register(_eventBus, this);
        }

        /// <summary>Removes and unregisters a handler by type.</summary>
        public void RemoveHandler<T>() where T : IBarGraphHandler
        {
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (_handlers[i] is T)
                {
                    _handlers[i].Unregister(_eventBus);
                    _handlers.RemoveAt(i);
                    return;
                }
            }
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
        //  Domain-reload snapshot (view state only — no data arrays)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Captures the current view state (zoom, pan, sort, selection) into a
        /// lightweight serializable struct.  Call from <c>OnDisable</c> before
        /// domain reload and hold the result in a <c>[SerializeField]</c> field.
        /// </summary>
        public BarGraphViewSnapshot CreateViewSnapshot()
        {
            int[] selected;
            if (_viewState.SelectedBars.Count > 0)
            {
                selected = new int[_viewState.SelectedBars.Count];
                _viewState.SelectedBars.CopyTo(selected);
            }
            else
            {
                selected = Array.Empty<int>();
            }

            return new BarGraphViewSnapshot
            {
                ZoomX           = _viewState.ZoomX,
                ZoomY           = _viewState.ZoomY,
                PanX            = _viewState.PanX,
                PanY            = _viewState.PanY,
                SortMode        = _viewState.SortMode,
                SortDescending  = _viewState.SortDescending,
                FocusedBarIndex = _viewState.FocusedBarIndex,
                SelectedBars    = selected,
                IsValid         = true,
            };
        }

        /// <summary>
        /// Restores a previously captured view snapshot.  Call AFTER data has
        /// been loaded (via <see cref="SetData"/>) so that sort maps and pan
        /// clamping work against the correct bar count.
        /// </summary>
        public void RestoreViewSnapshot(BarGraphViewSnapshot snap)
        {
            if (!snap.IsValid) return;

            _viewState.ZoomX          = Mathf.Clamp(snap.ZoomX, _viewState.MinZoomX, _viewState.MaxZoomX);
            _viewState.ZoomY          = Mathf.Clamp(snap.ZoomY, _viewState.MinZoomY, _viewState.MaxZoomY);
            _viewState.PanX           = snap.PanX;
            _viewState.PanY           = snap.PanY;
            _viewState.SortMode       = snap.SortMode;
            _viewState.SortDescending = snap.SortDescending;
            _viewState.SortDirty      = true;

            // Clamp index-based state against actual bar count
            int barCount = _model.BarCount;
            _viewState.FocusedBarIndex =
                (snap.FocusedBarIndex >= 0 && snap.FocusedBarIndex < barCount)
                    ? snap.FocusedBarIndex : -1;

            _viewState.SelectedBars.Clear();
            if (snap.SelectedBars != null)
            {
                for (int i = 0; i < snap.SelectedBars.Length; i++)
                {
                    int idx = snap.SelectedBars[i];
                    if (idx >= 0 && idx < barCount)
                        _viewState.SelectedBars.Add(idx);
                }
            }

            ClampViewState();
            NotifyViewChanged();
            FireSelectionChanged();
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

        /// <summary>
        /// Converts a screen-space pixel delta into data-space pan offsets.
        /// Absorbs the Y-axis inversion (screen Y-down vs chart Y-up) so that
        /// callers can apply both axes with the same sign: <c>startPan + delta</c>.
        /// </summary>
        internal Vector2 ScreenDeltaToPanDelta(Vector2 screenDelta)
        {
            float barStride = GetBarStride();
            float plotH     = GetPlotHeight();
            return new Vector2(
                -screenDelta.x / Mathf.Max(1f, barStride),
                 screenDelta.y / Mathf.Max(1f, plotH)
            );
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
            SnapBarX(displayIdx, plotX, stride, barW, out float barStartX, out float snapW);
            if (localPos.x < barStartX || localPos.x > barStartX + snapW) return -1;

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

            // 4 – Primary bars (stacked, segment-LOD, direct mesh)
            _quadCount = 0;   // Reset quad buffer for this repaint

            if (_model.BarCount > 0)
            {
                CalcViewSlice(plotW, out float stride, out float barW,
                              out int startDisp, out int endDisp);
                int viewCount = endDisp - startDisp;

                // Pre-size quad buffer to avoid repeated Array.Resize during
                // the draw pass.  Direct path: segment merge caps output at
                // ~plotH quads per bar.  LOD path: bounded by pixel count.
                // Overlay may add a similar amount — multiply by 2 if present.
                if (viewCount > 0)
                {
                    int estimatedQuads = stride >= 1f
                        ? viewCount * Mathf.CeilToInt(plotH)
                        : Mathf.CeilToInt(plotW);
                    if (_model.HasOverlay) estimatedQuads *= 2;
                    if (_quadBuf.Length < estimatedQuads)
                        _quadBuf = new QuadData[Mathf.Max(_quadBuf.Length * 2, estimatedQuads)];
                }

                if (viewCount > 0)
                {
                    if (stride >= 1f)
                        DrawDirectBars(startDisp, endDisp, plotX, plotY2, stride, barW,
                                       plotH, _model, false, 1f);
                    else
                        DrawLodBars(startDisp, endDisp, plotX, plotY2, plotW, plotH,
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
                        DrawDirectBars(startDisp, endDisp, plotX, plotY2, stride, barW,
                                       plotH, _model, true, _settings.OverlayOpacity);
                    else
                        DrawLodBars(startDisp, endDisp, plotX, plotY2, plotW, plotH,
                                    _model, _settings.OverlayOpacity);
                }
            }

            // Flush all accumulated bar quads to the GPU via direct mesh allocation.
            // This bypasses Painter2D entirely — no tessellation, no vertex ceiling.
            if (_quadCount > 0)
                FlushQuads(mgc);

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
            int startDisp, int endDisp,
            float plotX, float plotY2,
            float stride, float barW, float plotH,
            ChartDataModel model, bool useOverlay, float alpha)
        {
            BarEntry[]   bars     = useOverlay ? model.OverlayBars     : model.Bars;
            BarSegment[] segments = useOverlay ? model.OverlaySegments : model.Segments;
            int          barCount = useOverlay ? model.OverlayBarCount  : model.BarCount;

            // Effective Y scale: _effectiveMaxY applies Y zoom and pan
            float yScale = plotH / _effectiveMaxY * _viewState.ZoomY;
            // PanY shifts the bottom of the Y range
            float yOffset = _viewState.PanY * plotH;
            float plotTop = plotY2 - plotH;   // top edge of plot area

            for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
            {
                int dataIdx = _viewState.DisplayToData[dispIdx];
                if (dataIdx >= barCount) continue;

                ref readonly BarEntry bar = ref bars[dataIdx];
                SnapBarX(dispIdx, plotX, stride, barW, out float x, out float bw);
                float yBottom = plotY2 + yOffset;   // +yOffset shifts range up when panned

                // ── Segment-level LOD merge state (zero allocation) ─────────
                // When consecutive segments are each < 1 px tall, accumulate
                // their heights and emit one merged rect using the dominant
                // colour (largest-value segment wins — matches DrawLodBars
                // strategy).  This caps output to ~plotH rects per bar
                // regardless of segment count.
                float   mergeH      = 0f;
                Color32 mergeColor  = default;
                float   mergeDomVal = 0f;

                for (int s = 0; s < bar.SegmentCount; s++)
                {
                    ref readonly BarSegment seg = ref segments[bar.SegmentStart + s];
                    // Do NOT cap segH to plotH here. When ZoomY > 1 bars are taller
                    // than the plot area, and the cap would prevent them from ever
                    // reaching the top edge during Y-pan. Real clipping is done
                    // correctly by drawTop/drawH below.
                    float segH = seg.Value * yScale;

                    if (segH < 1f)
                    {
                        // Sub-pixel segment → accumulate into merge buffer.
                        Color32 c = ResolveSegmentColor(seg.Color, alpha);
                        if (seg.Value > mergeDomVal)
                        {
                            mergeDomVal = seg.Value;
                            mergeColor  = c;
                        }
                        mergeH += segH;

                        // Flush the merge buffer when accumulated height reaches 1 px.
                        if (mergeH >= 1f)
                        {
                            float mYTop = yBottom - mergeH;
                            if (mYTop < plotY2 && yBottom > plotTop)
                            {
                                float drawTop = Mathf.Max(mYTop, plotTop);
                                float drawH   = Mathf.Min(yBottom, plotY2) - drawTop;
                                if (drawH >= 0.5f)
                                    AddQuad(mergeColor, x, drawTop, bw, drawH);
                            }
                            yBottom     = mYTop;
                            mergeH      = 0f;
                            mergeDomVal = 0f;
                            mergeColor  = default;

                            if (yBottom <= plotTop) break;
                        }
                        continue;
                    }

                    // ≥ 1 px segment: flush any pending merge buffer first.
                    if (mergeH > 0f)
                    {
                        float mYTop = yBottom - mergeH;
                        if (mYTop < plotY2 && yBottom > plotTop)
                        {
                            float drawTop = Mathf.Max(mYTop, plotTop);
                            float drawH   = Mathf.Min(yBottom, plotY2) - drawTop;
                            if (drawH >= 0.5f)
                                AddQuad(mergeColor, x, drawTop, bw, drawH);
                        }
                        yBottom     = mYTop;
                        mergeH      = 0f;
                        mergeDomVal = 0f;
                        mergeColor  = default;

                        if (yBottom <= plotTop) break;
                    }

                    // Emit this segment as its own rect (existing logic).
                    float yTop = yBottom - segH;
                    if (yTop  >= plotY2)  { yBottom = yTop; continue; }   // below visible
                    if (yBottom <= plotTop) break;                         // above visible

                    float sDrawTop = Mathf.Max(yTop, plotTop);
                    float sDrawH   = Mathf.Min(yBottom, plotY2) - sDrawTop;
                    if (sDrawH < 0.5f) { yBottom = yTop; continue; }

                    Color32 sc = ResolveSegmentColor(seg.Color, alpha);
                    AddQuad(sc, x, sDrawTop, bw, sDrawH);
                    yBottom = yTop;
                }

                // Flush any remaining merge buffer after the segment loop.
                if (mergeH > 0f)
                {
                    float mYTop = yBottom - mergeH;
                    if (mYTop < plotY2 && yBottom > plotTop)
                    {
                        float drawTop = Mathf.Max(mYTop, plotTop);
                        float drawH   = Mathf.Min(yBottom, plotY2) - drawTop;
                        if (drawH >= 0.5f)
                            AddQuad(mergeColor, x, drawTop, bw, drawH);
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LOD bar rendering  (slot < 1 px → pixel-column merging)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawLodBars(
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

            float yScale  = plotH / _effectiveMaxY * _viewState.ZoomY;
            float yOffset = _viewState.PanY * plotH;

            for (int px = 0; px < pixelCount; px++)
            {
                LodPixel lp = _lodBuf[px];
                if (lp.MaxValue <= 0f) continue;

                // Same reasoning as DrawDirectBars: don't cap to plotH here.
                // Use the real uncapped height and clip to the plot boundary below.
                float barH    = lp.MaxValue * yScale;
                float yBottom = plotY2 + yOffset;
                float yTop    = yBottom - barH;

                // Clip to plot area
                float drawTop = Mathf.Max(yTop,    plotY2 - plotH);
                float drawBot = Mathf.Min(yBottom, plotY2);
                float drawH   = drawBot - drawTop;
                if (drawH < 0.5f) continue;

                float x = plotX + px;
                AddQuad(lp.Color, x, drawTop, 1f, drawH);
            }
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
                    // ── Normal mode ─────────────────────────────────────────
                    float selY = plotY2 - plotH;

                    // Fill pass — per-bar rects so highlight only covers bars,
                    // not the gaps between them.  Single path + OddEven is
                    // already correct (no alpha accumulation).
                    p.fillColor = _settings.SelectionFillColor;
                    p.BeginPath();
                    for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
                    {
                        int dataIdx = dispIdx < _viewState.DisplayToData.Length
                            ? _viewState.DisplayToData[dispIdx] : dispIdx;
                        if (!_viewState.SelectedBars.Contains(dataIdx)) continue;
                        SnapBarX(dispIdx, plotX, stride, barW, out float sx, out float sw);
                        PathRect(p, sx, selY, sw, plotH);
                    }
                    p.Fill(FillRule.OddEven);

                    // Rim stroke pass — merge contiguous selected bars into
                    // runs to avoid per-bar stroke alpha accumulation that
                    // makes selection appear more opaque at higher zoom.
                    _selectionRuns.Clear();
                    int runStart = -1;
                    for (int dispIdx = startDisp; dispIdx < endDisp; dispIdx++)
                    {
                        int dataIdx = dispIdx < _viewState.DisplayToData.Length
                            ? _viewState.DisplayToData[dispIdx] : dispIdx;
                        bool selected = _viewState.SelectedBars.Contains(dataIdx);

                        if (selected && runStart < 0)
                            runStart = dispIdx;
                        else if (!selected && runStart >= 0)
                        {
                            _selectionRuns.Add(runStart);
                            _selectionRuns.Add(dispIdx);
                            runStart = -1;
                        }
                    }
                    if (runStart >= 0)
                    {
                        _selectionRuns.Add(runStart);
                        _selectionRuns.Add(endDisp);
                    }

                    p.strokeColor = _settings.SelectionRimColor;
                    p.lineWidth   = 1.5f;
                    for (int i = 0; i < _selectionRuns.Count; i += 2)
                    {
                        int rs = _selectionRuns[i];
                        int re = _selectionRuns[i + 1];
                        SnapBarX(rs,     plotX, stride, barW, out float rx, out _);
                        SnapBarX(re - 1, plotX, stride, barW, out float ex, out float ew);
                        float w = ex + ew - rx;
                        p.BeginPath();
                        PathRect(p, rx, selY, w, plotH);
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
                SnapBarX(displayIdx, plotX, stride, barW, out float fx, out float fw);

                p.strokeColor = _settings.FocusRimColor;
                p.lineWidth   = 2f;
                p.BeginPath();
                PathRect(p, fx - 1f, plotY2 - plotH - 1f, fw + 2f, plotH + 2f);
                p.Stroke();
            }

            // ── Hovered bar tint — always single-bar, always fast ──────────────
            int hovIdx = _viewState.HoveredBarIndex;
            if (hovIdx >= 0 && hovIdx < _model.BarCount)
            {
                int displayIdx = hovIdx < _viewState.DataToDisplay.Length
                    ? _viewState.DataToDisplay[hovIdx] : hovIdx;
                SnapBarX(displayIdx, plotX, stride, barW, out float hx, out float hw);

                // In LOD mode use a 1 px wide tint; in normal mode use the full barW.
                if (lodMode) hw = 1f;

                p.fillColor = _settings.HoverTintColor;
                p.BeginPath();
                PathRect(p, hx, plotY2 - plotH, hw, plotH);
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

        private void EnsureLabelPool()
        {
            int yCount = Mathf.Min(_settings.MaxYLabels + 1, _settings.GridLineCount + 1);
            if (_yLabels.Count == yCount && _xLabels.Count == _settings.MaxXLabels)
                return;
            RebuildLabelPool();
        }

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
                lbl.style.top    = y - _settings.LabelHeight * 0.5f;
                lbl.style.width  = plotX - _settings.YLabelGap;
                lbl.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleRight);
                lbl.visible = true;
            }

            // X-axis bar labels — show as many as fit without overlapping
            CalcViewSlice(plotW, out float stride, out float barW, out int startDisp, out int endDisp);
            int viewCnt    = endDisp - startDisp;
            int showCount  = _settings.XLabelWidth > 0
                ? Mathf.Min(_xLabels.Count, Mathf.FloorToInt(plotW / _settings.XLabelWidth))
                : 0;
            bool show = showCount > 0 && viewCnt > 0;

            for (int j = 0; j < _xLabels.Count; j++)
            {
                Label lbl = _xLabels[j];
                if (!show || j >= showCount) { lbl.visible = false; continue; }

                float step = showCount > 1 ? (float)(viewCnt - 1) / (showCount - 1) : 0f;
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

                SnapBarX(dispIdx, plotX, stride, barW, out float sx, out float sw);
                float x = sx + sw * 0.5f;
                lbl.text = text;
                lbl.style.left   = x - _settings.XLabelWidth * 0.5f;
                lbl.style.top    = plotY2 + _settings.XLabelOffsetY;
                lbl.style.width  = _settings.XLabelWidth;
                lbl.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.UpperCenter);
                lbl.visible = (x >= plotX && x <= plotX + plotW);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Sort index (lazy rebuild, O(N log N) only when dirty)
        // ─────────────────────────────────────────────────────────────────────

        internal void EnsureSortMap()
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
        //  Pixel-snap helper (shared by draw, hit-test, highlights, labels)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the pixel-perfect X position and width for bar at
        /// <paramref name="dispIdx"/> using a single Bresenham distribution
        /// over slot positions: <c>slotEdge(i) = floor(i * totalPx / N)</c>.
        ///
        /// Each slot is either <c>floor(totalPx/N)</c> or that +1 pixel wide,
        /// with the extra pixels maximally spread.  The gap within each slot
        /// is a fixed integer, so all gaps are identical.  Bar width absorbs
        /// the ±1 px slot variation, which is visually masked by the color fill.
        ///
        /// Guarantees:
        ///  • <c>slotRight[i] == slotLeft[i+1]</c> — slots tile perfectly.
        ///  • All gaps are exactly the same pixel width.
        ///  • Bar widths differ by at most 1 px, variation maximally spread.
        ///  • Total coverage == virtual canvas width (no remainder).
        /// </summary>
        private void SnapBarX(int dispIdx, float plotX, float stride, float barW,
                              out float snapX, out float snapW)
        {
            int N = _model.BarCount;
            if (N <= 0) { snapX = plotX; snapW = 1f; return; }

            // Total virtual pixel width for all slots at current zoom.
            int totalPx = Mathf.RoundToInt(N * stride);

            // Fixed integer gap — same for every slot.
            int gapPx = Mathf.Max(0, Mathf.RoundToInt(stride - barW));

            // Bresenham: left edge of this slot and next slot.
            int slotLeft  = (int)((long)dispIdx       * totalPx / N);
            int slotRight = (int)((long)(dispIdx + 1) * totalPx / N);
            int slotW     = slotRight - slotLeft;

            // Bar fills the slot minus the fixed gap.
            int bw = Mathf.Max(1, slotW - gapPx);

            snapX = plotX + slotLeft - _viewState.PanX * stride;
            snapW = bw;
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
                SnapBarX(dispIdx, plotX, stride, barW, out float x, out float bw);
                if (x + bw < rect.xMin) continue;
                if (x > rect.xMax)      break;

                int dataIdx = dispIdx < _viewState.DisplayToData.Length
                    ? _viewState.DisplayToData[dispIdx] : dispIdx;
                if (dataIdx >= _model.BarCount) continue;

                float barH    = Mathf.Min(_model.Bars[dataIdx].TotalValue /
                                          _effectiveMaxY * plotH * _viewState.ZoomY, plotH);
                float barTopY = plotY2 - barH;
                var   barRect = new Rect(x, barTopY, bw, barH);

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
            EnsureLabelPool();
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
        //  Direct mesh quad buffer (zero GC, bypasses Painter2D tessellator)
        // ─────────────────────────────────────────────────────────────────────

        private void AddQuad(Color32 color, float x, float y, float w, float h)
        {
            if (_quadCount == _quadBuf.Length)
                Array.Resize(ref _quadBuf, _quadBuf.Length * 2);

            _quadBuf[_quadCount++] = new QuadData { X = x, Y = y, W = w, H = h, Color = color };
        }

        /// <summary>
        /// Writes all buffered quads to the GPU via chunked
        /// <see cref="MeshGenerationContext.Allocate"/> calls.
        /// Each quad = 4 vertices + 6 indices.  Max 16 383 quads per chunk
        /// (65 532 vertices, under the 65 535 UInt16 index limit).
        /// Multiple chunks per element are supported — no ceiling on total quads.
        /// </summary>
        private void FlushQuads(MeshGenerationContext mgc)
        {
            const int MAX_QUADS_PER_CHUNK = 16383;   // 65 532 / 4
            const int VERTS_PER_QUAD  = 4;
            const int INDICES_PER_QUAD = 6;

            int offset = 0;
            while (offset < _quadCount)
            {
                int chunkQuads = Math.Min(MAX_QUADS_PER_CHUNK, _quadCount - offset);
                int vertCount  = chunkQuads * VERTS_PER_QUAD;
                int idxCount   = chunkQuads * INDICES_PER_QUAD;

                // Pass Texture2D.whiteTexture so that texture × tint = tint.
                // Must remap UVs into uvRegion in case the atlas repacks it.
                MeshWriteData mwd = mgc.Allocate(vertCount, idxCount, Texture2D.whiteTexture);
                Vector2 uv = new Vector2(
                    mwd.uvRegion.x + mwd.uvRegion.width  * 0.5f,
                    mwd.uvRegion.y + mwd.uvRegion.height * 0.5f);

                for (int i = 0; i < chunkQuads; i++)
                {
                    ref QuadData q = ref _quadBuf[offset + i];
                    ushort vi = (ushort)(i * 4);

                    // Four corners: TL, TR, BR, BL
                    mwd.SetNextVertex(new Vertex
                    {
                        position = new Vector3(q.X,       q.Y,       Vertex.nearZ),
                        tint     = q.Color,
                        uv       = uv
                    });
                    mwd.SetNextVertex(new Vertex
                    {
                        position = new Vector3(q.X + q.W, q.Y,       Vertex.nearZ),
                        tint     = q.Color,
                        uv       = uv
                    });
                    mwd.SetNextVertex(new Vertex
                    {
                        position = new Vector3(q.X + q.W, q.Y + q.H, Vertex.nearZ),
                        tint     = q.Color,
                        uv       = uv
                    });
                    mwd.SetNextVertex(new Vertex
                    {
                        position = new Vector3(q.X,       q.Y + q.H, Vertex.nearZ),
                        tint     = q.Color,
                        uv       = uv
                    });

                    // Two triangles: TL-TR-BR, TL-BR-BL
                    mwd.SetNextIndex(vi);
                    mwd.SetNextIndex((ushort)(vi + 1));
                    mwd.SetNextIndex((ushort)(vi + 2));
                    mwd.SetNextIndex(vi);
                    mwd.SetNextIndex((ushort)(vi + 2));
                    mwd.SetNextIndex((ushort)(vi + 3));
                }
                offset += chunkQuads;
            }
            _quadCount = 0;
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
                height      = _settings.LabelHeight,
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
        /// Lightweight quad descriptor: position, size, and colour.
        /// Buffered by <see cref="AddQuad"/>, flushed to GPU by <see cref="FlushQuads"/>.
        /// </summary>
        private struct QuadData
        {
            public float   X, Y, W, H;
            public Color32 Color;
        }
    }
}

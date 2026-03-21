using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;
using BarGraph.Input;
using BarGraph.Input.Handlers;

namespace BarGraph.Runtime
{
    /// <summary>
    /// MonoBehaviour wrapper for <see cref="BarGraphElement"/>.
    /// Drop onto any GameObject with a <see cref="UIDocument"/>.
    /// Exposes the full API including stacked bars, overlay, sort, and events.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("UI Toolkit/Bar Graph Controller")]
    public sealed class BarGraphController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Graph Settings")]
        [SerializeField] private BarGraphSettings _settings = new BarGraphSettings();

        [Header("Container (leave blank for document root)")]
        [Tooltip("USS name of a VisualElement to inject the graph into.")]
        [SerializeField] private string _containerName = "";

        [Header("Sort")]
        [SerializeField] private SortMode _sortMode       = SortMode.None;
        [SerializeField] private bool     _sortDescending = true;

        [Header("Preview Data (Editor only)")]
        [SerializeField] private bool  _generatePreview = true;
        [SerializeField] private int   _previewBarCount = 60;

        // ── Events (Inspector-hookable) ───────────────────────────────────────

        [Header("Events")]
        public UnityEngine.Events.UnityEvent<int>   OnBarClicked;
        public UnityEngine.Events.UnityEvent<int>   OnSelectionChanged;

        // ── Domain-reload view-state persistence ─────────────────────────────

        [SerializeField, HideInInspector]
        private BarGraphViewSnapshot _viewSnapshot;

        // ── Runtime ──────────────────────────────────────────────────────────

        private UIDocument      _doc;
        private BarGraphElement _graph;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake() => _doc = GetComponent<UIDocument>();

        private void OnEnable()
        {
            EnsureGraph();
            if (_generatePreview) SetPreviewData();

            // Restore view state captured before the last domain reload
            if (_viewSnapshot.IsValid)
            {
                _graph.RestoreViewSnapshot(_viewSnapshot);
                _viewSnapshot = default;
            }
        }

        private void OnDisable()
        {
            if (_graph != null)
                _viewSnapshot = _graph.CreateViewSnapshot();

            _graph?.RemoveFromHierarchy();
            _graph = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API – mirrors BarGraphElement
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Set flat float data with a shared colour.</summary>
        public void SetData(IList<float> values, Color? barColor = null)
        {
            EnsureGraph();
            _graph.SetData(values, barColor, _settings);
        }

        /// <summary>Set single-segment BarEntry list.</summary>
        public void SetData(IList<BarEntry> bars)
        {
            EnsureGraph();
            _graph.SetData(bars, _settings);
        }

        /// <summary>Set full stacked bar data.</summary>
        public void SetStackedData(BarEntry[] bars, int barCount, BarSegment[] segments, int segCount)
        {
            EnsureGraph();
            _graph.SetData(bars, barCount, segments, segCount, _settings);
        }

        /// <summary>Set the comparison overlay (flat floats).</summary>
        public void SetOverlay(IList<float> values, Color? color = null)
        {
            EnsureGraph();
            _graph.SetOverlay(values, color);
        }

        public void ClearOverlay()    { EnsureGraph(); _graph.ClearOverlay(); }
        public void ClearData()       { EnsureGraph(); _graph.ClearData(); }
        public void ResetView()       { _graph?.ResetView(); }

        public void AppendBar(float value, Color? color = null, string label = null)
        {
            EnsureGraph();
            _graph.AppendBar(value, color, label);
        }

        public void UpdateSettings(BarGraphSettings s)
        {
            _settings = s;
            _graph?.UpdateSettings(s);
        }

        public void SetSortMode(SortMode mode, bool descending = true)
        {
            _sortMode       = mode;
            _sortDescending = descending;
            _graph?.SetSortMode(mode, descending);
        }

        /// <summary>Direct access to the underlying element for advanced usage.</summary>
        public BarGraphElement GraphElement { get { EnsureGraph(); return _graph; } }

        // ─────────────────────────────────────────────────────────────────────
        //  Internal
        // ─────────────────────────────────────────────────────────────────────

        private void EnsureGraph()
        {
            if (_graph != null) return;
            if (_doc == null || _doc.rootVisualElement == null) return;

            _graph             = new BarGraphElement();
            _graph.style.flexGrow = 1;

            _graph.SetInputSource(new BarGraphUIToolkitInput());
            _graph.AddHandler(new BarGraphHoverHandler());
            var selHandler = new BarGraphSelectionHandler();
            _graph.AddHandler(selHandler);
            _graph.AddHandler(new BarGraphPanHandler());
            _graph.AddHandler(new BarGraphZoomHandler());

            VisualElement container = string.IsNullOrEmpty(_containerName)
                ? _doc.rootVisualElement
                : (_doc.rootVisualElement.Q(_containerName) ?? _doc.rootVisualElement);

            container.Add(_graph);

            // Wire up Unity events
            selHandler.BarClicked   += args => OnBarClicked?.Invoke(args.DataIndex);
            _graph.SelectionChanged += args => OnSelectionChanged?.Invoke(args.SelectedDataIndices.Count);

            // Apply initial sort
            _graph.SetSortMode(_sortMode, _sortDescending);
        }

        private void SetPreviewData()
        {
            var data  = new List<float>(_previewBarCount);
            var over  = new List<float>(_previewBarCount);
            float prev = 50f;
            for (int i = 0; i < _previewBarCount; i++)
            {
                prev = Mathf.Clamp(prev + Random.Range(-12f, 12f), 2f, 100f);
                data.Add(prev);
                over.Add(prev * Random.Range(0.7f, 1.3f));
            }
            SetData(data, new Color(0.25f, 0.6f, 1f));
            SetOverlay(over, new Color(1f, 0.6f, 0.2f));
        }
    }
}

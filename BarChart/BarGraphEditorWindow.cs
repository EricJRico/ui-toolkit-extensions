#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Editor
{
    /// <summary>
    /// EditorWindow demo for the BarGraph V2 toolkit.
    /// Open via:  Window ▶ BarGraph ▶ Bar Graph Viewer
    ///
    /// Interaction
    /// ────────────
    ///  Scroll wheel          Zoom X
    ///  Shift + Scroll        Zoom Y
    ///  Middle-drag           Pan
    ///  Alt + Left-drag       Pan (alternative)
    ///  Left-click            Select bar
    ///  Ctrl + Left-click     Toggle bar in selection
    ///  Left-drag             Rubber-band multi-select
    ///  Arrow keys            Move keyboard focus
    ///  Shift + Arrow         Extend selection
    ///  Ctrl + A              Select all
    ///  Escape                Clear selection
    ///  Ctrl + R              Reset view
    /// </summary>
    public sealed class BarGraphEditorWindow : EditorWindow
    {
        // ── Menu ──────────────────────────────────────────────────────────────
        [MenuItem("Window/BarGraph/Bar Graph Viewer")]
        public static void Open()
        {
            var w = GetWindow<BarGraphEditorWindow>();
            w.titleContent = new GUIContent("Bar Graph Viewer", EditorGUIUtility.IconContent("d_UnityEditor.ProfilerWindow").image);
            w.minSize      = new Vector2(480, 320);
            w.Show();
        }

        // ── Live state ────────────────────────────────────────────────────────
        private BarGraphElement _graph;

        // Toolbar dropdowns / toggle state
        private DatasetMode _datasetMode   = DatasetMode.SineWave;
        private int         _barCount      = 120;
        private int         _segmentCount  = 3;
        private bool        _showOverlay   = false;
        private SortMode    _sortMode      = SortMode.None;
        private bool        _sortDesc      = true;
        private bool        _showGrid      = true;
        private bool        _showAxes      = true;
        private bool        _enableZoomY   = false;

        // Status bar labels (updated via events)
        private Label _statusHover;
        private Label _statusSelection;
        private Label _statusZoom;

        // ── EditorWindow lifecycle ────────────────────────────────────────────

        private void OnEnable()
        {
            // Clear any stale UI left from domain reload
            rootVisualElement.Clear();
            BuildUI();
            RefreshData();
        }

        private void OnDisable()
        {
            // Unsubscribe events so the GC can collect the graph cleanly
            if (_graph != null)
            {
                _graph.HoverChanged     -= OnHover;
                _graph.SelectionChanged -= OnSelection;
                _graph.ViewChanged      -= OnViewChanged;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI construction
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow      = 1;

            // ── Toolbar ───────────────────────────────────────────────────────
            root.Add(BuildToolbar());

            // ── Graph (fills remaining space) ─────────────────────────────────
            _graph = new BarGraphElement();
            _graph.style.flexGrow = 1;

            // Wire events before adding to hierarchy
            _graph.HoverChanged     += OnHover;
            _graph.SelectionChanged += OnSelection;
            _graph.ViewChanged      += OnViewChanged;
            _graph.BarClicked       += OnBarClicked;

            root.Add(_graph);

            // ── Status bar ────────────────────────────────────────────────────
            root.Add(BuildStatusBar());
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection  = FlexDirection.Row;
            bar.style.flexWrap       = Wrap.Wrap;
            bar.style.paddingLeft    = 4;
            bar.style.paddingRight   = 4;
            bar.style.paddingTop     = 3;
            bar.style.paddingBottom  = 3;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            bar.style.backgroundColor   = new Color(0.22f, 0.22f, 0.22f, 1f);

            // ── Dataset preset ────────────────────────────────────────────────
            bar.Add(TbLabel("Dataset:"));
            var modeField = new EnumField(_datasetMode);
            modeField.style.width    = 110;
            modeField.style.marginRight = 6;
            modeField.RegisterValueChangedCallback(e =>
            {
                _datasetMode = (DatasetMode)e.newValue;
                RefreshData();
            });
            bar.Add(modeField);

            // ── Bar count ─────────────────────────────────────────────────────
            bar.Add(TbLabel("Bars:"));
            var barCountField = new IntegerField { value = _barCount };
            barCountField.style.width    = 60;
            barCountField.style.marginRight = 6;
            barCountField.RegisterValueChangedCallback(e =>
            {
                _barCount = Mathf.Clamp(e.newValue, 1, 100_000);
                barCountField.SetValueWithoutNotify(_barCount);
                RefreshData();
            });
            bar.Add(barCountField);

            // ── Segments (stacked mode only) ──────────────────────────────────
            bar.Add(TbLabel("Segs:"));
            var segField = new IntegerField { value = _segmentCount };
            segField.style.width    = 40;
            segField.style.marginRight = 6;
            segField.RegisterValueChangedCallback(e =>
            {
                _segmentCount = Mathf.Clamp(e.newValue, 1, 8);
                segField.SetValueWithoutNotify(_segmentCount);
                RefreshData();
            });
            bar.Add(segField);

            // ── Separator ─────────────────────────────────────────────────────
            bar.Add(TbSep());

            // ── Sort ──────────────────────────────────────────────────────────
            bar.Add(TbLabel("Sort:"));
            var sortField = new EnumField(_sortMode);
            sortField.style.width    = 80;
            sortField.style.marginRight = 2;
            sortField.RegisterValueChangedCallback(e =>
            {
                _sortMode = (SortMode)e.newValue;
                _graph?.SetSortMode(_sortMode, _sortDesc);
            });
            bar.Add(sortField);

            var descToggle = new Toggle { value = _sortDesc, tooltip = "Descending" };
            descToggle.style.marginRight = 6;
            descToggle.RegisterValueChangedCallback(e =>
            {
                _sortDesc = e.newValue;
                _graph?.SetSortMode(_sortMode, _sortDesc);
            });
            bar.Add(descToggle);
            bar.Add(TbLabel("Desc"));
            bar.Add(TbSep());

            // ── Overlay ───────────────────────────────────────────────────────
            var overlayToggle = new Toggle { value = _showOverlay, tooltip = "Show overlay series" };
            overlayToggle.RegisterValueChangedCallback(e =>
            {
                _showOverlay = e.newValue;
                RefreshData();
            });
            bar.Add(overlayToggle);
            bar.Add(TbLabel("Overlay"));
            bar.Add(TbSep());

            // ── Grid / Axes toggles ───────────────────────────────────────────
            var gridToggle = new Toggle { value = _showGrid, tooltip = "Show grid" };
            gridToggle.RegisterValueChangedCallback(e =>
            {
                _showGrid = e.newValue;
                ApplySettings();
            });
            bar.Add(gridToggle);
            bar.Add(TbLabel("Grid"));

            var axesToggle = new Toggle { value = _showAxes, tooltip = "Show axes" };
            axesToggle.RegisterValueChangedCallback(e =>
            {
                _showAxes = e.newValue;
                ApplySettings();
            });
            bar.Add(axesToggle);
            bar.Add(TbLabel("Axes"));
            bar.Add(TbSep());

            // ── Zoom Y toggle ─────────────────────────────────────────────────
            var zoomYToggle = new Toggle { value = _enableZoomY, tooltip = "Enable Shift+Scroll to zoom Y axis" };
            zoomYToggle.RegisterValueChangedCallback(e =>
            {
                _enableZoomY = e.newValue;
                ApplySettings();
            });
            bar.Add(zoomYToggle);
            bar.Add(TbLabel("Y-Zoom"));
            bar.Add(TbSep());

            // ── Buttons ───────────────────────────────────────────────────────
            bar.Add(TbButton("Reset View", () => _graph?.ResetView()));
            bar.Add(TbButton("Randomise",  RefreshData));

            return bar;
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection     = FlexDirection.Row;
            bar.style.paddingLeft       = 8;
            bar.style.paddingRight      = 8;
            bar.style.paddingTop        = 2;
            bar.style.paddingBottom     = 2;
            bar.style.borderTopWidth    = 1;
            bar.style.borderTopColor    = new Color(0.15f, 0.15f, 0.15f, 1f);
            bar.style.backgroundColor   = new Color(0.19f, 0.19f, 0.19f, 1f);

            _statusHover     = StatusLabel("Hover: –");
            _statusSelection = StatusLabel("Selected: 0");
            _statusZoom      = StatusLabel("Zoom: 1.00×");

            bar.Add(_statusHover);
            bar.Add(TbSep());
            bar.Add(_statusSelection);
            bar.Add(TbSep());
            bar.Add(_statusZoom);

            // Keyboard shortcut hint (right-aligned)
            var hint = new Label("Scroll=ZoomX  Shift+Scroll=ZoomY  Middle/Alt+Drag=Pan  Ctrl+Click=Multi-select");
            hint.style.fontSize       = 9;
            hint.style.color          = new Color(0.5f, 0.5f, 0.5f, 1f);
            hint.style.flexGrow       = 1;
            hint.style.unityTextAlign = TextAnchor.MiddleRight;
            bar.Add(hint);

            return bar;
        }

        // ── Toolbar helpers ───────────────────────────────────────────────────

        private static Label TbLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize       = 11;
            l.style.color          = new Color(0.78f, 0.78f, 0.78f, 1f);
            l.style.marginLeft     = 2;
            l.style.marginRight    = 2;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        private static Label StatusLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize       = 10;
            l.style.color          = new Color(0.65f, 0.65f, 0.65f, 1f);
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.marginRight    = 4;
            return l;
        }

        private static Button TbButton(string text, System.Action action)
        {
            var b = new Button(action) { text = text };
            b.style.marginLeft  = 2;
            b.style.marginRight = 2;
            b.style.paddingLeft  = 8;
            b.style.paddingRight = 8;
            b.style.height = 20;
            return b;
        }

        private static VisualElement TbSep()
        {
            var s = new VisualElement();
            s.style.width           = 1;
            s.style.marginLeft      = 4;
            s.style.marginRight     = 4;
            s.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            s.style.alignSelf       = Align.Stretch;
            return s;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Data generation
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshData()
        {
            if (_graph == null) return;
            ApplySettings();

            switch (_datasetMode)
            {
                case DatasetMode.SineWave:    LoadSineWave();    break;
                case DatasetMode.Random:      LoadRandom();      break;
                case DatasetMode.Stacked:     LoadStacked();     break;
                case DatasetMode.Gaussian:    LoadGaussian();    break;
                case DatasetMode.StressTest:  LoadStress();      break;
            }

            if (_showOverlay)
                LoadOverlay();
            else
                _graph.ClearOverlay();

            _graph.SetSortMode(_sortMode, _sortDesc);
        }

        private void ApplySettings()
        {
            if (_graph == null) return;
            var s = new BarGraphSettings
            {
                ShowGrid        = _showGrid,
                ShowAxes        = _showAxes,
                EnableMouseZoomY = _enableZoomY,
                EnableYPan      = _enableZoomY,
            };
            _graph.UpdateSettings(s);
        }

        // ── Dataset loaders ───────────────────────────────────────────────────

        private void LoadSineWave()
        {
            int n = _barCount;
            var values = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float v = 50f + 40f * Mathf.Sin(t * Mathf.PI * 6f)
                               + 15f * Mathf.Sin(t * Mathf.PI * 18f);
                values.Add(Mathf.Max(0f, v));
            }
            _graph.SetData(values, new Color(0.3f, 0.65f, 1f));
        }

        private void LoadRandom()
        {
            int n = _barCount;
            var values = new List<float>(n);
            float prev = 50f;
            for (int i = 0; i < n; i++)
            {
                prev = Mathf.Clamp(prev + Random.Range(-12f, 12f), 2f, 100f);
                values.Add(prev);
            }
            _graph.SetData(values, new Color(0.35f, 0.85f, 0.5f));
        }

        private void LoadGaussian()
        {
            int n = _barCount;
            // Build a histogram-like normal distribution
            var values = new float[n];
            for (int sample = 0; sample < n * 80; sample++)
            {
                // Box-Muller
                float u1   = Mathf.Max(1e-6f, Random.value);
                float u2   = Random.value;
                float norm = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
                int   bin  = Mathf.Clamp(Mathf.RoundToInt(norm * (n / 6f) + n / 2f), 0, n - 1);
                values[bin]++;
            }
            var list = new List<float>(values);
            _graph.SetData(list, new Color(0.9f, 0.55f, 0.2f));
        }

        private static readonly Color32[] StackPalette =
        {
            new Color32( 65, 150, 255, 255),
            new Color32( 50, 200, 130, 255),
            new Color32(235, 155,  50, 255),
            new Color32(220,  80, 100, 255),
            new Color32(160,  90, 220, 255),
            new Color32( 80, 200, 220, 255),
            new Color32(240, 200,  60, 255),
            new Color32(200, 120,  80, 255),
        };

        private void LoadStacked()
        {
            int n      = _barCount;
            int segs   = Mathf.Clamp(_segmentCount, 1, StackPalette.Length);
            int total  = n * segs;

            var bars = new BarEntry[n];
            var segArr = new BarSegment[total];
            int cursor = 0;

            for (int b = 0; b < n; b++)
            {
                float sum   = 0f;
                int   start = cursor;
                for (int s = 0; s < segs; s++)
                {
                    float v = Random.Range(5f, 30f);
                    sum += v;
                    segArr[cursor++] = new BarSegment(v, StackPalette[s]);
                }
                bars[b] = new BarEntry(start, segs, sum, b.ToString());
            }
            _graph.SetData(bars, n, segArr, total);
        }

        private void LoadStress()
        {
            int n = _barCount;
            var segArr = new BarSegment[n];
            var barArr = new BarEntry[n];
            var gradient = new Color32[]
            {
                new Color32(0, 255, 128, 255),
                new Color32(128, 255, 0, 255),
                new Color32(255, 220, 0, 255),
                new Color32(255, 80,  0, 255),
            };

            for (int i = 0; i < n; i++)
            {
                float t   = (float)i / n;
                float v   = 50f + 45f * Mathf.Sin(i * 0.07f) * Mathf.Cos(i * 0.013f);
                int   ci  = Mathf.FloorToInt(t * (gradient.Length - 1));
                float lt  = t * (gradient.Length - 1) - ci;
                int   ci2 = Mathf.Min(ci + 1, gradient.Length - 1);
                Color32 c = Color32.Lerp(gradient[ci], gradient[ci2], lt);
                segArr[i] = new BarSegment(v, c);
                barArr[i] = new BarEntry(i, 1, v);
            }
            _graph.SetData(barArr, n, segArr, n);
        }

        private void LoadOverlay()
        {
            int n = _graph.ViewState != null ? _barCount : 0;
            var values = new List<float>(n);
            for (int i = 0; i < n; i++)
                values.Add(Random.Range(10f, 90f));
            _graph.SetOverlay(values, new Color(1f, 0.7f, 0.2f, 0.6f));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Event handlers → status bar updates
        // ─────────────────────────────────────────────────────────────────────

        private void OnHover(HoverChangedEventArgs e)
        {
            if (_statusHover == null) return;
            _statusHover.text = e.DataIndex >= 0
                ? $"Hover: bar {e.DataIndex}  val {e.TotalValue:F1}"
                : "Hover: –";
        }

        private void OnSelection(SelectionChangedEventArgs e)
        {
            if (_statusSelection == null) return;
            _statusSelection.text = $"Selected: {e.SelectedDataIndices.Count}";
        }

        private void OnViewChanged(ViewChangedEventArgs e)
        {
            if (_statusZoom == null) return;
            _statusZoom.text = $"Zoom X {e.ZoomX:F2}×  Y {e.ZoomY:F2}×  Pan {e.PanX:F1}";
        }

        private void OnBarClicked(BarClickedEventArgs e)
        {
            // Log to console so the user can see click events
            Debug.Log($"[BarGraph] Bar clicked  dataIndex={e.DataIndex}  value={e.TotalValue:F2}");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Dataset mode enum
        // ─────────────────────────────────────────────────────────────────────

        private enum DatasetMode
        {
            SineWave,
            Random,
            Gaussian,
            Stacked,
            StressTest,
        }
    }
}
#endif

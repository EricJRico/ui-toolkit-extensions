using System;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;
using BarGraph.Events;
using BarGraph.Input;
using BarGraph.Input.Handlers;

namespace BarGraph.EditorDemo
{
    /// <summary>
    /// Serializable state for a single gallery panel.
    /// Survives domain reload via [SerializeField] on the EditorWindow.
    /// </summary>
    [Serializable]
    public class GalleryPanelState
    {
        public DatasetPreset Preset        = DatasetPreset.SineWave;
        public int           BarCount      = 120;
        public int           SegmentCount  = 3;
        public bool          ShowOverlay;
        public bool          ShowGrid      = true;
        public bool          ShowAxes      = true;
        public SortMode      SortMode      = SortMode.None;
        public bool          SortDesc      = true;
        public bool          EnableZoomY;
        public BarGraphViewSnapshot ViewSnapshot;
    }

    /// <summary>Dataset preset types available in the gallery.</summary>
    public enum DatasetPreset
    {
        SineWave,
        Random,
        Gaussian,
        Stacked,
        StressTest,
        ManyStacks,
    }

    /// <summary>
    /// Encapsulates a single panel in the editor gallery:
    /// graph element, compact toolbar, and state management.
    /// </summary>
    public class GalleryPanel
    {
        public BarGraphElement Graph { get; private set; }
        public VisualElement   Root  { get; private set; }
        public GalleryPanelState State { get; private set; }

        private Label _titleLabel;
        private Label _statusLabel;
        private VisualElement _toolbar;
        private bool _toolbarVisible;

        public GalleryPanel(GalleryPanelState state)
        {
            State = state;
        }

        // ── UI construction ─────────────────────────────────────────────────

        public VisualElement BuildUI()
        {
            Root = new VisualElement();
            Root.style.width          = Length.Percent(50);
            Root.style.minHeight      = 280;
            Root.style.paddingTop     = 4;
            Root.style.paddingBottom  = 4;
            Root.style.paddingLeft    = 4;
            Root.style.paddingRight   = 4;
            Root.style.flexDirection  = FlexDirection.Column;

            // Card with border
            var card = new VisualElement();
            card.style.flexGrow         = 1;
            card.style.flexDirection    = FlexDirection.Column;
            card.style.backgroundColor  = new Color(0.18f, 0.18f, 0.18f, 1f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;
            card.style.borderTopWidth = card.style.borderRightWidth =
                card.style.borderBottomWidth = card.style.borderLeftWidth = 1;
            card.style.borderTopColor = card.style.borderRightColor =
                card.style.borderBottomColor = card.style.borderLeftColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            Root.Add(card);

            // Header row (title + gear toggle)
            var header = new VisualElement
            {
                style =
                {
                    flexDirection   = FlexDirection.Row,
                    paddingLeft     = 6,
                    paddingRight    = 4,
                    paddingTop      = 3,
                    paddingBottom   = 2,
                    backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
                    alignItems      = Align.Center,
                    borderTopLeftRadius  = 4,
                    borderTopRightRadius = 4
                }
            };
            card.Add(header);

            _titleLabel = new Label(PresetDisplayName(State.Preset))
            {
                style =
                {
                    fontSize              = 11,
                    color                 = new Color(0.82f, 0.82f, 0.85f, 1f),
                    flexGrow              = 1,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            };
            header.Add(_titleLabel);

            var gearBtn = new Button(() => ToggleToolbar()) { text = "\u2699" };
            gearBtn.style.width       = 22;
            gearBtn.style.height      = 18;
            gearBtn.style.fontSize    = 12;
            gearBtn.style.paddingLeft = gearBtn.style.paddingRight = 0;
            gearBtn.style.marginLeft  = 4;
            header.Add(gearBtn);

            var randomizeBtn = new Button(() => RefreshData()) { text = "\u21BB" };
            randomizeBtn.style.width       = 22;
            randomizeBtn.style.height      = 18;
            randomizeBtn.style.fontSize    = 12;
            randomizeBtn.style.paddingLeft = randomizeBtn.style.paddingRight = 0;
            randomizeBtn.style.marginLeft  = 2;
            randomizeBtn.tooltip           = "Randomize data";
            header.Add(randomizeBtn);

            // Collapsible toolbar
            _toolbar = BuildToolbar();
            _toolbar.style.display = DisplayStyle.None;
            card.Add(_toolbar);

            // Graph
            Graph = new BarGraphElement();
            Graph.style.flexGrow = 1;
            Graph.SetInputSource(new BarGraphUIToolkitInput());
            Graph.AddHandler(new BarGraphHoverHandler());
            Graph.AddHandler(new BarGraphSelectionHandler());
            Graph.AddHandler(new BarGraphPanHandler());
            Graph.AddHandler(new BarGraphZoomHandler());
            Graph.FormatYLabel = v => v >= 1000f ? $"{v / 1000f:F1}K" : $"{v:F0}";
            card.Add(Graph);

            // Status line
            _statusLabel = new Label("Hover: \u2013")
            {
                style =
                {
                    fontSize        = 9,
                    color           = new Color(0.55f, 0.55f, 0.55f, 1f),
                    paddingLeft     = 6,
                    paddingTop      = 2,
                    paddingBottom   = 2
                }
            };
            card.Add(_statusLabel);

            // Wire status events
            Graph.HoverChanged += e =>
            {
                if (_statusLabel == null) return;
                _statusLabel.text = e.DataIndex >= 0
                    ? $"Hover: bar {e.DataIndex}  val {e.TotalValue:F1}"
                    : "Hover: \u2013";
            };

            return Root;
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection    = FlexDirection.Row,
                    flexWrap         = Wrap.Wrap,
                    paddingLeft      = 4,
                    paddingRight     = 4,
                    paddingTop       = 3,
                    paddingBottom    = 3,
                    backgroundColor  = new Color(0.20f, 0.20f, 0.20f, 1f),
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0.12f, 0.12f, 0.12f, 1f)
                }
            };

            // Preset dropdown
            bar.Add(TbLabel("Preset:"));
            var presetField = new EnumField(State.Preset);
            presetField.style.width       = 100;
            presetField.style.marginRight = 4;
            presetField.RegisterValueChangedCallback(e =>
            {
                State.Preset = (DatasetPreset)e.newValue;
                _titleLabel.text = PresetDisplayName(State.Preset);
                RefreshData();
            });
            bar.Add(presetField);

            // Bar count
            bar.Add(TbLabel("Bars:"));
            var barField = new IntegerField { value = State.BarCount };
            barField.style.width       = 55;
            barField.style.marginRight = 4;
            barField.RegisterValueChangedCallback(e =>
            {
                State.BarCount = Mathf.Clamp(e.newValue, 1, 100_000);
                barField.SetValueWithoutNotify(State.BarCount);
                RefreshData();
            });
            bar.Add(barField);

            // Segment count
            bar.Add(TbLabel("Segs:"));
            var segField = new IntegerField { value = State.SegmentCount };
            segField.style.width       = 45;
            segField.style.marginRight = 4;
            segField.RegisterValueChangedCallback(e =>
            {
                State.SegmentCount = Mathf.Clamp(e.newValue, 1, 1000);
                segField.SetValueWithoutNotify(State.SegmentCount);
                RefreshData();
            });
            bar.Add(segField);

            // Sort toggle
            bar.Add(TbSep());
            var sortField = new EnumField(State.SortMode);
            sortField.style.width       = 70;
            sortField.style.marginRight = 2;
            sortField.RegisterValueChangedCallback(e =>
            {
                State.SortMode = (SortMode)e.newValue;
                Graph?.SetSortMode(State.SortMode, State.SortDesc);
            });
            bar.Add(sortField);

            // Overlay toggle
            bar.Add(TbSep());
            var overlayToggle = new Toggle { value = State.ShowOverlay, tooltip = "Overlay" };
            overlayToggle.RegisterValueChangedCallback(e =>
            {
                State.ShowOverlay = e.newValue;
                RefreshData();
            });
            bar.Add(overlayToggle);
            bar.Add(TbLabel("Overlay"));

            // Grid toggle
            var gridToggle = new Toggle { value = State.ShowGrid, tooltip = "Grid" };
            gridToggle.RegisterValueChangedCallback(e =>
            {
                State.ShowGrid = e.newValue;
                ApplySettings();
            });
            bar.Add(gridToggle);
            bar.Add(TbLabel("Grid"));

            return bar;
        }

        private void ToggleToolbar()
        {
            _toolbarVisible = !_toolbarVisible;
            _toolbar.style.display = _toolbarVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Data management ─────────────────────────────────────────────────

        public void RefreshData()
        {
            if (Graph == null) return;
            ApplySettings();

            switch (State.Preset)
            {
                case DatasetPreset.SineWave:   GalleryDataGenerators.LoadSineWave(Graph, State.BarCount); break;
                case DatasetPreset.Random:     GalleryDataGenerators.LoadRandom(Graph, State.BarCount);   break;
                case DatasetPreset.Gaussian:   GalleryDataGenerators.LoadGaussian(Graph, State.BarCount); break;
                case DatasetPreset.Stacked:    GalleryDataGenerators.LoadStacked(Graph, State.BarCount, State.SegmentCount, false); break;
                case DatasetPreset.StressTest: GalleryDataGenerators.LoadStress(Graph, State.BarCount);   break;
                case DatasetPreset.ManyStacks: GalleryDataGenerators.LoadManyStacks(Graph, State.BarCount, State.SegmentCount); break;
            }

            if (State.ShowOverlay)
                GalleryDataGenerators.LoadOverlay(Graph, State.BarCount);
            else
                Graph.ClearOverlay();

            Graph.SetSortMode(State.SortMode, State.SortDesc);
        }

        private void ApplySettings()
        {
            if (Graph == null) return;
            var s = new BarGraphSettings
            {
                ShowGrid         = State.ShowGrid,
                ShowAxes         = State.ShowAxes,
                EnableMouseZoomY = State.EnableZoomY,
                EnableYPan       = State.EnableZoomY,
            };
            Graph.UpdateSettings(s);
        }

        // ── Snapshots ───────────────────────────────────────────────────────

        public GalleryPanelState CreateSnapshot()
        {
            if (Graph != null)
                State.ViewSnapshot = Graph.CreateViewSnapshot();
            return State;
        }

        public void RestoreSnapshot(GalleryPanelState state)
        {
            State = state;
            if (Graph != null && state.ViewSnapshot.IsValid)
            {
                Graph.RestoreViewSnapshot(state.ViewSnapshot);
                state.ViewSnapshot = default;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static Label TbLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize       = 10;
            l.style.color          = new Color(0.70f, 0.70f, 0.70f, 1f);
            l.style.marginLeft     = 2;
            l.style.marginRight    = 2;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        private static VisualElement TbSep()
        {
            var s = new VisualElement();
            s.style.width           = 1;
            s.style.marginLeft      = 3;
            s.style.marginRight     = 3;
            s.style.backgroundColor = new Color(0.30f, 0.30f, 0.30f, 1f);
            s.style.alignSelf       = Align.Stretch;
            return s;
        }

        private static string PresetDisplayName(DatasetPreset preset)
        {
            switch (preset)
            {
                case DatasetPreset.SineWave:   return "Sine Wave";
                case DatasetPreset.Random:     return "Random Walk";
                case DatasetPreset.Gaussian:   return "Gaussian";
                case DatasetPreset.Stacked:    return "Stacked";
                case DatasetPreset.StressTest: return "Stress Test";
                case DatasetPreset.ManyStacks: return "Many Stacks (HSL)";
                default:                       return preset.ToString();
            }
        }
    }
}

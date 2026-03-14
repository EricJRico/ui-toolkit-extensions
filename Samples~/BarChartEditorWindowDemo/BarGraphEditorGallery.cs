using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.EditorDemo
{
    /// <summary>
    /// Multi-panel EditorWindow gallery showcasing all BarGraph features.
    /// Open via:  Window > BarGraph > Bar Graph Gallery
    ///
    /// Interaction (per panel)
    /// ────────────────────────
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
    public sealed class BarGraphEditorGallery : EditorWindow
    {
        // ── Menu ────────────────────────────────────────────────────────────
        [MenuItem("Window/BarGraph/Bar Graph Gallery")]
        public static void Open()
        {
            var w = GetWindow<BarGraphEditorGallery>();
            w.titleContent = new GUIContent("Bar Graph Gallery",
                EditorGUIUtility.IconContent("d_UnityEditor.ProfilerWindow").image);
            w.minSize = new Vector2(640, 480);
            w.Show();
        }

        // ── Serialized state (survives domain reload) ───────────────────────
        [SerializeField] private List<GalleryPanelState> _panelStates;
        [SerializeField] private bool  _animating;
        [SerializeField] private float _animationSpeed = 1f;

        // ── Transient ───────────────────────────────────────────────────────
        private List<GalleryPanel> _panels;
        private GalleryAnimator    _animator;
        private VisualElement      _galleryContainer;
        private Label              _globalStatus;
        private Button             _playPauseBtn;

        // ── Lifecycle ───────────────────────────────────────────────────────

        private void OnEnable()
        {
            rootVisualElement.Clear();
            BuildUI();

            // First open: create default 6-panel gallery
            if (_panelStates == null || _panelStates.Count == 0)
                _panelStates = CreateDefaultPanelStates();

            // Rebuild panels from state
            _panels = new List<GalleryPanel>();
            foreach (var state in _panelStates)
            {
                var panel = new GalleryPanel(state);
                _galleryContainer.Add(panel.BuildUI());
                panel.RefreshData();
                panel.RestoreSnapshot(state);
                _panels.Add(panel);
            }

            // Animator
            _animator = new GalleryAnimator(_panels);
            _animator.Speed = _animationSpeed;
            if (_animating)
                _animator.Play();

            UpdatePlayPauseLabel();
        }

        private void OnDisable()
        {
            // Capture snapshots for domain reload
            if (_panels != null)
            {
                _panelStates = new List<GalleryPanelState>();
                foreach (var panel in _panels)
                    _panelStates.Add(panel.CreateSnapshot());
            }

            _animator?.Stop();
        }

        private void Update()
        {
            if (_animator != null && _animator.Tick())
                Repaint();
        }

        // ── UI construction ─────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow     = 1;

            // Global toolbar
            root.Add(BuildGlobalToolbar());

            // Scrollable gallery
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            _galleryContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap      = Wrap.Wrap
                }
            };
            scroll.Add(_galleryContainer);

            // Global status bar
            root.Add(BuildStatusBar());
        }

        private VisualElement BuildGlobalToolbar()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection    = FlexDirection.Row,
                    flexWrap         = Wrap.Wrap,
                    paddingLeft      = 6,
                    paddingRight     = 6,
                    paddingTop       = 3,
                    paddingBottom    = 3,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0.15f, 0.15f, 0.15f, 1f),
                    backgroundColor   = new Color(0.22f, 0.22f, 0.22f, 1f),
                    alignItems        = Align.Center
                }
            };

            // Title
            bar.Add(new Label("Bar Graph Gallery")
            {
                style =
                {
                    fontSize              = 12,
                    color                 = new Color(0.85f, 0.85f, 0.88f, 1f),
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginRight           = 12
                }
            });

            // Animation controls
            _playPauseBtn = TbButton(_animating ? "\u23F8 Pause" : "\u25B6 Play", () =>
            {
                _animating = !_animating;
                if (_animating)
                    _animator?.Play();
                else
                    _animator?.Pause();
                UpdatePlayPauseLabel();
            });
            bar.Add(_playPauseBtn);

            bar.Add(TbButton("\u23F9 Stop", () =>
            {
                _animating = false;
                _animator?.Stop();
                UpdatePlayPauseLabel();
            }));

            bar.Add(TbLabel("Speed:"));
            var speedSlider = new Slider(0.1f, 5f) { value = _animationSpeed };
            speedSlider.style.width       = 80;
            speedSlider.style.marginRight = 4;
            speedSlider.RegisterValueChangedCallback(e =>
            {
                _animationSpeed = e.newValue;
                if (_animator != null) _animator.Speed = _animationSpeed;
            });
            bar.Add(speedSlider);

            bar.Add(TbSep());

            // Data controls
            bar.Add(TbButton("Randomize All", () =>
            {
                if (_panels == null) return;
                foreach (var p in _panels) p.RefreshData();
            }));

            bar.Add(TbButton("Reset All Views", () =>
            {
                if (_panels == null) return;
                foreach (var p in _panels) p.Graph?.ResetView();
            }));

            return bar;
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection   = FlexDirection.Row,
                    paddingLeft     = 8,
                    paddingRight    = 8,
                    paddingTop      = 2,
                    paddingBottom   = 2,
                    borderTopWidth  = 1,
                    borderTopColor  = new Color(0.15f, 0.15f, 0.15f, 1f),
                    backgroundColor = new Color(0.19f, 0.19f, 0.19f, 1f)
                }
            };

            _globalStatus = new Label("6 panels \u2014 Scroll=ZoomX  Shift+Scroll=ZoomY  Middle/Alt+Drag=Pan  Ctrl+Click=Multi-select")
            {
                style =
                {
                    fontSize   = 9,
                    color      = new Color(0.5f, 0.5f, 0.5f, 1f),
                    flexGrow   = 1
                }
            };
            bar.Add(_globalStatus);

            return bar;
        }

        private void UpdatePlayPauseLabel()
        {
            if (_playPauseBtn != null)
                _playPauseBtn.text = _animating ? "\u23F8 Pause" : "\u25B6 Play";
        }

        // ── Default panel states ────────────────────────────────────────────

        private static List<GalleryPanelState> CreateDefaultPanelStates()
        {
            return new List<GalleryPanelState>
            {
                new GalleryPanelState { Preset = DatasetPreset.SineWave,   BarCount = 120,   SegmentCount = 1  },
                new GalleryPanelState { Preset = DatasetPreset.Random,     BarCount = 80,    SegmentCount = 1  },
                new GalleryPanelState { Preset = DatasetPreset.Gaussian,   BarCount = 80,    SegmentCount = 1  },
                new GalleryPanelState { Preset = DatasetPreset.Stacked,    BarCount = 60,    SegmentCount = 4  },
                new GalleryPanelState { Preset = DatasetPreset.StressTest, BarCount = 10000, SegmentCount = 1  },
                new GalleryPanelState { Preset = DatasetPreset.ManyStacks, BarCount = 40,    SegmentCount = 100 },
            };
        }

        // ── Toolbar helpers ─────────────────────────────────────────────────

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

        private static Button TbButton(string text, System.Action action)
        {
            var b = new Button(action) { text = text };
            b.style.marginLeft   = 2;
            b.style.marginRight  = 2;
            b.style.paddingLeft  = 8;
            b.style.paddingRight = 8;
            b.style.height       = 20;
            b.style.fontSize     = 10;
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
    }
}

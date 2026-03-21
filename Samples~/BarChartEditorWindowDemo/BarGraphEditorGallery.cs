using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;
using BarGraph.Demo;
using BarGraph.Events;
using BarGraph.Input.Handlers;

namespace BarGraph.EditorDemo
{
    /// <summary>
    /// Multi-panel EditorWindow gallery showcasing all BarGraph features.
    /// Hosts the same <see cref="DemoPanel"/> instances used by the runtime demo.
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
        [SerializeField] private List<BarGraphViewSnapshot> _snapshots;
        [SerializeField] private bool  _animating;
        [SerializeField] private float _animationSpeed = 1f;

        // ── Transient ───────────────────────────────────────────────────────
        private List<DemoPanel> _panels;
        private VisualElement   _galleryContainer;
        private Label           _eventLog;
        private Button          _playPauseBtn;
        private double          _lastTime;
        private readonly Queue<string> _logLines = new Queue<string>(8);

        // ── Lifecycle ───────────────────────────────────────────────────────

        private void OnEnable()
        {
            rootVisualElement.Clear();
            BuildUI();

            // Create panels (same as runtime demo)
            _panels = new List<DemoPanel>
            {
                new SinePanel(),
                new RandomPanel(),
                new GaussianPanel(),
                new StackedPanel(),
                new StressPanel(),
                new LiveFeedPanel(),
            };

            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                _galleryContainer.Add(panel.Card);
                WireEvents(panel);

                // Restore view snapshot from domain reload
                if (_snapshots != null && i < _snapshots.Count && _snapshots[i].IsValid)
                    panel.Graph.RestoreViewSnapshot(_snapshots[i]);
            }

            _lastTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            // Capture view snapshots for domain reload
            if (_panels != null)
            {
                _snapshots = new List<BarGraphViewSnapshot>();
                foreach (var panel in _panels)
                    _snapshots.Add(panel.Graph.CreateViewSnapshot());
            }
        }

        private void Update()
        {
            if (_panels == null) return;

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTime);
            _lastTime = now;

            // Clamp dt to avoid huge jumps after long pauses
            if (dt > 0.1f) dt = 0.1f;

            float scaledDt = dt * _animationSpeed;

            bool dirty = false;
            foreach (var panel in _panels)
            {
                panel.OnUpdate(_animating ? scaledDt : dt);
                dirty = true;
            }

            if (dirty)
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

            // Event log
            BuildEventLog(root);

            // Status bar
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
                    _lastTime = EditorApplication.timeSinceStartup;
                UpdatePlayPauseLabel();
            });
            bar.Add(_playPauseBtn);

            bar.Add(TbButton("\u23F9 Stop", () =>
            {
                _animating = false;
                UpdatePlayPauseLabel();
            }));

            bar.Add(TbLabel("Speed:"));
            var speedSlider = new Slider(0.1f, 5f) { value = _animationSpeed };
            speedSlider.style.width       = 80;
            speedSlider.style.marginRight = 4;
            speedSlider.RegisterValueChangedCallback(e => _animationSpeed = e.newValue);
            bar.Add(speedSlider);

            bar.Add(TbSep());

            // Data controls
            bar.Add(TbButton("Regenerate All", () =>
            {
                if (_panels == null) return;
                foreach (var p in _panels) p.Regenerate();
            }));

            bar.Add(TbButton("Reset All Views", () =>
            {
                if (_panels == null) return;
                foreach (var p in _panels) p.Graph?.ResetView();
            }));

            return bar;
        }

        private void BuildEventLog(VisualElement root)
        {
            var logBox = new VisualElement();
            logBox.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
            logBox.style.marginTop       = 4;
            logBox.style.marginLeft      = 6;
            logBox.style.marginRight     = 6;
            logBox.style.paddingTop      = 4;
            logBox.style.paddingBottom   = 4;
            logBox.style.paddingLeft     = 6;
            logBox.style.paddingRight    = 6;
            logBox.style.height          = 60f;
            logBox.style.borderTopLeftRadius = logBox.style.borderTopRightRadius =
                logBox.style.borderBottomLeftRadius = logBox.style.borderBottomRightRadius = 4;
            root.Add(logBox);

            _eventLog = new Label("Events will appear here\u2026")
            {
                style =
                {
                    fontSize   = 9,
                    color      = new Color(0.6f, 0.8f, 0.6f, 1f),
                    whiteSpace = WhiteSpace.Normal
                }
            };
            logBox.Add(_eventLog);
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

            bar.Add(new Label("6 panels \u2014 Scroll=ZoomX  Shift+Scroll=ZoomY  Middle/Alt+Drag=Pan  Ctrl+Click=Multi-select")
            {
                style =
                {
                    fontSize   = 9,
                    color      = new Color(0.5f, 0.5f, 0.5f, 1f),
                    flexGrow   = 1
                }
            });

            return bar;
        }

        private void UpdatePlayPauseLabel()
        {
            if (_playPauseBtn != null)
                _playPauseBtn.text = _animating ? "\u23F8 Pause" : "\u25B6 Play";
        }

        // ── Events ──────────────────────────────────────────────────────────

        private void WireEvents(DemoPanel panel)
        {
            var name = panel.Title;
            var sel = panel.Graph.GetHandler<BarGraphSelectionHandler>();
            sel.BarClicked               += a => Log($"[{name}] BarClicked  data={a.DataIndex} val={a.TotalValue:F1}");
            panel.Graph.HoverChanged     += a => { if (a.DataIndex >= 0) Log($"[{name}] Hover  bar={a.DataIndex}"); };
            panel.Graph.SelectionChanged += a => Log($"[{name}] Selection  count={a.SelectedDataIndices.Count}");
            sel.DragCompleted            += a => Log($"[{name}] DragDone  selected={a.SelectedDataIndices.Count}");
        }

        private void Log(string msg)
        {
            _logLines.Enqueue(msg);
            while (_logLines.Count > 5) _logLines.Dequeue();
            if (_eventLog != null) _eventLog.text = string.Join("\n", _logLines);
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

        private static Button TbButton(string text, Action action)
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

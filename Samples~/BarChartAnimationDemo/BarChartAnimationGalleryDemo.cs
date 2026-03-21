using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Events;
using BarGraph.Input.Handlers;

namespace BarGraph.AnimationDemo
{
    /// <summary>
    /// Animation showcase gallery with three animated bar chart panels:
    /// Racing Bar Chart, Audio Spectrum, and Sorting Visualizer.
    ///
    /// Scene setup
    /// ────────────
    /// 1. Create a scene with a GameObject containing a UIDocument (default Panel Settings).
    /// 2. Add this script to the same GameObject.
    /// 3. Press Play.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("UI Toolkit/Bar Graph Animation Gallery Demo")]
    public sealed class BarChartAnimationGalleryDemo : MonoBehaviour
    {
        [Header("Demo Options")]
        [SerializeField] private bool _showEventLog = true;

        private UIDocument _doc;
        private readonly List<AnimationDemoPanel> _panels = new List<AnimationDemoPanel>();
        private Label _eventLog;
        private readonly Queue<string> _logLines = new Queue<string>(8);

        private void Awake() => _doc = GetComponent<UIDocument>();

        private void Start() => BuildUI();

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _panels.Count; i++)
                _panels[i].OnUpdate(dt);
        }

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;
            root.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
            root.style.flexDirection   = FlexDirection.Column;
            root.style.paddingLeft = root.style.paddingRight =
                root.style.paddingTop = root.style.paddingBottom = 10;

            // Title bar
            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    marginBottom  = 6,
                    alignItems    = Align.Center
                }
            };
            root.Add(titleRow);

            titleRow.Add(new Label("BarGraph Toolkit \u2014 Animation Showcase")
            {
                style =
                {
                    fontSize               = 16,
                    color                  = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow               = 1
                }
            });

            var regenerateAll = new Button(() =>
            {
                foreach (var p in _panels) p.Regenerate();
            }) { text = "Regenerate All" };
            regenerateAll.style.paddingLeft = regenerateAll.style.paddingRight = 10;
            regenerateAll.style.height = 22;
            titleRow.Add(regenerateAll);

            // Scrollable grid area
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            var grid = new VisualElement
            {
                style =
                {
                    flexDirection  = FlexDirection.Row,
                    flexWrap       = Wrap.Wrap,
                    justifyContent = Justify.FlexStart,
                    alignItems     = Align.Stretch
                }
            };
            scroll.Add(grid);

            // Create panels
            _panels.Add(new RacingBarPanel());
            _panels.Add(new AudioSpectrumPanel());
            _panels.Add(new SortingVisualizerPanel());

            foreach (var panel in _panels)
            {
                grid.Add(panel.Card);
                if (_showEventLog)
                    WireEvents(panel);
            }

            if (_showEventLog)
                BuildEventLog(root);
        }

        private void BuildEventLog(VisualElement root)
        {
            var logBox = new VisualElement();
            logBox.style.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 1f);
            logBox.style.marginTop       = 6;
            logBox.style.paddingTop      = 6;
            logBox.style.paddingBottom   = 6;
            logBox.style.paddingLeft     = 6;
            logBox.style.paddingRight    = 6;
            logBox.style.height          = 72f;
            logBox.style.borderTopLeftRadius = logBox.style.borderTopRightRadius =
                logBox.style.borderBottomLeftRadius = logBox.style.borderBottomRightRadius = 4;
            root.Add(logBox);

            _eventLog = new Label("Events will appear here\u2026")
            {
                style =
                {
                    fontSize   = 10,
                    color      = new Color(0.6f, 0.8f, 0.6f, 1f),
                    whiteSpace = WhiteSpace.Normal
                }
            };
            logBox.Add(_eventLog);
        }

        private void WireEvents(AnimationDemoPanel panel)
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
    }
}

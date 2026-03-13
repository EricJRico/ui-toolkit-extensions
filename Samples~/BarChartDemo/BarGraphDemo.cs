using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Demo
{
    /// <summary>
    /// Comprehensive demo cycling through every feature.
    ///
    /// Scene setup
    /// ────────────
    /// 1. Create a scene with a GameObject containing a UIDocument (default Panel Settings).
    /// 2. Add this script to the same GameObject.
    /// 3. Press Play.
    ///
    /// Controls during runtime
    /// ────────────────────────
    ///  Scroll          – Zoom X
    ///  Shift+Scroll    – Zoom Y (if enabled in settings)
    ///  Left-drag       – Rubber-band select
    ///  Middle-drag     – Pan
    ///  Alt+Left-drag   – Pan (alternative)
    ///  Arrow keys      – Navigate selection (click graph first to focus)
    ///  Ctrl+A          – Select all
    ///  Ctrl+R          – Reset view
    ///  Escape          – Clear selection
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("UI Toolkit/Bar Graph Demo (V2)")]
    public sealed class BarGraphDemo : MonoBehaviour
    {
        [Header("Demo options")]
        [SerializeField] private bool _showSideBySide     = true;
        [SerializeField] private bool _showEventLog       = true;
        [SerializeField] private int  _stressBarCount     = 10_000;
        [SerializeField] private int  _stackedSegmentCount = 4;

        // ── Runtime ───────────────────────────────────────────────────────────

        private UIDocument      _doc;
        private BarGraphElement _mainGraph;
        private BarGraphElement _liveGraph;
        private BarGraphElement _stressGraph;
        private Label           _eventLog;

        private readonly Queue<string> _logLines = new Queue<string>(8);

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()  => _doc = GetComponent<UIDocument>();
        private void Start()  { BuildUI(); StartCoroutine(DemoCycle()); }

        // ─────────────────────────────────────────────────────────────────────
        //  UI construction
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;
            root.style.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
            root.style.flexDirection   = FlexDirection.Column;
            root.style.paddingLeft = root.style.paddingRight =
                root.style.paddingTop = root.style.paddingBottom = 10;

            // Title bar
            var title = new Label("BarGraph Toolkit V2 — Demo") {
                style = { fontSize = 16, color = Color.white, marginBottom = 8,
                          unityFontStyleAndWeight = FontStyle.Bold } };
            root.Add(title);

            // Graph row
            if (_showSideBySide)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
                root.Add(row);
                _mainGraph   = AddGraph(row, "Main (stacked + hover + select)");
                _liveGraph   = AddGraph(row, "Live Feed (append)");
                _stressGraph = AddGraph(row, $"{_stressBarCount:N0} Bars (LOD)");
            }
            else
            {
                _mainGraph = AddGraph(root, "Main Graph", rowFlex: false, minH: 300);
            }

            // Controls
            AddControlRow(root);

            // Event log
            if (_showEventLog)
            {
                var logBox = new VisualElement();
                logBox.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
                logBox.style.marginTop       = 6;
                // style.padding shorthand does not exist in Unity 6's IStyle.
                logBox.style.paddingTop      = 6;
                logBox.style.paddingBottom   = 6;
                logBox.style.paddingLeft     = 6;
                logBox.style.paddingRight    = 6;
                logBox.style.height          = 72f;
                root.Add(logBox);

                _eventLog = new Label("Events will appear here…")
                {
                    style = { fontSize = 10, color = new Color(0.6f, 0.8f, 0.6f, 1f),
                              whiteSpace = WhiteSpace.Normal }
                };
                logBox.Add(_eventLog);
                WireEvents(_mainGraph, "Main");
            }
        }

        private BarGraphElement AddGraph(VisualElement parent, string title,
                                         bool rowFlex = true, float minH = 180f)
        {
            var wrap = new VisualElement();
            wrap.style.flexGrow     = rowFlex ? 1f : 0f;
            wrap.style.flexBasis    = rowFlex
                ? new StyleLength(StyleKeyword.Auto)
                : new StyleLength(Length.Percent(100));
            wrap.style.marginRight  = 8;
            wrap.style.marginBottom = 8;
            wrap.style.flexDirection = FlexDirection.Column;
            parent.Add(wrap);

            wrap.Add(new Label(title)
            {
                style = { fontSize = 10, color = new Color(0.65f, 0.65f, 0.7f, 1f), marginBottom = 3 }
            });

            var g = new BarGraphElement();
            g.style.flexGrow   = rowFlex ? 1 : 0;
            g.style.minHeight  = minH;
            g.style.borderTopLeftRadius = g.style.borderTopRightRadius =
                g.style.borderBottomLeftRadius = g.style.borderBottomRightRadius = 4;

            // Pluggable formatter demo
            g.FormatYLabel = v => v >= 1000f ? $"{v / 1000f:F1}K" : $"{v:F0}";

            wrap.Add(g);
            return g;
        }

        private void AddControlRow(VisualElement root)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8, flexWrap = Wrap.Wrap } };
            root.Add(row);

            Button Btn(string label, System.Action action)
            {
                var b = new Button(action) { text = label };
                b.style.marginRight = b.style.marginBottom = 4;
                b.style.paddingLeft = b.style.paddingRight = 10;
                return b;
            }

            row.Add(Btn("Flat Random",    () => _mainGraph?.SetData(FlatRandom(80), new Color(0.3f, 0.65f, 1f))));
            row.Add(Btn("Stacked", () =>
            {
                // Call once and cache – each call generates independent random
                // data, so calling 4 times produced mismatched bars/segments.
                var d = MakeStackedBars(_stackedSegmentCount, 60);
                _mainGraph?.SetData(d.bars, d.barCount, d.segments, d.segCount);
            }));
            row.Add(Btn("Sort ↓",         () => _mainGraph?.SetSortMode(SortMode.ByValue, true)));
            row.Add(Btn("Sort ↑",         () => _mainGraph?.SetSortMode(SortMode.ByValue, false)));
            row.Add(Btn("Sort Off",       () => _mainGraph?.SetSortMode(SortMode.None)));
            row.Add(Btn("Overlay On",     () => _mainGraph?.SetOverlay(FlatRandom(80), new Color(1f, 0.7f, 0.2f))));
            row.Add(Btn("Overlay Off",    () => _mainGraph?.ClearOverlay()));
            row.Add(Btn("Reset View",     () => _mainGraph?.ResetView()));
            row.Add(Btn("Stress Test",    RunStress));
        }

        private void WireEvents(BarGraphElement g, string name)
        {
            g.BarClicked  += a => Log($"[{name}] BarClicked  data={a.DataIndex} val={a.TotalValue:F1}");
            g.HoverChanged += a => { if (a.DataIndex >= 0) Log($"[{name}] Hover       bar={a.DataIndex}"); };
            g.SelectionChanged += a => Log($"[{name}] Selection  count={a.SelectedDataIndices.Count}");
            g.DragCompleted += a => Log($"[{name}] DragDone   selected={a.SelectedDataIndices.Count}");
            g.ViewChanged   += a => { /* fire-every-scroll, skip logging */ };
        }

        private void Log(string msg)
        {
            _logLines.Enqueue(msg);
            while (_logLines.Count > 5) _logLines.Dequeue();
            if (_eventLog != null) _eventLog.text = string.Join("\n", _logLines);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Demo coroutine
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator DemoCycle()
        {
            // ── 1. Flat data (minimal API) ────────────────────────────────────
            _mainGraph.SetData(new float[] { 10, 45, 30, 75, 55, 90, 20, 60 });
            _liveGraph?.SetData(new float[] { 0 });
            yield return new WaitForSeconds(2f);

            // ── 2. Stacked bars ───────────────────────────────────────────────
            var (bars, barCount, segs, segCount) = MakeStackedBars(_stackedSegmentCount, 40);
            _mainGraph.SetData(bars, barCount, segs, segCount);
            yield return new WaitForSeconds(2f);

            // ── 3. Overlay ────────────────────────────────────────────────────
            _mainGraph.SetOverlay(FlatRandom(40), new Color(1f, 0.75f, 0.2f));
            yield return new WaitForSeconds(2f);

            // ── 4. Sort by value ──────────────────────────────────────────────
            _mainGraph.SetSortMode(SortMode.ByValue, true);
            yield return new WaitForSeconds(2f);
            _mainGraph.SetSortMode(SortMode.None);

            // ── 5. Live streaming ─────────────────────────────────────────────
            if (_liveGraph != null)
            {
                _liveGraph.ClearData();
                for (int i = 0; i < 80; i++)
                {
                    float v = 50f + 40f * Mathf.Sin(i * 0.25f) + Random.Range(-5f, 5f);
                    _liveGraph.AppendBar(v, Color.Lerp(Color.cyan, Color.magenta, i / 80f));
                    yield return new WaitForSeconds(0.04f);
                }
            }
            yield return new WaitForSeconds(1f);

            // ── 6. Custom formatter demo ──────────────────────────────────────
            _mainGraph.FormatYLabel = v => $"${v:F0}";
            _mainGraph.SetData(FlatRandom(30, 0f, 5000f), new Color(0.4f, 0.9f, 0.5f));
            yield return new WaitForSeconds(2f);
            _mainGraph.FormatYLabel = null; // reset to default

            // ── 7. Stress test ────────────────────────────────────────────────
            RunStress();
            yield return new WaitForSeconds(3f);

            StartCoroutine(DemoCycle()); // loop
        }

        private void RunStress()
        {
            BarGraphElement target = _stressGraph ?? _mainGraph;
            if (target == null) return;

            int n = _stressBarCount;
            var values  = new float[n];
            var colors  = new Color32[n];
            var gradient = new[] { (Color32)Color.cyan, (Color32)Color.green,
                                   (Color32)Color.yellow, (Color32)Color.red };

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                values[i]  = 50f + 45f * Mathf.Sin(i * 0.07f) * Mathf.Cos(i * 0.013f);
                int ci     = Mathf.FloorToInt(t * (gradient.Length - 1));
                float lt   = t * (gradient.Length - 1) - ci;
                int   ci2  = Mathf.Min(ci + 1, gradient.Length - 1);
                colors[i]  = Color32.Lerp(gradient[ci], gradient[ci2], lt);
            }

            var segBuf  = new BarSegment[n];
            var barBuf  = new BarEntry[n];
            for (int i = 0; i < n; i++)
            {
                segBuf[i] = new BarSegment(values[i], colors[i]);
                barBuf[i] = new BarEntry(i, 1, values[i]);
            }
            target.SetData(barBuf, n, segBuf, n);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Data generators
        // ─────────────────────────────────────────────────────────────────────

        private static IList<float> FlatRandom(int count,
            float min = 5f, float max = 100f)
        {
            var list = new List<float>(count);
            float prev = (min + max) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                prev = Mathf.Clamp(prev + Random.Range(-15f, 15f), min, max);
                list.Add(prev);
            }
            return list;
        }

        private static readonly Color32[] StackPalette =
        {
            new Color32( 65, 150, 255, 255),
            new Color32( 50, 200, 130, 255),
            new Color32(235, 155,  50, 255),
            new Color32(220,  80, 100, 255),
            new Color32(160,  90, 220, 255),
        };

        /// <summary>Returns pre-filled arrays ready to pass to SetData.</summary>
        private static (BarEntry[] bars, int barCount, BarSegment[] segments, int segCount)
            MakeStackedBars(int segPerBar, int barCount)
        {
            int totalSegs  = barCount * segPerBar;
            var bars       = new BarEntry[barCount];
            var segs       = new BarSegment[totalSegs];
            int segCursor  = 0;

            for (int b = 0; b < barCount; b++)
            {
                float total   = 0f;
                int   segStart = segCursor;
                for (int s = 0; s < segPerBar; s++)
                {
                    float v  = Random.Range(5f, 30f);
                    total   += v;
                    segs[segCursor++] = new BarSegment(v, StackPalette[s % StackPalette.Length]);
                }
                bars[b] = new BarEntry(segStart, segPerBar, total, b.ToString());
            }
            return (bars, barCount, segs, totalSegs);
        }
    }
}

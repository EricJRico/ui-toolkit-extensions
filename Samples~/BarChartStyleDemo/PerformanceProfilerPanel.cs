using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Frame-time profiler panel with heatmap-coloured bars (green → yellow → red).
    /// </summary>
    sealed class PerformanceProfilerPanel : StyleDemoPanel
    {
        int _frameCount = 60;

        static readonly Color32 HeatGreen  = new Color32(50, 230, 75, 255);
        static readonly Color32 HeatYellow = new Color32(255, 230, 50, 255);
        static readonly Color32 HeatRed    = new Color32(255, 50, 25, 255);

        static PanelTheme MakeTheme() => new PanelTheme
        {
            CardBackground   = new Color(0.04f, 0.05f, 0.08f),
            CardBorder       = new Color(0.12f, 0.14f, 0.2f),
            TitleColor       = new Color(0.4f, 0.9f, 0.4f),
            ThemeClassName   = "bar-graph--performance-profiler",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("PerformanceProfiler"),
            BehaviorSettings = new BarGraphSettings
            {
                GridLineCount = 4,
            }
        };

        public PerformanceProfilerPanel()
            : base("Performance Profiler", MakeTheme()) { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Frames", 30, 200, _frameCount, v =>
            {
                _frameCount = v;
                Regenerate();
            }));
            AddControl(MakeButton("Regenerate", Regenerate));
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_frameCount];
            var segments = new BarSegment[_frameCount];

            for (int i = 0; i < _frameCount; i++)
            {
                float value = Random.Range(8f, 16f);
                if (Random.value < 0.15f)
                    value = Random.Range(25f, 45f);

                segments[i] = new BarSegment(value, FrameTimeColor(value));
                bars[i]     = new BarEntry(i, 1, value, $"#{i + 1}");
            }

            Graph.SetData(bars, _frameCount, segments, _frameCount);
            Graph.FormatYLabel = v => $"{v:F1}ms";
        }

        static Color32 FrameTimeColor(float value)
        {
            if (value <= 16.67f)
                return HeatGreen;

            if (value <= 33.33f)
            {
                float t = (value - 16.67f) / 16.67f;
                return Color32.Lerp(HeatGreen, HeatYellow, t);
            }

            float tRed = Mathf.Clamp01((value - 33.33f) / 16.67f);
            return Color32.Lerp(HeatYellow, HeatRed, tRed);
        }
    }
}

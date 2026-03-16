using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Cyberpunk network intrusion panel — dense ICE-layer bars across nodes.
    /// Wide + short layout.
    /// </summary>
    sealed class NeonNetrunnerPanel : StyleDemoPanel
    {
        private int _nodeCount = 25;

        private const int SegmentsPerNode = 3;

        private static readonly Color32 Ice1Color = new Color32(  0, 255, 210, 255); // cyan
        private static readonly Color32 Ice2Color = new Color32(180,  50, 255, 255); // purple
        private static readonly Color32 Ice3Color = new Color32(255,   0, 150, 255); // hot pink

        static PanelTheme BuildTheme() => new PanelTheme
        {
            CardBackground   = new Color(0.03f, 0.01f, 0.07f),
            CardBorder       = new Color(0.16f, 0.06f, 0.24f),
            TitleColor       = new Color(0f, 1f, 0.82f),
            ThemeClassName   = "bar-graph--neon-netrunner",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("NeonNetrunner"),
            BehaviorSettings = new BarGraphSettings
            {
                MaxXLabels = 20,
            }
        };

        public NeonNetrunnerPanel() : base("Neon Netrunner", BuildTheme())
        {
            // Wide + short layout
            Card.style.flexBasis = new StyleLength(Length.Percent(100));
            Graph.style.minHeight = 120;
            Graph.style.maxHeight = 180;
        }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Nodes", 15, 40, _nodeCount, v =>
            {
                _nodeCount = v;
                Regenerate();
            }));
            AddControl(MakeButton("Regenerate", Regenerate));
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_nodeCount];
            var segments = new BarSegment[_nodeCount * SegmentsPerNode];
            int segIdx   = 0;

            for (int i = 0; i < _nodeCount; i++)
            {
                int   segStart = segIdx;
                float ice1     = Random.Range(40f, 180f);
                float ice2     = Random.Range(20f, 120f);
                float ice3     = Random.Range(10f, 80f);
                float total    = ice1 + ice2 + ice3;

                segments[segIdx++] = new BarSegment(ice1, Ice1Color);
                segments[segIdx++] = new BarSegment(ice2, Ice2Color);
                segments[segIdx++] = new BarSegment(ice3, Ice3Color);

                bars[i] = new BarEntry(segStart, SegmentsPerNode, total, $"N-{i + 1:D2}");
            }

            Graph.SetData(bars, _nodeCount, segments, segIdx);
            Graph.FormatYLabel = v => $"{v:F0}b";
        }
    }
}

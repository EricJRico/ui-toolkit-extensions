using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Fantasy game resource economy visualisation with stacked bars per turn.
    /// </summary>
    sealed class ResourceEconomyPanel : StyleDemoPanel
    {
        private int _turnCount = 12;

        private const int SegmentsPerTurn = 5;

        private static readonly Color32 GoldColor    = new Color32(255, 215,   0, 255);
        private static readonly Color32 WoodColor    = new Color32(140,  90,  40, 255);
        private static readonly Color32 StoneColor   = new Color32(155, 155, 155, 255);
        private static readonly Color32 CrystalColor = new Color32( 75, 230, 240, 255);
        private static readonly Color32 FoodColor    = new Color32(100, 205,  50, 255);

        private static PanelTheme BuildTheme() => new PanelTheme
        {
            CardBackground   = new Color(0.09f, 0.07f, 0.04f),
            CardBorder       = new Color(0.25f, 0.18f, 0.08f),
            TitleColor       = new Color(0.95f, 0.85f, 0.5f),
            ThemeClassName   = "bar-graph--resource-economy",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("ResourceEconomy"),
            BehaviorSettings = null
        };

        public ResourceEconomyPanel() : base("Resource Economy", BuildTheme()) { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Turns", 4, 24, _turnCount, v =>
            {
                _turnCount = v;
                Regenerate();
            }));

            AddControl(MakeButton("Regenerate", Regenerate));
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_turnCount];
            var segments = new BarSegment[_turnCount * SegmentsPerTurn];
            int segIdx   = 0;

            for (int turn = 0; turn < _turnCount; turn++)
            {
                int   segStart = segIdx;
                float total    = 0f;

                float gold    = Random.Range(20f, 60f) + turn * 5f;
                float wood    = Random.Range(30f, 50f);
                float stone   = Random.Range(15f, 35f) + turn * 2f;
                float crystal = turn < 4 ? 0f : Random.Range(5f, 20f);
                float food    = Mathf.Max(5f, Random.Range(40f, 55f) - turn * 2f);

                segments[segIdx++] = new BarSegment(gold,    GoldColor);
                segments[segIdx++] = new BarSegment(wood,    WoodColor);
                segments[segIdx++] = new BarSegment(stone,   StoneColor);
                segments[segIdx++] = new BarSegment(crystal, CrystalColor);
                segments[segIdx++] = new BarSegment(food,    FoodColor);

                total = gold + wood + stone + crystal + food;

                bars[turn] = new BarEntry(segStart, SegmentsPerTurn, total, $"T{turn + 1}");
            }

            Graph.SetData(bars, _turnCount, segments, segIdx);
            Graph.FormatYLabel = v => v >= 1000f ? $"{v / 1000f:F1}K" : $"{v:F0}";
        }
    }
}

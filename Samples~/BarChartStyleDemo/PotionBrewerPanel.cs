using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Fantasy potion recipe panel — stacked ingredient segments per potion.
    /// Tall + narrow layout.
    /// </summary>
    sealed class PotionBrewerPanel : StyleDemoPanel
    {
        private int _potionCount = 7;

        private const int SegmentsPerPotion = 5;

        private static readonly Color32 DragonBlood   = new Color32(180,  30,  30, 255);
        private static readonly Color32 MoonstoneDust = new Color32(140, 160, 200, 255);
        private static readonly Color32 Starleaf      = new Color32( 40, 170,  60, 255);
        private static readonly Color32 PhoenixAsh    = new Color32(230, 140,  40, 255);
        private static readonly Color32 VoidEssence   = new Color32(120,  40, 160, 255);

        private static readonly string[] PotionNames =
        {
            "Elixir", "Draught", "Tonic", "Salve", "Philter",
            "Ichor", "Brew", "Balm", "Poultice", "Cordial"
        };

        static PanelTheme BuildTheme() => new PanelTheme
        {
            CardBackground   = new Color(0.13f, 0.09f, 0.06f),
            CardBorder       = new Color(0.31f, 0.24f, 0.12f),
            TitleColor       = new Color(1f, 0.82f, 0.39f),
            ThemeClassName   = "bar-graph--potion-brewer",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("PotionBrewer"),
            BehaviorSettings = null
        };

        public PotionBrewerPanel() : base("Potion Brewer", BuildTheme())
        {
            // Tall + narrow layout
            Card.style.flexBasis = new StyleLength(Length.Percent(38));
            Card.style.minWidth  = 320;
            Graph.style.minHeight = 280;
        }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Potions", 4, 10, _potionCount, v =>
            {
                _potionCount = v;
                Regenerate();
            }));
            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_potionCount];
            var segments = new BarSegment[_potionCount * SegmentsPerPotion];
            int segIdx   = 0;

            for (int i = 0; i < _potionCount; i++)
            {
                int   segStart = segIdx;
                float blood    = Random.Range(8f, 35f);
                float moon     = Random.Range(5f, 25f);
                float star     = Random.Range(10f, 30f);
                float phoenix  = Random.Range(3f, 20f);
                float voidE    = Random.Range(2f, 15f);
                float total    = blood + moon + star + phoenix + voidE;

                segments[segIdx++] = new BarSegment(blood,   DragonBlood);
                segments[segIdx++] = new BarSegment(moon,    MoonstoneDust);
                segments[segIdx++] = new BarSegment(star,    Starleaf);
                segments[segIdx++] = new BarSegment(phoenix, PhoenixAsh);
                segments[segIdx++] = new BarSegment(voidE,   VoidEssence);

                bars[i] = new BarEntry(segStart, SegmentsPerPotion, total, PotionNames[i]);
            }

            Graph.SetData(bars, _potionCount, segments, segIdx);
            Graph.FormatYLabel = v => $"{v:F0}";
            Graph.SetSortMode(IsSorted ? SortMode.ByValue : SortMode.None, SortDescending);
        }
    }
}

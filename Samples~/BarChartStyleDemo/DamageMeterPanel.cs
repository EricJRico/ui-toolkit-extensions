using UnityEngine;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    sealed class DamageMeterPanel : StyleDemoPanel
    {
        private static readonly (string Name, Color32 Color)[] Abilities =
        {
            ("Fireball",      new Color32(255,  90,  25, 255)),
            ("Ice Lance",     new Color32( 75, 155, 255, 255)),
            ("Shadow Bolt",   new Color32(155,  50, 205, 255)),
            ("Moonfire",      new Color32( 75, 215,  75, 255)),
            ("Lightning",     new Color32(100, 180, 255, 255)),
            ("Chaos Bolt",    new Color32(230,  50, 100, 255)),
            ("Arcane Blast",  new Color32(180, 130, 255, 255)),
            ("Plague Strike", new Color32(130, 200,  50, 255)),
            ("Wrath",         new Color32( 80, 220,  80, 255)),
            ("Frostbolt",     new Color32(100, 170, 240, 255)),
            ("Immolate",      new Color32(255, 140,  30, 255)),
            ("Starfire",      new Color32(220, 200,  80, 255)),
        };

        public DamageMeterPanel() : base("Damage Meter", new PanelTheme
        {
            CardBackground = new Color(0.08f, 0.04f, 0.04f),
            CardBorder     = new Color(0.3f,  0.1f,  0.1f),
            TitleColor     = new Color(0.9f,  0.85f, 0.8f),
            GraphSettings  = new BarGraphSettings
            {
                BackgroundColor     = new Color(0.06f, 0.03f, 0.03f),
                AxisColor           = new Color(0.4f,  0.25f, 0.25f),
                GridLineColor       = new Color(0.2f,  0.08f, 0.08f),
                LabelColor          = new Color(0.85f, 0.8f,  0.75f),
                SelectionRimColor   = new Color(1f,    0.3f,  0.2f, 0.9f),
                PaddingBottom       = 56,
                XLabelWidth         = 80,
                MaxXLabels          = 15,
                BarSpacingRatio     = 0.15f,
            }
        })
        { }

        protected override void BuildControls()
        {
            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddResetViewButton();

            IsSorted = true;
            Regenerate();
        }

        public override void Regenerate()
        {
            int count    = Abilities.Length;
            var bars     = new BarEntry[count];
            var segments = new BarSegment[count];

            for (int i = 0; i < count; i++)
            {
                float value = Random.Range(8000f, 85000f);
                segments[i] = new BarSegment(value, Abilities[i].Color);
                bars[i]     = new BarEntry(i, 1, value, Abilities[i].Name);
            }

            Graph.SetData(bars, count, segments, count);
            Graph.FormatYLabel = v => $"{v / 1000f:F0}K";
            Graph.SetSortMode(IsSorted ? SortMode.ByValue : SortMode.None, SortDescending);
        }
    }
}

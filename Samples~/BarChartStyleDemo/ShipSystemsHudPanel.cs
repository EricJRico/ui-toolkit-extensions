using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Sci-fi fleet subsystem power panel — stacked segments per ship.
    /// Wide + medium layout.
    /// </summary>
    sealed class ShipSystemsHudPanel : StyleDemoPanel
    {
        private int _fleetSize = 10;

        private const int SegmentsPerShip = 4;

        private static readonly Color32 ShieldsColor     = new Color32( 60, 200, 160, 255);
        private static readonly Color32 WeaponsColor     = new Color32(255, 170,   0, 255);
        private static readonly Color32 EnginesColor     = new Color32( 80, 160,  80, 255);
        private static readonly Color32 LifeSupportColor = new Color32(180, 200,  60, 255);

        private static readonly string[] ShipPrefixes =
        {
            "SHV", "CRU", "FRG", "DST", "CRV",
            "CAR", "TNK", "SCT", "INT", "DRD",
            "BGS", "CMD", "SPR", "HVY", "LGT", "ARC"
        };

        static PanelTheme BuildTheme() => new PanelTheme
        {
            CardBackground   = new Color(0.02f, 0.05f, 0.03f),
            CardBorder       = new Color(0.10f, 0.22f, 0.12f),
            TitleColor       = new Color(1f, 0.67f, 0f),
            ThemeClassName   = "bar-graph--ship-systems-hud",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("ShipSystemsHud"),
            BehaviorSettings = null
        };

        public ShipSystemsHudPanel() : base("Ship Systems HUD", BuildTheme())
        {
            // Wide + medium layout
            Card.style.flexBasis = new StyleLength(Length.Percent(100));
        }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Fleet", 6, 16, _fleetSize, v =>
            {
                _fleetSize = v;
                Regenerate();
            }));
            AddControl(MakeButton("Regenerate", Regenerate));
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_fleetSize];
            var segments = new BarSegment[_fleetSize * SegmentsPerShip];
            int segIdx   = 0;

            for (int i = 0; i < _fleetSize; i++)
            {
                int   segStart = segIdx;
                float shields  = Random.Range(20f, 100f);
                float weapons  = Random.Range(30f, 120f);
                float engines  = Random.Range(15f, 80f);
                float life     = Random.Range(10f, 50f);
                float total    = shields + weapons + engines + life;

                segments[segIdx++] = new BarSegment(shields, ShieldsColor);
                segments[segIdx++] = new BarSegment(weapons, WeaponsColor);
                segments[segIdx++] = new BarSegment(engines, EnginesColor);
                segments[segIdx++] = new BarSegment(life,    LifeSupportColor);

                string prefix = ShipPrefixes[i % ShipPrefixes.Length];
                bars[i] = new BarEntry(segStart, SegmentsPerShip, total, $"{prefix}-{i + 1:D2}");
            }

            Graph.SetData(bars, _fleetSize, segments, segIdx);
            Graph.FormatYLabel = v => $"{v:F0}MW";
        }
    }
}

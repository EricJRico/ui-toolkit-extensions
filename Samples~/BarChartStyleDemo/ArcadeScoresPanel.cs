using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Retro arcade high-score panel — bold primaries, chunky bars.
    /// Square-ish layout, shares row with Potion Brewer.
    /// </summary>
    sealed class ArcadeScoresPanel : StyleDemoPanel
    {
        private int _playerCount = 12;

        private static readonly string[] Initials =
        {
            "AAA", "BOB", "ACE", "ZAP", "MAX", "JET", "RAD", "GUS",
            "DOT", "BUG", "CPU", "RAM", "ROM", "HEX", "BIT", "KEY",
            "DMA", "ALT", "TAB", "ESC"
        };

        private static readonly Color32[] PrimaryColors =
        {
            new Color32(255,  60,  60, 255), // red
            new Color32( 60, 100, 255, 255), // blue
            new Color32( 50, 255,  50, 255), // green
            new Color32(255, 255,  50, 255), // yellow
            new Color32(255,  60, 255, 255), // magenta
            new Color32( 60, 255, 255, 255), // cyan
        };

        public ArcadeScoresPanel() : base("Arcade High Scores", new PanelTheme
        {
            CardBackground   = new Color(0.04f, 0.04f, 0.11f),
            CardBorder       = new Color(0.20f, 0.20f, 0.39f),
            TitleColor       = new Color(0.20f, 1f, 0.20f),
            ThemeClassName   = "bar-graph--arcade-scores",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("ArcadeScores"),
            BehaviorSettings = new BarGraphSettings
            {
                MaxXLabels = 20,
            }
        })
        {
            // Square-ish layout, shares row with Potion Brewer
            Card.style.flexBasis = new StyleLength(Length.Percent(55));
        }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Players", 8, 20, _playerCount, v =>
            {
                _playerCount = v;
                Regenerate();
            }));
            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddResetViewButton();

            IsSorted = true;
            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_playerCount];
            var segments = new BarSegment[_playerCount];

            for (int i = 0; i < _playerCount; i++)
            {
                float score = Random.Range(1500f, 99999f);
                Color32 color = PrimaryColors[i % PrimaryColors.Length];

                segments[i] = new BarSegment(score, color);
                bars[i]     = new BarEntry(i, 1, score, Initials[i]);
            }

            Graph.SetData(bars, _playerCount, segments, _playerCount);
            Graph.FormatYLabel = v => v >= 1000f ? $"{v / 1000f:F0}K" : $"{v:F0}";
            Graph.SetSortMode(IsSorted ? SortMode.ByValue : SortMode.None, SortDescending);
        }
    }
}

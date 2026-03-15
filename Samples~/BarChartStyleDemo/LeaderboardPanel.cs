using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.StyleDemo
{
    sealed class LeaderboardPanel : StyleDemoPanel
    {
        private static readonly string[] NamePool =
        {
            "xNoScope",    "ProGamer",   "ShadowFX",   "NightOwl",
            "IceQueen",    "BlazeKing",  "CyberWolf",  "PixelDust",
            "StormRider",  "GhostRecon", "NeonByte",   "VortexGG",
            "ZeroHero",    "RogueOne",   "AcePlayer",  "TurboMage",
            "SwiftEdge",   "DarkStar",   "FluxCore",   "HyperNova",
            "IronSight",   "JadeStrike", "KryptonX",   "LunarBeat",
        };

        private int _playerCount = 12;

        public LeaderboardPanel() : base("Leaderboard", new PanelTheme
        {
            CardBackground   = new Color(0.04f, 0.04f, 0.09f),
            CardBorder       = new Color(0.12f, 0.12f, 0.25f),
            TitleColor       = new Color(0.9f,  0.92f, 1f),
            ThemeClassName   = "bar-graph--leaderboard",
            ThemeStyleSheet  = Resources.Load<StyleSheet>("Leaderboard"),
            BehaviorSettings = new BarGraphSettings
            {
                MaxXLabels = 24,
            }
        })
        { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Players", 8, 24, _playerCount, v =>
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
            // Generate scores in pool order
            var playerScores = new float[_playerCount];
            for (int i = 0; i < _playerCount; i++)
                playerScores[i] = Random.Range(1200f, 28000f);

            // Determine rank for each player (for color assignment)
            var ranked = new int[_playerCount];
            for (int i = 0; i < _playerCount; i++) ranked[i] = i;
            System.Array.Sort(ranked, (a, b) => playerScores[b].CompareTo(playerScores[a]));

            var rankOf = new int[_playerCount];
            for (int r = 0; r < _playerCount; r++) rankOf[ranked[r]] = r;

            int localPlayer = ranked[Random.Range(3, _playerCount)];

            // Build bars in original pool order (sort toggle re-orders visually)
            var bars     = new BarEntry[_playerCount];
            var segments = new BarSegment[_playerCount];

            for (int i = 0; i < _playerCount; i++)
            {
                Color32 color;
                switch (rankOf[i])
                {
                    case 0:  color = new Color32(255, 215,   0, 255); break;
                    case 1:  color = new Color32(190, 190, 200, 255); break;
                    case 2:  color = new Color32(205, 130,  50, 255); break;
                    default: color = i == localPlayer
                                 ? new Color32( 75, 180, 255, 255)
                                 : new Color32(100, 115, 140, 255);
                             break;
                }

                segments[i] = new BarSegment(playerScores[i], color);
                bars[i]     = new BarEntry(i, 1, playerScores[i], NamePool[i]);
            }

            Graph.SetData(bars, _playerCount, segments, _playerCount);
            Graph.FormatYLabel = v => v >= 1000f ? $"{v / 1000f:F1}K" : $"{v:F0}";
            Graph.SetSortMode(IsSorted ? SortMode.ByValue : SortMode.None, SortDescending);
        }
    }
}

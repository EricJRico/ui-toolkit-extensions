using UnityEngine;
using BarGraph.Core;

namespace BarGraph.AnimationDemo
{
    /// <summary>
    /// Racing bar chart showing fictional tech companies competing for market cap
    /// across 20 quarters. Bars reorder smoothly as rankings shift.
    /// </summary>
    sealed class RacingBarPanel : AnimationDemoPanel
    {
        private static readonly (string Name, Color32 Color)[] Companies =
        {
            ("NovaTech",    new Color32(  0, 210, 230, 255)),
            ("CyberPulse",  new Color32(220,  50, 180, 255)),
            ("ArcLight",    new Color32(240, 180,  40, 255)),
            ("NeuroLink",   new Color32( 80, 220,  60, 255)),
            ("QuantumByte", new Color32( 70, 130, 255, 255)),
            ("SkyForge",    new Color32(255, 140,  30, 255)),
            ("DataVault",   new Color32(  0, 190, 170, 255)),
            ("PixelWave",   new Color32(255, 100, 160, 255)),
            ("TerraCode",   new Color32( 50, 200, 100, 255)),
            ("FusionGrid",  new Color32(255,  80,  50, 255)),
        };

        private const int TimeSteps   = 20;
        private const float StepDuration = 1.5f; // seconds per quarter at 1x speed

        private float[][] _keyframes; // [company][timeStep]
        private bool _loop = true;

        // Pre-allocated work arrays
        private readonly BarEntry[]   _bars     = new BarEntry[Companies.Length];
        private readonly BarSegment[] _segments = new BarSegment[Companies.Length];

        public RacingBarPanel() : base("Market Cap Race", new AnimationPanelTheme
        {
            CardBackground = new Color(0.04f, 0.05f, 0.10f),
            CardBorder     = new Color(0.6f,  0.5f,  0.2f),
            TitleColor     = new Color(0.9f,  0.85f, 0.6f),
            GraphSettings  = new BarGraphSettings
            {
                BackgroundColor   = new Color(0.03f, 0.04f, 0.09f),
                AxisColor         = new Color(0.6f,  0.5f,  0.25f),
                GridLineColor     = new Color(0.12f, 0.12f, 0.20f),
                LabelColor        = new Color(0.85f, 0.82f, 0.7f),
                SelectionRimColor = new Color(0.9f,  0.75f, 0.2f, 0.9f),
                FocusRimColor     = new Color(1f,    0.9f,  0.4f, 1f),
                PaddingBottom     = 56,
                XLabelWidth       = 80,
                MaxXLabels        = 12,
                BarSpacingRatio   = 0.15f,
            }
        })
        { }

        protected override void BuildControls()
        {
            AddPlayPauseToggle();
            AddSpeedSlider(0.2f, 5f, 1f);

            AddControl(MakeToggleButton("Loop Off", "Loop", true, on => _loop = on));
            AddControl(MakeButton("Reset", () =>
            {
                Elapsed = 0f;
                UpdateGraph();
            }));
            AddControl(MakeButton("Regenerate", Regenerate));
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            Elapsed = 0f;
            _keyframes = new float[Companies.Length][];

            for (int c = 0; c < Companies.Length; c++)
            {
                _keyframes[c] = new float[TimeSteps];

                // Each company starts between 5B and 40B
                float value = Random.Range(5e9f, 40e9f);
                // Base quarterly growth rate varies per company
                float growthRate = Random.Range(0.98f, 1.08f);

                _keyframes[c][0] = value;

                for (int t = 1; t < TimeSteps; t++)
                {
                    // Growth + random walk
                    value *= growthRate * Random.Range(0.9f, 1.12f);

                    // Occasional jump (product launch or crash) — 15% chance
                    if (Random.value < 0.15f)
                        value *= Random.Range(0.7f, 1.5f);

                    value = Mathf.Max(value, 1e9f); // floor at 1B
                    _keyframes[c][t] = value;
                }
            }

            Graph.FormatYLabel = v => v >= 1e9f ? $"${v / 1e9f:F0}B" : $"${v / 1e6f:F0}M";

            UpdateGraph();
        }

        public override void OnUpdate(float dt)
        {
            if (!IsPlaying) return;

            float totalDuration = (TimeSteps - 1) * StepDuration;
            Elapsed += dt * Speed;

            if (Elapsed >= totalDuration)
            {
                if (_loop)
                    Elapsed %= totalDuration;
                else
                {
                    Elapsed = totalDuration;
                    IsPlaying = false;
                }
            }

            UpdateGraph();
        }

        private void UpdateGraph()
        {
            float pos = Elapsed / StepDuration;
            int step  = Mathf.Min((int)pos, TimeSteps - 2);
            float frac = pos - step;

            // Quarter label: Q1-Q4, Year 1-5
            int quarter = (step % 4) + 1;
            int year    = (step / 4) + 1;
            TitleLabel.text = $"Market Cap Race \u2014 Q{quarter} Year {year}";

            for (int c = 0; c < Companies.Length; c++)
            {
                float v0 = _keyframes[c][step];
                float v1 = _keyframes[c][Mathf.Min(step + 1, TimeSteps - 1)];
                float value = Mathf.Lerp(v0, v1, frac);

                _segments[c] = new BarSegment(value, Companies[c].Color);
                _bars[c]     = new BarEntry(c, 1, value, Companies[c].Name);
            }

            Graph.SetData(_bars, Companies.Length, _segments, Companies.Length);
            Graph.SetSortMode(SortMode.ByValue, true);
        }
    }
}

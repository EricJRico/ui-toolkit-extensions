using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Demo
{
    /// <summary>
    /// Pushes the bar graph to its limits with tens of thousands of bars,
    /// coloured by a four-stop gradient.
    /// </summary>
    public sealed class StressPanel : DemoPanel
    {
        private int _barCount = 10000;

        public StressPanel() : base("Stress Test") { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Bars", 100, 100000, _barCount, v =>
            {
                _barCount = v;
                Regenerate();
            }));

            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddZoomYToggle();
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_barCount];
            var segments = new BarSegment[_barCount];

            // Four-stop gradient: cyan -> green -> yellow -> red
            Color cyan   = new Color(0f,   1f,   1f);
            Color green  = new Color(0f,   1f,   0.5f);
            Color yellow = new Color(1f,   0.863f, 0f);
            Color red    = new Color(1f,   0.314f, 0f);

            for (int i = 0; i < _barCount; i++)
            {
                float value = 50f + 45f * Mathf.Sin(i * 0.07f) * Mathf.Cos(i * 0.013f);
                float t     = (float)i / _barCount;

                // Lerp through 3 equal spans
                Color color;
                if (t < 1f / 3f)
                    color = Color.Lerp(cyan, green, t * 3f);
                else if (t < 2f / 3f)
                    color = Color.Lerp(green, yellow, (t - 1f / 3f) * 3f);
                else
                    color = Color.Lerp(yellow, red, (t - 2f / 3f) * 3f);

                segments[i] = new BarSegment(value, color);
                bars[i]     = new BarEntry(i, 1, value);
            }

            Graph.SetData(bars, _barCount, segments, _barCount);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Demo
{
    /// <summary>
    /// Displays a random-walk dataset as a bar graph.
    /// </summary>
    sealed class RandomPanel : DemoPanel
    {
        private int _barCount = 80;

        public RandomPanel() : base("Random Walk") { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Bars", 10, 500, _barCount, v =>
            {
                _barCount = v;
                Regenerate();
            }));

            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddOverlayToggle();
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var values = new List<float>(_barCount);
            float current = 50f;

            for (int i = 0; i < _barCount; i++)
            {
                current += Random.Range(-15f, 15f);
                current  = Mathf.Clamp(current, 2f, 100f);
                values.Add(current);
            }

            Graph.SetData(values, new Color(0.35f, 0.85f, 0.5f));

            if (HasOverlay) ApplyOverlay();
        }

        protected override void ApplyOverlay()
        {
            var overlay = new List<float>(_barCount);

            for (int i = 0; i < _barCount; i++)
                overlay.Add(Random.Range(10f, 90f));

            Graph.SetOverlay(overlay, new Color(1f, 0.7f, 0.2f, 0.6f));
        }
    }
}

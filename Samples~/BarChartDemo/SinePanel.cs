using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Demo
{
    /// <summary>
    /// Displays a composite sine wave as a bar graph.
    /// </summary>
    sealed class SinePanel : DemoPanel
    {
        private int   _barCount  = 120;
        private float _frequency = 6f;
        private float _amplitude = 40f;

        public SinePanel() : base("Sine Wave") { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Bars", 10, 500, _barCount, v =>
            {
                _barCount = v;
                Regenerate();
            }));

            AddControl(MakeSlider("Freq", 1f, 20f, _frequency, v =>
            {
                _frequency = v;
                Regenerate();
            }));

            AddControl(MakeSlider("Amp", 10f, 80f, _amplitude, v =>
            {
                _amplitude = v;
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

            for (int i = 0; i < _barCount; i++)
            {
                float t = (float)i / _barCount;
                float v = 50f
                        + _amplitude * Mathf.Sin(t * Mathf.PI * _frequency)
                        + 15f        * Mathf.Sin(t * Mathf.PI * 18f);
                values.Add(Mathf.Max(0f, v));
            }

            Graph.SetData(values, new Color(0.3f, 0.65f, 1f));

            if (HasOverlay) ApplyOverlay();
        }

        protected override void ApplyOverlay()
        {
            var overlay = new List<float>(_barCount);

            for (int i = 0; i < _barCount; i++)
                overlay.Add(Random.Range(0f, 100f));

            Graph.SetOverlay(overlay, new Color(1f, 0.7f, 0.2f, 0.6f));
        }
    }
}

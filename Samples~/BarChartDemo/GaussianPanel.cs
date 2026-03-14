using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Demo
{
    /// <summary>
    /// Displays a Gaussian (normal) distribution histogram generated via Box-Muller.
    /// </summary>
    sealed class GaussianPanel : DemoPanel
    {
        private int _binCount         = 80;
        private int _sampleMultiplier = 80;

        public GaussianPanel() : base("Gaussian Distribution") { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Bins", 20, 300, _binCount, v =>
            {
                _binCount = v;
                Regenerate();
            }));

            AddControl(MakeSliderInt("Samples x", 20, 200, _sampleMultiplier, v =>
            {
                _sampleMultiplier = v;
                Regenerate();
            }));

            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var values = new List<float>(_binCount);
            for (int i = 0; i < _binCount; i++)
                values.Add(0f);

            int totalSamples = _binCount * _sampleMultiplier;

            for (int i = 0; i < totalSamples; i++)
            {
                float u1   = Mathf.Max(1e-6f, Random.value);
                float u2   = Random.value;
                float norm = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
                int   bin  = Mathf.Clamp(Mathf.RoundToInt(norm * _binCount / 6f + _binCount / 2f),
                                         0, _binCount - 1);
                values[bin]++;
            }

            Graph.SetData(values, new Color(0.9f, 0.55f, 0.2f));
        }
    }
}

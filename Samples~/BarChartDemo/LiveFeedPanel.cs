using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Demo
{
    /// <summary>
    /// Streams bars into the graph in real time using <see cref="BarGraphElement.AppendBar"/>.
    /// Automatically wraps around after <see cref="_maxBars"/> entries.
    /// </summary>
    public sealed class LiveFeedPanel : DemoPanel
    {
        private bool  _playing       = false;
        private float _barsPerSecond = 50f;
        private int   _maxBars       = 200;
        private float _accumulator   = 0f;
        private int   _barIndex      = 0;

        public LiveFeedPanel() : base("Live Feed") { }

        protected override void BuildControls()
        {
            AddControl(MakeToggleButton("Pause", "Play", false, on =>
            {
                _playing = on;
            }));

            AddControl(MakeButton("Step", AppendOneBar));

            AddControl(MakeSlider("Speed", 1f, 200f, _barsPerSecond, v =>
            {
                _barsPerSecond = v;
            }));

            AddControl(MakeSliderInt("Max", 50, 2000, _maxBars, v =>
            {
                _maxBars = v;
            }));

            AddControl(MakeButton("Clear", () =>
            {
                Graph.ClearData();
                _barIndex    = 0;
                _accumulator = 0f;
            }));

            AddZoomYToggle();
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            Graph.ClearData();
            _barIndex    = 0;
            _accumulator = 0f;
        }

        public override void OnUpdate(float dt)
        {
            if (!_playing) return;

            _accumulator += dt;
            float interval = 1f / _barsPerSecond;

            while (_accumulator >= interval)
            {
                _accumulator -= interval;
                AppendOneBar();
            }
        }

        private void AppendOneBar()
        {
            float value = 50f + 40f * Mathf.Sin(_barIndex * 0.25f) + Random.Range(-5f, 5f);
            Color color = Color.Lerp(Color.cyan, Color.magenta,
                (_barIndex % _maxBars) / (float)_maxBars);

            Graph.AppendBar(value, color);
            _barIndex++;

            if (_barIndex % _maxBars == 0)
                Graph.ClearData();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Demo
{
    /// <summary>
    /// Displays stacked bars with configurable segment count and HSL hue-rotation
    /// colour support for large palettes (up to 1000 segments).
    /// </summary>
    public sealed class StackedPanel : DemoPanel
    {
        private int  _barCount     = 40;
        private int  _segmentCount = 4;
        private bool _useHsl;

        private Button _hslToggle;

        private static readonly Color32[] StackPalette =
        {
            new Color32( 65, 150, 255, 255),
            new Color32( 50, 200, 130, 255),
            new Color32(235, 155,  50, 255),
            new Color32(220,  80, 100, 255),
            new Color32(160,  90, 220, 255),
            new Color32( 80, 200, 220, 255),
            new Color32(240, 200,  60, 255),
            new Color32(200, 120,  80, 255),
        };

        public StackedPanel() : base("Stacked Bars") { }

        protected override void BuildControls()
        {
            AddControl(MakeSliderInt("Bars", 5, 200, _barCount, v =>
            {
                _barCount = v;
                Regenerate();
            }));

            AddControl(MakeSliderInt("Segments", 1, 1000, _segmentCount, v =>
            {
                _segmentCount = v;

                if (v > StackPalette.Length && !_useHsl)
                {
                    _useHsl = true;
                    _hslToggle.text = "HSL On";
                }

                Regenerate();
            }));

            _hslToggle = MakeToggleButton("HSL On", "HSL Off", _useHsl, on =>
            {
                _useHsl = on;
                Regenerate();
            });
            AddControl(_hslToggle);

            AddControl(MakeButton("Regenerate", Regenerate));
            AddSortToggle();
            AddOverlayToggle();
            AddZoomYToggle();
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            var bars     = new BarEntry[_barCount];
            var segments = new BarSegment[_barCount * _segmentCount];
            int segIdx   = 0;

            for (int b = 0; b < _barCount; b++)
            {
                int   segStart = segIdx;
                float total    = 0f;

                for (int s = 0; s < _segmentCount; s++)
                {
                    float value = Random.Range(5f, 30f);
                    total += value;

                    Color color;
                    if (_useHsl || _segmentCount > StackPalette.Length)
                        color = HslColorUtility.HueRotation(s, _segmentCount);
                    else
                        color = StackPalette[s % StackPalette.Length];

                    segments[segIdx++] = new BarSegment(value, color);
                }

                bars[b] = new BarEntry(segStart, _segmentCount, total);
            }

            Graph.SetData(bars, _barCount, segments, segIdx);

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

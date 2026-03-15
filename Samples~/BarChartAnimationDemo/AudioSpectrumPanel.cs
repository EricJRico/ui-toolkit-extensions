using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.AnimationDemo
{
    /// <summary>
    /// Simulated music visualizer with frequency bands that pulse to a synthesized beat.
    /// No grid or axes — clean neon spectrum aesthetic.
    /// </summary>
    sealed class AudioSpectrumPanel : AnimationDemoPanel
    {
        private int   _bandCount = 32;
        private int   _bpm       = 120;
        private float _decay     = 6f;

        private float[] _current;    // smoothed display values
        private float   _beatTimer;
        private float   _phase;      // global oscillator phase

        // Pre-allocated work arrays (resized when band count changes)
        private BarEntry[]   _bars;
        private BarSegment[] _segments;

        public AudioSpectrumPanel() : base("Frequency Visualizer", new AnimationPanelTheme
        {
            CardBackground  = new Color(0.05f, 0.03f, 0.08f),
            CardBorder      = new Color(0.25f, 0.1f,  0.35f),
            TitleColor      = new Color(0.95f, 0.7f,  0.95f),
            ThemeClassName  = "audio-spectrum-theme",
            ThemeStyleSheet = Resources.Load<StyleSheet>("AudioSpectrumTheme"),
            BehaviorSettings = new BarGraphSettings
            {
                ShowGrid = false,
                ShowAxes = false,
                MinValue = 0f,
                MaxValue = 100f,
            }
        })
        { }

        protected override void BuildControls()
        {
            AddPlayPauseToggle();

            AddControl(MakeSliderInt("BPM", 80, 180, _bpm, v => _bpm = v));
            AddControl(MakeSliderInt("Bands", 16, 64, _bandCount, v =>
            {
                _bandCount = v;
                AllocateArrays();
            }));
            AddControl(MakeSlider("Decay", 1f, 15f, _decay, v => _decay = v));
            AddControl(MakeButton("Regenerate", Regenerate));

            Regenerate();
        }

        public override void Regenerate()
        {
            _phase     = 0f;
            _beatTimer = 0f;
            AllocateArrays();
        }

        private void AllocateArrays()
        {
            _current  = new float[_bandCount];
            _bars     = new BarEntry[_bandCount];
            _segments = new BarSegment[_bandCount];
        }

        public override void OnUpdate(float dt)
        {
            if (!IsPlaying || _current == null) return;

            float effectiveDt = dt * Speed;
            _phase += effectiveDt;

            // Beat timing
            float beatInterval = 60f / _bpm;
            _beatTimer += effectiveDt;
            float timeSinceBeat = _beatTimer % beatInterval;
            bool onBeat = _beatTimer >= beatInterval;
            if (onBeat) _beatTimer -= beatInterval;

            // Beat impulse (exponential decay from last beat)
            float beatPulse = Mathf.Exp(-_decay * timeSinceBeat);

            for (int i = 0; i < _bandCount; i++)
            {
                float t = (float)i / (_bandCount - 1); // 0..1 across bands

                // Oscillator bank — each affects a frequency range
                float bass   = OscillatorContribution(t, 0f,   0.25f, 2f,  80f, i);
                float mid    = OscillatorContribution(t, 0.2f, 0.65f, 4f,  50f, i);
                float treble = OscillatorContribution(t, 0.55f, 1f,   8f,  30f, i);

                float target = bass + mid + treble;

                // Beat impulse adds energy primarily to bass/mid
                float beatWeight = 1f - t * 0.7f; // bass gets more
                target += beatPulse * 60f * beatWeight;

                // Clamp and add slight noise
                target = Mathf.Clamp(target + Random.Range(-2f, 2f), 0f, 100f);

                // Smooth towards target
                float smoothRate = target > _current[i] ? 25f : _decay * 1.5f;
                _current[i] = Mathf.Lerp(_current[i], target, effectiveDt * smoothRate);

                // Color: hue rotates from red (bass) through yellow, green, cyan, blue (treble)
                float hue = t * 0.7f;                         // 0=red, 0.7=blue
                float brightness = 0.6f + 0.4f * (_current[i] / 100f); // brighter when loud
                Color color = Color.HSVToRGB(hue, 0.85f, brightness);

                _segments[i] = new BarSegment(_current[i], (Color32)color);
                _bars[i]     = new BarEntry(i, 1, _current[i]);
            }

            Graph.SetData(_bars, _bandCount, _segments, _bandCount);
        }

        private float OscillatorContribution(float bandPos, float rangeMin, float rangeMax,
            float freq, float amplitude, int bandIndex)
        {
            // Gaussian-like falloff centered on the band's frequency range
            float center = (rangeMin + rangeMax) * 0.5f;
            float sigma  = (rangeMax - rangeMin) * 0.5f;
            float dist   = (bandPos - center) / Mathf.Max(sigma, 0.01f);
            float weight = Mathf.Exp(-0.5f * dist * dist);

            // Phase offset per band creates a travelling wave effect
            float phaseOffset = bandIndex * 0.15f;
            float osc = Mathf.Sin(_phase * freq * Mathf.PI * 2f + phaseOffset);
            osc = (osc + 1f) * 0.5f; // normalize 0..1

            return osc * amplitude * weight;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using BarGraph.Core;

namespace BarGraph.EditorDemo
{
    /// <summary>
    /// Drives per-panel animations in the editor gallery.
    /// Uses <c>EditorApplication.timeSinceStartup</c> for delta-time
    /// since <c>Time.deltaTime</c> is zero outside Play mode.
    /// </summary>
    public class GalleryAnimator
    {
        private readonly List<GalleryPanel> _panels;
        private bool   _running;
        private double _lastTime;
        private float  _speed = 1f;

        // Per-panel animation state
        private float[] _phase;
        private int[]   _appendIndex;

        public bool  Running { get => _running; set => _running = value; }
        public float Speed   { get => _speed;   set => _speed = Mathf.Max(0.1f, value); }

        public GalleryAnimator(List<GalleryPanel> panels)
        {
            _panels      = panels;
            _phase       = new float[panels.Count];
            _appendIndex = new int[panels.Count];
        }

        public void Play()
        {
            _running  = true;
            _lastTime = EditorApplication.timeSinceStartup;
        }

        public void Pause()  => _running = false;

        public void Stop()
        {
            _running = false;
            for (int i = 0; i < _phase.Length; i++)
            {
                _phase[i]       = 0f;
                _appendIndex[i] = 0;
            }
        }

        /// <summary>
        /// Call from <c>EditorWindow.Update()</c>.
        /// Returns true if any panel was mutated (caller should Repaint).
        /// </summary>
        public bool Tick()
        {
            if (!_running) return false;

            double now = EditorApplication.timeSinceStartup;
            float  dt  = (float)(now - _lastTime) * _speed;
            _lastTime  = now;

            bool dirty = false;
            for (int i = 0; i < _panels.Count; i++)
            {
                if (AnimatePanel(_panels[i], i, dt))
                    dirty = true;
            }
            return dirty;
        }

        private bool AnimatePanel(GalleryPanel panel, int index, float dt)
        {
            if (panel.Graph == null) return false;
            _phase[index] += dt;

            switch (panel.State.Preset)
            {
                case DatasetPreset.SineWave:
                    return AnimateSine(panel, index);

                case DatasetPreset.Random:
                    if (_phase[index] >= 2f)
                    {
                        _phase[index] -= 2f;
                        GalleryDataGenerators.LoadRandom(panel.Graph, panel.State.BarCount);
                        return true;
                    }
                    return false;

                case DatasetPreset.Gaussian:
                    if (_phase[index] >= 1.5f)
                    {
                        _phase[index] -= 1.5f;
                        GalleryDataGenerators.LoadGaussian(panel.Graph, panel.State.BarCount);
                        return true;
                    }
                    return false;

                case DatasetPreset.Stacked:
                case DatasetPreset.ManyStacks:
                    if (_phase[index] >= 2f)
                    {
                        _phase[index] -= 2f;
                        bool useHsl = panel.State.Preset == DatasetPreset.ManyStacks
                                   || panel.State.SegmentCount > 8;
                        GalleryDataGenerators.LoadStacked(panel.Graph,
                            panel.State.BarCount, panel.State.SegmentCount, useHsl);
                        return true;
                    }
                    return false;

                case DatasetPreset.StressTest:
                    return AnimateStressColorShift(panel, index);

                default:
                    return false;
            }
        }

        private bool AnimateSine(GalleryPanel panel, int index)
        {
            if (_phase[index] < 0.05f) return false;
            _phase[index] = 0f;

            int n = panel.State.BarCount;
            float offset = (float)EditorApplication.timeSinceStartup * _speed * 0.5f;

            var values = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n + offset;
                float v = 50f + 40f * Mathf.Sin(t * Mathf.PI * 6f)
                               + 15f * Mathf.Sin(t * Mathf.PI * 18f);
                values.Add(Mathf.Max(0f, v));
            }
            panel.Graph.SetData(values, new Color(0.3f, 0.65f, 1f));
            return true;
        }

        private bool AnimateStressColorShift(GalleryPanel panel, int index)
        {
            if (_phase[index] < 0.1f) return false;
            _phase[index] = 0f;

            int n = panel.State.BarCount;
            float shift = (float)EditorApplication.timeSinceStartup * _speed * 0.15f;

            var segArr = new BarSegment[n];
            var barArr = new BarEntry[n];
            var gradient = new Color32[]
            {
                new Color32(  0, 255, 255, 255),
                new Color32(  0, 255, 128, 255),
                new Color32(255, 220,   0, 255),
                new Color32(255,  80,   0, 255),
            };

            for (int i = 0; i < n; i++)
            {
                float t   = ((float)i / n + shift) % 1f;
                float v   = 50f + 45f * Mathf.Sin(i * 0.07f) * Mathf.Cos(i * 0.013f);
                int   ci  = Mathf.FloorToInt(t * (gradient.Length - 1));
                float lt  = t * (gradient.Length - 1) - ci;
                int   ci2 = Mathf.Min(ci + 1, gradient.Length - 1);
                Color32 c = Color32.Lerp(gradient[ci], gradient[ci2], lt);
                segArr[i] = new BarSegment(v, c);
                barArr[i] = new BarEntry(i, 1, v);
            }
            panel.Graph.SetData(barArr, n, segArr, n);
            return true;
        }
    }
}

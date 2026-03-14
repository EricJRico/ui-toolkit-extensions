using System.Collections.Generic;
using UnityEngine;
using BarGraph.Core;

namespace BarGraph.EditorDemo
{
    /// <summary>
    /// Dataset generators for the editor gallery panels.
    /// </summary>
    static class GalleryDataGenerators
    {
        // ── Fixed palette for small segment counts ──────────────────────────

        public static readonly Color32[] StackPalette =
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

        // ── HSL hue rotation ────────────────────────────────────────────────

        public static Color HueRotation(int index, int total,
            float saturation = 0.75f, float value = 0.85f)
        {
            float hue = (float)index / Mathf.Max(1, total);
            return Color.HSVToRGB(hue, saturation, value);
        }

        // ── Sine wave ───────────────────────────────────────────────────────

        public static void LoadSineWave(BarGraphElement graph, int barCount,
            float frequency = 6f, float amplitude = 40f)
        {
            var values = new List<float>(barCount);
            for (int i = 0; i < barCount; i++)
            {
                float t = (float)i / barCount;
                float v = 50f + amplitude * Mathf.Sin(t * Mathf.PI * frequency)
                               + 15f * Mathf.Sin(t * Mathf.PI * 18f);
                values.Add(Mathf.Max(0f, v));
            }
            graph.SetData(values, new Color(0.3f, 0.65f, 1f));
        }

        // ── Random walk ─────────────────────────────────────────────────────

        public static void LoadRandom(BarGraphElement graph, int barCount)
        {
            var values = new List<float>(barCount);
            float prev = 50f;
            for (int i = 0; i < barCount; i++)
            {
                prev = Mathf.Clamp(prev + Random.Range(-12f, 12f), 2f, 100f);
                values.Add(prev);
            }
            graph.SetData(values, new Color(0.35f, 0.85f, 0.5f));
        }

        // ── Gaussian ────────────────────────────────────────────────────────

        public static void LoadGaussian(BarGraphElement graph, int binCount, int sampleMultiplier = 80)
        {
            var values = new float[binCount];
            int totalSamples = binCount * sampleMultiplier;
            for (int s = 0; s < totalSamples; s++)
            {
                float u1   = Mathf.Max(1e-6f, Random.value);
                float u2   = Random.value;
                float norm = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
                int   bin  = Mathf.Clamp(Mathf.RoundToInt(norm * (binCount / 6f) + binCount / 2f), 0, binCount - 1);
                values[bin]++;
            }
            graph.SetData(new List<float>(values), new Color(0.9f, 0.55f, 0.2f));
        }

        // ── Stacked ─────────────────────────────────────────────────────────

        public static void LoadStacked(BarGraphElement graph, int barCount, int segmentCount, bool useHsl)
        {
            int total  = barCount * segmentCount;
            var bars   = new BarEntry[barCount];
            var segs   = new BarSegment[total];
            int cursor = 0;

            for (int b = 0; b < barCount; b++)
            {
                float sum   = 0f;
                int   start = cursor;
                for (int s = 0; s < segmentCount; s++)
                {
                    float v = Random.Range(5f, 30f);
                    sum += v;
                    Color c = (useHsl || segmentCount > StackPalette.Length)
                        ? HueRotation(s, segmentCount)
                        : (Color)StackPalette[s % StackPalette.Length];
                    segs[cursor++] = new BarSegment(v, c);
                }
                bars[b] = new BarEntry(start, segmentCount, sum);
            }
            graph.SetData(bars, barCount, segs, total);
        }

        // ── Many stacks (HSL) ───────────────────────────────────────────────

        public static void LoadManyStacks(BarGraphElement graph, int barCount, int segmentCount)
        {
            LoadStacked(graph, barCount, segmentCount, true);
        }

        // ── Stress test ─────────────────────────────────────────────────────

        public static void LoadStress(BarGraphElement graph, int barCount)
        {
            var segArr = new BarSegment[barCount];
            var barArr = new BarEntry[barCount];
            var gradient = new Color32[]
            {
                new Color32(  0, 255, 255, 255),
                new Color32(  0, 255, 128, 255),
                new Color32(255, 220,   0, 255),
                new Color32(255,  80,   0, 255),
            };

            for (int i = 0; i < barCount; i++)
            {
                float t   = (float)i / barCount;
                float v   = 50f + 45f * Mathf.Sin(i * 0.07f) * Mathf.Cos(i * 0.013f);
                int   ci  = Mathf.FloorToInt(t * (gradient.Length - 1));
                float lt  = t * (gradient.Length - 1) - ci;
                int   ci2 = Mathf.Min(ci + 1, gradient.Length - 1);
                Color32 c = Color32.Lerp(gradient[ci], gradient[ci2], lt);
                segArr[i] = new BarSegment(v, c);
                barArr[i] = new BarEntry(i, 1, v);
            }
            graph.SetData(barArr, barCount, segArr, barCount);
        }

        // ── Overlay ─────────────────────────────────────────────────────────

        public static void LoadOverlay(BarGraphElement graph, int barCount)
        {
            var values = new List<float>(barCount);
            for (int i = 0; i < barCount; i++)
                values.Add(Random.Range(10f, 90f));
            graph.SetOverlay(values, new Color(1f, 0.7f, 0.2f, 0.6f));
        }
    }
}

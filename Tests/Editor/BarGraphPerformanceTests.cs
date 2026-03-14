using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Tests
{
    /// <summary>
    /// Performance tests for <see cref="BarGraphElement"/> worst-case scenarios.
    ///
    /// Rendering tests use <c>Measure.Frames()</c> to measure actual per-frame cost
    /// through the real UI Toolkit rendering pipeline.
    ///
    /// Data operation tests use <c>Measure.Method().GC()</c> to measure execution
    /// time and GC allocations for synchronous operations.
    /// </summary>
    [TestFixture]
    public class BarGraphPerformanceTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Test host window
        // ─────────────────────────────────────────────────────────────────

        private PerfTestWindow _window;

        private class PerfTestWindow : EditorWindow
        {
            public BarGraphElement Graph;

            public static PerfTestWindow Create(int width, int height)
            {
                var w = CreateInstance<PerfTestWindow>();
                w.minSize = new Vector2(width, height);
                w.maxSize = new Vector2(width, height);
                w.ShowUtility();

                w.Graph = new BarGraphElement();
                w.Graph.style.flexGrow = 1;
                w.rootVisualElement.Add(w.Graph);

                return w;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Data generators
        // ─────────────────────────────────────────────────────────────────

        private static float[] GenerateFlatData(int count)
        {
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = 10f + (i % 90);
            return data;
        }

        private static (BarEntry[] bars, BarSegment[] segments) GenerateStackedData(
            int barCount, int segsPerBar, int uniqueColours)
        {
            int totalSegs = barCount * segsPerBar;
            var bars = new BarEntry[barCount];
            var segments = new BarSegment[totalSegs];

            int cursor = 0;
            for (int b = 0; b < barCount; b++)
            {
                float sum = 0f;
                int start = cursor;
                for (int s = 0; s < segsPerBar; s++)
                {
                    float v = 5f + (s * 3f);
                    sum += v;
                    int bucket = uniqueColours > 0 ? cursor % uniqueColours : cursor;
                    byte r = (byte)((bucket * 37) % 256);
                    byte g = (byte)((bucket * 73) % 256);
                    byte b2 = (byte)((bucket * 131) % 256);
                    segments[cursor] = new BarSegment(v, new Color32(r, g, b2, 255));
                    cursor++;
                }
                bars[b] = new BarEntry(start, segsPerBar, sum);
            }
            return (bars, segments);
        }

        private static (BarEntry[] bars, BarSegment[] segments) GenerateUniqueColourBars(int count)
        {
            var bars = new BarEntry[count];
            var segments = new BarSegment[count];
            for (int i = 0; i < count; i++)
            {
                float v = 10f + (i % 90);
                byte r = (byte)((i * 37) % 256);
                byte g = (byte)((i * 73 + 50) % 256);
                byte b = (byte)((i * 131 + 100) % 256);
                segments[i] = new BarSegment(v, new Color32(r, g, b, 255));
                bars[i] = new BarEntry(i, 1, v);
            }
            return (bars, segments);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Per-Frame Rendering Tests (Measure.Frames)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest, Performance]
        public IEnumerator Frame_MaxSegmentThroughput_1K_Bars_x_1000_Segments()
        {
            // 1K bars × 1000 segments = 1M segment iterations, single colour
            var (bars, segments) = GenerateStackedData(1000, 1000, 1);

            _window = PerfTestWindow.Create(1200, 600);
            _window.Graph.SetData(bars, bars.Length, segments, segments.Length);

            // Let layout settle
            yield return null;

            yield return Measure.Frames()
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }

        [UnityTest, Performance]
        public IEnumerator Frame_MaxUniqueColours_10K_Bars()
        {
            // 10K bars, every bar a different colour = 10K BeginPath/Fill calls
            var (bars, segments) = GenerateUniqueColourBars(10_000);

            _window = PerfTestWindow.Create(1200, 600);
            _window.Graph.SetData(bars, bars.Length, segments, segments.Length);

            yield return null;

            yield return Measure.Frames()
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }

        [UnityTest, Performance]
        public IEnumerator Frame_MaxBarCount_10K_DirectPath()
        {
            // 10K single-colour bars, wide window so stride >= 1px
            // (100K bars can't reach stride >= 1px even at MaxZoomX=50 in 2000px)
            _window = PerfTestWindow.Create(2000, 600);
            _window.Graph.SetData(GenerateFlatData(10_000));

            yield return null;

            yield return Measure.Frames()
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }

        [UnityTest, Performance]
        public IEnumerator Frame_MaxBarCount_100K_LODPath()
        {
            // 100K bars on narrow element → stride < 1px → LOD pixel-column merging
            // Cost should be bounded by plot width (~436px), not bar count
            _window = PerfTestWindow.Create(500, 400);
            _window.Graph.SetData(GenerateFlatData(100_000));

            yield return null;

            yield return Measure.Frames()
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }

        [UnityTest, Performance]
        public IEnumerator Frame_MaxSelection_100K_AllSelected()
        {
            // 100K bars, all selected — worst case for DrawHighlights
            int barCount = 100_000;
            _window = PerfTestWindow.Create(500, 400);
            _window.Graph.SetData(GenerateFlatData(barCount));

            for (int i = 0; i < barCount; i++)
                _window.Graph.ViewState.SelectedBars.Add(i);

            yield return null;

            yield return Measure.Frames()
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }

        [UnityTest, Performance]
        public IEnumerator Frame_DeepStack_ManyColours_1K_x_100_x_100()
        {
            // 1K bars × 100 segments/bar × 100 unique colours
            var (bars, segments) = GenerateStackedData(1000, 100, 100);

            _window = PerfTestWindow.Create(1200, 600);
            _window.Graph.SetData(bars, bars.Length, segments, segments.Length);

            yield return null;

            yield return Measure.Frames()
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Data Operation Tests (Measure.Method + GC)
        // ─────────────────────────────────────────────────────────────────

        [Test, Performance]
        public void Data_SetData_100K_SteadyState()
        {
            var graph = new BarGraphElement();
            var data = GenerateFlatData(100_000);

            // Warm up — grow arrays to full size
            graph.SetData(data);

            Measure.Method(() =>
            {
                graph.SetData(data);
            })
            .GC()
            .WarmupCount(3)
            .MeasurementCount(10)
            .Run();
        }

        [Test, Performance]
        public void Data_Sort_100K_Bars()
        {
            var graph = new BarGraphElement();
            graph.SetData(GenerateFlatData(100_000));

            Measure.Method(() =>
            {
                graph.ViewState.SortDirty = true;
                graph.EnsureSortMap();
            })
            .GC()
            .WarmupCount(3)
            .MeasurementCount(10)
            .Run();
        }

        [Test, Performance]
        public void Data_AppendBar_10K()
        {
            var graph = new BarGraphElement();
            graph.SetData(GenerateFlatData(100));

            Measure.Method(() =>
            {
                graph.ClearData();
                for (int i = 0; i < 10_000; i++)
                    graph.AppendBar(10f + (i % 90));
            })
            .GC()
            .WarmupCount(2)
            .MeasurementCount(5)
            .Run();
        }

        [Test, Performance]
        public void Data_Snapshot_100K_AllSelected()
        {
            var graph = new BarGraphElement();
            int barCount = 100_000;
            graph.SetData(GenerateFlatData(barCount));

            for (int i = 0; i < barCount; i++)
                graph.ViewState.SelectedBars.Add(i);

            Measure.Method(() =>
            {
                graph.CreateViewSnapshot();
            })
            .GC()
            .WarmupCount(3)
            .MeasurementCount(10)
            .Run();
        }
    }
}

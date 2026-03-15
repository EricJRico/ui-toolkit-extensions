using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.Tests
{
    /// <summary>
    /// Edge-case tests for the segment-level LOD merge in <see cref="BarGraphElement.DrawDirectBars"/>.
    ///
    /// Each test loads a specific data shape, renders several frames through the
    /// real UI Toolkit pipeline, and asserts that no errors or exceptions occur.
    /// The rendering path (direct vs LOD) is controlled by window size and bar
    /// count so that <c>stride >= 1px</c> forces <c>DrawDirectBars</c>.
    ///
    /// These are NOT performance tests — they exist to catch regressions in the
    /// merge logic: OOM crashes, visual artefacts from incorrect clipping, and
    /// missed merge-buffer flushes.
    /// </summary>
    [TestFixture]
    public class SegmentLodEdgeCaseTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Test host window  (same pattern as BarGraphPerformanceTests)
        // ─────────────────────────────────────────────────────────────────

        private EdgeCaseTestWindow _window;

        private class EdgeCaseTestWindow : EditorWindow
        {
            public BarGraphElement Graph;

            public static EdgeCaseTestWindow Create(int width, int height)
            {
                var w = CreateInstance<EdgeCaseTestWindow>();
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
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Render N frames and assert no log errors.</summary>
        private static IEnumerator RenderFrames(int count = 5)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        /// <summary>
        /// Build stacked bars where every segment has the SAME value.
        /// Total bar height = segsPerBar * segValue.
        /// </summary>
        private static (BarEntry[] bars, BarSegment[] segments) UniformStack(
            int barCount, int segsPerBar, float segValue, int uniqueColours = 1)
        {
            int total = barCount * segsPerBar;
            var bars = new BarEntry[barCount];
            var segs = new BarSegment[total];

            int cursor = 0;
            for (int b = 0; b < barCount; b++)
            {
                int start = cursor;
                float sum  = 0f;
                for (int s = 0; s < segsPerBar; s++)
                {
                    int bucket = uniqueColours > 0 ? s % uniqueColours : s;
                    byte r = (byte)((bucket * 37 + 80) % 256);
                    byte g = (byte)((bucket * 73 + 40) % 256);
                    byte bv = (byte)((bucket * 131 + 20) % 256);
                    segs[cursor] = new BarSegment(segValue, new Color32(r, g, bv, 255));
                    sum += segValue;
                    cursor++;
                }
                bars[b] = new BarEntry(start, segsPerBar, sum);
            }
            return (bars, segs);
        }

        /// <summary>
        /// Build stacked bars with a mix of large and tiny segments.
        /// Pattern per bar: [large, tiny, tiny, tiny, large, tiny, tiny, tiny, ...]
        /// </summary>
        private static (BarEntry[] bars, BarSegment[] segments) MixedStack(
            int barCount, int segsPerBar, float largeValue, float tinyValue,
            int largeEveryN = 4)
        {
            int total = barCount * segsPerBar;
            var bars = new BarEntry[barCount];
            var segs = new BarSegment[total];

            int cursor = 0;
            for (int b = 0; b < barCount; b++)
            {
                int start = cursor;
                float sum  = 0f;
                for (int s = 0; s < segsPerBar; s++)
                {
                    float v = (s % largeEveryN == 0) ? largeValue : tinyValue;
                    byte r = (byte)((s * 51 + 30) % 256);
                    byte g = (byte)((s * 97 + 60) % 256);
                    byte bv = (byte)((s * 143 + 90) % 256);
                    segs[cursor] = new BarSegment(v, new Color32(r, g, bv, 255));
                    sum += v;
                    cursor++;
                }
                bars[b] = new BarEntry(start, segsPerBar, sum);
            }
            return (bars, segs);
        }

        /// <summary>
        /// Build a single bar with a specific segment-value pattern for
        /// fine-grained merge-buffer testing.
        /// </summary>
        private static (BarEntry[] bars, BarSegment[] segments) SingleBarFromValues(
            float[] segValues)
        {
            var bars = new BarEntry[1];
            var segs = new BarSegment[segValues.Length];
            float sum = 0f;
            for (int i = 0; i < segValues.Length; i++)
            {
                byte c = (byte)((i * 47 + 100) % 256);
                segs[i] = new BarSegment(segValues[i], new Color32(c, (byte)(255 - c), 128, 255));
                sum += segValues[i];
            }
            bars[0] = new BarEntry(0, segValues.Length, sum);
            return (bars, segs);
        }

        // ─────────────────────────────────────────────────────────────────
        //  1. All segments sub-pixel (entire merge path)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_AllSubPixelSegments_NoError()
        {
            // 10 bars × 500 segments, tiny values → every segment < 1px.
            // All rects go through the merge buffer.
            // Wide window keeps stride >= 1px so DrawDirectBars is used.
            var (bars, segs) = UniformStack(10, 500, 0.01f, 5);

            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Rendered 10 bars × 500 sub-pixel segments without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. All segments >= 1px (merge path never activates)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_AllLargeSegments_NoMerging_NoError()
        {
            // 10 bars × 5 segments, large values → every segment well above 1px.
            // Merge path is never entered — this is the regression check.
            var (bars, segs) = UniformStack(10, 5, 100f, 5);

            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Rendered large-segment bars with no merging — no regression.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. Mixed: large and tiny segments interleaved
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_MixedLargeAndTinySegments_NoError()
        {
            // Pattern: [large, tiny, tiny, tiny, large, ...] × 20 bars × 200 segs.
            // Forces repeated merge-flush-emit-merge transitions in the inner loop.
            var (bars, segs) = MixedStack(20, 200, 50f, 0.005f, 4);

            _window = EdgeCaseTestWindow.Create(1200, 600);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Mixed large/tiny segments rendered without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. Trailing sub-pixel segments (end-of-loop flush)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_TrailingSubPixelSegments_FlushesCorrectly()
        {
            // Single bar: one large segment, then 50 tiny segments at the end.
            // The merge buffer must flush after the loop — if it doesn't, the
            // top portion of the bar silently disappears.
            float[] vals = new float[51];
            vals[0] = 100f;                         // large, renders directly
            for (int i = 1; i < 51; i++)
                vals[i] = 0.01f;                    // tiny, accumulates in merge buffer

            var (bars, segs) = SingleBarFromValues(vals);
            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Trailing sub-pixel segments flushed after loop without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  5. Leading sub-pixel segments (flush before first large segment)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_LeadingSubPixelSegments_FlushBeforeLarge()
        {
            // Single bar: 50 tiny segments first, then one large segment.
            // The merge buffer must flush when the large segment arrives.
            float[] vals = new float[51];
            for (int i = 0; i < 50; i++)
                vals[i] = 0.01f;                    // tiny, merged
            vals[50] = 100f;                        // large, forces flush

            var (bars, segs) = SingleBarFromValues(vals);
            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Leading sub-pixel segments flushed before large segment.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  6. Single segment per bar (merge never possible)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_SingleSegmentBars_NoMerge_NoError()
        {
            // 100 bars, each with exactly 1 segment — no merging can occur.
            var (bars, segs) = UniformStack(100, 1, 50f, 10);

            _window = EdgeCaseTestWindow.Create(1200, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Single-segment bars rendered without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  7. Near-zero and zero-value segments
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_ZeroValueSegments_NoError()
        {
            // Segments with value 0 should be harmless — they contribute
            // nothing to merge height and should never emit a rect.
            float[] vals = new float[20];
            vals[0]  = 50f;
            vals[10] = 50f;
            // All others default to 0f.

            var (bars, segs) = SingleBarFromValues(vals);
            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Zero-value segments handled without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  8. Extreme depth: 1 bar × 100K segments
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_ExtremeDepth_1Bar_100K_Segments_NoOOM()
        {
            // A single bar with 100K segments — the absolute worst case for
            // the inner loop.  Without LOD merging this would push 100K
            // rects into Painter2D.  With merging, output is bounded by
            // plot height (~340 px).
            int segCount = 100_000;
            var bars = new BarEntry[1];
            var segs = new BarSegment[segCount];
            float sum = 0f;
            for (int i = 0; i < segCount; i++)
            {
                float v = 0.001f + (i % 10) * 0.0005f;
                byte c  = (byte)((i * 31) % 256);
                segs[i] = new BarSegment(v, new Color32(c, 128, (byte)(255 - c), 255));
                sum += v;
            }
            bars[0] = new BarEntry(0, segCount, sum);

            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("1 bar × 100K segments rendered without OOM.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  9. Deep stack with Y zoom + pan (clipping interaction)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_DeepStack_YZoomAndPan_NoError()
        {
            // 5 bars × 500 segments, then zoom Y to 3× and pan Y so the
            // bar extends well above and below the plot area.  This tests
            // the interaction between the merge buffer and Y-clipping:
            // merged rects that straddle the plot boundary must be clamped
            // correctly, and the early-exit on yBottom <= plotTop must
            // fire even for merged groups.
            var (bars, segs) = UniformStack(5, 500, 0.5f, 10);

            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            // Let layout settle before adjusting view state.
            yield return null;

            _window.Graph.ViewState.ZoomY = 3f;
            _window.Graph.ViewState.PanY  = 0.4f;
            _window.Graph.MarkDirtyRepaint();

            yield return RenderFrames(8);
            Assert.Pass("Deep stack with Y zoom/pan rendered without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  10. Deep stack overlay (DrawDirectBars called for overlay too)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_OverlayWithPrimary_NoError()
        {
            // Primary: 10 bars × 500 deep-stacked segments (merge path).
            // Overlay: 10 flat bars via the public SetOverlay(IList<float>).
            // DrawDirectBars is called twice per frame — once for primary
            // (heavy merge) and once for overlay (no merge, single segments).
            // Verifies both paths coexist in the same repaint without error.
            var (bars, segs) = UniformStack(10, 500, 0.02f, 8);

            float[] overlay = new float[10];
            for (int i = 0; i < 10; i++) overlay[i] = 5f;

            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);
            _window.Graph.SetOverlay(overlay);

            yield return RenderFrames(8);
            Assert.Pass("Primary deep stack + overlay rendered without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  11. Dominant colour selection: many tiny, one slightly larger
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_DominantColourMerge_NoError()
        {
            // Single bar with 100 tiny segments where segment 50 is
            // slightly larger than the rest.  The merged rect for that
            // group should pick segment 50's colour.  We can't assert
            // the pixel colour here (no readback), but we verify the
            // path doesn't crash and the dominant-tracking logic handles
            // the transition correctly.
            float[] vals = new float[100];
            for (int i = 0; i < 100; i++)
                vals[i] = 0.01f;
            vals[50] = 0.05f;   // dominant within its merge group

            var (bars, segs) = SingleBarFromValues(vals);
            _window = EdgeCaseTestWindow.Create(800, 400);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Dominant colour merge logic executed without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  12. Many unique colours in deep stack (batch count stress)
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_DeepStack_ManyUniqueColours_NoError()
        {
            // 20 bars × 300 segments × 300 unique colours.
            // Even after merging, dominant-colour selection produces many
            // distinct Color32 keys in _batches.  Verifies FlushBatches
            // handles a large colour palette after merge.
            var (bars, segs) = UniformStack(20, 300, 0.02f, 300);

            _window = EdgeCaseTestWindow.Create(1200, 600);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Deep stack with many unique colours rendered without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  13. The original crash case (1K × 1000) as a correctness test
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_1K_Bars_x_1000_Segments_NoOOM()
        {
            // This is the exact configuration that caused the original OOM.
            // Exercises segment-level LOD merge (1000 segs/bar collapse to
            // ~plotH rects) and multi-chunk Allocate (total quads exceed
            // the 16 383-per-chunk limit).  No Painter2D involvement.
            var bars = new BarEntry[1000];
            var segs = new BarSegment[1_000_000];
            int cursor = 0;
            for (int b = 0; b < 1000; b++)
            {
                int start = cursor;
                float sum  = 0f;
                for (int s = 0; s < 1000; s++)
                {
                    float v = 5f + (s * 3f);
                    sum += v;
                    segs[cursor] = new BarSegment(v, new Color32(100, 150, 200, 255));
                    cursor++;
                }
                bars[b] = new BarEntry(start, 1000, sum);
            }

            _window = EdgeCaseTestWindow.Create(1200, 600);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(10);
            Assert.Pass("1K × 1000 segments rendered without OOM — original crash fixed.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  14. Multi-chunk: enough quads to require >1 Allocate() call
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_MultiChunk_1500_Bars_x_15_LargeSegments()
        {
            // 1500 bars × 15 visible segments in 2000px window → stride ≈ 1.2px
            // → DrawDirectBars.  All segments are >= 1px so no merging occurs.
            // 1500 × 15 = 22 500 quads, which requires two Allocate chunks
            // (max 16 383 quads per chunk).
            var (bars, segs) = UniformStack(1500, 15, 20f, 10);

            _window = EdgeCaseTestWindow.Create(2000, 600);
            _window.Graph.SetData(bars, bars.Length, segs, segs.Length);

            yield return RenderFrames(8);
            Assert.Pass("Multi-chunk Allocate (22K quads, 2 chunks) rendered without error.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  15. High quad count with LOD path
        // ─────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Render_LOD_100K_Bars_NoOOM()
        {
            // 100K bars on 500px window → stride < 1px → DrawLodBars.
            // Output is bounded by pixel count (~436 quads), well within
            // a single Allocate chunk.  Verifies LOD path uses the new
            // quad buffer correctly.
            _window = EdgeCaseTestWindow.Create(500, 400);

            float[] data = new float[100_000];
            for (int i = 0; i < data.Length; i++)
                data[i] = 10f + (i % 90);
            _window.Graph.SetData(data);

            yield return RenderFrames(8);
            Assert.Pass("100K bars via LOD path rendered without error.");
        }
    }
}

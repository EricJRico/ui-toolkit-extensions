using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BarGraph.Core;

namespace BarGraph.Tests
{
    /// <summary>
    /// Integration tests for <see cref="BarGraphViewSnapshot"/> round-trip through
    /// <see cref="BarGraphElement.CreateViewSnapshot"/> and
    /// <see cref="BarGraphElement.RestoreViewSnapshot"/>.
    ///
    /// Each test creates a real <see cref="BarGraphElement"/>, loads real data,
    /// sets real state, snapshots, builds a fresh element, restores, and verifies
    /// the result matches.
    ///
    /// NOTE: Pan values (PanX/PanY) are tested via snapshot capture rather than
    /// full round-trip because <c>RestoreViewSnapshot</c> calls <c>ClampViewState</c>
    /// which depends on <c>contentRect</c> having real layout dimensions.  In EditMode
    /// tests the element is never attached to a panel, so contentRect is zero-sized
    /// and pan gets clamped to 0.  Pan round-trip is verified manually in-editor.
    /// </summary>
    [TestFixture]
    public class BarGraphViewSnapshotTests
    {
        private BarGraphElement _source;
        private BarGraphElement _target;

        [SetUp]
        public void SetUp()
        {
            _source = new BarGraphElement();
            _target = new BarGraphElement();

            // Load identical data into both elements so restore operates
            // against the same bar count the snapshot was captured from.
            var data = new List<float> { 10f, 25f, 5f, 40f, 15f, 30f, 20f, 35f, 8f, 50f };
            var color = new Color(0.3f, 0.6f, 1f);
            _source.SetData(data, color);
            _target.SetData(data, color);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Feature round-trips
        // ─────────────────────────────────────────────────────────────────

        // ZoomX range: [1.0 .. 50.0],  ZoomY range: [0.1 .. 20.0]
        // RestoreViewSnapshot clamps to these bounds, so test at min, max, and mid.
        [TestCase(1.0f,  0.1f)]   // lower bounds
        [TestCase(25.0f, 10.0f)]  // mid range
        [TestCase(50.0f, 20.0f)]  // upper bounds
        public void Zoom_SurvivesRoundTrip(float zoomX, float zoomY)
        {
            _source.ViewState.ZoomX = zoomX;
            _source.ViewState.ZoomY = zoomY;

            var snap = _source.CreateViewSnapshot();
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(zoomX, _target.ViewState.ZoomX, 0.001f);
            Assert.AreEqual(zoomY, _target.ViewState.ZoomY, 0.001f);
        }

        [Test]
        public void Pan_CapturedInSnapshot()
        {
            // Pan round-trip can't be tested via RestoreViewSnapshot because
            // ClampViewState depends on contentRect layout (zero in EditMode).
            // Instead, verify the snapshot struct captures pan values correctly.
            float zoomX = 5f, panX = 3.7f, panY = 0.4f;
            _source.ViewState.ZoomX = zoomX;
            _source.ViewState.PanX  = panX;
            _source.ViewState.PanY  = panY;

            var snap = _source.CreateViewSnapshot();

            Assert.AreEqual(panX,  snap.PanX,  0.001f);
            Assert.AreEqual(panY,  snap.PanY,  0.001f);
            Assert.AreEqual(zoomX, snap.ZoomX, 0.001f);
        }

        [Test]
        public void SortMode_SurvivesRoundTrip()
        {
            _source.SetSortMode(SortMode.ByValue, descending: true);

            var snap = _source.CreateViewSnapshot();
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(SortMode.ByValue, _target.ViewState.SortMode);
            Assert.IsTrue(_target.ViewState.SortDescending);
            Assert.IsTrue(_target.ViewState.SortDirty,
                "SortDirty should be set so the sort map rebuilds on next repaint");
        }

        [Test]
        public void SortMode_Ascending_SurvivesRoundTrip()
        {
            _source.SetSortMode(SortMode.ByValue, descending: false);

            var snap = _source.CreateViewSnapshot();
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(SortMode.ByValue, _target.ViewState.SortMode);
            Assert.IsFalse(_target.ViewState.SortDescending);
        }

        [Test]
        public void Selection_SurvivesRoundTrip()
        {
            var selectedIndices = new[] { 2, 5, 8 };
            foreach (int idx in selectedIndices)
                _source.ViewState.SelectedBars.Add(idx);

            var snap = _source.CreateViewSnapshot();
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(selectedIndices.Length, _target.ViewState.SelectedBars.Count);
            foreach (int idx in selectedIndices)
                Assert.IsTrue(_target.ViewState.SelectedBars.Contains(idx), $"Should contain bar {idx}");
        }

        // FocusedBarIndex range: [0 .. barCount-1] (10 bars in SetUp)
        // RestoreViewSnapshot resets to -1 if out of range, so test at bounds.
        [TestCase(0)]   // first bar
        [TestCase(5)]   // mid range
        [TestCase(9)]   // last bar
        public void FocusedBar_SurvivesRoundTrip(int focusIdx)
        {
            _source.ViewState.FocusedBarIndex = focusIdx;

            var snap = _source.CreateViewSnapshot();
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(focusIdx, _target.ViewState.FocusedBarIndex);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Full state round-trip
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void AllFeatures_SurviveRoundTrip()
        {
            // Set every feature to a non-default value
            float zoomX = 4.2f, zoomY = 1.8f, panX = 2.5f, panY = 0.3f;
            var sortMode = SortMode.ByValue;
            bool sortDesc = true;
            int focusIdx = 3;
            var selectedIndices = new[] { 1, 3, 9 };

            _source.ViewState.ZoomX = zoomX;
            _source.ViewState.ZoomY = zoomY;
            _source.ViewState.PanX  = panX;
            _source.ViewState.PanY  = panY;
            _source.SetSortMode(sortMode, descending: sortDesc);
            _source.ViewState.FocusedBarIndex = focusIdx;
            foreach (int idx in selectedIndices)
                _source.ViewState.SelectedBars.Add(idx);

            var snap = _source.CreateViewSnapshot();

            // Verify snapshot captured everything (including pan, which
            // can't survive restore in a headless test due to ClampViewState)
            Assert.AreEqual(zoomX, snap.ZoomX, 0.001f);
            Assert.AreEqual(zoomY, snap.ZoomY, 0.001f);
            Assert.AreEqual(panX,  snap.PanX,  0.001f);
            Assert.AreEqual(panY,  snap.PanY,  0.001f);
            Assert.AreEqual(sortMode, snap.SortMode);
            Assert.AreEqual(sortDesc, snap.SortDescending);
            Assert.AreEqual(focusIdx, snap.FocusedBarIndex);
            Assert.AreEqual(selectedIndices.Length, snap.SelectedBars.Length);
            Assert.IsTrue(snap.IsValid);

            // Restore and verify layout-independent values round-trip
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(zoomX, _target.ViewState.ZoomX, 0.001f);
            Assert.AreEqual(zoomY, _target.ViewState.ZoomY, 0.001f);
            Assert.AreEqual(sortMode, _target.ViewState.SortMode);
            Assert.AreEqual(sortDesc, _target.ViewState.SortDescending);
            Assert.AreEqual(focusIdx, _target.ViewState.FocusedBarIndex);
            Assert.AreEqual(selectedIndices.Length, _target.ViewState.SelectedBars.Count);
            foreach (int idx in selectedIndices)
                Assert.IsTrue(_target.ViewState.SelectedBars.Contains(idx), $"Should contain bar {idx}");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Events fire on restore
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void Restore_FiresViewChangedEvent_WithAllFields()
        {
            _source.ViewState.ZoomX = 3.0f;
            _source.ViewState.ZoomY = 1.5f;
            _source.ViewState.PanX  = 2.0f;
            _source.ViewState.PanY  = 0.2f;
            var snap = _source.CreateViewSnapshot();

            bool fired = false;
            float receivedZoomX = 0f, receivedZoomY = 0f;
            float receivedPanX = 0f, receivedPanY = 0f;
            _target.ViewChanged += args =>
            {
                fired = true;
                receivedZoomX = args.ZoomX;
                receivedZoomY = args.ZoomY;
                receivedPanX  = args.PanX;
                receivedPanY  = args.PanY;
            };

            _target.RestoreViewSnapshot(snap);

            Assert.IsTrue(fired, "ViewChanged event should fire on restore");
            Assert.AreEqual(snap.ZoomX, receivedZoomX, 0.001f);
            Assert.AreEqual(snap.ZoomY, receivedZoomY, 0.001f);
            // Pan values in the event are post-clamp (may differ from snap in
            // headless tests), but must match what's actually on the view state
            Assert.AreEqual(_target.ViewState.PanX, receivedPanX, 0.001f);
            Assert.AreEqual(_target.ViewState.PanY, receivedPanY, 0.001f);
        }

        [Test]
        public void Restore_FiresSelectionChangedEvent_WithCorrectIndices()
        {
            var expectedIndices = new[] { 0, 4, 7 };
            foreach (int idx in expectedIndices)
                _source.ViewState.SelectedBars.Add(idx);

            var snap = _source.CreateViewSnapshot();

            bool fired = false;
            var receivedIndices = new List<int>();
            _target.SelectionChanged += args =>
            {
                fired = true;
                foreach (int idx in args.SelectedDataIndices)
                    receivedIndices.Add(idx);
            };

            _target.RestoreViewSnapshot(snap);

            Assert.IsTrue(fired, "SelectionChanged event should fire on restore");
            Assert.AreEqual(expectedIndices.Length, receivedIndices.Count);
            foreach (int idx in expectedIndices)
                Assert.IsTrue(receivedIndices.Contains(idx), $"Event should contain index {idx}");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Unity serialization round-trip
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void Snapshot_SurvivesJsonUtilityRoundTrip()
        {
            float zoomX = 5.0f, zoomY = 2.5f, panX = 4.0f;
            var sortMode = SortMode.ByValue;
            bool sortDesc = false;
            int focusIdx = 6;
            var selectedIndices = new[] { 1, 6 };

            _source.ViewState.ZoomX = zoomX;
            _source.ViewState.ZoomY = zoomY;
            _source.ViewState.PanX  = panX;
            _source.SetSortMode(sortMode, descending: sortDesc);
            _source.ViewState.FocusedBarIndex = focusIdx;
            foreach (int idx in selectedIndices)
                _source.ViewState.SelectedBars.Add(idx);

            var snap = _source.CreateViewSnapshot();

            // Round-trip through Unity's serializer (same path as domain reload)
            string json = JsonUtility.ToJson(snap);
            var deserialized = JsonUtility.FromJson<BarGraphViewSnapshot>(json);

            // Verify the struct survived serialization
            Assert.IsTrue(deserialized.IsValid);
            Assert.AreEqual(zoomX, deserialized.ZoomX, 0.001f);
            Assert.AreEqual(zoomY, deserialized.ZoomY, 0.001f);
            Assert.AreEqual(panX,  deserialized.PanX,  0.001f);
            Assert.AreEqual(sortMode, deserialized.SortMode);
            Assert.AreEqual(sortDesc, deserialized.SortDescending);
            Assert.AreEqual(focusIdx, deserialized.FocusedBarIndex);
            Assert.AreEqual(selectedIndices.Length, deserialized.SelectedBars.Length);

            // Restore and verify layout-independent values
            _target.RestoreViewSnapshot(deserialized);

            Assert.AreEqual(zoomX, _target.ViewState.ZoomX, 0.001f);
            Assert.AreEqual(zoomY, _target.ViewState.ZoomY, 0.001f);
            Assert.AreEqual(sortMode, _target.ViewState.SortMode);
            Assert.AreEqual(sortDesc, _target.ViewState.SortDescending);
            Assert.AreEqual(focusIdx, _target.ViewState.FocusedBarIndex);
            Assert.AreEqual(selectedIndices.Length, _target.ViewState.SelectedBars.Count);
            foreach (int idx in selectedIndices)
                Assert.IsTrue(_target.ViewState.SelectedBars.Contains(idx), $"Should contain bar {idx}");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Stacked data round-trip
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void Snapshot_WorksWithStackedData()
        {
            // Build stacked bars (3 segments per bar, 5 bars)
            var segments = new BarSegment[]
            {
                new BarSegment(10f, Color.red), new BarSegment(20f, Color.green), new BarSegment(5f, Color.blue),
                new BarSegment(15f, Color.red), new BarSegment(25f, Color.green), new BarSegment(8f, Color.blue),
                new BarSegment(12f, Color.red), new BarSegment(18f, Color.green), new BarSegment(3f, Color.blue),
                new BarSegment(30f, Color.red), new BarSegment(10f, Color.green), new BarSegment(7f, Color.blue),
                new BarSegment(22f, Color.red), new BarSegment(14f, Color.green), new BarSegment(9f, Color.blue),
            };
            var bars = new BarEntry[]
            {
                new BarEntry(0,  3, 35f),
                new BarEntry(3,  3, 48f),
                new BarEntry(6,  3, 33f),
                new BarEntry(9,  3, 47f),
                new BarEntry(12, 3, 45f),
            };

            _source.SetData(bars, 5, segments, 15);
            _target.SetData(bars, 5, segments, 15);

            float zoomX = 2.0f;
            var sortMode = SortMode.ByValue;
            bool sortDesc = true;
            int focusIdx = 3;
            var selectedIndices = new[] { 1, 3 };

            _source.ViewState.ZoomX = zoomX;
            _source.SetSortMode(sortMode, descending: sortDesc);
            foreach (int idx in selectedIndices)
                _source.ViewState.SelectedBars.Add(idx);
            _source.ViewState.FocusedBarIndex = focusIdx;

            var snap = _source.CreateViewSnapshot();
            _target.RestoreViewSnapshot(snap);

            Assert.AreEqual(zoomX, _target.ViewState.ZoomX, 0.001f);
            Assert.AreEqual(sortMode, _target.ViewState.SortMode);
            Assert.AreEqual(focusIdx, _target.ViewState.FocusedBarIndex);
            Assert.AreEqual(selectedIndices.Length, _target.ViewState.SelectedBars.Count);
            foreach (int idx in selectedIndices)
                Assert.IsTrue(_target.ViewState.SelectedBars.Contains(idx), $"Should contain bar {idx}");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Restore onto different data size (real domain reload scenario)
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void Snapshot_RestoresOntoSmallerDataset()
        {
            // Source has 10 bars
            var inRangeIndices  = new[] { 2, 5 };
            var outOfRangeIndex = 8;
            float zoomX = 3.0f;

            foreach (int idx in inRangeIndices)
                _source.ViewState.SelectedBars.Add(idx);
            _source.ViewState.SelectedBars.Add(outOfRangeIndex);
            _source.ViewState.FocusedBarIndex = outOfRangeIndex;
            _source.ViewState.ZoomX = zoomX;

            var snap = _source.CreateViewSnapshot();

            // Target only has 6 bars (simulates reload with different data)
            int targetBarCount = 6;
            _target.SetData(new List<float> { 10f, 20f, 30f, 40f, 50f, 60f });
            _target.RestoreViewSnapshot(snap);

            // In-range indices survive, out-of-range index is discarded
            Assert.AreEqual(inRangeIndices.Length, _target.ViewState.SelectedBars.Count);
            foreach (int idx in inRangeIndices)
                Assert.IsTrue(_target.ViewState.SelectedBars.Contains(idx), $"Should contain bar {idx}");
            Assert.IsFalse(_target.ViewState.SelectedBars.Contains(outOfRangeIndex),
                $"Bar {outOfRangeIndex} is beyond target bar count {targetBarCount}");

            // Focused bar is out of range, should reset to -1
            Assert.AreEqual(-1, _target.ViewState.FocusedBarIndex);

            // Zoom should still be restored
            Assert.AreEqual(zoomX, _target.ViewState.ZoomX, 0.001f);
        }
    }
}

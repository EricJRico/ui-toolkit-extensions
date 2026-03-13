# BarGraph V2 — Unity UI Toolkit Bar Graph Toolkit

A zero-dependency, high-performance stacked bar graph for Unity UI Toolkit (2022 LTS+).
All rendering goes through `Painter2D`. No per-bar VisualElements. Handles 1 to 10,000+ bars.

---

## File layout

```
BarGraphV2/
├── Runtime/
│   ├── BarSegment.cs           – One colour+value slice of a stacked bar
│   ├── BarEntry.cs             – One bar column (references flat segment array)
│   ├── ChartDataModel.cs       – Pre-allocated backing arrays; zero-alloc SetData
│   ├── ChartViewState.cs       – All mutable zoom/pan/hover/selection/sort state
│   ├── BarGraphSettings.cs     – All visual + behaviour config (serializable)
│   ├── BarGraphElement.cs      – Core VisualElement (rendering + interaction)
│   ├── BarGraphController.cs   – MonoBehaviour drop-in wrapper
│   │
│   ├── Events/
│   │   └── BarGraphEvents.cs   – All event arg structs + BarClickedUIEvent
│   │
│   └── Manipulators/
│       ├── HoverManipulator.cs      – Cursor-bar hit-test, no per-bar elements
│       ├── SelectionManipulator.cs  – Click-select + pointer-captured drag-select
│       ├── PanManipulator.cs        – Middle-click / Alt+drag fractional pan
│       └── ZoomManipulator.cs       – Scroll zoom anchored to cursor position
│
├── Styles/
│   └── BarGraph.uss
│
└── Demo/
    └── BarGraphDemo.cs         – Full feature demo coroutine
```

---

## Quickstart (3 lines)

```csharp
var graph = new BarGraphElement();
graph.style.flexGrow = 1;
graph.SetData(new float[] { 10, 45, 30, 75, 90 });
rootElement.Add(graph);
```

---

## Data model

### Flat bars (single colour, fastest path)
```csharp
graph.SetData(new float[] { 12, 45, 7, 88 }, Color.cyan);
```

### Stacked bars (multi-segment)
```csharp
// Flat segment array – no jagged arrays, cache-friendly
var segs = new BarSegment[]
{
    new(20f, Color.blue),   // bar 0, segment 0
    new(35f, Color.green),  // bar 0, segment 1
    new(40f, Color.red),    // bar 1, segment 0
    new(15f, Color.yellow), // bar 1, segment 1
};
var bars = new BarEntry[]
{
    new BarEntry(segmentStart: 0, segmentCount: 2, totalValue: 55f, label: "A"),
    new BarEntry(segmentStart: 2, segmentCount: 2, totalValue: 55f, label: "B"),
};
graph.SetData(bars, bars.Length, segs, segs.Length);
```

### Append (live streaming, O(1))
```csharp
graph.AppendBar(value: 73f, color: Color.cyan, label: "Now");
```

### Overlay (comparison dataset)
```csharp
graph.SetOverlay(new float[] { 30, 60, 25, 70 }, Color.yellow);
graph.ClearOverlay();
```

---

## Sort mode

```csharp
graph.SetSortMode(SortMode.ByValue, descending: true);  // tallest bars first
graph.SetSortMode(SortMode.ByValue, descending: false); // shortest first
graph.SetSortMode(SortMode.None);                       // restore data order
```

Sort rebuilds a `DisplayToData` index map — **original data is never mutated**.
Events always carry both `DataIndex` (stable) and `DisplayIndex` (sorted position).

---

## Pluggable label formatters

```csharp
// Y-axis: show dollar amounts
graph.FormatYLabel = v => $"${v:F0}";

// X-axis: show month names from an external list
var months = new[] { "Jan","Feb","Mar","Apr","May","Jun" };
graph.FormatXLabel = i => i < months.Length ? months[i] : i.ToString();

// Reset to built-in SI formatter
graph.FormatYLabel = null;
```

---

## Events

All event args are `readonly struct` — dispatched on the stack, zero heap allocation.

```csharp
graph.BarClicked       += args => Debug.Log($"Clicked bar {args.DataIndex}, value={args.TotalValue}");
graph.SelectionChanged += args => Debug.Log($"{args.SelectedDataIndices.Count} bars selected");
graph.DragCompleted    += args => Debug.Log($"Drag rect {args.DragRect}, {args.SelectedDataIndices.Count} selected");
graph.HoverChanged     += args => Debug.Log($"Hovered bar {args.DataIndex}");
graph.ViewChanged      += args => Debug.Log($"Zoom {args.ZoomX:F2}x, Pan {args.PanX:F2}");
```

For visual-tree event bubbling (ancestor elements can intercept):
```csharp
parentElement.RegisterCallback<BarClickedUIEvent>(e => Debug.Log(e.BarIndex));
```

---

## Interaction

| Input               | Action                              |
|---------------------|-------------------------------------|
| Scroll              | Zoom X (anchored to cursor)         |
| Shift+Scroll        | Zoom Y (if `EnableMouseZoomY`)      |
| Left-click          | Select bar (toggle with Alt)        |
| Left-drag           | Rubber-band multi-select            |
| Alt+Left-drag       | Pan                                 |
| Middle-drag         | Pan                                 |
| ← → Arrow keys      | Move keyboard selection             |
| Shift + ←→          | Extend selection                    |
| Home / End          | Jump to first / last bar            |
| Space / Return      | Activate focused bar (fires BarClicked) |
| Ctrl+A              | Select all                          |
| Escape              | Clear selection                     |
| Ctrl+R              | Reset view                          |

---

## Performance

### Rendering
- **Colour-batched Painter2D**: bars sharing the same `Color32` are accumulated
  into a single `BeginPath…Fill` path. Draw calls = O(unique colours), not O(bars).
- **Struct-of-arrays `RectBatch`**: parallel `float[]` arrays per colour — no boxing,
  sequential memory, grows via doubling, clears in O(1).
- **Label pool**: a fixed set of `Label` elements is repositioned each repaint — zero
  per-frame allocation for text.

### LOD (sub-pixel bars)
When bar slot width < 1 px, the LOD path merges all bars mapping to each screen pixel
(keeping the max value). Cost is bounded by `floor(plotWidth)` pixels, not bar count.
10 000 bars in a 400 px panel = 400 iterations.

### Data updates
`SetData` copies into pre-allocated `BarEntry[]` / `BarSegment[]` arrays (doubling
growth). After warm-up: **zero heap allocations** on every subsequent `SetData` call
of equal or lesser size.

`AppendBar` is O(1) amortised.

### Sort
Rebuilds lazily only when `SortDirty` is set (data change or explicit `SetSortMode`).
Uses `Array.Sort` with a `Comparer<int>` on the index array — original data untouched.

---

## Minimum Unity version
**Unity 2022.1+** (Painter2D, `PointerManipulator`).  
Tested on 2022 LTS, 2023 LTS, and Unity 6.

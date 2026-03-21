# Plan: Render-Time Dim Selection

## Context

When bars are selected (drag-select or click), non-selected bars should dim to draw focus to the selection. Currently selection is indicated by an overlay fill + rim on selected bars — the non-selected bars stay at full brightness, making it hard to visually isolate the selected region in dense charts.

## Approach

Dim at render time in the draw loop. No data mutation, no wrapper class, no caching. The element's data stays clean — tooltips, hit testing, and event args always reflect real values.

## Design

### How it works

`DrawDirectBars` and `DrawLodBars` already pass an `alpha` parameter to `ResolveSegmentColor` for each bar. When selection is non-empty and the current bar is not selected, multiply `alpha` by a configurable dim factor. Selected bars render at full brightness. That's it.

### USS property

One new custom property:

| Property | Default | Notes |
|----------|---------|-------|
| `--bar-graph-dim-opacity` | `1.0` | Alpha multiplier for non-selected bars when selection is active. `1.0` = no dimming (current behavior). `0.3` = strong dim. Only applies when `SelectedBars.Count > 0`. |

Default of `1.0` means existing behavior is unchanged — dimming is opt-in via USS.

### Enabling dimming

Add to your USS:

```css
.bar-graph {
    --bar-graph-dim-opacity: 0.3;
}
```

Or per-theme:

```css
.bar-graph--profiler {
    --bar-graph-dim-opacity: 0.25;
}
```

---

## Implementation

### Step 1: Add USS property

**File:** `BarChart/Core/BarGraphElement.cs`

```csharp
// Declaration
static readonly CustomStyleProperty<float> k_DimOpacity = new("--bar-graph-dim-opacity");

// ResolvedVisuals field
public float DimOpacity;

// Default
DimOpacity = 1f,

// Resolution
if (cs.TryGetValue(k_DimOpacity, out f)) _vis.DimOpacity = f;
```

### Step 2: Apply in draw loops

**File:** `BarChart/Core/BarGraphElement.cs`

In `DrawDirectBars`, before the per-bar segment loop, compute the effective alpha:

```csharp
bool hasSelection = _viewState.SelectedBars.Count > 0;

// Inside the bar loop, before segment iteration:
float alpha = (hasSelection && !_viewState.SelectedBars.Contains(dataIdx))
    ? _vis.DimOpacity
    : 1f;
```

The `alpha` variable already exists and is passed to `ResolveSegmentColor`. Replace its current fixed value with this conditional.

Same change in `DrawLodBars` for the LOD rendering path.

### Step 3: Segment selection dimming

When a segment is selected (`SelectedSegmentBar >= 0`), the same logic applies — all bars except the one containing the selected segment should dim:

```csharp
bool hasSelection = _viewState.SelectedBars.Count > 0
                 || _viewState.SelectedSegmentBar >= 0;

bool isSelected = _viewState.SelectedBars.Contains(dataIdx)
               || _viewState.SelectedSegmentBar == dataIdx;

float alpha = (hasSelection && !isSelected) ? _vis.DimOpacity : 1f;
```

---

## Files Changed

| File | Change |
|------|--------|
| `BarChart/Core/BarGraphElement.cs` | Add `k_DimOpacity` USS property, `DimOpacity` field in `ResolvedVisuals`, resolve in `ResolveCustomStyles`, apply in `DrawDirectBars` and `DrawLodBars` |
| `BarChart/Resources/BarGraph.uss` | Add `--bar-graph-dim-opacity: 1;` default (opt-in, no visual change) |

No new files. No new classes. No data mutation.

---

## What Does NOT Change

- `BarGraphSettings` — no new behavioral properties
- `ChartViewState` — no new state
- `ChartDataModel` — data stays clean
- Hit testing — unaffected, reads data not rendered alpha
- Event args — unaffected, carry real segment values
- Tooltips — unaffected, read real data
- Selection/hover highlight rendering — unaffected, draws on top of dimmed bars

---

## Verification

1. No USS override set → all bars render at full brightness regardless of selection (default `1.0`)
2. Set `--bar-graph-dim-opacity: 0.3` → drag-select a region → non-selected bars dim, selected stay bright
3. Clear selection → all bars restore to full brightness immediately
4. Click a segment in a stacked bar → all other bars dim, selected bar stays bright
5. Change `--bar-graph-dim-opacity` value in USS → visual updates on next repaint
6. LOD path (many bars, stride < 1px) → dimming applies correctly
7. Overlay rendering → dimming applies only to primary bars, overlay unaffected
8. Performance → no measurable regression (one HashSet.Contains per bar, already done in highlight rendering)

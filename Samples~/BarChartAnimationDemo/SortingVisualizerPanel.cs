using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;

namespace BarGraph.AnimationDemo
{
    enum StepType { Compare, Swap, MarkSorted, SetPivot, ClearPivot, Place }

    struct SortStep
    {
        public StepType Type;
        public int      IndexA;
        public int      IndexB; // -1 when unused
        public int      Value;  // used by Place

        public SortStep(StepType type, int a, int b = -1)
        {
            Type   = type;
            IndexA = a;
            IndexB = b;
            Value  = 0;
        }

        public static SortStep PlaceValue(int index, int value)
            => new SortStep { Type = StepType.Place, IndexA = index, IndexB = -1, Value = value };
    }

    /// <summary>
    /// Sorting algorithm visualizer with 5 algorithms. Bars are colored by state:
    /// default (dim green), comparing (red), swapping (yellow), pivot (cyan), sorted (bright green).
    /// </summary>
    sealed class SortingVisualizerPanel : AnimationDemoPanel
    {
        private static readonly Color32 ColorDefault   = new Color32( 42,  90,  42, 255);
        private static readonly Color32 ColorComparing = new Color32(255,  68,  68, 255);
        private static readonly Color32 ColorSwapping  = new Color32(255, 204,   0, 255);
        private static readonly Color32 ColorPivot     = new Color32(  0, 204, 204, 255);
        private static readonly Color32 ColorSorted    = new Color32( 68, 255,  68, 255);

        private static readonly string[] AlgorithmNames =
            { "Bubble", "Selection", "Insertion", "Merge", "Quick" };

        private int   _barCount        = 50;
        private int   _algorithmIndex  = 0;
        private float _stepsPerSecond  = 30f;

        private int[]           _array;
        private Color32[]       _colorState;
        private List<SortStep>  _steps;
        private int             _currentStep;
        private float           _stepAccum;
        private bool            _finished;

        // Pre-allocated work arrays
        private BarEntry[]   _bars;
        private BarSegment[] _segments;

        public SortingVisualizerPanel() : base("Algorithm Theater", new AnimationPanelTheme
        {
            CardBackground  = new Color(0.03f, 0.06f, 0.03f),
            CardBorder      = new Color(0.1f,  0.25f, 0.1f),
            TitleColor      = new Color(0.4f,  0.95f, 0.4f),
            ThemeClassName  = "sorting-visualizer-theme",
            ThemeStyleSheet = Resources.Load<StyleSheet>("SortingVisualizerTheme"),
            BehaviorSettings = new BarGraphSettings
            {
                ShowGrid      = true,
                GridLineCount = 4,
            }
        })
        { }

        protected override void BuildControls()
        {
            AddPlayPauseToggle();

            AddControl(MakeButton("Step", () =>
            {
                if (!_finished) AdvanceOneStep();
                RebuildGraph();
            }));

            AddControl(MakeSlider("Speed", 1f, 200f, _stepsPerSecond, v => _stepsPerSecond = v));
            AddControl(MakeSliderInt("Bars", 20, 100, _barCount, v =>
            {
                _barCount = v;
                Regenerate();
            }));

            var algoBtn = MakeButton($"Algo: {AlgorithmNames[_algorithmIndex]}", null);
            algoBtn.clicked += () =>
            {
                _algorithmIndex = (_algorithmIndex + 1) % AlgorithmNames.Length;
                algoBtn.text = $"Algo: {AlgorithmNames[_algorithmIndex]}";
                Regenerate();
            };
            AddControl(algoBtn);

            AddControl(MakeButton("Shuffle", Regenerate));
            AddResetViewButton();

            Regenerate();
        }

        public override void Regenerate()
        {
            // Shuffled array of values 1..N
            _array      = new int[_barCount];
            _colorState = new Color32[_barCount];
            _bars       = new BarEntry[_barCount];
            _segments   = new BarSegment[_barCount];

            for (int i = 0; i < _barCount; i++)
                _array[i] = i + 1;

            // Fisher-Yates shuffle
            for (int i = _barCount - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_array[i], _array[j]) = (_array[j], _array[i]);
            }

            for (int i = 0; i < _barCount; i++)
                _colorState[i] = ColorDefault;

            // Pre-compute sort steps
            int[] copy = (int[])_array.Clone();
            _steps = _algorithmIndex switch
            {
                0 => RecordBubbleSort(copy),
                1 => RecordSelectionSort(copy),
                2 => RecordInsertionSort(copy),
                3 => RecordMergeSort(copy),
                4 => RecordQuickSort(copy),
                _ => RecordBubbleSort(copy),
            };

            _currentStep = 0;
            _stepAccum   = 0f;
            _finished    = false;
            IsPlaying    = true;

            TitleLabel.text = $"Algorithm Theater \u2014 {AlgorithmNames[_algorithmIndex]}";
            Graph.FormatYLabel = v => $"{v:F0}";

            RebuildGraph();
        }

        public override void OnUpdate(float dt)
        {
            if (!IsPlaying || _finished) return;

            _stepAccum += dt * _stepsPerSecond * Speed;

            bool changed = false;
            while (_stepAccum >= 1f && !_finished)
            {
                _stepAccum -= 1f;
                AdvanceOneStep();
                changed = true;
            }

            if (changed) RebuildGraph();
        }

        private void AdvanceOneStep()
        {
            if (_currentStep >= _steps.Count)
            {
                _finished = true;
                for (int i = 0; i < _barCount; i++)
                    _colorState[i] = ColorSorted;
                return;
            }

            var step = _steps[_currentStep++];

            // Clear transient highlights (keep sorted and pivot)
            for (int i = 0; i < _barCount; i++)
            {
                if (_colorState[i].Equals(ColorComparing) || _colorState[i].Equals(ColorSwapping))
                    _colorState[i] = ColorDefault;
            }

            switch (step.Type)
            {
                case StepType.Compare:
                    _colorState[step.IndexA] = ColorComparing;
                    if (step.IndexB >= 0) _colorState[step.IndexB] = ColorComparing;
                    break;

                case StepType.Swap:
                    (_array[step.IndexA], _array[step.IndexB]) =
                        (_array[step.IndexB], _array[step.IndexA]);
                    _colorState[step.IndexA] = ColorSwapping;
                    _colorState[step.IndexB] = ColorSwapping;
                    break;

                case StepType.MarkSorted:
                    _colorState[step.IndexA] = ColorSorted;
                    if (step.IndexB >= 0) _colorState[step.IndexB] = ColorSorted;
                    break;

                case StepType.SetPivot:
                    _colorState[step.IndexA] = ColorPivot;
                    break;

                case StepType.ClearPivot:
                    if (!_colorState[step.IndexA].Equals(ColorSorted))
                        _colorState[step.IndexA] = ColorDefault;
                    break;

                case StepType.Place:
                    _array[step.IndexA] = step.Value;
                    _colorState[step.IndexA] = ColorSwapping;
                    break;
            }
        }

        private void RebuildGraph()
        {
            for (int i = 0; i < _barCount; i++)
            {
                float value = _array[i];
                _segments[i] = new BarSegment(value, _colorState[i]);
                _bars[i]     = new BarEntry(i, 1, value);
            }

            Graph.SetData(_bars, _barCount, _segments, _barCount);
        }

        // ── Algorithm Recorders ─────────────────────────────────────────────
        // Each method sorts a copy and records every comparison/swap as steps.

        private static List<SortStep> RecordBubbleSort(int[] arr)
        {
            var steps = new List<SortStep>();
            int n = arr.Length;

            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < n - 1 - i; j++)
                {
                    steps.Add(new SortStep(StepType.Compare, j, j + 1));
                    if (arr[j] > arr[j + 1])
                    {
                        steps.Add(new SortStep(StepType.Swap, j, j + 1));
                        (arr[j], arr[j + 1]) = (arr[j + 1], arr[j]);
                    }
                }
                steps.Add(new SortStep(StepType.MarkSorted, n - 1 - i));
            }
            steps.Add(new SortStep(StepType.MarkSorted, 0));
            return steps;
        }

        private static List<SortStep> RecordSelectionSort(int[] arr)
        {
            var steps = new List<SortStep>();
            int n = arr.Length;

            for (int i = 0; i < n - 1; i++)
            {
                int minIdx = i;
                for (int j = i + 1; j < n; j++)
                {
                    steps.Add(new SortStep(StepType.Compare, minIdx, j));
                    if (arr[j] < arr[minIdx])
                        minIdx = j;
                }
                if (minIdx != i)
                {
                    steps.Add(new SortStep(StepType.Swap, i, minIdx));
                    (arr[i], arr[minIdx]) = (arr[minIdx], arr[i]);
                }
                steps.Add(new SortStep(StepType.MarkSorted, i));
            }
            steps.Add(new SortStep(StepType.MarkSorted, n - 1));
            return steps;
        }

        private static List<SortStep> RecordInsertionSort(int[] arr)
        {
            var steps = new List<SortStep>();
            int n = arr.Length;

            steps.Add(new SortStep(StepType.MarkSorted, 0));

            for (int i = 1; i < n; i++)
            {
                int j = i;
                while (j > 0)
                {
                    steps.Add(new SortStep(StepType.Compare, j - 1, j));
                    if (arr[j - 1] > arr[j])
                    {
                        steps.Add(new SortStep(StepType.Swap, j - 1, j));
                        (arr[j - 1], arr[j]) = (arr[j], arr[j - 1]);
                        j--;
                    }
                    else
                    {
                        break;
                    }
                }
                steps.Add(new SortStep(StepType.MarkSorted, i));
            }
            return steps;
        }

        private static List<SortStep> RecordMergeSort(int[] arr)
        {
            var steps = new List<SortStep>();
            MergeSortRecursive(arr, 0, arr.Length - 1, steps);

            // Mark all as sorted at the end
            for (int i = 0; i < arr.Length; i++)
                steps.Add(new SortStep(StepType.MarkSorted, i));

            return steps;
        }

        private static void MergeSortRecursive(int[] arr, int left, int right, List<SortStep> steps)
        {
            if (left >= right) return;

            int mid = (left + right) / 2;
            MergeSortRecursive(arr, left, mid, steps);
            MergeSortRecursive(arr, mid + 1, right, steps);
            Merge(arr, left, mid, right, steps);
        }

        private static void Merge(int[] arr, int left, int mid, int right, List<SortStep> steps)
        {
            int[] temp = new int[right - left + 1];
            int i = left, j = mid + 1, k = 0;

            while (i <= mid && j <= right)
            {
                steps.Add(new SortStep(StepType.Compare, i, j));
                if (arr[i] <= arr[j])
                    temp[k++] = arr[i++];
                else
                    temp[k++] = arr[j++];
            }

            while (i <= mid) temp[k++] = arr[i++];
            while (j <= right) temp[k++] = arr[j++];

            // Write back with placement visuals
            for (int m = 0; m < temp.Length; m++)
            {
                int idx = left + m;
                if (arr[idx] != temp[m])
                    steps.Add(SortStep.PlaceValue(idx, temp[m]));
                arr[idx] = temp[m];
            }
        }

        private static List<SortStep> RecordQuickSort(int[] arr)
        {
            var steps = new List<SortStep>();
            QuickSortRecursive(arr, 0, arr.Length - 1, steps);

            // Mark all as sorted at the end
            for (int i = 0; i < arr.Length; i++)
                steps.Add(new SortStep(StepType.MarkSorted, i));

            return steps;
        }

        private static void QuickSortRecursive(int[] arr, int low, int high, List<SortStep> steps)
        {
            if (low >= high)
            {
                if (low == high)
                    steps.Add(new SortStep(StepType.MarkSorted, low));
                return;
            }

            int pivotIdx = Partition(arr, low, high, steps);
            steps.Add(new SortStep(StepType.MarkSorted, pivotIdx));

            QuickSortRecursive(arr, low, pivotIdx - 1, steps);
            QuickSortRecursive(arr, pivotIdx + 1, high, steps);
        }

        private static int Partition(int[] arr, int low, int high, List<SortStep> steps)
        {
            // Median-of-three pivot selection
            int mid = (low + high) / 2;
            if (arr[low] > arr[mid])
            {
                steps.Add(new SortStep(StepType.Swap, low, mid));
                (arr[low], arr[mid]) = (arr[mid], arr[low]);
            }
            if (arr[low] > arr[high])
            {
                steps.Add(new SortStep(StepType.Swap, low, high));
                (arr[low], arr[high]) = (arr[high], arr[low]);
            }
            if (arr[mid] > arr[high])
            {
                steps.Add(new SortStep(StepType.Swap, mid, high));
                (arr[mid], arr[high]) = (arr[high], arr[mid]);
            }

            // Move pivot (median) to high-1
            steps.Add(new SortStep(StepType.Swap, mid, high));
            (arr[mid], arr[high]) = (arr[high], arr[mid]);

            int pivotValue = arr[high];
            steps.Add(new SortStep(StepType.SetPivot, high));

            int i = low;
            for (int j = low; j < high; j++)
            {
                steps.Add(new SortStep(StepType.Compare, j, high));
                if (arr[j] < pivotValue)
                {
                    if (i != j)
                    {
                        steps.Add(new SortStep(StepType.Swap, i, j));
                        (arr[i], arr[j]) = (arr[j], arr[i]);
                    }
                    i++;
                }
            }

            // Place pivot in final position
            if (i != high)
            {
                steps.Add(new SortStep(StepType.Swap, i, high));
                (arr[i], arr[high]) = (arr[high], arr[i]);
            }

            return i;
        }
    }
}

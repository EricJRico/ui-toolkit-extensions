using System;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;
using BarGraph.Input;

namespace BarGraph.ControlsDemo
{
    /// <summary>
    /// Base class for controls cheat-sheet panels.
    /// Each panel has a small graph, instruction text, and a live status label.
    /// </summary>
    abstract class ControlsDemoPanel
    {
        public VisualElement   Card  { get; }
        public BarGraphElement Graph { get; }
        public string          Title { get; }

        protected readonly Label StatusLabel;

        private static readonly Color CardBg        = new Color(0.06f, 0.07f, 0.10f);
        private static readonly Color CardBorder    = new Color(0.18f, 0.22f, 0.32f);
        private static readonly Color FocusBorder   = new Color(0.35f, 0.55f, 0.95f);
        private static readonly Color TitleColor    = new Color(0.75f, 0.82f, 0.95f);
        private static readonly Color InstrColor    = new Color(0.50f, 0.55f, 0.65f);
        private static readonly Color StatusColor   = new Color(0.40f, 0.85f, 0.55f);

        protected ControlsDemoPanel(string title, string instruction, BarGraphSettings settings)
        {
            Title = title;

            // Card
            Card = new VisualElement();
            Card.style.backgroundColor     = CardBg;
            Card.style.borderTopLeftRadius  = Card.style.borderTopRightRadius  =
            Card.style.borderBottomLeftRadius = Card.style.borderBottomRightRadius = 4;
            Card.style.borderTopWidth = Card.style.borderRightWidth =
            Card.style.borderBottomWidth = Card.style.borderLeftWidth = 1;
            Card.style.borderTopColor = Card.style.borderRightColor =
            Card.style.borderBottomColor = Card.style.borderLeftColor = CardBorder;
            Card.style.paddingTop    = 6;
            Card.style.paddingBottom = 6;
            Card.style.paddingLeft   = 6;
            Card.style.paddingRight  = 6;
            Card.style.flexDirection = FlexDirection.Column;

            Card.style.flexBasis = new StyleLength(Length.Percent(48));
            Card.style.minWidth  = 380;
            Card.style.flexGrow  = 1;
            Card.style.marginTop = Card.style.marginBottom =
            Card.style.marginLeft = Card.style.marginRight = 6;

            // Title
            Card.Add(new Label(title)
            {
                style =
                {
                    fontSize               = 11,
                    color                  = TitleColor,
                    marginBottom           = 4,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            });

            // Graph
            Graph = new BarGraphElement();
            Graph.style.flexGrow  = 1;
            Graph.style.minHeight = 120;
            Graph.style.borderTopLeftRadius  = Graph.style.borderTopRightRadius  =
            Graph.style.borderBottomLeftRadius = Graph.style.borderBottomRightRadius = 3;
            Graph.focusable = true;

            Graph.UpdateSettings(settings);
            Graph.SetInputSource(new BarGraphUIToolkitInput());
            Graph.AddManipulator(new NavigationSuppressor());

            // Visual focus indicator — highlight the card border when this graph
            // has keyboard focus so the user knows where input is going.
            Graph.RegisterCallback<FocusInEvent>(_ =>
            {
                Card.style.borderTopColor = Card.style.borderRightColor =
                Card.style.borderBottomColor = Card.style.borderLeftColor = FocusBorder;
            });
            Graph.RegisterCallback<FocusOutEvent>(_ =>
            {
                Card.style.borderTopColor = Card.style.borderRightColor =
                Card.style.borderBottomColor = Card.style.borderLeftColor = CardBorder;
            });

            Card.Add(Graph);

            // Instruction
            Card.Add(new Label(instruction)
            {
                style =
                {
                    fontSize               = 10,
                    color                  = InstrColor,
                    marginTop              = 4,
                    unityFontStyleAndWeight = FontStyle.Italic,
                    whiteSpace             = WhiteSpace.Normal
                }
            });

            // Status
            StatusLabel = new Label("Ready")
            {
                style =
                {
                    fontSize  = 10,
                    color     = StatusColor,
                    marginTop = 2
                }
            };
            Card.Add(StatusLabel);

            SetupHandlers();
            Regenerate();
            WireStatusEvents();
        }

        protected abstract void SetupHandlers();
        protected abstract void WireStatusEvents();

        protected void SetStatus(string text) => StatusLabel.text = text;

        /// <summary>Generates 20 colorful bars with varied heights.</summary>
        public void Regenerate()
        {
            const int count = 20;
            var bars     = new BarEntry[count];
            var segments = new BarSegment[count];

            for (int i = 0; i < count; i++)
            {
                float value = 30f + 50f * Mathf.Sin(i * 0.45f) + UnityEngine.Random.Range(-8f, 8f);
                value = Mathf.Max(5f, value);
                float hue = (float)i / count;
                Color color = Color.HSVToRGB(hue, 0.75f, 0.85f);
                string label = ((char)('A' + i)).ToString();

                segments[i] = new BarSegment(value, (Color32)color);
                bars[i]     = new BarEntry(i, 1, value, label);
            }

            Graph.SetData(bars, count, segments, count);
        }
    }
}

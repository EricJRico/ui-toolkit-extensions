using System;
using UnityEngine;
using UnityEngine.UIElements;
using BarGraph.Core;
using BarGraph.Input;
using BarGraph.Input.Handlers;

namespace BarGraph.StyleDemo
{
    /// <summary>
    /// Per-panel visual identity applied to card chrome and graph.
    /// Visual/appearance properties live in USS theme files;
    /// only behavioural properties remain in <see cref="BehaviorSettings"/>.
    /// </summary>
    struct PanelTheme
    {
        public Color      CardBackground;
        public Color      CardBorder;
        public Color      TitleColor;
        public string     ThemeClassName;      // e.g. "bar-graph--damage-meter"
        public StyleSheet ThemeStyleSheet;     // loaded from Resources
        public BarGraphSettings BehaviorSettings;  // behavioral only
    }

    /// <summary>
    /// Abstract base for every style-demo panel.
    /// Applies a <see cref="PanelTheme"/> to the card chrome and graph,
    /// then delegates data and controls to the subclass.
    /// </summary>
    abstract class StyleDemoPanel
    {
        // ── Public accessors ────────────────────────────────────────────────
        public VisualElement   Card  { get; }
        public BarGraphElement Graph { get; }
        public string          Title { get; }

        // ── Common state ────────────────────────────────────────────────────
        protected bool IsSorted;
        protected bool SortDescending = true;

        // ── Internal references ─────────────────────────────────────────────
        protected readonly VisualElement ControlsRow;
        protected readonly VisualElement SlidersRow;
        protected readonly VisualElement ButtonsRow;

        // ── Construction ────────────────────────────────────────────────────

        protected StyleDemoPanel(string title, PanelTheme theme)
        {
            Title = title;

            // Card container
            Card = new VisualElement();
            Card.style.backgroundColor     = theme.CardBackground;
            Card.style.borderTopLeftRadius  = Card.style.borderTopRightRadius  =
            Card.style.borderBottomLeftRadius = Card.style.borderBottomRightRadius = 4;
            Card.style.borderTopWidth = Card.style.borderRightWidth =
            Card.style.borderBottomWidth = Card.style.borderLeftWidth = 1;
            Card.style.borderTopColor = Card.style.borderRightColor =
            Card.style.borderBottomColor = Card.style.borderLeftColor = theme.CardBorder;
            Card.style.paddingTop    = 6;
            Card.style.paddingBottom = 6;
            Card.style.paddingLeft   = 6;
            Card.style.paddingRight  = 6;
            Card.style.flexDirection = FlexDirection.Column;

            // Flex sizing for 2-column grid
            Card.style.flexBasis = new StyleLength(Length.Percent(48));
            Card.style.minWidth  = 400;
            Card.style.flexGrow  = 1;
            Card.style.marginTop = Card.style.marginBottom =
            Card.style.marginLeft = Card.style.marginRight = 6;

            // Title
            Card.Add(new Label(title)
            {
                style =
                {
                    fontSize               = 11,
                    color                  = theme.TitleColor,
                    marginBottom           = 4,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            });

            // Graph
            Graph = new BarGraphElement();
            Graph.style.flexGrow  = 1;
            Graph.style.minHeight = 160;
            Graph.style.borderTopLeftRadius  = Graph.style.borderTopRightRadius  =
            Graph.style.borderBottomLeftRadius = Graph.style.borderBottomRightRadius = 3;

            if (theme.ThemeStyleSheet != null)
                Graph.styleSheets.Add(theme.ThemeStyleSheet);
            if (!string.IsNullOrEmpty(theme.ThemeClassName))
                Graph.AddToClassList(theme.ThemeClassName);
            if (theme.BehaviorSettings != null)
                Graph.UpdateSettings(theme.BehaviorSettings);

            Graph.SetInputSource(new BarGraphUIToolkitInput());
            Graph.AddHandler(new BarGraphHoverHandler());
            Graph.AddHandler(new BarGraphSelectionHandler());
            Graph.AddHandler(new BarGraphPanHandler());
            Graph.AddHandler(new BarGraphZoomHandler());

            Card.Add(Graph);

            // Controls area
            ControlsRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    marginTop     = 4
                }
            };

            SlidersRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap      = Wrap.Wrap
                }
            };
            ControlsRow.Add(SlidersRow);

            ButtonsRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap      = Wrap.Wrap,
                    marginTop     = 2
                }
            };
            ControlsRow.Add(ButtonsRow);

            Card.Add(ControlsRow);

            // Subclass-specific controls
            BuildControls();
        }

        // ── Subclass hooks ──────────────────────────────────────────────────

        protected abstract void BuildControls();
        public abstract void Regenerate();
        public virtual void OnUpdate(float dt) { }

        // ── Common control helpers ──────────────────────────────────────────

        protected void AddControl(VisualElement control)
        {
            if (control is BaseSlider<int> || control is BaseSlider<float>
                || control.ClassListContains("demo-slider"))
                SlidersRow.Add(control);
            else
                ButtonsRow.Add(control);
        }

        protected Button MakeButton(string label, Action action)
        {
            var b = new Button(action) { text = label };
            b.style.marginRight  = b.style.marginBottom = 3;
            b.style.paddingLeft  = b.style.paddingRight  = 8;
            b.style.paddingTop   = b.style.paddingBottom = 2;
            b.style.height       = 20;
            b.style.fontSize     = 10;
            return b;
        }

        protected Button MakeToggleButton(string labelOn, string labelOff,
            bool initial, Action<bool> onChanged)
        {
            bool state = initial;
            var btn = new Button();
            btn.text = state ? labelOn : labelOff;
            btn.style.marginRight  = btn.style.marginBottom = 3;
            btn.style.paddingLeft  = btn.style.paddingRight  = 8;
            btn.style.paddingTop   = btn.style.paddingBottom = 2;
            btn.style.height       = 20;
            btn.style.fontSize     = 10;
            btn.clicked += () =>
            {
                state = !state;
                btn.text = state ? labelOn : labelOff;
                onChanged(state);
            };
            return btn;
        }

        private static readonly Color LabelColor = new Color(0.72f, 0.72f, 0.76f, 1f);

        private VisualElement MakeSliderGroup(string label, VisualElement slider)
        {
            var group = new VisualElement();
            group.AddToClassList("demo-slider");
            group.style.flexDirection = FlexDirection.Row;
            group.style.alignItems   = Align.Center;
            group.style.minWidth     = 180;
            group.style.flexGrow     = 1;
            group.style.maxWidth     = 280;
            group.style.marginRight  = 6;

            var lbl = new Label(label);
            lbl.style.fontSize = 10;
            lbl.style.color    = LabelColor;
            lbl.style.minWidth = 40;
            group.Add(lbl);

            slider.style.flexGrow = 1;
            group.Add(slider);

            var valueLbl = new Label();
            valueLbl.style.fontSize       = 10;
            valueLbl.style.color          = LabelColor;
            valueLbl.style.minWidth       = 32;
            valueLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            group.Add(valueLbl);

            if (slider is SliderInt si)
            {
                valueLbl.text = si.value.ToString();
                si.RegisterValueChangedCallback(e => valueLbl.text = e.newValue.ToString());
            }
            else if (slider is Slider sf)
            {
                valueLbl.text = sf.value.ToString("F1");
                sf.RegisterValueChangedCallback(e => valueLbl.text = e.newValue.ToString("F1"));
            }

            return group;
        }

        protected VisualElement MakeSliderInt(string label, int min, int max, int value,
            Action<int> onChanged)
        {
            var s = new SliderInt(min, max) { value = value };
            s.RegisterValueChangedCallback(e => onChanged(e.newValue));
            return MakeSliderGroup(label, s);
        }

        protected VisualElement MakeSlider(string label, float min, float max, float value,
            Action<float> onChanged)
        {
            var s = new Slider(min, max) { value = value };
            s.RegisterValueChangedCallback(e => onChanged(e.newValue));
            return MakeSliderGroup(label, s);
        }

        protected void AddSortToggle()
        {
            AddControl(MakeToggleButton("Sort Off", "Sort \u2193", false, on =>
            {
                IsSorted = on;
                Graph.SetSortMode(on ? SortMode.ByValue : SortMode.None, SortDescending);
            }));

            AddControl(MakeToggleButton("\u2191", "\u2193", true, desc =>
            {
                SortDescending = desc;
                if (IsSorted) Graph.SetSortMode(SortMode.ByValue, SortDescending);
            }));
        }

        protected void AddResetViewButton()
        {
            AddControl(MakeButton("Reset View", () => Graph.ResetView()));
        }
    }
}

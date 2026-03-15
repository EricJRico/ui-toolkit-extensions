using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.ControlsDemo
{
    /// <summary>
    /// Interactive controls cheat sheet — 6 small panels, each teaching
    /// one interaction pattern with a live status label.
    ///
    /// Scene setup
    /// ────────────
    /// 1. Create a scene with a GameObject containing a UIDocument (default Panel Settings).
    /// 2. Add this script to the same GameObject.
    /// 3. Press Play.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("UI Toolkit/Bar Graph Controls Gallery Demo")]
    public sealed class BarChartControlsGalleryDemo : MonoBehaviour
    {
        private UIDocument _doc;
        private readonly List<ControlsDemoPanel> _panels = new List<ControlsDemoPanel>();

        private void Awake() => _doc = GetComponent<UIDocument>();

        private void Start() => BuildUI();

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;
            root.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
            root.style.flexDirection   = FlexDirection.Column;
            root.style.paddingLeft = root.style.paddingRight =
                root.style.paddingTop = root.style.paddingBottom = 10;

            // Title bar
            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    marginBottom  = 6,
                    alignItems    = Align.Center
                }
            };
            root.Add(titleRow);

            titleRow.Add(new Label("BarGraph Toolkit \u2014 Controls Cheat Sheet")
            {
                style =
                {
                    fontSize               = 16,
                    color                  = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow               = 1
                }
            });

            var regenerateAll = new Button(() =>
            {
                foreach (var p in _panels) p.Regenerate();
            }) { text = "Regenerate All" };
            regenerateAll.style.paddingLeft = regenerateAll.style.paddingRight = 10;
            regenerateAll.style.height = 22;
            titleRow.Add(regenerateAll);

            // Scrollable grid
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            var grid = new VisualElement
            {
                style =
                {
                    flexDirection  = FlexDirection.Row,
                    flexWrap       = Wrap.Wrap,
                    justifyContent = Justify.FlexStart,
                    alignItems     = Align.Stretch
                }
            };
            scroll.Add(grid);

            // Create panels
            _panels.Add(new WasdNavigationPanel());
            _panels.Add(new ScrollZoomPanel());
            _panels.Add(new MousePanPanel());
            _panels.Add(new SelectionPanel());
            _panels.Add(new KeyboardFocusPanel());
            _panels.Add(new ShortcutsPanel());

            foreach (var panel in _panels)
                grid.Add(panel.Card);
        }
    }
}

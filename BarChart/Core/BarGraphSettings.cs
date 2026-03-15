using System;
using UnityEngine;

namespace BarGraph.Core
{
    /// <summary>All visual and behaviour settings for a <see cref="BarGraphElement"/>.</summary>
    [Serializable]
    public class BarGraphSettings
    {
        // ── Colours ──────────────────────────────────────────────────────────
        [Header("Colours")]
        public Color BackgroundColor     = new Color(0.07f, 0.07f, 0.10f, 1.00f);
        public Color DefaultBarColor     = new Color(0.25f, 0.60f, 1.00f, 1.00f);
        public Color AxisColor           = new Color(0.55f, 0.55f, 0.60f, 1.00f);
        public Color GridLineColor       = new Color(0.18f, 0.18f, 0.24f, 1.00f);
        public Color LabelColor          = new Color(0.75f, 0.75f, 0.78f, 1.00f);
        public Color HoverTintColor      = new Color(1.00f, 1.00f, 1.00f, 0.18f);
        public Color SelectionFillColor  = new Color(1.00f, 1.00f, 1.00f, 0.22f);
        public Color SelectionRimColor   = new Color(0.40f, 0.70f, 1.00f, 0.90f);
        public Color DragRectFillColor   = new Color(0.35f, 0.65f, 1.00f, 0.08f);
        public Color DragRectBorderColor = new Color(0.35f, 0.65f, 1.00f, 0.60f);
        public Color FocusRimColor       = new Color(1.00f, 0.80f, 0.20f, 1.00f);
        public Color OverlayTint         = new Color(1.00f, 1.00f, 1.00f, 0.38f);

        // ── Padding ───────────────────────────────────────────────────────────
        [Header("Padding")]
        public float PaddingLeft   = 52f;
        public float PaddingRight  = 12f;
        public float PaddingTop    = 12f;
        public float PaddingBottom = 32f;

        // ── Bars ──────────────────────────────────────────────────────────────
        [Header("Bars")]
        [Range(0f, 0.9f)]
        public float BarSpacingRatio = 0.12f;
        public float MinBarWidthPx   = 1f;

        // ── Grid ──────────────────────────────────────────────────────────────
        [Header("Grid & Axes")]
        public bool ShowGrid      = true;
        public bool ShowAxes      = true;
        public int  GridLineCount = 5;

        // ── Value range ───────────────────────────────────────────────────────
        [Header("Value Range")]
        public float MinValue = 0f;
        /// <summary>0 = auto-scale to data maximum.</summary>
        public float MaxValue = 0f;

        // ── Interaction ───────────────────────────────────────────────────────
        [Header("Interaction")]
        public bool  EnableMouseZoomX = true;
        public bool  EnableMouseZoomY = false;
        public bool  EnableMousePan   = true;
        public bool  EnableYPan       = false;
        public bool  EnableSelection  = true;
        [Range(0.02f, 0.5f)]
        public float ZoomSpeed = 0.12f;

        // ── Labels ────────────────────────────────────────────────────────────
        [Header("Labels")]
        public int   MaxXLabels    = 12;
        public int   MaxYLabels    = 6;
        public int   LabelFontSize = 10;
        public float LabelHeight   = 18f;
        public float XLabelWidth   = 40f;
        public float XLabelOffsetY = 3f;
        public float YLabelGap     = 4f;

        // ── Overlay ───────────────────────────────────────────────────────────
        [Header("Overlay")]
        public float OverlayOpacity = 0.35f;
    }
}

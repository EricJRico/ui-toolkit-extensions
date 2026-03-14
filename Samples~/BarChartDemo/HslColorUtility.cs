using UnityEngine;

namespace BarGraph.Demo
{
    /// <summary>
    /// HSL hue-rotation colour generation for stacked bars with many segments.
    /// </summary>
    static class HslColorUtility
    {
        /// <summary>
        /// Returns a colour from a smooth rainbow hue rotation.
        /// Uses Unity's HSVToRGB which takes H, S, V in [0, 1].
        /// </summary>
        public static Color HueRotation(int index, int total,
            float saturation = 0.75f, float value = 0.85f)
        {
            float hue = (float)index / Mathf.Max(1, total);
            return Color.HSVToRGB(hue, saturation, value);
        }

        /// <summary>
        /// Batch-fills a pre-allocated Color32 array with evenly spaced hues.
        /// Avoids per-segment heap allocation.
        /// </summary>
        public static void FillHueRotation(Color32[] buffer, int count,
            float saturation = 0.75f, float value = 0.85f)
        {
            float inv = 1f / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
                buffer[i] = Color.HSVToRGB(i * inv, saturation, value);
        }
    }
}

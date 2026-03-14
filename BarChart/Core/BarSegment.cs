using System;
using UnityEngine;

namespace BarGraph.Core
{
    /// <summary>
    /// One coloured slice inside a stacked bar.
    /// A bar with a single segment behaves identically to a flat bar.
    /// </summary>
    [Serializable]
    public readonly struct BarSegment
    {
        /// <summary>Magnitude of this segment (always positive).</summary>
        public readonly float Value;

        /// <summary>
        /// Fill colour.  default(Color32) defers to <see cref="BarGraphSettings.DefaultBarColor"/>.
        /// </summary>
        public readonly Color32 Color;

        public BarSegment(float value, Color32 color)
        {
            Value = Mathf.Max(0f, value);
            Color = color;
        }

        public BarSegment(float value, Color color)
            : this(value, (Color32)color) { }

        // Explicit default(Color32) — 'default' alone is ambiguous between
        // the Color32 and Color overloads in older C# language versions.
        public BarSegment(float value)
            : this(value, default(Color32)) { }
    }
}

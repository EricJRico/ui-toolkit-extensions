using UnityEngine;
using UnityEngine.UIElements;

namespace BarGraph.Manipulators
{
    /// <summary>
    /// Tracks which bar is under the cursor without allocating per-frame.
    /// Writes <see cref="ChartViewState.HoveredBarIndex"/> via the element's
    /// internal <see cref="BarGraphElement.InternalSetHover"/> method.
    /// </summary>
    internal sealed class HoverManipulator : Manipulator
    {
        private readonly BarGraphElement _chart;

        internal HoverManipulator(BarGraphElement chart) => _chart = chart;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerLeaveEvent>(OnLeave);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerLeaveEvent>(OnLeave);
        }

        private void OnMove(PointerMoveEvent evt)
        {
            // evt.localPosition is Vector3 in Unity 6; HitTestBar expects Vector2.
            int hit = _chart.HitTestBar((Vector2)evt.localPosition);
            _chart.InternalSetHover(hit);
        }

        private void OnLeave(PointerLeaveEvent evt)
        {
            _chart.InternalSetHover(-1);
        }
    }
}

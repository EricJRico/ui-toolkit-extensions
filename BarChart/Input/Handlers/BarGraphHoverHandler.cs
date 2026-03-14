using UnityEngine;
using BarGraph.Core;

namespace BarGraph.Input.Handlers
{
    /// <summary>
    /// Tracks which bar is under the cursor and updates
    /// <see cref="ChartViewState.HoveredBarIndex"/> via
    /// <see cref="BarGraphElement.InternalSetHover"/>.
    /// </summary>
    public sealed class BarGraphHoverHandler : IBarGraphHandler
    {
        private BarGraphElement _element;

        public void Register(BarGraphEventBus actions, BarGraphElement element)
        {
            _element = element;
            actions.Hovered   += OnHovered;
            actions.HoverLeft += OnHoverLeft;
        }

        public void Unregister(BarGraphEventBus actions)
        {
            actions.Hovered   -= OnHovered;
            actions.HoverLeft -= OnHoverLeft;
            _element = null;
        }

        private void OnHovered(Vector2 pos)  => _element?.InternalSetHover(_element.HitTestBar(pos));
        private void OnHoverLeft()            => _element?.InternalSetHover(-1);
    }
}

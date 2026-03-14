using UnityEngine;
using BarGraph.Core;
using BarGraph.Input;

namespace BarGraph.Input.Handlers
{
    /// <summary>
    /// Handles click-to-select and rubber-band drag selection.
    /// Subscribes to select actions from <see cref="BarGraphEventBus"/>.
    /// </summary>
    public sealed class BarGraphSelectionHandler : IBarGraphHandler
    {
        private BarGraphElement _element;

        private Vector2 _dragStart;
        private bool    _dragging;
        private bool    _additive;
        private int     _clickedBar;

        public void Register(BarGraphEventBus actions, BarGraphElement element)
        {
            _element = element;
            actions.SelectPressed   += OnPressed;
            actions.SelectDragged   += OnDragged;
            actions.SelectReleased  += OnReleased;
            actions.SelectCancelled += OnCancelled;
        }

        public void Unregister(BarGraphEventBus actions)
        {
            actions.SelectPressed   -= OnPressed;
            actions.SelectDragged   -= OnDragged;
            actions.SelectReleased  -= OnReleased;
            actions.SelectCancelled -= OnCancelled;
            _element = null;
        }

        private void OnPressed(Vector2 pos, bool additive)
        {
            _dragging   = false;
            _additive   = additive;
            _dragStart  = pos;
            _clickedBar = _element.HitTestBar(pos);
        }

        private void OnDragged(Vector2 pos)
        {
            _dragging = true;
            _element.InternalUpdateDragRect(MakeRect(_dragStart, pos));
        }

        private void OnReleased(Vector2 pos)
        {
            if (_dragging)
                _element.InternalCommitDragSelection(MakeRect(_dragStart, pos), _additive);
            else
                _element.InternalSelectBar(_clickedBar, _additive);

            _dragging = false;
        }

        private void OnCancelled()
        {
            if (_dragging)
                _element.InternalCancelDrag();
            _dragging = false;
        }

        private static Rect MakeRect(Vector2 a, Vector2 b) => new Rect(
            Mathf.Min(a.x, b.x),
            Mathf.Min(a.y, b.y),
            Mathf.Abs(b.x - a.x),
            Mathf.Abs(b.y - a.y));
    }
}

using UnityEngine.UIElements;

namespace BarGraph.ControlsDemo
{
    /// <summary>
    /// Manipulator that suppresses UI Toolkit navigation events so they don't
    /// move focus between graphs in the controls demo grid.
    /// Add alongside <see cref="BarGraph.Input.BarGraphUIToolkitInput"/>.
    /// </summary>
    sealed class NavigationSuppressor : Manipulator
    {
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<NavigationMoveEvent>(OnNav);
            target.RegisterCallback<NavigationSubmitEvent>(OnNav);
            target.RegisterCallback<NavigationCancelEvent>(OnNav);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<NavigationMoveEvent>(OnNav);
            target.UnregisterCallback<NavigationSubmitEvent>(OnNav);
            target.UnregisterCallback<NavigationCancelEvent>(OnNav);
        }

        private void OnNav<T>(T evt) where T : EventBase<T>, new()
        {
            target.focusController?.IgnoreEvent(evt);
            evt.StopPropagation();
        }
    }
}

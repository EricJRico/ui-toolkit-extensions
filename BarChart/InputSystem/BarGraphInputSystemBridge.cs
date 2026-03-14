#if UNITY_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

using BarGraph.Input;

namespace BarGraph.InputSystem
{
    /// <summary>
    /// Optional bridge that wires Unity Input System <see cref="InputAction"/>
    /// assets to a <see cref="BarGraphEventBus"/> instance.
    ///
    /// Usage
    /// ──────
    /// 1. Create an <see cref="InputActionAsset"/> with actions matching the
    ///    fields below, or assign existing actions directly.
    /// 2. Add this component to a GameObject in the scene.
    /// 3. Assign the <see cref="BarGraphElement"/> via the controller, or
    ///    call <see cref="Bind"/> from code.
    ///
    /// This class only exists when the Input System package is installed
    /// (ENABLE_INPUT_SYSTEM scripting define is present).
    /// It has no effect on Editor-only workflows — use
    /// <see cref="BarGraphUIToolkitInput"/> there instead.
    /// </summary>
    public sealed class BarGraphInputSystemBridge : MonoBehaviour
    {
        [Header("Input Actions")]
        [Tooltip("Action that provides a Vector2 screen position for hover.")]
        public InputActionReference Hover;

        [Tooltip("Action that fires on select press. Expects a Vector2 position.")]
        public InputActionReference SelectPress;

        [Tooltip("Action that fires on select release. Expects a Vector2 position.")]
        public InputActionReference SelectRelease;

        [Tooltip("Action that fires while select is held and dragging. Expects a Vector2 position.")]
        public InputActionReference SelectDrag;

        [Tooltip("Action that fires on pan press. Expects a Vector2 position.")]
        public InputActionReference PanPress;

        [Tooltip("Action that fires while panning. Expects a Vector2 screen position (not delta).")]
        public InputActionReference PanDrag;

        [Tooltip("Action that fires on pan release.")]
        public InputActionReference PanRelease;

        [Tooltip("Action that provides a float scroll delta for X zoom.")]
        public InputActionReference ZoomX;

        [Tooltip("Action that provides a float scroll delta for Y zoom.")]
        public InputActionReference ZoomY;

        [Tooltip("Action that provides a Vector2 cursor position used as zoom anchor.")]
        public InputActionReference ZoomAnchor;

        // ── Bound actions instance ────────────────────────────────────────────

        private BarGraphEventBus _actions;

        /// <summary>
        /// Bind this bridge to a <see cref="BarGraphEventBus"/> instance.
        /// Call this after retrieving <c>actions</c> from your
        /// <see cref="BarGraphElement"/>.
        /// </summary>
        public void Bind(BarGraphEventBus actions)
        {
            Unbind();
            _actions = actions;
            Subscribe();
        }

        public void Unbind()
        {
            if (_actions == null) return;
            Unsubscribe();
            _actions = null;
        }

        private void OnDestroy() => Unbind();

        // ── Subscription ──────────────────────────────────────────────────────

        private void Subscribe()
        {
            if (Hover        != null) Hover.action.performed        += OnHover;
            if (SelectPress  != null) SelectPress.action.performed  += OnSelectPress;
            if (SelectRelease!= null) SelectRelease.action.performed+= OnSelectRelease;
            if (SelectDrag   != null) SelectDrag.action.performed   += OnSelectDrag;
            if (PanPress     != null) PanPress.action.performed     += OnPanPress;
            if (PanDrag      != null) PanDrag.action.performed      += OnPanDrag;
            if (PanRelease   != null) PanRelease.action.performed   += OnPanRelease;
            if (ZoomX        != null) ZoomX.action.performed        += OnZoomX;
            if (ZoomY        != null) ZoomY.action.performed        += OnZoomY;
        }

        private void Unsubscribe()
        {
            if (Hover        != null) Hover.action.performed        -= OnHover;
            if (SelectPress  != null) SelectPress.action.performed  -= OnSelectPress;
            if (SelectRelease!= null) SelectRelease.action.performed-= OnSelectRelease;
            if (SelectDrag   != null) SelectDrag.action.performed   -= OnSelectDrag;
            if (PanPress     != null) PanPress.action.performed     -= OnPanPress;
            if (PanDrag      != null) PanDrag.action.performed      -= OnPanDrag;
            if (PanRelease   != null) PanRelease.action.performed   -= OnPanRelease;
            if (ZoomX        != null) ZoomX.action.performed        -= OnZoomX;
            if (ZoomY        != null) ZoomY.action.performed        -= OnZoomY;
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private void OnHover(InputAction.CallbackContext ctx)
            => _actions.FireHovered(ctx.ReadValue<Vector2>());

        private void OnSelectPress(InputAction.CallbackContext ctx)
            => _actions.FireSelectPressed(ctx.ReadValue<Vector2>(), false);

        private void OnSelectRelease(InputAction.CallbackContext ctx)
            => _actions.FireSelectReleased(ctx.ReadValue<Vector2>());

        private void OnSelectDrag(InputAction.CallbackContext ctx)
            => _actions.FireSelectDragged(ctx.ReadValue<Vector2>());

        private void OnPanPress(InputAction.CallbackContext ctx)
            => _actions.FirePanPressed(ctx.ReadValue<Vector2>());

        private void OnPanDrag(InputAction.CallbackContext ctx)
            => _actions.FirePanDragged(ctx.ReadValue<Vector2>());

        private void OnPanRelease(InputAction.CallbackContext ctx)
            => _actions.FirePanReleased();

        private void OnZoomX(InputAction.CallbackContext ctx)
        {
            float delta  = ctx.ReadValue<float>();
            Vector2 anchor = ZoomAnchor != null
                ? ZoomAnchor.action.ReadValue<Vector2>()
                : Vector2.zero;
            _actions.FireZoomXRequested(delta, anchor);
        }

        private void OnZoomY(InputAction.CallbackContext ctx)
        {
            float delta  = ctx.ReadValue<float>();
            Vector2 anchor = ZoomAnchor != null
                ? ZoomAnchor.action.ReadValue<Vector2>()
                : Vector2.zero;
            _actions.FireZoomYRequested(delta, anchor);
        }
    }
}
#endif

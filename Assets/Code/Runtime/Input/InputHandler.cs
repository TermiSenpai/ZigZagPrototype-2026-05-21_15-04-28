using System;
using UnityEngine;
using UnityEngine.EventSystems;
using ZigZag.Runtime.Events;
using UnityInput = UnityEngine.Input;

namespace ZigZag.Runtime.Input
{
    /// <summary>
    /// Single-action input abstraction. Fires <see cref="OnTapped"/> on any of:
    /// left mouse click, first touch on a mobile device (Unity maps touch 0 to mouse
    /// button 0 automatically), or <see cref="KeyCode.Space"/> as an editor shortcut.
    /// </summary>
    /// <remarks>
    /// ADR-006 in <c>zigzag_architecture.md</c> selects the classic
    /// <c>UnityEngine.Input</c> over the new Input System for this prototype. Wrapping
    /// the call inside this handler keeps a future migration to a one-file change.
    ///
    /// Two complementary suppression mechanisms keep UI clicks from doubling as
    /// game taps: <see cref="_onShopOpened"/>/<see cref="_onShopClosed"/> blocks
    /// ALL input (mouse + keyboard) while the shop overlay is up, and a
    /// per-click <see cref="EventSystem.IsPointerOverGameObject()"/> check skips
    /// taps that land on a UI raycast target (Menu's SHOP button, Retry button,
    /// any future UI widget shown over gameplay).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class InputHandler : MonoBehaviour
    {
        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: suspends OnTapped until the shop closes.")]
        private GameEventSO _onShopOpened;

        [SerializeField, Tooltip("Listened-to: re-enables OnTapped.")]
        private GameEventSO _onShopClosed;

        public event Action OnTapped;

        private bool _isBlocked;

        private void OnEnable()
        {
            if (_onShopOpened != null) _onShopOpened.Register(HandleShopOpened);
            if (_onShopClosed != null) _onShopClosed.Register(HandleShopClosed);
        }

        private void OnDisable()
        {
            if (_onShopOpened != null) _onShopOpened.Unregister(HandleShopOpened);
            if (_onShopClosed != null) _onShopClosed.Unregister(HandleShopClosed);
        }

        private void HandleShopOpened() => _isBlocked = true;
        private void HandleShopClosed() => _isBlocked = false;

        private void Update()
        {
            if (_isBlocked) return;

            if (UnityInput.GetMouseButtonDown(0))
            {
                // The same mouse-down also drives Unity UI Buttons. If the click landed on a
                // UI element with a raycast target, it belongs to the UI and should not also
                // start the run. Space is unaffected — it never has a pointer position.
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                OnTapped?.Invoke();
                return;
            }

            if (UnityInput.GetKeyDown(KeyCode.Space))
            {
                OnTapped?.Invoke();
            }
        }
    }
}

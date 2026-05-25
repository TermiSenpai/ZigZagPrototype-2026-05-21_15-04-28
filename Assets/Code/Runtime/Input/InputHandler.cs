using System;
using UnityEngine;
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
    /// The shop overlay suspends tap routing via <see cref="_onShopOpened"/>/
    /// <see cref="_onShopClosed"/> so a tap inside the shop UI does not start a run.
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
            if (UnityInput.GetMouseButtonDown(0) || UnityInput.GetKeyDown(KeyCode.Space))
            {
                OnTapped?.Invoke();
            }
        }
    }
}

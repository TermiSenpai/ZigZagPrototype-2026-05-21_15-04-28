using System;
using UnityEngine;
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
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class InputHandler : MonoBehaviour
    {
        public event Action OnTapped;

        private void Update()
        {
            if (UnityInput.GetMouseButtonDown(0) || UnityInput.GetKeyDown(KeyCode.Space))
            {
                OnTapped?.Invoke();
            }
        }
    }
}

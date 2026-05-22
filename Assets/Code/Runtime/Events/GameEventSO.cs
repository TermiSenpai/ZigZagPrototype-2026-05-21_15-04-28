using System;
using UnityEngine;

namespace ZigZag.Runtime.Events
{
    /// <summary>
    /// Parameterless ScriptableObject event channel. Lets senders broadcast and
    /// receivers listen without holding direct references to each other.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md §6 and ADR-004 in zigzag_architecture.md prescribe SO channels
    /// for cross-system events. Subscribers must <see cref="Register"/> in
    /// <c>OnEnable</c> and <see cref="Unregister"/> in <c>OnDisable</c>.
    /// </remarks>
    [CreateAssetMenu(menuName = "ZigZag/Events/Game Event", fileName = "SO_GameEvent")]
    public sealed class GameEventSO : ScriptableObject
    {
        private event Action _listeners;

        public void Raise() => _listeners?.Invoke();

        public void Register(Action listener) => _listeners += listener;

        public void Unregister(Action listener) => _listeners -= listener;
    }

    /// <summary>
    /// Typed variant of <see cref="GameEventSO"/>. Concrete subclasses pick a
    /// payload type (see <see cref="IntGameEventSO"/>) and add their own
    /// <c>CreateAssetMenu</c> attribute.
    /// </summary>
    public abstract class GameEventSO<T> : ScriptableObject
    {
        private event Action<T> _listeners;

        public void Raise(T payload) => _listeners?.Invoke(payload);

        public void Register(Action<T> listener) => _listeners += listener;

        public void Unregister(Action<T> listener) => _listeners -= listener;
    }
}

using UnityEngine;

namespace ZigZag.Runtime.Events
{
    /// <summary>
    /// String-payload event channel. Used for cross-system events whose payload is a
    /// stable identifier (skin id, achievement key, ...). Subscribers must
    /// <see cref="GameEventSO{T}.Register"/> in <c>OnEnable</c> and
    /// <see cref="GameEventSO{T}.Unregister"/> in <c>OnDisable</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "ZigZag/Events/String Event", fileName = "SO_StringEvent")]
    public sealed class StringGameEventSO : GameEventSO<string> { }
}

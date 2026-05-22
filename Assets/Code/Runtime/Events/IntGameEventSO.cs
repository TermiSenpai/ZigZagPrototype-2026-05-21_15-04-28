using UnityEngine;

namespace ZigZag.Runtime.Events
{
    /// <summary>
    /// Integer-payload event channel. Used for score changes, gem values, and any
    /// other cross-system event whose payload fits in a single <see cref="int"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "ZigZag/Events/Int Event", fileName = "SO_IntEvent")]
    public sealed class IntGameEventSO : GameEventSO<int> { }
}

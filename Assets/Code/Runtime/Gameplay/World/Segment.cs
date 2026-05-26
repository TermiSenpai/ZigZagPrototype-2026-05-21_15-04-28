using System.Collections.Generic;
using UnityEngine;

namespace ZigZag.Runtime.Gameplay.World
{
    /// <summary>
    /// A straight run of platform cubes along a single world axis. Owns the cubes
    /// it spawns until the <see cref="PathGenerator"/> releases them back to the pool.
    /// </summary>
    /// <remarks>
    /// Plain C# class (not a <see cref="MonoBehaviour"/>): a segment is a value the
    /// generator manipulates, not an entity with a Unity lifecycle. The cubes are
    /// exposed read-only — CLAUDE.md §5 forbids leaking mutable collections.
    /// </remarks>
    public sealed class Segment
    {
        private readonly List<GameObject> _cubes;

        public Vector3 Direction { get; }

        public int CubeCount => _cubes.Count;

        public IReadOnlyList<GameObject> Cubes => _cubes;

        public Vector3 LastCubePosition => _cubes[_cubes.Count - 1].transform.position;

        /// <summary>
        /// Index of the first cube whose fall animation has not been triggered yet.
        /// Cubes within a segment are passed by the ball in order (cube 0 first,
        /// cube N-1 last), so the generator only needs to walk forward from this
        /// counter each frame instead of scanning every cube.
        /// </summary>
        public int FallTriggerIndex { get; private set; }

        public Segment(Vector3 direction, int initialCapacity = 8)
        {
            Direction = direction;
            _cubes = new List<GameObject>(initialCapacity);
        }

        public void AddCube(GameObject cube)
        {
            _cubes.Add(cube);
        }

        public void AdvanceFallTrigger()
        {
            FallTriggerIndex++;
        }
    }
}

using UnityEngine;
using ZigZag.Runtime.Gameplay.Collectibles;
using ZigZag.Runtime.Gameplay.Scoring;
using ZigZag.Runtime.Gameplay.World;

namespace ZigZag.Runtime.Core
{
    /// <summary>
    /// Per-scene composition root. Runs before any other script (via
    /// <see cref="DefaultExecutionOrderAttribute"/>) and asserts the main actors are
    /// wired. It does not instantiate or resolve services on its own — each
    /// component owns its own setup; the bootstrap exists so a missing reference
    /// fails loudly here instead of as a <c>NullReferenceException</c> later.
    /// </summary>
    /// <remarks>
    /// Lives in Core (not UI), so its dependencies are gameplay-side actors only —
    /// the UI controller validates itself in its own Awake. This keeps the
    /// asmdef graph acyclic (Core → Gameplay, UI → Events, no link between Core and UI).
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene's platform pool. Initializes itself on Awake; the bootstrap only asserts it exists.")]
        private PlatformPool _platformPool;

        [SerializeField, Tooltip("Scene's path generator.")]
        private PathGenerator _pathGenerator;

        [SerializeField, Tooltip("Scene's game state machine.")]
        private GameStateMachine _stateMachine;

        [SerializeField, Tooltip("Scene's score manager.")]
        private ScoreManager _scoreManager;

        [SerializeField, Tooltip("Scene's gem pool.")]
        private GemPool _gemPool;

        [SerializeField, Tooltip("Scene's gem spawner.")]
        private GemSpawner _gemSpawner;

        private void Awake()
        {
            Debug.Assert(_platformPool != null, $"{nameof(GameBootstrap)} requires a {nameof(PlatformPool)} reference.", this);
            Debug.Assert(_pathGenerator != null, $"{nameof(GameBootstrap)} requires a {nameof(PathGenerator)} reference.", this);
            Debug.Assert(_stateMachine != null, $"{nameof(GameBootstrap)} requires a {nameof(GameStateMachine)} reference.", this);
            Debug.Assert(_scoreManager != null, $"{nameof(GameBootstrap)} requires a {nameof(ScoreManager)} reference.", this);
            Debug.Assert(_gemPool != null, $"{nameof(GameBootstrap)} requires a {nameof(GemPool)} reference.", this);
            Debug.Assert(_gemSpawner != null, $"{nameof(GameBootstrap)} requires a {nameof(GemSpawner)} reference.", this);
        }
    }
}

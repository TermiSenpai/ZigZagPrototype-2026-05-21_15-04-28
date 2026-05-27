# ZigZag — Technical Architecture

> Technical document complementary to the GDD ([`zigzag_gdd.en.md`](zigzag_gdd.en.md)). Captures architecture decisions, class signatures, the event system, code conventions and trade-offs (lightweight ADRs).
>
> **Aligned with [`CLAUDE.md`](CLAUDE.md) and [`.claude/agents/AGENTS.md`](.claude/agents/AGENTS.md). If any conflict arises, `CLAUDE.md` wins.**

**Unity version:** 2022.3.62f2 LTS
**Render pipeline:** Built-in (project default). // TODO: evaluate URP migration before vertical slice (CLAUDE §1).
**Code language:** C# (.NET Standard 2.1 / Unity 2022.3).
**Language:** the **code** (identifiers, comments, `TODO:`, logs, asset names, commit messages) is **exclusively in English**. This design document is in Spanish for author velocity, as made explicit in [`CLAUDE.md` §2.1](CLAUDE.md).

---

## 1. Purpose of the document

This document exists to:

1. Make **technical decisions explicit** so a reviewer sees them on opening the code.
2. Serve as the **contract for the main classes before they are written** (design first, code after).
3. Document **conventions** that will be applied uniformly.
4. Justify trade-offs in mini-ADR format.

It is not API documentation generated from code. It does not replace `///` comments in the code itself.

---

## 2. Architecture overview

### 2.1 Philosophy

- **Small components with a single responsibility** (SRP). Each `MonoBehaviour` does one thing.
- **Communication via events**, not via direct calls between unrelated systems.
- **Configuration outside code**, in `ScriptableObject`.
- **Inspector injection**, never `FindObjectOfType` or `GameObject.Find` at runtime.
- **Pool everything instantiable**, no `Instantiate` / `Destroy` during gameplay.
- **Determinism in gameplay**: no `DateTime.Now`, no `UnityEngine.Random` without a seed for generation.

### 2.2 Layers and dependency direction

```
┌────────────────────────────────────────────────────────────┐
│  Presentation   UI · Audio · VFX                            │  → only listens to events
└────────────────────────────────────────────────────────────┘
                          ▲
┌────────────────────────────────────────────────────────────┐
│  Coordination   Core (GameStateMachine, GameBootstrap)      │  → orchestrates gameplay
└────────────────────────────────────────────────────────────┘
                          ▲
┌────────────────────────────────────────────────────────────┐
│  Gameplay   Player · World · Collectibles · Economy ·       │
│             Scoring · Cosmetics · Aesthetics · CameraSystem │  → publishes events
└────────────────────────────────────────────────────────────┘
                          ▲
┌────────────────────────────────────────────────────────────┐
│  Data / Input   GameConfigSO · GameEventSO · InputHandler   │  → depends on nobody
└────────────────────────────────────────────────────────────┘
```

Rule: **UI → Gameplay → Core → Data**. Never upward. Never sideways without an interface or event.

### 2.3 Hard rules (inherited from CLAUDE.md §2)

These act as a permanent checklist:

1. **English** in all code.
2. Deferred work as `// TODO: <description> (<context>)`. **Never** `FIXME`, `XXX` or untagged notes.
3. **Mandatory patterns** for non-trivial systems (see §6).
4. **Mandatory encapsulation** — never `public` mutable.
5. **Independence** — interfaces, events or `GameEventSO`, never unnecessary cross references.
6. **Determinism** in gameplay code.
7. **Minimal `Update()`** — cache, subscribe, pool, move to `FixedUpdate` or coroutines.
8. **Never** edit `.meta` by hand nor commit `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`.
9. **Force-text serialization** assumed for scenes and prefabs (do not touch `EditorSettings.asset`).
10. **Read before asking.**

---

### 2.4 Design patterns chosen before typing code

`CLAUDE.md` §6 sets the catalog of mandatory patterns for non-trivial systems and the anti-patterns to reject on sight. **That catalog was written before the first `.cs` in the repository** — the patterns are not retrospective findings, they are decisions made during design (this document + the GDD) and applied later when writing code.

Patterns actually applied in the implementation (pattern → real repo classes mapping):

| Pattern | Where it lives | Why it was chosen |
|---------|----------------|-------------------|
| **ScriptableObject as data container** | `GameConfigSO`, `PaletteRulesSO`, `BallSkinSO`, `BallSkinCatalogSO` | Editable configuration without recompiling, natural encapsulation (`private + serialized + get-only`), designer-tunable, hot-reload in editor. |
| **Event Channel (SO Pub-Sub)** | 15 `SO_*` assets under `Assets/Settings/Events/` | Cross-system communication without sender and receiver referencing each other directly. Replaces singletons and `FindObjectOfType`. |
| **Observer (native C# event)** | `BallController.OnDirectionChanged` / `OnFell`, `InputHandler.OnTapped`, `Gem` callbacks | Local variant of pub-sub: when emitter and listener live in the same layer/asmdef and a full SO asset isn't worth the ceremony. Symmetric subscription in `OnEnable`/`OnDisable`. |
| **Finite State Machine (FSM)** | `GameStateMachine` with `enum GameState { Menu, Playing, GameOver }` | Three states, explicit transitions. A hand-rolled FSM is more readable than pulling in a framework. |
| **Object Pool** | `PlatformPool`, `GemPool` (via `UnityEngine.Pool.ObjectPool<T>` from 2022 LTS) | Zero `Instantiate`/`Destroy` in the hot path. Prewarming in `Awake`. ADR-002. |
| **Template Method (generic hierarchy)** | `GameEventSO<T>` abstract → `IntGameEventSO`, `StringGameEventSO` | A single implementation of Register/Unregister/Raise; concrete payloads only declare the type. |
| **Strategy (data-driven)** | `BallSkinSO` (swap `Material`), `PaletteRulesSO` (swap HSV ranges) | The "strategy" is selected by dragging another asset, not by instantiating another class. Pure editorial workflow. |
| **Catalog (simplified Repository)** | `BallSkinCatalogSO` with `GetById(string)` | Centralized lookup without a Dictionary running at runtime; the serialized array preserves display order = shop order. |
| **Composition Root / Bootstrap** | `GameBootstrap` with `[DefaultExecutionOrder(-1000)]` | Single validation point for serialized refs via `Debug.Assert`. It does not instantiate or resolve anything — composition already lives in the scene; the bootstrap just yells if something is missing before the first frame. |
| **MVP-lite (View ⇄ Presenter ⇄ Model)** | `UIController` (View), SO channels (Presenter), `ScoreManager`/`CoinsWallet`/`SkinInventory` (Model) | The View only reads and updates widgets; it never contains business logic. The Model never knows the View — it pushes via channel. |
| **Composition over inheritance** | Every `MonoBehaviour` has one responsibility. The ball doesn't extend anything; it receives collaborators via `[SerializeField]`. | Inheritance only when there is a true `is-a`; in arcade gameplay this rarely happens. |
| **Pure helpers (stateless)** | `ScoreCalculator`, `CameraFollowMath`, `PaletteSampler` | `static class` without Unity lifecycle. Testable in EditMode without mocks; separates arithmetic from side-effects (raises, persistence). |

Anti-patterns explicitly avoided (list only — justification lives in `CLAUDE.md` §6):

- God `GameManager`.
- Global singletons (`Instance ??= this`).
- `static` mutable state.
- `SendMessage`, `BroadcastMessage`, `FindObjectOfType`, `GameObject.Find` outside of bootstrap/editor.
- Business logic in editor scripts.
- `Resources.Load` in hot paths.

Chronological order was: GDD → this architecture document → `CLAUDE.md` with the catalog of mandatory patterns → first code commit. Each subsequent iteration adds classes that fit one of the patterns in the table; if an iteration requires a new pattern, it is introduced explicitly in its spec with justification (the plans in `docs/superpowers/plans/` document it).

---

## 3. Code conventions (aligned to CLAUDE.md §4 and §5)

### 3.1 Naming — single reference table

| Element                   | Convention                                | Example                                                |
| ------------------------- | ----------------------------------------- | ------------------------------------------------------ |
| Namespace                 | `ZigZag.<Layer>.<Feature>`                | `ZigZag.Runtime.Gameplay.Player`                       |
| Class / Struct / Enum     | `PascalCase`                              | `BallController`                                       |
| Interface                 | `IPascalCase`                             | `IDamageable`                                          |
| Method / Property         | `PascalCase`                              | `StartMoving`, `CurrentSpeed`                          |
| Private field             | `_camelCase`                              | `_rigidbody`                                           |
| Serialized field          | `[SerializeField] private` + `_camelCase` | `[SerializeField] private float _forwardSpeed;`        |
| Constant                  | `PascalCase` (**not** SCREAMING)          | `MaxLives`, `DefaultGravity`                           |
| Static readonly           | `PascalCase`                              | `DefaultGravity`                                       |
| Local / parameter         | `camelCase`                               | `deltaTime`                                            |
| Assembly                  | `ZigZag.<Layer>[.<Feature>]`              | `ZigZag.Runtime.Gameplay`                              |
| C# event                  | `On<Noun><PastTense>`                     | `OnDirectionChanged`, `OnGemCollected`                 |
| `ScriptableObject` asset  | `SO_<Name>` (file), class `<Name>SO`      | `SO_GameConfig.asset`, `GameConfigSO`                  |
| Prefab                    | `P_<Name>`                                | `P_Player`, `P_PlatformCube`                           |
| Scene                     | `S_<Name>`                                | `S_Main`                                               |
| Material                  | `M_<Name>`                                | `M_Platform_A`                                         |

**No optionality.** These are the conventions. If at any point something doesn't fit, discuss it and update this document — no ad-hoc variants are applied.

### 3.2 Golden rules

1. **No magic numbers.** Every configurable numeric constant lives in `GameConfigSO`.
2. **No `Instantiate` / `Destroy` at runtime.** Only during initial setup (`Awake` / `Start` of `GameBootstrap`).
3. **`event` keyword mandatory** on public C# events. Never `public Action<T> OnX`.
4. **Subscribe in `OnEnable`, unsubscribe in `OnDisable`.** Symmetric. No exceptions.
5. **Invoke events with `?.Invoke(...)`.** Never direct `.Invoke(...)`.
6. **No lambdas in event subscriptions.** They can't be unsubscribed.
7. **`[SerializeField] private`** to expose to the inspector. Never `public` mutable.
8. **`sealed`** by default on concrete classes. Inheritance only when there is a real `is-a`.
9. **One class, one responsibility.** ~200 lines usually indicates it's time to split.
10. **`///` comments** on public APIs of reusable classes. Body comments only when the code doesn't explain itself.
11. **Validate inputs at boundaries.** Public service methods check `null` / range and either throw `ArgumentException` / `ArgumentNullException`, or do `Debug.LogError` + early return.

### 3.3 Validation of serialized references

Standard pattern to detect misconfigurations at runtime:

```csharp
private void Awake()
{
    Debug.Assert(_config != null, $"{nameof(BallController)} requires {nameof(GameConfigSO)}", this);
    Debug.Assert(_inputHandler != null, $"{nameof(BallController)} requires {nameof(InputHandler)}", this);
}

#if UNITY_EDITOR
private void OnValidate()
{
    if (_forwardSpeed < 0f) _forwardSpeed = 0f;
}
#endif
```

`OnValidate` runs in the editor and aborts misconfigurations before play. `Debug.Assert` fails at runtime with a stack trace instead of throwing a `NullReferenceException` 50 frames later.

---

## 4. Folder structure

Aligned to [`CLAUDE.md` §3](CLAUDE.md). **Flat layout with no `_Project/` prefix** — shorter paths, simpler navigation. The trade-off (alphabetical mixing with third-party packages if imported in the future) is accepted.

```
Assets/
├── Art/                                 # Sprites, models, textures, materials
├── Audio/                               # Clips, mixers
├── Code/
│   ├── Runtime/
│   │   ├── Core/                        # GameBootstrap, GameStateMachine, GameState (enum)
│   │   ├── Gameplay/
│   │   │   ├── Player/                  # BallController
│   │   │   ├── World/                   # PathGenerator, Segment, PlatformPool, PlatformFaller
│   │   │   ├── Collectibles/            # Gem, GemSpawner, GemPool
│   │   │   ├── Economy/                 # CoinsWallet
│   │   │   ├── Scoring/                 # ScoreManager, ScoreCalculator
│   │   │   ├── Cosmetics/               # BallSkinSO, BallSkinCatalogSO, SkinInventory, BallSkinApplier
│   │   │   ├── Aesthetics/              # PaletteRulesSO, PaletteSampler, PaletteController
│   │   │   └── CameraSystem/            # CameraFollow, CameraFollowMath
│   │   ├── Input/                       # InputHandler
│   │   ├── UI/                          # UIController, MenuPanel, HUDPanel, GameOverPanel
│   │   ├── Audio/                       # AudioManager
│   │   ├── Data/                        # GameConfigSO
│   │   ├── Events/                      # GameEventSO, GameEventSO<T>, IntGameEventSO, ...
│   │   └── Utilities/                   # Pure helpers, extensions
│   ├── Editor/                          # Editor-only tools (asmdef with includePlatforms: [Editor])
│   └── Tests/
│       ├── EditMode/
│       └── PlayMode/
├── Prefabs/                             # P_Ball, P_PlatformCube, P_Gem, P_ShopRow
├── Scenes/                              # S_Main.unity
├── Settings/                            # SO_GameConfig.asset + SO_* event assets
└── VFX/
```

**Asmdef naming independent of path:** `.asmdef` files are named `ZigZag.<Layer>.<Feature>` (e.g. `ZigZag.Runtime.Data`) regardless of where they sit physically. The assembly name defines the namespace contract; the path only organizes files.

---

## 5. Assembly Definitions (.asmdef)

One `.asmdef` per Runtime folder, per [`CLAUDE.md` §3](CLAUDE.md). The setup cost is one-shot; the gain is fast incremental compiles and hard enforcement of dependency direction.

| asmdef                         | Path                                              | Internal references                             | Notes                                                              |
| ------------------------------ | ------------------------------------------------- | ----------------------------------------------- | ------------------------------------------------------------------ |
| `ZigZag.Runtime.Core`          | `Assets/Code/Runtime/Core/`                       | Events, Data                                    |                                                                    |
| `ZigZag.Runtime.Data`          | `Assets/Code/Runtime/Data/`                       | —                                               | Only configuration `ScriptableObject`s.                            |
| `ZigZag.Runtime.Events`        | `Assets/Code/Runtime/Events/`                     | —                                               | `GameEventSO` and typed variants.                                  |
| `ZigZag.Runtime.Input`         | `Assets/Code/Runtime/Input/`                      | Events                                          |                                                                    |
| `ZigZag.Runtime.Gameplay`      | `Assets/Code/Runtime/Gameplay/`                   | Core, Data, Events, Input, Utilities            | All gameplay features live in sub-namespaces.                      |
| `ZigZag.Runtime.UI`            | `Assets/Code/Runtime/UI/`                         | Core, Data, Events                              | TextMeshPro. **Never** references Gameplay directly.               |
| `ZigZag.Runtime.Audio`         | `Assets/Code/Runtime/Audio/`                      | Data, Events                                    |                                                                    |
| `ZigZag.Runtime.Utilities`     | `Assets/Code/Runtime/Utilities/`                  | —                                               | Pure C#, helpers, extensions.                                      |
| `ZigZag.Editor`                | `Assets/Code/Editor/`                             | Any Runtime asmdef                              | `includePlatforms: [Editor]`. **Never** enters the player build.   |
| `ZigZag.Tests.EditMode`        | `Assets/Code/Tests/EditMode/`                     | Runtime + `UnityEngine.TestRunner`              | `defineConstraints: [UNITY_INCLUDE_TESTS]`.                        |
| `ZigZag.Tests.PlayMode`        | `Assets/Code/Tests/PlayMode/`                     | Runtime + `UnityEngine.TestRunner`              | `defineConstraints: [UNITY_INCLUDE_TESTS]`.                        |

**Post-setup verification:** from any Runtime asmdef it should be impossible to do `using ZigZag.Editor.*`. If Unity compiles that, the editor asmdef is misconfigured.

---

## 6. Event system (final decision: hybrid)

`CLAUDE.md` §6 lists **Event Channel (SO)** as the default pattern for cross-system communication, and **`static` mutable state** as an explicit anti-pattern. Therefore the decision is:

- **Global / cross-system events → `GameEventSO` (ScriptableObject Event Channel).** Lets the UI listen to Gameplay without holding a reference, and the event listing lives as assets a reviewer can explore.
- **Local events (one component exposes, another subscribes within the same system) → `event Action<T>` on the emitter component.** Plain C# Observer pattern. CLAUDE.md §6 explicitly allows it "when SO channels are overkill".

No `static` mutable. No singletons.

### 6.1 `GameEventSO` (skeleton, aligned with `CLAUDE.md` §13)

```csharp
using System;
using UnityEngine;

namespace ZigZag.Runtime.Events
{
    /// <summary>
    /// Parameterless event channel. Raise from any sender, listen from any receiver,
    /// without the two ever holding references to each other.
    /// </summary>
    [CreateAssetMenu(menuName = "ZigZag/Events/Game Event", fileName = "SO_GameEvent")]
    public sealed class GameEventSO : ScriptableObject
    {
        private event Action _listeners;

        public void Raise() => _listeners?.Invoke();
        public void Register(Action listener) => _listeners += listener;
        public void Unregister(Action listener) => _listeners -= listener;
    }
}
```

Payload variant (created on first use, not before):

```csharp
public abstract class GameEventSO<T> : ScriptableObject
{
    private event Action<T> _listeners;
    public void Raise(T payload) => _listeners?.Invoke(payload);
    public void Register(Action<T> listener) => _listeners += listener;
    public void Unregister(Action<T> listener) => _listeners -= listener;
}

[CreateAssetMenu(menuName = "ZigZag/Events/Int Event", fileName = "SO_IntEvent")]
public sealed class IntGameEventSO : GameEventSO<int> { }
```

### 6.2 Event catalog

**Global (`SO_*.asset` assets in `Assets/Settings/Events/`):**

| Asset                       | Type                | Raised by                    | Typical subscribers                               |
| --------------------------- | ------------------- | ---------------------------- | ------------------------------------------------- |
| `SO_OnGameStarted`          | `GameEventSO`       | `GameStateMachine`           | `BallController`, `PathGenerator`, `UIController` |
| `SO_OnGameOver`             | `GameEventSO`       | `GameStateMachine`           | `PathGenerator`, `UIController`, `AudioManager`, `ScoreManager` |
| `SO_OnGameReset`            | `GameEventSO`       | `GameStateMachine`           | All systems with mutable state                    |
| `SO_OnRetryRequested`       | `GameEventSO`       | `UIController` (Retry button)| `GameStateMachine`                                |
| `SO_OnScoreChanged`         | `IntGameEventSO`    | `ScoreManager`               | `UIController` (HUD), `PaletteController`         |
| `SO_OnBestScoreChanged`     | `IntGameEventSO`    | `ScoreManager`               | `UIController` (Menu, GameOver)                   |
| `SO_OnGemCollected`         | `IntGameEventSO`    | `Gem`                        | `CoinsWallet`, `AudioManager`                     |
| `SO_OnCoinsChanged`         | `IntGameEventSO`    | `CoinsWallet`                | `UIController`, `ShopPanel`                       |
| `SO_OnSessionCoinsChanged`  | `IntGameEventSO`    | `CoinsWallet`                | `UIController` (GameOver `+N coins`)              |
| `SO_OnDirectionChanged`     | `GameEventSO`       | `BallController.FlipDirection` | `AudioManager`                                  |
| `SO_OnSkinPurchaseRequested`| `StringGameEventSO` | `ShopRowView`                | `SkinInventory`                                   |
| `SO_OnSkinEquipRequested`   | `StringGameEventSO` | `ShopRowView`                | `SkinInventory`                                   |
| `SO_OnSkinEquipped`         | `StringGameEventSO` | `SkinInventory`              | `BallSkinApplier`, `ShopPanel`                    |
| `SO_OnInventoryChanged`     | `GameEventSO`       | `SkinInventory`              | `ShopPanel`                                       |
| `SO_OnShopOpened`           | `GameEventSO`       | `ShopPanel`                  | `InputHandler`                                    |
| `SO_OnShopClosed`           | `GameEventSO`       | `ShopPanel`                  | `InputHandler`                                    |

**Local (C# `event` on the emitter component):**

- `InputHandler.OnTapped` — `BallController` and `AudioManager` (optional) listen to it.
- `BallController.OnDirectionChanged` — telemetry/immediate feedback. Private to the Gameplay system.
- `BallController.OnFell` — `GameStateMachine` listens to it to transition to `GameOver`.

If a local event is needed in more than two systems, promote it to `GameEventSO`.

### 6.3 Subscription rules

- `event` keyword mandatory for public C# events.
- `GameEventSO` is injected via inspector: `[SerializeField] private GameEventSO _onGameStarted;`.
- Subscription in `OnEnable`, unsubscription in `OnDisable`. Symmetric.
- No lambdas in subscriptions (they can't be unsubscribed → memory leaks).
- Invoke C# events with `?.Invoke(...)`.

### 6.4 Key flows

**Run start:**
```
User clicks on Menu
  └─> InputHandler.OnTapped (local C# event)
       └─> GameStateMachine.HandleMenuTap()
            └─> SO_OnGameStarted.Raise()
                 ├─> BallController: start movement
                 ├─> ScoreManager: reset counters
                 ├─> PathGenerator: start generation
                 ├─> UIController: hide menu, show HUD
                 └─> AudioManager: (optional) ambient
```

**Gem pickup:**
```
Ball enters Gem trigger
  └─> Gem.OnTriggerEnter
       ├─> GemPool.Release(this)
       └─> SO_OnGemCollected.Raise(value)
            ├─> ScoreManager: adds points
            │    └─> SO_OnScoreChanged.Raise(newScore)
            │         └─> UIController: updates HUD
            ├─> AudioManager: PlayPickup
            └─> VFX: spawn particles
```

**Game Over:**
```
Ball leaves the path and position.y < threshold
  └─> BallController.OnFell (local C# event)
       └─> GameStateMachine.HandleBallFell()
            └─> SO_OnGameOver.Raise()
                 ├─> ScoreManager.SaveBestIfHigher → SO_OnBestScoreChanged.Raise
                 ├─> PathGenerator: stop generation
                 ├─> BallController: stop movement (via GameStateMachine.StopMoving)
                 ├─> UIController: GameOver panel
                 └─> AudioManager: PlayDeath
```

---

## 7. Class catalog

For each class: responsibility, dependencies and public signature. No implementation included — that's code.

### 7.1 `GameConfigSO` (`ZigZag.Runtime.Data`)

**Responsibility:** single container for tunable parameters. Encapsulated: private fields, read via property.

```csharp
namespace ZigZag.Runtime.Data
{
    [CreateAssetMenu(fileName = "SO_GameConfig", menuName = "ZigZag/Game Config")]
    public sealed class GameConfigSO : ScriptableObject
    {
        [Header("Movement")]
        [SerializeField] private float _initialSpeed = 5f;
        [SerializeField] private float _acceleration = 0.05f;
        [SerializeField] private float _maxSpeed = 12f;
        [SerializeField] private float _fallSpeed = 9.8f;
        [SerializeField] private float _fallThreshold = -2f;

        [Header("Path Generation")]
        [SerializeField] private Vector3 _cubeSize = new(1f, 0.3f, 1f);
        [SerializeField] private int _segmentMinLength = 3;
        [SerializeField] private int _segmentMaxLength = 8;
        [SerializeField] private float _aheadBuffer = 30f;
        [SerializeField] private float _behindBuffer = 10f;
        [SerializeField] private int _generationSeed = 0;        // 0 = random at runtime

        [Header("Gems")]
        [SerializeField, Range(0f, 1f)] private float _gemSpawnProbability = 0.3f;
        [SerializeField] private int _gemValue = 10;

        [Header("Score")]
        [SerializeField] private int _distanceMultiplier = 1;

        [Header("Camera")]
        [SerializeField] private float _cameraFollowSmoothTime = 0.15f;
        [SerializeField] private float _cameraOrthographicSize = 6f;

        [Header("Polish")]
        [SerializeField] private float _freezeFrameOnDeath = 0.1f;

        [Header("Pooling")]
        [SerializeField] private int _platformPoolInitialSize = 50;
        [SerializeField] private int _gemPoolInitialSize = 20;

        // Read-only properties — mandatory encapsulation (CLAUDE §5).
        public float InitialSpeed             => _initialSpeed;
        public float Acceleration             => _acceleration;
        public float MaxSpeed                 => _maxSpeed;
        public float FallSpeed                => _fallSpeed;
        public float FallThreshold            => _fallThreshold;
        public Vector3 CubeSize               => _cubeSize;
        public int SegmentMinLength           => _segmentMinLength;
        public int SegmentMaxLength           => _segmentMaxLength;
        public float AheadBuffer              => _aheadBuffer;
        public float BehindBuffer             => _behindBuffer;
        public int GenerationSeed             => _generationSeed;
        public float GemSpawnProbability      => _gemSpawnProbability;
        public int GemValue                   => _gemValue;
        public int DistanceMultiplier         => _distanceMultiplier;
        public float CameraFollowSmoothTime   => _cameraFollowSmoothTime;
        public float CameraOrthographicSize   => _cameraOrthographicSize;
        public float FreezeFrameOnDeath       => _freezeFrameOnDeath;
        public int PlatformPoolInitialSize    => _platformPoolInitialSize;
        public int GemPoolInitialSize         => _gemPoolInitialSize;
    }
}
```

### 7.2 `GameStateMachine` (`ZigZag.Runtime.Core`)

**Responsibility:** holds the current state (`Menu | Playing | GameOver`) and raises the corresponding `GameEventSO`s. Replaces the old `static class GameEvents`.

**Injected dependencies (Inspector):**
- `GameConfigSO _config`
- `InputHandler _inputHandler`
- `GameEventSO _onGameStarted, _onGameOver, _onGameReset`
- `BallController _ball` (only to listen to its local `OnFell`)

```csharp
namespace ZigZag.Runtime.Core
{
    public enum GameState { Menu, Playing, GameOver }

    [DisallowMultipleComponent]
    public sealed class GameStateMachine : MonoBehaviour
    {
        public GameState CurrentState { get; private set; } = GameState.Menu;

        public void StartGame();
        public void EndGame();
        public void ResetGame();
    }
}
```

**Events it subscribes to:**
- `InputHandler.OnTapped` (only valid in `Menu`)
- `BallController.OnFell`

### 7.3 `InputHandler` (`ZigZag.Runtime.Input`)

**Responsibility:** abstracts input capture. ADR-006 settles on classic `UnityEngine.Input`.

```csharp
namespace ZigZag.Runtime.Input
{
    [DisallowMultipleComponent]
    public sealed class InputHandler : MonoBehaviour
    {
        public event Action OnTapped;
        // Update: if Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) → OnTapped?.Invoke();
    }
}
```

### 7.4 `BallController` (`ZigZag.Runtime.Gameplay.Player`)

**Responsibility:** moves the ball, toggles direction, detects fall.

**Injected dependencies:**
- `GameConfigSO _config`
- `InputHandler _inputHandler`

```csharp
namespace ZigZag.Runtime.Gameplay.Player
{
    [DisallowMultipleComponent]
    public sealed class BallController : MonoBehaviour
    {
        public event Action<Vector3> OnDirectionChanged;
        public event Action OnFell;
        public event Action OnReset;       // fired inside ResetTo (iter 10)

        public Vector3 CurrentDirection { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool IsMoving { get; private set; }

        public void StartMoving();
        public void StopMoving();
        public void ResetTo(Vector3 position);
    }
}
```

**Internal directions:** `new Vector3(-1f, 0f, 0f)` (pure -X) and `new Vector3(0f, 0f, 1f)` (pure +Z). The internal names `AlongNegativeX` and `AlongPositiveZ` reflect the world axis, not the on-screen appearance. The 45° zigzag illusion is produced by the isometric camera (-45° Y rotation) which projects both axes as diagonals on screen — same trick as the original Ketchapp ZigZag. The path is built with axis-aligned cubes (90° world-space turns).

**Local C# events (iter 10):** `OnReset` is added next to `OnDirectionChanged`/`OnFell`. It fires from `ResetTo(position)` right at the end, after repositioning the ball and resetting flags. Single current consumer: `BallTrailColorizer` uses it to call `_trail.Clear()` and avoid the visible straight line between the death point and the spawn (the `TrailRenderer` would otherwise interpolate the teleport as if it were motion). It's a local C# event because the consumer lives in the same asmdef (`ZigZag.Runtime.Gameplay`) — an SO channel would be ceremony (see ADR-004).

### 7.5 `PathGenerator` (`ZigZag.Runtime.Gameplay.World`)

**Responsibility:** generates and despawns segments ahead of / behind the ball.

**Injected dependencies:**
- `GameConfigSO _config`
- `Transform _ballTransform`
- `PlatformPool _platformPool`
- `GemSpawner _gemSpawner`

```csharp
namespace ZigZag.Runtime.Gameplay.World
{
    [DisallowMultipleComponent]
    public sealed class PathGenerator : MonoBehaviour
    {
        public void StartGeneration();
        public void StopGeneration();
        public void ResetGenerator();
    }
}
```

**Internal notes:**
- `Queue<Segment>` of active segments.
- `System.Random` with a seed for reproducibility (not `UnityEngine.Random`, which is global).
- In `Update`, if the last cube is closer than `AheadBuffer` to the ball, generate a segment.
- Returns to the pool the cubes `BehindBuffer` behind.

### 7.6 `Segment` (`ZigZag.Runtime.Gameplay.World`)

```csharp
namespace ZigZag.Runtime.Gameplay.World
{
    public sealed class Segment
    {
        public Vector3 StartPosition { get; }
        public Vector3 Direction { get; }
        public int CubeCount { get; }
        public IReadOnlyList<GameObject> Cubes => _cubes;

        private readonly List<GameObject> _cubes;
        public Segment(Vector3 start, Vector3 direction, List<GameObject> cubes) { ... }
    }
}
```

`class` (not `struct`): contains a `List<GameObject>`, avoids value-copy issues. Exposed as `IReadOnlyList<GameObject>` (CLAUDE §5 — never expose mutable collections).

### 7.7 `Gem` (`ZigZag.Runtime.Gameplay.Collectibles`)

```csharp
namespace ZigZag.Runtime.Gameplay.Collectibles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class Gem : MonoBehaviour
    {
        [SerializeField] private IntGameEventSO _onGemCollected;

        public int Value { get; private set; }
        public void Initialize(int value);
        // OnTriggerEnter: _onGemCollected.Raise(Value) + return to pool
    }
}
```

### 7.8 `GemSpawner` (`ZigZag.Runtime.Gameplay.Collectibles`)

```csharp
namespace ZigZag.Runtime.Gameplay.Collectibles
{
    [DisallowMultipleComponent]
    public sealed class GemSpawner : MonoBehaviour
    {
        public void TryPlaceCollectibleOnSegment(Segment segment);
    }
}
```

### 7.9 `ScoreManager` (`ZigZag.Runtime.Gameplay.Scoring`)

```csharp
namespace ZigZag.Runtime.Gameplay.Scoring
{
    [DisallowMultipleComponent]
    public sealed class ScoreManager : MonoBehaviour
    {
        public int CurrentScore { get; private set; }
        public int BestScore { get; private set; }

        public void ResetScore();
        public void SaveBestIfHigher();
    }
}
```

**Events:** subscribed to `SO_OnGemCollected` (adds to score), `SO_OnGameOver` (calls `SaveBestIfHigher`). Persistence: `PlayerPrefs.GetInt("BestScore", 0)` in `Awake`, `SetInt` + `Save` on save.

### 7.10 `CameraFollow` and `CameraFollowMath` (`ZigZag.Runtime.Gameplay.CameraSystem`)

```csharp
namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    [DisallowMultipleComponent]
    public sealed class CameraFollow : MonoBehaviour
    {
        public Transform Target { get; }
        public void SetTarget(Transform target);
    }

    public static class CameraFollowMath
    {
        public static Vector3 ComputeDesiredPosition(
            Vector3 cameraOrigin,
            Vector3 targetOrigin,
            Vector3 targetCurrent,
            Vector3 forwardAxis,
            float lockedY);
    }
}
```

Namespace `CameraSystem` (not `Camera`) to avoid colliding with `UnityEngine.Camera`.

**Movement rule (ADR-014):** the camera advances **only** along the global forward axis `(-1, 0, 1)/√2`. The perpendicular component of the target's displacement is discarded — the frame stays still laterally and the ball visibly snakes across the screen, reproducing the original ZigZag behavior. Y is locked to the camera's initial Y so it doesn't chase the ball as it falls. The math lives in `CameraFollowMath` (static, no Unity lifecycle, covered by EditMode tests in `Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs`).

**Snap to origin on retry (iter 10):** the camera subscribes to `SO_OnGameReset` and, on receipt, moves its `transform.position` to `(_cameraOrigin.x, _lockedY, _cameraOrigin.z)` and resets `_smoothVelocity = Vector3.zero`. Without this, after a long run the camera was far from the origin and the next run's `SmoothDamp` produced a visible several-world-unit slingshot backward before settling above the menu. The handler is null-safe over the channel and guards against `!_originsCaptured` (origins not yet captured → nothing to return to).

**`GlobalForward` source (iter 10):** the constant is read from `GameConfigSO.GlobalForward`. Previously it lived duplicated in `PathGenerator`, `CameraFollow` and `ScoreManager`; ADR-015 closes the explicit debt registered in the iter 4.2 devlog.

> **Note — sections 7.11 to 7.13 retired and then repopulated.** They originally contained `IPowerup`, `MagnetPowerup` and `PowerupManager`. The magnet powerup was descoped in iteration 5; the skin shop took its slot (iter 5), and later the thin cosmetic components that paint the trail and death burst according to the equipped skin filled the slot (iter 10). Numbers 7.11/7.12/7.13 are reused for those components. Subsequent sections (7.14 `UIController` onward) keep their historical numbering so cross-references with the devlog (`§7.17 CoinsWallet`, `§7.18 GameBootstrap`) don't break.

### 7.11 `BallSkinApplier`, `BallTrailColorizer`, `BallDeathBurst` (Cosmetics + Player layers)

Three thin components that react to the `SO_OnSkinEquipped` channel (string payload = skin id) and keep the ball's presentation coherent without coupling to `BallController`.

```csharp
namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    [DisallowMultipleComponent]
    public sealed class BallSkinApplier : MonoBehaviour
    {
        // Lives on the ball, listens to SO_OnSkinEquipped, swaps MeshRenderer.sharedMaterial = skin.Material.
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallController))]
    public sealed class BallTrailColorizer : MonoBehaviour
    {
        // Iter 10. Authoritative over the TrailRenderer's appearance:
        //  - material (shader-fallback cascade → defeats the magenta InternalErrorShader placeholder)
        //  - width, time, minVertexDistance (reproducible defaults; defeats the oversized-trail
        //    trap from the Inspector's Width Curve being nudged to several world units)
        //  - startColor/endColor tinted on skin equip (same channel as BallSkinApplier)
        //  - _trail.Clear() on BallController.OnReset (without this, respawn paints a straight
        //    line from the death point to the spawn)
        // The material is statically shared across instances (one allocation per session).
    }
}

namespace ZigZag.Runtime.Gameplay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallController))]
    public sealed class BallDeathBurst : MonoBehaviour
    {
        // Iter 10. Child ParticleSystem built in Awake (sphere shape, world-space, 36 particles,
        // lifetime 0.65 s, alpha 1→0). Subscribes to BallController.OnFell (C# event); in
        // HandleFell snaps the host to the impact point before Play(true), so the burst stays
        // anchored where the ball left the path, not where it ends up after the freeze-frame.
        // Optional skin sync via _catalog + _onSkinEquipped slots (null-safe). Static shared
        // material. Mirror of Gem.BuildPickupBurst — same shader-fallback cascade.
    }
}
```

**Decisions:**

- **Native trail + thin colorizer, not a custom all-in-one component.** Unity's `TrailRenderer` is already the right implementation. The only things the native component doesn't know are picking the color per equipped skin, assigning a safe material, and clearing on respawn — and the colorizer covers them in ~120 lines including docstrings.
- **Colorizer is authoritative over trail width**, not just color, as a response to the "magenta oversized trail" incident detected in the first iter-10 build. `[SerializeField, Range]` fields replace the inspector's `Width Curve` (an `AnimationCurve` with two editable keys — an accidental drag breaks the build with no compile warning).
- **`BallDeathBurst` with a direct C# event, not an SO channel.** Audio listens to `SO_OnGameOver` from another asmdef, so there an SO channel is mandatory. The death burst lives in the same asmdef as `BallController` and a local event is enough — consistent with ADR-004.
- **Optional skin sync in the burst.** The `_catalog`/`_onSkinEquipped` slots are null-safe. White→orange default contrasts with every skin and every cycling palette; with the catalog wired, feedback gains visual consistency with the ball. It's not mandatory for the burst to work.

### 7.14 `UIController` (`ZigZag.Runtime.UI`)

```csharp
namespace ZigZag.Runtime.UI
{
    [DisallowMultipleComponent]
    public sealed class UIController : MonoBehaviour
    {
        public void ShowMenu();
        public void ShowHUD();
        public void ShowGameOver(int finalScore, int bestScore, bool isNewRecord);
    }
}
```

Subscribed to `SO_OnGameStarted`, `SO_OnGameOver`, `SO_OnScoreChanged`, `SO_OnBestScoreChanged`, `SO_OnCoinsChanged`, `SO_OnSessionCoinsChanged`, `SO_OnShopOpened`, `SO_OnShopClosed`.

**Animated HUD count-up (iter 10):** `HandleScoreChanged` no longer paints the raw int. It sets `_targetHudScore` and re-derives `_hudCountUpSpeed = gap / _hudScoreCatchUpDuration` (default 0.5 s). In `Update` it interpolates `_displayedHudScore` with `Mathf.MoveTowards` on `Time.unscaledDeltaTime` — `unscaledDeltaTime` is deliberate so the death freeze-frame (`Time.timeScale = 0`) doesn't pause the animation, which would otherwise feel like a jerk on reaching the GameOver panel. `_lastShownHudScore` skips rewriting the `TextMeshProUGUI.text` when the int to display hasn't changed (without this the TMP regenerates mesh 60 times per second). Immediate snap-down if the target is lower (reset-to-0 case). The GameOver panel score still jumps to the final value with no animation. The multiplier-agnostic speed guarantees any rebalance of `_distanceMultiplier` doesn't change the feel — the HUD always takes the same time to reach the new total.

**Shop hides the Menu (iter 10):** `HandleShopOpened`/`HandleShopClosed` toggle `_menuPanel.SetActive`. Previously the shop overlay sat on top of the menu panel; now it disappears while the shop is open. The `SO_OnShopOpened`/`SO_OnShopClosed` channels already existed (raised by `ShopPanel` to suppress taps in `InputHandler`); the `UIController` hooks in as a second listener — the channel goes from 1 to 2 listeners with no new code on the raiser side.

### 7.15 `AudioManager` (`ZigZag.Runtime.Audio`)

```csharp
namespace ZigZag.Runtime.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        public void PlayTap();
        public void PlayPickup();
        public void PlayDeath();
    }
}
```

Subscribed to `InputHandler.OnTapped` (local), `SO_OnGemCollected`, `SO_OnGameOver`.

### 7.16 Pools (`ZigZag.Runtime.Gameplay.World` / `.Collectibles`)

Lightweight wrappers around `UnityEngine.Pool.ObjectPool<T>`.

```csharp
[DisallowMultipleComponent]
public sealed class PlatformPool : MonoBehaviour
{
    public GameObject Get();
    public void Release(GameObject platform);
}
```

Same pattern for `GemPool`. Internally: `ObjectPool<GameObject>` with `createFunc`, `actionOnGet`, `actionOnRelease`, `actionOnDestroy`.

### 7.17 `CoinsWallet` (`ZigZag.Runtime.Gameplay.Economy`)

**Responsibility:** accumulate coins awarded by gems and persist the wallet across runs. The only point that touches the `PlayerPrefs` key `"Coins"`.

**Injected dependencies:**
- `IntGameEventSO _onGemCollected` (inbound)
- `GameEventSO _onGameReset` (inbound)
- `IntGameEventSO _onCoinsChanged` (outbound)
- `IntGameEventSO _onSessionCoinsChanged` (outbound)

```csharp
namespace ZigZag.Runtime.Gameplay.Economy
{
    [DisallowMultipleComponent]
    public sealed class CoinsWallet : MonoBehaviour
    {
        public int TotalCoins { get; private set; }
        public int SessionCoins { get; private set; }
        // No Spend(int) API yet — added when the shop iteration lands.
    }
}
```

**Events:** subscribed to `SO_OnGemCollected` (adds to wallet + session) and `SO_OnGameReset` (resets `SessionCoins`, `TotalCoins` intact). Persistence: `PlayerPrefs.SetInt + Save` on every pickup — player data must not be lost to an abrupt shutdown mid-run.

### 7.18 `GameBootstrap` (`ZigZag.Runtime.Core`)

**Responsibility:** scene entry point. Resolves references, initializes pools, calls `UIController.ShowMenu`. Replaces the Service Locator pattern for this scope (a single composition point is enough with one scene).

```csharp
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class GameBootstrap : MonoBehaviour
{
    // serialized references to all systems
    // Awake: validate refs, initialize pools, set initial UI
}
```

---

## 8. Justified technical decisions (ADRs)

Each decision is defensible. If a reviewer asks "why X?", the answer is here.

### ADR-001 — Kinematic ball (no Rigidbody with gravity)

**Context:** the ball moves at constant speed along two fixed diagonals and "falls" when it leaves the path.

**Decision:** move via `transform.position += dir * speed * dt`. No Rigidbody. The fall is simulated with its own `fallSpeed`.

**Alternatives:**
- Dynamic Rigidbody with gravity: PhysX friction, collisions that slow the ball down, non-deterministic across machines.
- Kinematic Rigidbody: better but still adds cost without benefit.

**Consequences:** deterministic movement. "Am I on the ground" detection has to be done by hand (downward raycast).

### ADR-002 — `UnityEngine.Pool.ObjectPool<T>`

**Decision:** use Unity's native pool (2021+).

**Alternatives:** custom pool (more code), external plugins (forbidden by the brief).

**Consequences:** less code, standard API. Slight loss of control over pool internals, irrelevant.

### ADR-003 — `PlayerPrefs` for best score

**Decision:** `PlayerPrefs.GetInt` / `SetInt` with key `"BestScore"`.

**Alternatives:** JSON with `JsonUtility` (over-engineering for an int), SQLite (absurd).

**Consequences:** trivial. No profiles, irrelevant to scope.

### ADR-004 — Hybrid events: global `GameEventSO` + local C# `event`

**Context:** we need decoupled cross-system communication and, additionally, one-off events between adjacent components.

**Decision:**
- **`GameEventSO`** for events consumed by more than one system (Score, GameOver, GemCollected, etc.).
- **C# `event Action<T>`** for events published by one component and consumed by another in the same system (`InputHandler.OnTapped`, `BallController.OnFell`).

**Rejected alternatives:**
- `static class GameEvents` with `static` events: violates the `CLAUDE.md` §6 rule ("`static` mutable state" as anti-pattern) and drags a lifecycle coupled to the application domain, problematic if the scene reloads.
- Inspector `UnityEvent`: reflection, ~5–10× slower, allocations.
- Only `GameEventSO` for everything: creating an asset for `InputHandler.OnTapped` or `BallController.OnFell` is overkill (CLAUDE §6 admits C# events "when the SO channels are overkill").

**Consequences:**
- Type-safe at compile time.
- No static mutable state.
- Global events appear as navigable assets in the editor (self-documented for the reviewer).
- Cost: strict discipline of symmetric subscription/unsubscription. Mitigated by the `OnEnable` / `OnDisable` rule.

### ADR-005 — `ScriptableObject` for configuration with read-only properties

**Decision:** all game parameters in `GameConfigSO`. Fields `[SerializeField] private`, read via property. Mandatory encapsulation (CLAUDE §5).

**Rejected alternatives:**
- Constants in code (recompile to tune).
- JSON loaded at runtime (unnecessary IO).
- `[SerializeField]` scattered across MonoBehaviours (dispersion).
- `public` mutable fields on the SO (breaks encapsulation — CLAUDE §5).

**Consequences:** a single asset to tune everything. Fast iteration in week 2. Zero runtime cost.

### ADR-006 — Classic input (`UnityEngine.Input`) instead of the new Input System

**Decision:** `Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)` encapsulated in `InputHandler`.

**Alternatives:** new Input System (more powerful but requires Action Map setup; no benefit for 1 button).

**Consequences:** less code, no dependencies. Future migration trivial because it's encapsulated.

### ADR-007 — Manual `SmoothDamp` camera (no Cinemachine)

**Decision:** custom `CameraFollow` with `Vector3.SmoothDamp`.

**Alternatives:** Cinemachine (extra package, questionable against the brief), child of the ball (jitter).

**Consequences:** ~20 lines of our own. Easier to review. **See also ADR-014** — refinement of the follow axis (forward only).

### ADR-008 — Single scene

**Decision:** everything in `S_Main.unity`. States managed by `GameStateMachine` + `UIController`.

**Alternatives:** separate scenes with `SceneManager.LoadScene` (reload, loss of visual continuity).

**Consequences:** instantaneous transitions. The UI shows/hides panels.

### ADR-009 — Powerups out of scope (reversed decision)

**Final decision:** powerups (originally: magnet) **do not ship in the prototype**. The slot was reassigned to iteration 5 "Shop + skins" (see `project_scope_magnet_skipped` and devlog iter 5).

**Justification:** two weeks weren't enough for a full powerup system *plus* the visual polish. The shop demonstrates the same point (extensibility of the architecture without touching gameplay) with a smaller code surface and delivers immediate playable value (unlockable skins).

**Consequences:** the ADR-009 number is occupied by this note to preserve the numbering of the rest of the ADRs (cited from the devlog). No `IPowerup`, `MagnetPowerup` or `PowerupManager` exists in the repo.

### ADR-010 — `GameStateMachine` MonoBehaviour as coordinator (not static, not singleton)

**Decision:** one `GameStateMachine` class instanced in the scene (referenced by `GameBootstrap`). Replaces the "static class GameEvents" pattern.

**Rejected alternatives:**
- `static class` with static events: CLAUDE §6 lists "`static` mutable state" as anti-pattern.
- Singleton MonoBehaviour `Instance ??= this`: CLAUDE §5 explicitly forbids it.

**Consequences:** lifecycle tied to the scene (which is what we want). Subscriptions to `GameEventSO` happen in `OnEnable`, released in `OnDisable`. No global state.

### ADR-011 — One asmdef per Runtime folder

**Decision:** follow CLAUDE.md §3 to the letter. One `.asmdef` per Runtime folder + Editor + Tests separately.

**Alternatives:**
- A single `ZigZag.Runtime.asmdef`: simple to start, but breaks the hard barrier between layers (UI could call Gameplay directly without Unity preventing it).
- No asmdef: impossible to keep editor code out of the player build.

**Consequences:** ~20 minutes initial setup. Fast incremental compiles. Dependency direction enforced by the compiler.

### ADR-012 — `sealed` by default on concrete classes

**Decision:** every concrete class not designed for inheritance is declared `sealed`.

**Justification:** CLAUDE §5 prescribes it. Reduces surface area for accidental changes and allows better inlining.

**Consequences:** if a class ever needs to be extended, `sealed` is removed with justification.

### ADR-013 — Coins wallet separated from score, persisted per pickup

**Context:** gems initially added to a single `CurrentScore` combined with distance. The user asks that gems be persistent currency (prepared for a future shop) and that score reflects only distance.

**Decision:**
- New `CoinsWallet` class (sub-feature `Gameplay/Economy/`, same `ZigZag.Runtime.Gameplay` asmdef). The single point in the project that touches the `PlayerPrefs` key `"Coins"`.
- `ScoreManager` stops listening to `SO_OnGemCollected`. `CurrentScore` = distance.
- Coins persistence **on every pickup**, not on GameOver.

**Rejected alternatives:**
- Keep combined score and expose a `CoinsOnly` getter: hides the separation, mixes responsibilities, contradicts the direction toward a shop.
- Persist only on GameOver: an abrupt shutdown (alt-F4, editor crash) would rob the player of coins. Coins are real currency, not a volatile number.
- Rename `Gem` → `Coin`: high churn mid-prototype; the visual is still a gem (pink octahedron). The "the collectible is a gem, what it awards are coins" nomenclature is consistent with how other games model the topic.
- Build `ICurrency` + generic `CurrencyService`: YAGNI with a single currency.

**Consequences:**
- Score and wallet evolve independently; adding cosmetics paid with coins (iteration 5: shop + skins) doesn't touch `ScoreManager`.
- Two PlayerPrefs keys (`"BestScore"`, `"Coins"`) instead of one. Trivial.
- When the shop arrives, `CoinsWallet` adds `bool TrySpend(int)` with a sufficient-funds guard, without touching the rest of the system.

---

## 9. SOLID applied (concrete)

| Principle                          | Application in this project                                                                                                       |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| **S** — Single Responsibility      | `BallController` moves; `ScoreManager` counts; `UIController` displays; `PathGenerator` generates. None does two things.          |
| **O** — Open/Closed                | Adding a new skin = create a `BallSkinSO` `.asset` + drag it onto the catalog. `SkinInventory`, `BallSkinApplier` and `ShopPanel` are untouched. Adding a new event channel = `GameEventSO<T>` with the right `T`, without touching the dispatcher.                                     |
| **L** — Liskov Substitution        | Any concrete `GameEventSO<T>` (`IntGameEventSO`, `StringGameEventSO`) is interchangeable wherever a channel of that payload is expected. The consumer only knows the abstract base. |
| **I** — Interface Segregation      | `GameEventSO` has 3 methods (`Raise`, `Register`, `Unregister`) and `GameEventSO<T>` adds the typed ones. No "fat" interfaces.    |
| **D** — Dependency Inversion       | `BallController` depends on `GameConfigSO` (data abstraction). Systems are inspector-injected, not created internally.            |

---

## 10. Performance rules (aligned to `CLAUDE.md` §8)

Fixed rules. This isn't "optimize when it hurts"; it's the default discipline. Profile before changing anything.

- **No allocations in `Update` / `FixedUpdate`.** No `new List<T>()`, LINQ, `string` concat, `foreach` over `IEnumerable<T>`, `params`.
- **`for` over `foreach`** in hot paths. `foreach` over `List<T>` is acceptable; over an interface it isn't.
- **Cache `Camera.main`** in `Awake`. Never read it in `Update`.
- **Cache transforms and components** in `Awake`. Zero `GetComponent` in `Update` / `FixedUpdate`.
- **`Animator.StringToHash`** in `static readonly int` (doesn't apply yet — no Animator — but it stands as a rule).
- **Non-alloc physics:** `Physics.RaycastNonAlloc`, `OverlapSphereNonAlloc` with cached buffers.
- **`SetActive` instead of `Instantiate`/`Destroy`** (which is precisely pooling).
- **No `tag` string compares in hot paths.** Cache `LayerMask` or direct reference.
- **The Profiler rules.** Any optimization requires a prior measurement.

---

## 11. Logging & diagnostics (aligned to `CLAUDE.md` §9)

- `Debug.Log` only behind a conditional or a project logger. Verbose logs in hot paths forbidden.
- `Debug.LogError` for real bugs; `Debug.LogWarning` for recoverable misconfigurations; nothing else unless it adds value.
- `Debug.Assert` / `UnityEngine.Assertions.Assert` for invariants in development.
- Methods of a custom logger marked with `[Conditional("UNITY_EDITOR")]` or `[Conditional("DEVELOPMENT_BUILD")]` so they get stripped from release.

---

## 12. Tests (prototype scope)

**Basic** tests (explicit decision: low priority on a 2-week prototype; the goal is to show discipline, not exhaustive coverage).

### 12.1 What is tested

**EditMode (pure C#):**
- `ScoreCalculator` — distance projection on `GlobalForward`, clamp to zero, multiplier.
- `CameraFollowMath` — follow projection on the forward axis, Y locked, perpendicular discarded.
- `CoinsWallet.TrySpend` — success, insufficient funds, non-positive amount.
- `SkinInventory.ParseOwnedCsv` — known IDs, unknown IDs discarded, whitespace ignored, empty/null CSV.
- `PaletteSampler` — complementary hue, circular distance, minimum distance respected.

**PlayMode (MonoBehaviour / coroutines):**
- `BallController` — when `OnTapped` is raised, `CurrentDirection` inverts in the next frame.
- `PathGenerator` — after N seconds, there is at least one active segment and no duplicates.

### 12.2 Conventions

- AAA: Arrange, Act, Assert. One logical assert per test.
- Naming: `Method_State_ExpectedResult` — `ScoreManager_AfterGemCollected_AddsValueToScore`.
- No mocks of Unity types. If something needs a mock, extract it to a C# interface and mock the interface.
- Fixed seeds in deterministic tests (`PathGenerator` with seed = 42).
- `[TearDown]` to destroy GameObjects created in PlayMode.

### 12.3 Quantitative target

5–10 tests across both modes. Enough for a reviewer to see discipline without bleeding the sprint dry.

---

## 13. Source control (aligned to `CLAUDE.md` §11)

- **`main`** = stable. Work on `feat/<short>`, `fix/<short>`, `chore/<short>`.
- **Atomic commits.** One conceptual change per commit. Subject in **English**, imperative: `Add ball controller`, not `Added` or `Adds`.
- **Force-text serialization** verified in `EditorSettings.asset` (default in 2022 LTS).
- **`.meta` with its asset**, never separately.
- **`.gitignore`** already handles `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build/`, `*.csproj`, `*.sln`. Don't touch.
- **PRs / merges** optional for a solo project; even so, keep the format as if a reviewer existed.

---

## 14. MonoBehaviour template (canonical)

Identical to [`CLAUDE.md` §12](CLAUDE.md). Every new MonoBehaviour starts from this template and deviates only with justification:

```csharp
using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Player
{
    /// <summary>
    /// Drives the player's zig-zag movement along the path.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField, Tooltip("Forward speed in units/second.")]
        private float _forwardSpeed = 5f;

        [SerializeField, Tooltip("ScriptableObject channel raised when the player falls off the path.")]
        private GameEventSO _onPlayerFell;

        private Rigidbody _rigidbody;

        public float ForwardSpeed => _forwardSpeed;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            // TODO: subscribe to input service once it exists.
        }

        private void OnDisable()
        {
            // TODO: unsubscribe.
        }

        private void FixedUpdate()
        {
            // Physics integration goes here. No GetComponent, no allocations.
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_forwardSpeed < 0f) _forwardSpeed = 0f;
        }
#endif
    }
}
```

---

## 15. Specialized agents

Subagents are defined in [`.claude/agents/`](.claude/agents/) and cataloged in [`AGENTS.md`](.claude/agents/AGENTS.md).

| I'm going to…                                                | Agent                           |
| ------------------------------------------------------------ | ------------------------------- |
| Design a system, choose a pattern, set boundaries            | `unity-architect`               |
| Write / modify a gameplay script                             | `unity-gameplay-programmer`     |
| Diagnose frame time, GC, draw calls, physics                 | `unity-performance-profiler`    |
| Review C# changes against project rules                      | `unity-code-reviewer`           |
| Add EditMode / PlayMode tests                                | `unity-test-author`             |
| Create custom inspectors, EditorWindows, build hooks         | `unity-editor-tooling`          |
| Build HUD, menu, screen, prompt                              | `unity-ui-developer`            |

**Typical flows:**

- **New feature:** `unity-architect` → `unity-gameplay-programmer` (and/or `unity-ui-developer`) → `unity-test-author` → `unity-code-reviewer` → `unity-performance-profiler` (only if it touches a hot path).
- **Bugfix:** `unity-code-reviewer` (audit) → `unity-gameplay-programmer` (fix) → `unity-test-author` (regression).
- **Optimization:** `unity-performance-profiler` (measure) → `unity-gameplay-programmer` (apply) → `unity-performance-profiler` (verify).

---

## 16. Identified technical risks and mitigations

| Risk                                                                    | Mitigation                                                                                                  |
| ----------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Memory leaks from undetached events                                     | Symmetric `OnEnable` / `OnDisable` rule, applied uniformly.                                                 |
| Frame drops from undetected `Instantiate`                               | Profiler open periodically in week 1. Pool from day 3.                                                      |
| Unstable "on ground" detection                                          | Raycast with a platform-specific `LayerMask`.                                                               |
| Camera jitter                                                           | Parameterized `SmoothDamp`, not a child of the ball.                                                        |
| Broken determinism across machines                                      | Generation with `System.Random` seeded (not `UnityEngine.Random`).                                          |
| Freeze-frame coroutine leaves `Time.timeScale = 0` after unload         | `GameStateMachine.OnDisable` stops the coroutine and defensively restores `Time.timeScale = 1f`.            |
| `GameEventSO` with residual subscriptions after scene reload            | `OnEnable` / `OnDisable` on each listener guarantees cleanup. With a single scene there's no reload, no risk. |
| `int` score may overflow in eternal runs                                | `long` not needed for a realistic endless. If it happens, it's a good problem to have.                      |
| Misconfigured asmdefs leaking editor code into the player               | Manual test: Windows build with `ZigZag.Editor` set to `includePlatforms: [Editor]`.                        |

---

## 17. Code quality checklist (pre-delivery)

Before packaging the zip:

- [ ] Zero console warnings on opening the project.
- [ ] Zero errors running `S_Main`.
- [ ] All public C# events use the `event` keyword.
- [ ] All subscriptions have their matching unsubscription (grep `+=` and `-=`).
- [ ] Zero `Instantiate` / `Destroy` in gameplay methods (only `Awake` / `Start` / `GameBootstrap`).
- [ ] Zero `FindObjectOfType`, `GameObject.Find`, `GameObject.FindWithTag` in runtime loops.
- [ ] Zero magic numbers; all configurable values come from `GameConfigSO`.
- [ ] Naming consistent with the §3.1 table across the project.
- [ ] One class = one file, same name.
- [ ] 3-level namespaces applied consistently.
- [ ] `sealed` applied by default on concrete classes.
- [ ] `///` comments on public APIs of main classes.
- [ ] `SO_GameConfig.asset` present and populated.
- [ ] All `GameEventSO.asset`s created and referenced by their subscribers/emitters.
- [ ] No dead code, no leftover `Debug.Log`.
- [ ] **No `FIXME`, no `XXX`, no untagged notes.** Any deferral is `// TODO: <description> (<context>)` (`CLAUDE.md` §2.2).
- [ ] 5–10 EditMode/PlayMode tests passing.
- [ ] Editor asmdef with `includePlatforms: [Editor]` verified (doesn't enter the build).
- [ ] Windows build compiles without critical warnings.

---

## 18. What is NOT in this document (and where to find it)

- **Game design (mechanics, parameters, scope):** [`zigzag_gdd.en.md`](zigzag_gdd.en.md).
- **Project-wide rules:** [`CLAUDE.md`](CLAUDE.md).
- **Subagent catalog:** [`.claude/agents/AGENTS.md`](.claude/agents/AGENTS.md).
- **Concrete implementation:** the code.
- **Builds and Unity configuration:** `README.md` (TODO create).

---

## 19. How to use this document during development

1. **Before starting a day:** look at which class is up and review its section in §7.
2. **Before committing:** verify §3 (conventions) + §17 (checklist).
3. **When an architectural doubt comes up:** consult ADRs (§8) or add a new one if the decision is new.
4. **At the end of each week:** go through the full §17.
5. **If a rule in this document conflicts with `CLAUDE.md`:** `CLAUDE.md` wins and this document is updated.

### ADR-014 — Camera advances only on the global forward axis

**Decision:** `CameraFollow` projects the target's displacement onto `(-1, 0, 1)/√2` and applies only that component; the perpendicular is discarded. Y is locked to the camera's initial Y.

**Alternatives considered:**
- Track target X and Z independently (original implementation). Keeps the ball centered on screen; breaks the Ketchapp ZigZag feel where the camera only "rises" and the ball snakes.
- Track the full delta but with much stronger damping on the perpendicular. More complex to tune, approximately the same visual result.

**Justification:** GDD pillar #2 ("instant readability") and the reference game's feel require the player to perceive the ball's lateral oscillation as primary visual information. If the camera compensates for it, the oscillation stops being readable.

**Consequences:**
- The ball's accumulated lateral excursion becomes visible. If it exceeds the frustum width, `orthographicSize` must be tuned or `PathGenerator` biased to bound the drift. This calibration is an independent change axis and is addressed as tuning, not as new code.
- Resetting the ball at spawn produces a smooth scroll-back of the camera (same behavior as the previous implementation; not a regression). **Iter 10 update:** that scroll-back stops being smooth and becomes an instant snap to the origin via `CameraFollow.HandleGameReset` — a long run accumulates several units of forward progress and the next run's `SmoothDamp` produced a visible slingshot. Snap + `_smoothVelocity` reset eliminates it.
- Math extracted to `CameraFollowMath` for unit testing in EditMode, following the `ScoreCalculator` pattern.

### ADR-015 — `GameConfigSO.GlobalForward` as the single source of truth

**Decision:** the `Vector3 GlobalForward = new Vector3(-1, 0, 1).normalized` constant lives as a `public static readonly` field on `GameConfigSO`. `PathGenerator`, `CameraFollow` and `ScoreManager` read it from there instead of declaring their own copy.

**Alternatives considered:**
- Keep one local copy per consumer (state before iter 10). Zero coupling between modules, guaranteed drift the day someone retouches one without the others — the debt was explicitly registered in the iter 4.2 devlog.
- Promote it to a `static class GameConstants` in its own asmdef. Theoretically cleaner, but adds an asmdef for a single constant; `GameConfigSO` is already referenced by the entire gameplay layer.

**Justification:** the constant defines the path's geometry and appears as an argument to `ScoreCalculator.ComputeDistanceScore`, `CameraFollowMath.ComputeDesiredPosition`, and the math of `EnsureAhead`/`RecycleBehind`/`TriggerFalls`. A single source guarantees any change (unlikely, but conceivable — a 60°/120° rotated axis in some future level pack) lands in one place.

**Consequences:**
- Four sequential commits (`dc72c52` `93f34c7` `33c743b` `f460d21`) consolidate the migration. Small per-consumer diffs; the axis is byte-identical, all 24 EditMode tests pass without modification.
- Closes the explicit TODO from iter 4.2.
- Zero runtime cost: `static readonly Vector3` is initialized once at type load and referenced like any field; doesn't require a `GameConfigSO` instance.

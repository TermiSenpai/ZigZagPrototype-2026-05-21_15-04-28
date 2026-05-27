<div align="center">

# ZigZag Prototype

**Unity 2022.3 LTS · C# .NET Standard 2.1 · Built-in Render Pipeline**

A ZigZag-style arcade prototype built as a junior game developer technical test, engineered with a strict
encapsulation discipline, ScriptableObject-driven architecture, and a complete set of decision documents
(GDD, architecture ADRs, devlog) tracked alongside the code.

`v1.0` · mobile-portrait 608×1080 · 1 scene · 0 third-party packages

[English](#english) · [Castellano](#castellano) · Devlog ([EN](devlog.en.md) · [ES](devlog.md)) · GDD ([EN](zigzag_gdd.en.md) · [ES](zigzag_gdd.md)) · Architecture ([EN](zigzag_architecture.en.md) · [ES](zigzag_architecture.md))

</div>

---

<a name="english"></a>

## English

### Table of contents

1. [What is this](#en-overview)
2. [How it was built — Claude Code Opus 4.7](#en-claude)
3. [Tech stack & requirements](#en-stack)
4. [Project structure](#en-structure)
5. [How to run](#en-run)
6. [Architecture at a glance](#en-architecture)
7. [Design patterns (chosen before any code)](#en-patterns)
8. [Script catalog](#en-scripts)
9. [Event channels (data flow)](#en-events)
10. [Iteration roadmap](#en-iterations)
11. [Testing](#en-testing)
12. [References](#en-references)

---

<a name="en-overview"></a>

### 1. What is this

A vertical-slice clone of the classic Ketchapp ZigZag: the ball rolls forward along a procedural,
infinite path made of axis-aligned cubes. A single tap (or click, or `Space`) flips the ball between
two world axes (`-X` and `+Z`). Miss the path and the ball falls. The longer you stay on, the higher
the score; pick up gems to earn coins; spend coins in the shop on cosmetic ball skins.

**Goals of the codebase:**

- Demonstrate strict encapsulation (`private` fields, ScriptableObject configuration, event channels).
- Demonstrate independence between systems (no `FindObjectOfType`, no god `GameManager`, no global singletons).
- Demonstrate runtime hygiene (zero allocations in `Update`, pooling for everything that spawns, cached
  component lookups).
- Demonstrate that an LLM-assisted workflow can deliver production-quality code when paired with a
  written architecture and a hard rules document — see [§ 2](#en-claude).

---

<a name="en-claude"></a>

### 2. How it was built — Claude Code Opus 4.7

Every line of code, every ADR, every plan and every devlog entry was produced with
**[Claude Code](https://www.anthropic.com/claude-code)** running the **Claude Opus 4.7** model. The
workflow was designed around *compounding decisions*: each iteration writes down its plan, its trade-offs
and its known holes before any code is committed, and every later iteration can refer back to those
documents instead of rediscovering the context.

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Spec  ──►  Plan  ──►  Brainstorm  ──►  Code  ──►  Tests  ──►  Devlog      │
│   (gdd)     (plans/)    (chat)         (.cs)      (EditMode)    (devlog.md) │
│                                                                            │
│                                  ▲                                         │
│                                  │                                         │
│                          CLAUDE.md hard rules                              │
│                  (encapsulation, naming, perf, patterns)                   │
└────────────────────────────────────────────────────────────────────────────┘
```

| Phase                  | Artifact                                            | Location                        |
| ---------------------- | --------------------------------------------------- | ------------------------------- |
| Design                 | Game Design Document                                | [`zigzag_gdd.en.md`](zigzag_gdd.en.md) ([ES](zigzag_gdd.md)) |
| Architecture           | C4 + ADRs                                           | [`zigzag_architecture.en.md`](zigzag_architecture.en.md) ([ES](zigzag_architecture.md)) |
| Per-iteration spec     | Design of a feature before any code                 | `docs/superpowers/specs/`       |
| Per-iteration plan     | Step-by-step implementation plan with task checklist| `docs/superpowers/plans/`       |
| Project rules          | Mandatory rules the AI must respect                 | [`CLAUDE.md`](CLAUDE.md)        |
| Per-iteration devlog   | What was done, why, what's pending                  | [`devlog.en.md`](devlog.en.md) ([ES](devlog.md)) |

**Why Opus 4.7?** Opus 4.7 has the deepest reasoning in the Claude 4.X family, which is what this
project's discipline asks for: every decision is documented with its rejected alternatives, every script
is rejected if it doesn't fit the rules in [`CLAUDE.md`](CLAUDE.md). A model that "just writes code"
would have produced a faster but less defensible prototype.

> **About this README.** This README itself was also generated with **Claude Code Opus 4.7**, after a
> read-only analysis of the full repository: every C# script in `Assets/Code/`, the [`devlog.en.md`](devlog.en.md)
> history, the per-iteration plans and specs under `docs/superpowers/`, the `git log` and the asmdef
> graph. The model then synthesized the documentation you are reading — same pattern as every other
> artifact in the repo: *understand the source of truth first, then describe it*.

**The cycle for each feature was:**

1. Open a new chat with the *brainstorming* superpower → explore approaches.
2. Write a `spec` document → agreed interface + tradeoffs + future considerations.
3. Write a `plan` document → ordered tasks with code snippets per step.
4. Execute the plan with TDD where applicable (`ScoreCalculator`, `CameraFollowMath`, `CoinsWallet.TrySpend`,
   `SkinInventory.ParseOwnedCsv`, `PaletteSampler`).
5. Append a devlog entry describing decisions taken *during* implementation (not just before).

This way the repository is auditable: a reviewer can read the spec, the plan and the devlog, and the
final code is a known transformation of those.

---

<a name="en-stack"></a>

### 3. Tech stack & requirements

| Component        | Version / value                              |
| ---------------- | -------------------------------------------- |
| Unity Editor     | 2022.3.62f2 LTS                              |
| Render pipeline  | Built-in                                     |
| .NET / C#        | .NET Standard 2.1 / C# 9                     |
| Test framework   | Unity Test Framework (NUnit, EditMode only)  |
| External packages| None — only what 2022.3 LTS ships with       |
| Target platform  | PC standalone, mobile-portrait window (608×1080) |
| Input            | `UnityEngine.Input` (touch maps to mouse 0)  |
| Persistence      | `PlayerPrefs` (keys: `BestScore`, `Coins`, `OwnedSkins`, `EquippedSkin`) |

---

<a name="en-structure"></a>

### 4. Project structure

```
Assets/
├── Art/                              # Materials, sprites
├── Audio/                            # 3 SFX + 1 music track
├── Code/
│   ├── Runtime/
│   │   ├── Audio/        ── ZigZag.Runtime.Audio.asmdef        (refs: Events)
│   │   ├── Core/         ── ZigZag.Runtime.Core.asmdef         (refs: Events, Data, Input, Gameplay)
│   │   ├── Data/         ── ZigZag.Runtime.Data.asmdef         (no refs)
│   │   ├── Events/       ── ZigZag.Runtime.Events.asmdef       (no refs)
│   │   ├── Gameplay/     ── ZigZag.Runtime.Gameplay.asmdef     (refs: Data, Events)
│   │   │   ├── Aesthetics/   (palette cycling)
│   │   │   ├── CameraSystem/ (camera follow)
│   │   │   ├── Collectibles/ (gems)
│   │   │   ├── Cosmetics/    (ball skins)
│   │   │   ├── Economy/      (coins wallet)
│   │   │   ├── Player/       (ball controller)
│   │   │   ├── Scoring/      (score)
│   │   │   └── World/        (path generation + pool + faller)
│   │   ├── Input/        ── ZigZag.Runtime.Input.asmdef        (refs: Events)
│   │   └── UI/           ── ZigZag.Runtime.UI.asmdef           (refs: Events, Gameplay, Unity.TextMeshPro)
│   └── Tests/
│       └── EditMode/     ── ZigZag.Tests.EditMode.asmdef       (refs: all of the above)
├── Prefabs/                          # P_Ball, P_PlatformCube, P_Gem, P_ShopRow
├── Scenes/                           # SampleScene.unity
└── Settings/                         # SO_GameConfig, SO_PaletteRules, SO_*Event, SO_Skin_*, SO_BallSkinCatalog
```

**Assembly definitions form a DAG with strictly downward dependencies** (UI → Gameplay → Core → Data/Events).
This is enforced by the asmdef references; a cycle is a compile error. The architecture document tracks the
exact graph in `zigzag_architecture.en.md §5`.

---

<a name="en-run"></a>

### 5. How to run

**From the Unity Editor:**

1. Clone the repository.
2. Open the project with Unity Hub. The first import takes ~2 minutes.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Press **Play**. Tap / click / `Space` to start. Tap again to flip direction.

**From a Windows build:**

1. `File → Build And Run` (the scene is already in `Build Settings`, version `1.0`, 608×1080 portrait).

**Run the EditMode tests:**

1. `Window → General → Test Runner → EditMode → Run All`. 24 tests should pass.

---

<a name="en-architecture"></a>

### 6. Architecture at a glance

The game is a graph of independent systems that communicate through **ScriptableObject event channels**.
Nobody holds a hard reference to anybody they don't strictly need.

```
              ┌──────────────┐
              │ InputHandler │
              └───────┬──────┘
                     OnTapped (C# event)
                      │
                      ▼
              ┌────────────────────┐         SO_OnGameStarted ───────────┐
              │ GameStateMachine   │─────────SO_OnGameOver ─────────────┐│
              │  Menu/Playing/Over │─────────SO_OnGameReset ───────────┐││
              └─┬─────┬─────┬──────┘                                   │││
                │     │     │                                          │││
       FlipDir  │     │     │ StartMoving/StopMoving/ResetTo            │││
                ▼     ▼     ▼                                          │││
              ┌──────────────┐                                         │││
              │ BallControl. │── OnFell ─►(state machine listens)      │││
              └──────┬───────┘                                         │││
                     │ raises SO_OnDirectionChanged                    │││
                     │                                                 │││
                     │                ┌──────────────┐ ◄───────────────┘││
                     ├───listens─────►│ PathGenerator│◄────reset ────────┘│
                     │  ballPos        └──────────────┘◄────game over ───┘
                     │
                     ▼
              ┌──────────────┐                          ┌──────────────┐
              │ CameraFollow │                          │ PaletteCtrl. │
              └──────────────┘                          └──────┬───────┘
                                                               │ SO_OnScoreChanged
                                                               │ (IntGameEventSO)
                                                               │
   Gem.OnTrigger ──► SO_OnGemCollected (int) ──► CoinsWallet ──┘─►SO_OnCoinsChanged
                                                  │
                                                  └──► ScoreManager (distance) ──► SO_OnScoreChanged
                                                                                 ──► SO_OnBestScoreChanged

   UI side (one-way listen, never drives):
       UIController, AudioManager, BallSkinApplier, PaletteController, ShopPanel
       — all only subscribe to channels; none of them feed gameplay logic.
```

**Key invariants** (full list in `zigzag_architecture.en.md`):

- The ball is **kinematic** (ADR-001): movement is `transform.position +=`, fall is a hand-rolled
  downward velocity. No `Rigidbody` on the ball.
- Pools (`UnityEngine.Pool.ObjectPool<T>`) own everything spawned at runtime. **Zero `Instantiate`/`Destroy`
  after `Awake`** (ADR-002).
- Generation determinism is opt-in: `GameConfigSO.GenerationSeed = 0` → fresh seed every run;
  any other value → same path every time (debugging only).
- The camera moves **only along the global forward axis** `(-1, 0, 1)/√2` (ADR-014). The ball serpentines
  laterally over it.

---

<a name="en-patterns"></a>

### 7. Design patterns (chosen before any code)

The pattern catalog was **decided up-front**, before the first `.cs` was committed. The chronological
order of the design phase was:

```
   1.  GDD (zigzag_gdd.md)              ─── what the game must do
                │
                ▼
   2.  Architecture (zigzag_architecture.md)  ─── how it must be structured
                │
                ▼
   3.  CLAUDE.md §6 — mandatory pattern catalog  ─── which patterns to use, which to reject
                │
                ▼
   4.  First commit of code  ─── every iteration since then maps cleanly into the catalog
```

Once code started, each iteration's spec opened with "which existing patterns from the catalog does this
feature use?" If a new pattern was needed, the spec had to justify it before any code was written. This
is why the codebase has zero ad-hoc managers, zero singletons, and zero god objects — they were ruled out
before they could be invented.

The patterns the prototype actually relies on (and where each one lives):

| Pattern | Where it lives | Why this pattern |
|---------|----------------|------------------|
| **ScriptableObject as data container** | [`GameConfigSO`](Assets/Code/Runtime/Data/GameConfigSO.cs), [`PaletteRulesSO`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteRulesSO.cs), [`BallSkinSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs), [`BallSkinCatalogSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs) | Tunable values live in assets, not code. `[SerializeField] private` + `get`-only properties enforce encapsulation. Designers can rebalance without recompiling. |
| **Event Channel (SO pub-sub)** | 15 `SO_*` assets under `Assets/Settings/Events/`, base types in [`Events/`](Assets/Code/Runtime/Events/) | Decouples emitters from receivers — each side only knows the asset. No `FindObjectOfType`, no singletons. New listeners are zero-code. |
| **Observer (native C# events)** | [`BallController.OnDirectionChanged`/`OnFell`](Assets/Code/Runtime/Gameplay/Player/BallController.cs), [`InputHandler.OnTapped`](Assets/Code/Runtime/Input/InputHandler.cs) | Local variant of pub-sub: used when the emitter and the listener live in the same assembly and an SO asset would be ceremonial. Symmetric `Register`/`Unregister` in `OnEnable`/`OnDisable`. |
| **Finite State Machine (FSM)** | [`GameStateMachine`](Assets/Code/Runtime/Core/GameStateMachine.cs) with `enum GameState { Menu, Playing, GameOver }` | Three explicit states, explicit transitions, no hidden flags. Hand-rolled — a framework would add weight without value at this scale. |
| **Object Pool** | [`PlatformPool`](Assets/Code/Runtime/Gameplay/World/PlatformPool.cs), [`GemPool`](Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs) via `UnityEngine.Pool.ObjectPool<T>` | Zero `Instantiate`/`Destroy` in hot paths. Prewarming in `Awake`. Cubes recycle invisibly behind the ball. ADR-002. |
| **Template Method (generic abstract)** | [`GameEventSO<T>`](Assets/Code/Runtime/Events/GameEventSO.cs) → [`IntGameEventSO`](Assets/Code/Runtime/Events/IntGameEventSO.cs), [`StringGameEventSO`](Assets/Code/Runtime/Events/StringGameEventSO.cs) | One implementation of `Register`/`Unregister`/`Raise`; concrete payload types just declare `T`. Adding `Vector3GameEventSO` is one line. |
| **Strategy (data-driven)** | [`BallSkinSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs) swaps a `Material`, [`PaletteRulesSO`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteRulesSO.cs) swaps HSV ranges | The "strategy" is picked by dragging another asset, not by instantiating another class. Pure editorial workflow. |
| **Catalog (lookup repository)** | [`BallSkinCatalogSO.GetById(string)`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs) | Centralized lookup over an ordered array (display order = shop order). No runtime `Dictionary` allocation. |
| **Composition Root / Bootstrap** | [`GameBootstrap`](Assets/Code/Runtime/Core/GameBootstrap.cs) with `[DefaultExecutionOrder(-1000)]` | Single place where every serialized ref is `Debug.Assert`-validated. Composition itself lives in the scene; the bootstrap only screams if something's missing before frame 1. |
| **MVP-lite (View ⇄ Presenter ⇄ Model)** | View: [`UIController`](Assets/Code/Runtime/UI/UIController.cs), [`ShopPanel`](Assets/Code/Runtime/UI/Shop/ShopPanel.cs). Presenter: SO channels. Model: [`ScoreManager`](Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs), [`CoinsWallet`](Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs), [`SkinInventory`](Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs) | View only reads channels and refreshes widgets; never holds business logic. Model never references the View — it pushes through channels. |
| **Composition over inheritance** | Every `MonoBehaviour` does one thing; collaborators arrive via `[SerializeField]`. The ball does **not** extend anything; the state machine does **not** extend anything. | Inheritance only for true `is-a`. Arcade gameplay rarely meets that bar. |
| **Pure helpers (no state)** | [`ScoreCalculator`](Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs), [`CameraFollowMath`](Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs), [`PaletteSampler`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteSampler.cs) | `static class`, no Unity lifecycle. Testable in EditMode without mocks. Separates arithmetic from side-effects (raises, `PlayerPrefs`). |

**Anti-patterns explicitly rejected up-front** (full justification in [`CLAUDE.md`](CLAUDE.md) §6):

- God `GameManager`
- `static` mutable state
- Singletons via `Instance ??= this`
- `SendMessage` / `BroadcastMessage`
- `FindObjectOfType` / `GameObject.Find` outside bootstrap or editor code
- `Resources.Load` in hot paths
- Business logic in editor scripts

Reading the iteration plans in `docs/superpowers/plans/` confirms this discipline: each plan opens with
the existing patterns the feature reuses, and a *Decisions* section listing the rejected alternatives —
that "shop is a catalog of SOs, not an enum" or "music is a second AudioSource on the camera, not a new
MonoBehaviour" are the kind of pattern-level decisions that were made before any code was written.

---

<a name="en-scripts"></a>

### 8. Script catalog

Each script is listed by its **layer**, with the file path and a one-paragraph description of how to use
it. The shape of every `MonoBehaviour` and `ScriptableObject` is dictated by [`CLAUDE.md`](CLAUDE.md) — read
that file once to understand the conventions.

#### 7.1 — `Data/` (configuration)

| Script | Role |
|--------|------|
| [`GameConfigSO`](Assets/Code/Runtime/Data/GameConfigSO.cs) | Single source of truth for every tunable gameplay value: speed, fall, ground check, path generation, gem probability, score multiplier, freeze-frame duration, platform-fall threshold, pool sizes. Asset: `SO_GameConfig.asset`. **All fields private + serialized, all properties get-only**. `OnValidate` clamps every numeric to safe bounds. |

**How to use:** drag `SO_GameConfig.asset` into the matching slot of any component that needs tuning.
Never modify it from code — that would defeat the encapsulation. To rebalance, edit the asset in the
Inspector.

#### 7.2 — `Events/` (cross-system communication)

| Script | Role |
|--------|------|
| [`GameEventSO`](Assets/Code/Runtime/Events/GameEventSO.cs) | Parameterless SO event channel. Listeners `Register(Action)` in `OnEnable` and `Unregister(Action)` in `OnDisable`. Raisers call `Raise()`. |
| [`GameEventSO<T>`](Assets/Code/Runtime/Events/GameEventSO.cs) | Generic abstract typed channel. Subclasses define concrete payload types. |
| [`IntGameEventSO`](Assets/Code/Runtime/Events/IntGameEventSO.cs) | `: GameEventSO<int>`. Used for score, coins, gem value. |
| [`StringGameEventSO`](Assets/Code/Runtime/Events/StringGameEventSO.cs) | `: GameEventSO<string>`. Used for skin id transport. |

**How to use:** to add a new event, create an asset (`Right-click → Create → ZigZag → Events → Game Event`),
drag it into the inspector of both the raiser and the listener. No code change required to wire a new pair.

```
   Raiser side                   Listener side
   ─────────────                 ─────────────
   [SerializeField]              [SerializeField]
   private GameEventSO            private GameEventSO
        _onSomething;                  _onSomething;

   void Whenever()                void OnEnable() => _onSomething.Register(Handle);
   { _onSomething.Raise(); }      void OnDisable() => _onSomething.Unregister(Handle);

                                  void Handle() { /* ... */ }
```

#### 7.3 — `Input/`

| Script | Role |
|--------|------|
| [`InputHandler`](Assets/Code/Runtime/Input/InputHandler.cs) | Thin wrapper over `UnityEngine.Input`. Fires `OnTapped` (C# event) on left mouse click, `Space`, or first touch (Unity maps touch 0 → mouse 0 automatically). Two suppression mechanisms: `SO_OnShopOpened/Closed` blocks all input while the shop is up; per-click `EventSystem.IsPointerOverGameObject()` ignores taps that hit a UI raycast target. |

**How to use:** attach to a root GameObject named `InputHandler`. Wire the shop channels in the inspector
(optional — `null`-safe). Other systems subscribe to `OnTapped` via C# event (only the state machine does
this in the prototype).

#### 7.4 — `Core/` (game flow)

| Script | Role |
|--------|------|
| [`GameState`](Assets/Code/Runtime/Core/GameState.cs) | `enum GameState { Menu, Playing, GameOver }`. |
| [`GameStateMachine`](Assets/Code/Runtime/Core/GameStateMachine.cs) | The brain. Routes `InputHandler.OnTapped` according to current state (Menu→Start, Playing→Flip, GameOver→ignore). Listens to `BallController.OnFell` and triggers the *freeze-frame on death* coroutine: `Time.timeScale = 0` for `GameConfigSO.FreezeFrameOnDeathSeconds` real-time seconds, then raises `SO_OnGameOver`. Listens to `SO_OnRetryRequested` and resets ball + raises `SO_OnGameReset`. |
| [`GameBootstrap`](Assets/Code/Runtime/Core/GameBootstrap.cs) | `[DefaultExecutionOrder(-1000)]`. Asserts every wired dependency in `Awake` so a missing reference shows up before the first frame instead of as a runtime `NullReferenceException`. No service location, no instantiation — just validation. |

**How to use:** put one `GameStateMachine` in the scene, wire all serialized refs (input, ball, ball spawn,
config, the 4 lifecycle SO channels), and a `GameBootstrap` next to it with refs to every system you want
asserted.

#### 7.5 — `Gameplay/Player/`

| Script | Role |
|--------|------|
| [`BallController`](Assets/Code/Runtime/Gameplay/Player/BallController.cs) | The ball. Constant-speed motion along one of two world axes (`-X` or `+Z`), `Physics.Raycast` ground check, hand-rolled fall, no-slip visual rolling (`ω = v/r`), idempotent `StartMoving`/`StopMoving`/`ResetTo(position)`/`FlipDirection()`. Raises three C# events — `OnDirectionChanged(Vector3)`, `OnFell`, `OnReset` (fired inside `ResetTo`, consumed by `BallTrailColorizer` to clear the trail on respawn) — plus an optional `SO_OnDirectionChanged` channel for presentation (audio). |
| [`BallDeathBurst`](Assets/Code/Runtime/Gameplay/Player/BallDeathBurst.cs) | Builds a procedural one-shot `ParticleSystem` child in `Awake` (sphere shape, world-space, 36 particles, lifetime 0.65 s) and subscribes to `BallController.OnFell`. Snaps the burst host to the impact point before `Play(true)` so the burst stays anchored where the ball left the path, not where it ends up after the freeze-frame. Also owns the ball's visibility around the death event: disables the sibling `MeshRenderer` on fall so the dead ball doesn't sit visible under the Game Over UI, and re-enables it on `BallController.OnReset` for respawn (the GameObject stays active so every subscription on the ball survives the cycle). Optional skin sync via `_catalog` + `_onSkinEquipped` slots — `null`-safe; if left empty, the inspector-authored `_burstColor` (white→orange) is used. |

**How to use:** attach to the ball GameObject (Unity Sphere primitive at scale 1). Drag `SO_GameConfig`
and the SO direction channel into its slots. The state machine drives the lifecycle — the ball never
listens to input directly.

#### 7.6 — `Gameplay/World/` (path generation + recycling + collapse)

| Script | Role |
|--------|------|
| [`Segment`](Assets/Code/Runtime/Gameplay/World/Segment.cs) | Pure C#. A run of cubes along one direction; exposes `IReadOnlyList<GameObject> Cubes`. Carries `FallTriggerIndex`, a monotonic watermark used by `PathGenerator.TriggerFalls`. |
| [`PlatformPool`](Assets/Code/Runtime/Gameplay/World/PlatformPool.cs) | `UnityEngine.Pool.ObjectPool<GameObject>` wrapper. Prewarms `GameConfigSO.PlatformPoolInitialSize` cubes in `Awake`. Owns a runtime-instanced `RuntimeMaterial` (clone of the prefab's material) so the `PaletteController` can recolor every active cube with a single `SetColor`. Material is destroyed in `OnDestroy`. |
| [`PathGenerator`](Assets/Code/Runtime/Gameplay/World/PathGenerator.cs) | Spawns and recycles segments around the ball. `EnsureAhead`: spawns cubes until the path covers `GameConfigSO.AheadBuffer` (measured by `Vector3.Dot(lastCube - ball, GlobalForward)`). `RecycleBehind`: releases the oldest segment once its last cube is beyond `BehindBuffer`. `TriggerFalls`: calls `PlatformFaller.Begin()` on cubes that crossed `PlatformFallStartBehind`. Uses `System.Random` seeded from `GameConfigSO.GenerationSeed` for determinism. Picks a random start direction per run (mirror pair so the score baseline is unaffected) and applies asymmetric drift caps so the path naturally zigzags wider on the leading side. |
| [`PlatformFaller`](Assets/Code/Runtime/Gameplay/World/PlatformFaller.cs) | One per cube. Hand-rolled gravity integration after `Begin()` is called. Resets state in `OnDisable` (the pool deactivates the cube before reuse). `MaxFallDistance = 60` caps integration so an orphaned cube doesn't fall forever. |

**How to use:** place a `PlatformPool` and a `PathGenerator` GameObject in the scene, give the pool a
prefab (`P_PlatformCube`), give the generator the pool + ball transform + lifecycle channels.
`PlatformFaller` is a component on the prefab itself (default `_gravity = 18`).

#### 7.7 — `Gameplay/Collectibles/`

| Script | Role |
|--------|------|
| [`Gem`](Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs) | `[RequireComponent(Collider, Rigidbody)]` (Rigidbody is kinematic — Unity 2022 needs at least one side of a trigger to have a Rigidbody). `OnTriggerEnter` with the ball → raises `SO_OnGemCollected(value)` + returns itself to the pool. `Initialize(value, pool)` is called by the spawner on every `Get()`. |
| [`GemPool`](Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs) | Twin of `PlatformPool`. Prewarms `GemConfigSO.GemPoolInitialSize` instances. |
| [`GemSpawner`](Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs) | Called from `PathGenerator` when a segment is finalized. Random dice against `GemSpawnProbability`; if it passes, picks a random cube in the segment, parents a gem above its center at `GemHeightAboveCubeCenter`. `ReleaseGemsBehind(ballPos, GlobalForward, BehindBuffer)` sweeps stranded gems once per frame so the pool doesn't grow unbounded. |

#### 7.8 — `Gameplay/Economy/`

| Script | Role |
|--------|------|
| [`CoinsWallet`](Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs) | Sole owner of `PlayerPrefs["Coins"]`. Listens to `SO_OnGemCollected(int)` (adds to wallet + session); listens to `SO_OnGameReset` (resets session, wallet intact). `TrySpend(int)` is the public spending API used by `SkinInventory`: returns `false` on non-positive amount or insufficient funds, otherwise deducts, persists immediately and raises `SO_OnCoinsChanged`. |

#### 7.9 — `Gameplay/Scoring/`

| Script | Role |
|--------|------|
| [`ScoreCalculator`](Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs) | Pure `static class`. `ComputeDistanceScore(ballPos, origin, forwardAxis, multiplier)` projects displacement onto `(-1,0,1)/√2` and returns `Mathf.FloorToInt(progress) * multiplier`, clamped to ≥ 0. Covered by 7 EditMode tests. |
| [`ScoreManager`](Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs) | `MonoBehaviour`. Recomputes `_distanceScore` in `Update`, raises `SO_OnScoreChanged` only when the integer crosses a new value (not every frame). Persists `BestScore` in `PlayerPrefs` on `SO_OnGameOver`, raises `SO_OnBestScoreChanged` if it improved. |

#### 7.10 — `Gameplay/CameraSystem/`

| Script | Role |
|--------|------|
| [`CameraFollowMath`](Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs) | Pure static helper. `ComputeDesiredPosition(...)` projects the target's delta onto `GlobalForward` and returns the desired camera position with `Y` locked. Covered by 7 EditMode tests. |
| [`CameraFollow`](Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs) | `MonoBehaviour`. `LateUpdate` calls into the math helper, then `Vector3.SmoothDamp` toward the result with `GameConfigSO.CameraFollowSmoothTime`. Subscribes to `SO_OnGameReset` and snaps the transform back to the captured origin (resetting `_smoothVelocity`) so a long run does not slingshot the view back over many world units when the player retries. |

#### 7.11 — `Gameplay/Cosmetics/`

| Script | Role |
|--------|------|
| [`BallSkinSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs) | Per-skin data: `Id` (stable, never rename), `DisplayName`, `Price`, `Material`. |
| [`BallSkinCatalogSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs) | Ordered list. First entry is the default (`Price = 0`, always owned). `GetById(string)` for lookups. `OnValidate` checks uniqueness and the price-0 invariant. |
| [`SkinInventory`](Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs) | Sole owner of `PlayerPrefs["OwnedSkins"]` (CSV) and `["EquippedSkin"]` (id). Brokers purchase (validates funds via `CoinsWallet.TrySpend`, auto-equips on success) and equip requests raised by the shop. Broadcasts `SO_OnSkinEquipped` (string) + `SO_OnInventoryChanged` (parameterless) on every mutation. |
| [`BallSkinApplier`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs) | Lives on the ball. Listens to `SO_OnSkinEquipped`, resolves the skin via the catalog, swaps `MeshRenderer.sharedMaterial`. Uses `sharedMaterial` deliberately (not `.material`, which would heap-alloc and break batching). |
| [`BallTrailColorizer`](Assets/Code/Runtime/Gameplay/Cosmetics/BallTrailColorizer.cs) | Owns the look of the ball's `TrailRenderer`: assigns a guaranteed-valid runtime material with a shader-fallback cascade (avoids the magenta `Hidden/InternalErrorShader` placeholder), applies authored width / time / vertex-distance defaults from `[SerializeField, Range]` fields (avoids the oversized-trail trap when the inspector's curve gets nudged), and keeps `startColor` / `endColor` aligned with the equipped `BallSkinSO` by listening to `SO_OnSkinEquipped`. Also listens to `BallController.OnReset` and calls `_trail.Clear()` so the death-fall trail doesn't linger across respawn. |

#### 7.12 — `Gameplay/Aesthetics/`

| Script | Role |
|--------|------|
| [`PaletteRulesSO`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteRulesSO.cs) | Configuration for palette cycling: threshold step (score), transition duration, HSV ranges, min-hue distance from previous, initial colors, shader property name (default `_Color` for Built-in, `_BaseColor` for URP — change here without touching code). |
| [`PaletteSampler`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteSampler.cs) | Pure static helper. `Sample(rng, rules, previousHue)` returns a `(platformColor, cameraColor, primaryHue)` tuple where camera uses the complementary hue. Up to 8 retries to respect min-hue-distance; graceful degradation on the last sample. Covered by EditMode tests. |
| [`PaletteController`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteController.cs) | Listens to `SO_OnScoreChanged`. Every `ScoreThresholdStep` points crossed, lerps `PlatformPool.RuntimeMaterial._Color` and `Camera.backgroundColor` to a fresh complementary pair over `TransitionSeconds`. Cancels in-flight transitions; snaps back to initial palette on `SO_OnGameReset`. Deterministic with `GameConfigSO.GenerationSeed != 0`. |

#### 7.13 — `UI/`

| Script | Role |
|--------|------|
| [`UIController`](Assets/Code/Runtime/UI/UIController.cs) | Owns the three game panels (Menu, HUD, GameOver) and toggles them based on lifecycle SO channels. Updates TMP texts on `SO_OnScoreChanged`, `SO_OnBestScoreChanged`, `SO_OnCoinsChanged`, `SO_OnSessionCoinsChanged`. The HUD score animates with a count-up — speed is auto-derived as `gap / _hudScoreCatchUpDuration` per change, runs on `Time.unscaledDeltaTime` so it survives the death freeze-frame, and snaps down on reset. The GameOver score jumps to the final value with no animation. Shows a `_newRecordBadge` if the run beat the best score. `OnShopButtonClicked()` is wired to the SHOP button and calls `_shopPanel.OpenShop()`. Listens to `SO_OnShopOpened` / `SO_OnShopClosed` to hide/restore the Menu panel while the shop overlay is up. |
| [`Shop/ShopRowView`](Assets/Code/Runtime/UI/Shop/ShopRowView.cs) | Pure presentation for one shop row. `Bind(skin)` sets the static fields once; `Refresh(owned, equipped, canAfford)` swaps the button label between `BUY {price}`, `EQUIP`, `EQUIPPED` and toggles `interactable`. Raises `SO_OnSkinPurchaseRequested` or `SO_OnSkinEquipRequested` on click, with the skin id as payload. |
| [`Shop/ShopPanel`](Assets/Code/Runtime/UI/Shop/ShopPanel.cs) | Builds one `ShopRowView` per catalog entry on `Start` inside a `VerticalLayoutGroup`. `OpenShop()` activates the root + raises `SO_OnShopOpened`; `CloseShop()` does the reverse. Refreshes all rows on `SO_OnInventoryChanged` or `SO_OnCoinsChanged`. |

#### 7.14 — `Audio/`

| Script | Role |
|--------|------|
| [`AudioManager`](Assets/Code/Runtime/Audio/AudioManager.cs) | `[RequireComponent(AudioSource)]`. Subscribes to `SO_OnDirectionChanged` (parameterless), `SO_OnGemCollected` (int payload ignored), `SO_OnGameOver`. Each handler is `_audioSource.PlayOneShot(clip, volume)`. Volumes are per-clip serialized fields, not in `GameConfigSO` (mixing gain, not game design). `null` clips → silent no-op (lets you test wiring before the assets exist). |

**Background music.** The looping music track (`Assets/Audio/music.mp3`) is **not** driven by code. It
lives as a second `AudioSource` component on the `Main Camera` GameObject, configured directly in the
Inspector: `Play On Awake = true`, `Loop = true`, `Volume = 0.036` (low on purpose so SFX stay
perceptually dominant), 2D (no spatialization). Keeping music separate from `AudioManager`'s
`PlayOneShot` source means SFX can never interrupt the track, and the volume balance is a one-field
inspector edit rather than a code change. To add a fade-in / fade-out on game over, introduce a minimal
`MusicController` that listens to `SO_OnGameOver` / `SO_OnGameReset` and ramps `AudioSource.volume` — not
done yet (YAGNI).

#### 7.15 — Tests (`Assets/Code/Tests/EditMode/`)

| Test fixture | Covers |
|--------------|--------|
| `ScoreCalculatorTests` | Distance projection: zero progress, +Z only, -X only, diagonal, backward clamp, multiplier, multiplier-zero. |
| `CameraFollowMathTests` | Camera math: static target, +Z motion, -X motion, perpendicular = zero, diagonal, Y-drop ignored, `lockedY` overrides. |
| `CoinsWalletTests` | `TrySpend`: success path, insufficient funds, non-positive amount. |
| `SkinInventoryTests` | `ParseOwnedCsv`: all-known ids, unknowns dropped, whitespace ignored, empty/null returns empty set. |
| `PaletteSamplerTests` | Complement hue arithmetic, circular distance, min-hue-distance respected, range sampling. |

---

<a name="en-events"></a>

### 9. Event channels (data flow)

Every event channel is a `ScriptableObject` asset under `Assets/Settings/Events/`. The same asset is dragged
into the inspector slot of every system that interacts with it — there is no code coupling between sender
and receiver.

| Channel | Type | Raised by | Listened by |
|---------|------|-----------|-------------|
| `SO_OnGameStarted` | `GameEventSO` | `GameStateMachine` | `PathGenerator`, `PaletteController`, `UIController` |
| `SO_OnGameOver` | `GameEventSO` | `GameStateMachine` (after freeze-frame) | `PathGenerator`, `ScoreManager`, `UIController`, `AudioManager` |
| `SO_OnGameReset` | `GameEventSO` | `GameStateMachine` | `PathGenerator`, `CoinsWallet`, `ScoreManager`, `PaletteController`, `UIController` |
| `SO_OnRetryRequested` | `GameEventSO` | `UIController` (button) | `GameStateMachine` |
| `SO_OnGemCollected` | `IntGameEventSO` | `Gem.OnTriggerEnter` | `CoinsWallet`, `AudioManager` |
| `SO_OnCoinsChanged` | `IntGameEventSO` | `CoinsWallet` | `UIController`, `ShopPanel` |
| `SO_OnSessionCoinsChanged` | `IntGameEventSO` | `CoinsWallet` | `UIController` |
| `SO_OnScoreChanged` | `IntGameEventSO` | `ScoreManager` | `UIController`, `PaletteController` |
| `SO_OnBestScoreChanged` | `IntGameEventSO` | `ScoreManager` | `UIController` |
| `SO_OnDirectionChanged` | `GameEventSO` | `BallController.FlipDirection` | `AudioManager` |
| `SO_OnSkinPurchaseRequested` | `StringGameEventSO` | `ShopRowView` | `SkinInventory` |
| `SO_OnSkinEquipRequested` | `StringGameEventSO` | `ShopRowView` | `SkinInventory` |
| `SO_OnSkinEquipped` | `StringGameEventSO` | `SkinInventory` | `BallSkinApplier`, `ShopPanel` |
| `SO_OnInventoryChanged` | `GameEventSO` | `SkinInventory` | `ShopPanel` |
| `SO_OnShopOpened` / `SO_OnShopClosed` | `GameEventSO` | `ShopPanel` | `InputHandler`, `UIController` (hides the Menu panel while the shop overlay is up) |

---

<a name="en-iterations"></a>

### 10. Iteration roadmap

The full devlog is at [`devlog.en.md`](devlog.en.md). One paragraph per iteration:

| # | Date | Iteration | Outcome |
|---|------|-----------|---------|
| 1 | 2026-05-21 | **Movement baseline** | Ball + Input + Config. The ball moves on `transform.position`, raycast ground check, hand-rolled fall. |
| 1.5 | 2026-05-22 | **Layout addendum** | Code moves from `Assets/_Project/` to `Assets/Code/` directly. |
| 2 | 2026-05-22 | **Game loop** | `GameEventSO`, `GameStateMachine`, `UIController`, Menu → Playing → GameOver → Retry. Direction model rebased to world axes (-X / +Z). |
| 3 | 2026-05-22 | **Procedural path + pool** | `PathGenerator` + `PlatformPool` using `UnityEngine.Pool.ObjectPool<T>`, deterministic `System.Random`, ahead/behind buffers measured along `GlobalForward`. |
| 4 | 2026-05-22 | **Gems + score + persistence** | `Gem`/`GemPool`/`GemSpawner`, `ScoreCalculator` (pure + tested), `ScoreManager` with `PlayerPrefs` best-score, HUD wired. |
| 4.1 | 2026-05-23 | **Wallet split** | `CoinsWallet` becomes the persistent currency; `ScoreManager` is now distance-only. Foundation for the shop. |
| 4.2 | 2026-05-24 | **Forward-only camera** | `CameraFollowMath` (pure + tested) + `CameraFollow` refactor. The camera advances only along `GlobalForward`; the ball serpentines over it. ADR-014. |
| 5 | 2026-05-25 | **Shop + ball skins** | Catalog of `BallSkinSO`, `SkinInventory` with PlayerPrefs persistence, `BallSkinApplier`, `ShopPanel` overlay, `CoinsWallet.TrySpend`. Replaces the descoped magnet powerup. |
| 6 | 2026-05-25 | **Audio + freeze-frame + rolling** | New `Audio` asmdef + `AudioManager`. `Time.timeScale = 0` for 0.1s real-time on death before the GameOver panel appears. No-slip visual rolling on the ball (`ω = v/r`). |
| 7 | 2026-05-25 | **Palette cycling** | `PaletteRulesSO` + `PaletteSampler` + `PaletteController`. Every 50 score points the platform and camera colors lerp to a fresh complementary pair. |
| 8 | 2026-05-26 | **Final polish** | `PlatformFaller` (passed platforms collapse), mobile-portrait 608×1080 build config, version `0.9`, audio assets imported, `_distanceMultiplier` rebalanced 3→1, looping background music wired as a self-contained second `AudioSource` on the `Main Camera` (no code). |
| 9 | 2026-05-27 | **Gem feedback** | Procedural particle burst built in code on gem pickup (world-space, shared static material, zero new assets); `GemSpawner` tracks each gem's supporting cube and drives its Y in `LateUpdate` so gems fall in sync with collapsing platforms instead of hovering. |
| 10 | 2026-05-27 | **Final polish (trail + death burst + GlobalForward)** | Native `TrailRenderer` on the ball with `BallTrailColorizer` (owns runtime material + width to defeat the magenta/oversized-trail trap, tints to the equipped skin, clears on respawn); procedural `BallDeathBurst` particle system at the impact point (mirror of `Gem` burst, optional skin sync, hides the ball `MeshRenderer` on death and restores it on respawn so the dead ball doesn't sit visible under the Game Over UI); `GameConfigSO.GlobalForward` consolidated as single source of truth (closes the iter 4.2 debt); `CameraFollow` snaps back to origin on `SO_OnGameReset`; HUD score now animates with a multiplier-agnostic count-up using `Time.unscaledDeltaTime` (survives the freeze-frame); shop overlay hides the Menu panel while open. Bundle version bumped `0.9 → 1.0.0`. |

---

<a name="en-testing"></a>

### 11. Testing

The repository ships with **24 EditMode tests** across 5 fixtures, covering the pure functions that are
the hardest to validate by hand. PlayMode tests were deliberately skipped — the prototype's runtime
behavior is verified by playing it, and writing PlayMode tests would have crowded out feature work in a
two-week window.

```
Window → General → Test Runner → EditMode → Run All
```

All tests are AAA-structured, one logical assertion per test, file paths mirror runtime structure.

---

<a name="en-references"></a>

### 12. References

| Topic                              | Path                                                          |
| ---------------------------------- | ------------------------------------------------------------- |
| Project rules (mandatory)          | [`CLAUDE.md`](CLAUDE.md)                                       |
| Game design document               | [`zigzag_gdd.en.md`](zigzag_gdd.en.md) ([ES](zigzag_gdd.md))   |
| Architecture (C4 + ADRs)           | [`zigzag_architecture.en.md`](zigzag_architecture.en.md) ([ES](zigzag_architecture.md)) |
| Per-iteration specs                | [`docs/superpowers/specs/`](docs/superpowers/specs/)           |
| Per-iteration plans                | [`docs/superpowers/plans/`](docs/superpowers/plans/)           |
| Devlog                             | [`devlog.en.md`](devlog.en.md) ([ES](devlog.md))               |
| Agent prompts (Claude subagents)   | [`.claude/agents/`](.claude/agents/)                           |
| Original Junior Game Developer brief | [`Junior Game Developer - Technical Test.pdf`](Junior%20Game%20Developer%20-%20Technical%20Test.pdf) |

**External references** (read while building):

- [Unity 2022.3 LTS — Object Pooling](https://docs.unity3d.com/2022.3/Documentation/Manual/object-pool.html)
- [Unity 2022.3 LTS — ScriptableObject](https://docs.unity3d.com/2022.3/Documentation/Manual/class-ScriptableObject.html)
- [Unity 2022.3 LTS — `Physics.Raycast`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.Raycast.html)
- [Unity 2022.3 LTS — `destroyCancellationToken`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour-destroyCancellationToken.html)
- [Game Programming Patterns — Robert Nystrom](https://gameprogrammingpatterns.com/) (ScriptableObject channel architecture inspired by the *Observer* and *Service Locator* chapters)
- [Ryan Hipple — Unite Austin 2017 *Game Architecture with Scriptable Objects*](https://www.youtube.com/watch?v=raQ3iHhE_Kk) (origin of the SO event channel pattern as used here)
- [Anthropic Claude Code](https://www.anthropic.com/claude-code) — the AI pair programmer used for this project
- [Anthropic Claude Opus 4.7 announcement](https://www.anthropic.com/news) — the underlying model

---
---

<a name="castellano"></a>

## Castellano

### Índice

1. [Qué es esto](#es-overview)
2. [Cómo se construyó — Claude Code Opus 4.7](#es-claude)
3. [Stack técnico y requisitos](#es-stack)
4. [Estructura del proyecto](#es-structure)
5. [Cómo ejecutarlo](#es-run)
6. [Arquitectura de un vistazo](#es-architecture)
7. [Patrones de diseño (elegidos antes de picar código)](#es-patterns)
8. [Catálogo de scripts](#es-scripts)
9. [Canales de eventos (flujo de datos)](#es-events)
10. [Hoja de ruta por iteración](#es-iterations)
11. [Tests](#es-testing)
12. [Referencias](#es-references)

---

<a name="es-overview"></a>

### 1. Qué es esto

Un clon en vertical-slice del clásico ZigZag de Ketchapp: la bola rueda hacia delante por un camino
procedural e infinito hecho de cubos alineados a los ejes. Un único tap (o click, o `Space`) hace que la
bola alterne entre dos ejes del mundo (`-X` y `+Z`). Si fallas el camino, la bola cae. Cuanto más
aguantes, mayor el score; recoges gemas para ganar coins; gastas coins en la tienda para skins cosméticos
de la bola.

**Objetivos del código:**

- Demostrar encapsulación estricta (campos `private`, configuración por ScriptableObject, canales de evento).
- Demostrar independencia entre sistemas (sin `FindObjectOfType`, sin `GameManager` dios, sin singletons globales).
- Demostrar higiene en runtime (cero allocs en `Update`, pooling para todo lo que spawnea, refs cacheadas).
- Demostrar que un workflow asistido por LLM puede entregar código de calidad de producción cuando se
  acompaña de una arquitectura escrita y un documento de reglas — ver [§ 2](#es-claude).

---

<a name="es-claude"></a>

### 2. Cómo se construyó — Claude Code Opus 4.7

Cada línea de código, cada ADR, cada plan y cada entrada del devlog fue producida con
**[Claude Code](https://www.anthropic.com/claude-code)** corriendo el modelo **Claude Opus 4.7**. El
workflow está diseñado en torno a *decisiones que componen*: cada iteración escribe su plan, sus
trade-offs y sus huecos conocidos antes de commitear código, y cada iteración posterior puede consultar
esos documentos en lugar de redescubrir el contexto.

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Spec  ──►  Plan  ──►  Brainstorm  ──►  Código  ──►  Tests  ──►  Devlog    │
│   (gdd)     (plans/)    (chat)         (.cs)         (EditMode)  (devlog.md)│
│                                                                            │
│                                  ▲                                         │
│                                  │                                         │
│                       Reglas duras de CLAUDE.md                            │
│              (encapsulación, naming, rendimiento, patrones)                │
└────────────────────────────────────────────────────────────────────────────┘
```

| Fase                     | Artefacto                                              | Localización                  |
| ------------------------ | ------------------------------------------------------ | ----------------------------- |
| Diseño                   | Game Design Document                                   | [`zigzag_gdd.md`](zigzag_gdd.md) ([EN](zigzag_gdd.en.md)) |
| Arquitectura             | C4 + ADRs                                              | [`zigzag_architecture.md`](zigzag_architecture.md) ([EN](zigzag_architecture.en.md)) |
| Spec por iteración       | Diseño de una feature antes de tocar código            | `docs/superpowers/specs/`     |
| Plan por iteración       | Plan paso-a-paso con checklist de tareas               | `docs/superpowers/plans/`     |
| Reglas del proyecto      | Reglas obligatorias que la IA debe respetar            | [`CLAUDE.md`](CLAUDE.md)      |
| Devlog por iteración     | Qué se hizo, por qué, qué queda pendiente              | [`devlog.md`](devlog.md) ([EN](devlog.en.md)) |

**¿Por qué Opus 4.7?** Opus 4.7 tiene el razonamiento más profundo de la familia Claude 4.X, que es lo
que la disciplina de este proyecto exige: cada decisión queda documentada con sus alternativas
descartadas, cada script se rechaza si no encaja con las reglas de [`CLAUDE.md`](CLAUDE.md). Un modelo que
"sólo escribe código" habría producido un prototipo más rápido pero menos defendible.

> **Sobre este README.** Este propio README también fue generado con **Claude Code Opus 4.7**, tras un
> análisis read-only del repositorio completo: cada script C# en `Assets/Code/`, el histórico de
> [`devlog.md`](devlog.md), los planes y specs por iteración bajo `docs/superpowers/`, el `git log` y el
> grafo de asmdefs. El modelo sintetizó después la documentación que estás leyendo — mismo patrón que el
> resto de artefactos del repo: *entender la fuente de verdad primero, describirla después*.

**El ciclo por feature fue:**

1. Abrir un chat nuevo con el *superpower* de *brainstorming* → explorar enfoques.
2. Escribir un documento `spec` → interfaz acordada + trade-offs + consideraciones de futuro.
3. Escribir un documento `plan` → tareas ordenadas con snippets de código por paso.
4. Ejecutar el plan con TDD donde aplica (`ScoreCalculator`, `CameraFollowMath`, `CoinsWallet.TrySpend`,
   `SkinInventory.ParseOwnedCsv`, `PaletteSampler`).
5. Añadir una entrada al devlog describiendo las decisiones tomadas *durante* la implementación (no sólo
   las de antes).

Así el repositorio queda auditable: un revisor puede leer la spec, el plan y el devlog, y el código final
es una transformación conocida de esos tres documentos.

---

<a name="es-stack"></a>

### 3. Stack técnico y requisitos

| Componente        | Versión / valor                              |
| ----------------- | -------------------------------------------- |
| Unity Editor      | 2022.3.62f2 LTS                              |
| Render pipeline   | Built-in                                     |
| .NET / C#         | .NET Standard 2.1 / C# 9                     |
| Test framework    | Unity Test Framework (NUnit, sólo EditMode)  |
| Paquetes externos | Ninguno — sólo lo que trae 2022.3 LTS de base|
| Plataforma target | PC standalone, ventana mobile-portrait (608×1080) |
| Input             | `UnityEngine.Input` (touch mapea a mouse 0)  |
| Persistencia      | `PlayerPrefs` (claves: `BestScore`, `Coins`, `OwnedSkins`, `EquippedSkin`) |

---

<a name="es-structure"></a>

### 4. Estructura del proyecto

```
Assets/
├── Art/                              # Materiales, sprites
├── Audio/                            # 3 SFX + 1 música
├── Code/
│   ├── Runtime/
│   │   ├── Audio/        ── ZigZag.Runtime.Audio.asmdef        (refs: Events)
│   │   ├── Core/         ── ZigZag.Runtime.Core.asmdef         (refs: Events, Data, Input, Gameplay)
│   │   ├── Data/         ── ZigZag.Runtime.Data.asmdef         (sin refs)
│   │   ├── Events/       ── ZigZag.Runtime.Events.asmdef       (sin refs)
│   │   ├── Gameplay/     ── ZigZag.Runtime.Gameplay.asmdef     (refs: Data, Events)
│   │   │   ├── Aesthetics/   (paleta cíclica)
│   │   │   ├── CameraSystem/ (cámara que sigue)
│   │   │   ├── Collectibles/ (gemas)
│   │   │   ├── Cosmetics/    (skins de la bola)
│   │   │   ├── Economy/      (wallet de coins)
│   │   │   ├── Player/       (controller de la bola)
│   │   │   ├── Scoring/      (score)
│   │   │   └── World/        (generación de camino + pool + faller)
│   │   ├── Input/        ── ZigZag.Runtime.Input.asmdef        (refs: Events)
│   │   └── UI/           ── ZigZag.Runtime.UI.asmdef           (refs: Events, Gameplay, Unity.TextMeshPro)
│   └── Tests/
│       └── EditMode/     ── ZigZag.Tests.EditMode.asmdef       (refs: todas las anteriores)
├── Prefabs/                          # P_Ball, P_PlatformCube, P_Gem, P_ShopRow
├── Scenes/                           # SampleScene.unity
└── Settings/                         # SO_GameConfig, SO_PaletteRules, SO_*Event, SO_Skin_*, SO_BallSkinCatalog
```

**Los assembly definitions forman un DAG con dependencias estrictamente descendentes** (UI → Gameplay →
Core → Data/Events). Esto se garantiza por las referencias de asmdef; un ciclo es un error de compilación.
El documento de arquitectura lleva el grafo exacto en `zigzag_architecture.md §5`.

---

<a name="es-run"></a>

### 5. Cómo ejecutarlo

**Desde el Editor de Unity:**

1. Clona el repositorio.
2. Abre el proyecto con Unity Hub. La primera importación tarda ~2 minutos.
3. Abre `Assets/Scenes/SampleScene.unity`.
4. Pulsa **Play**. Tap / click / `Space` para empezar. Tap otra vez para flipear dirección.

**Desde un build de Windows:**

1. `File → Build And Run` (la escena ya está en `Build Settings`, version `1.0`, 608×1080 portrait).

**Ejecutar los tests EditMode:**

1. `Window → General → Test Runner → EditMode → Run All`. 24 tests deberían pasar.

---

<a name="es-architecture"></a>

### 6. Arquitectura de un vistazo

El juego es un grafo de sistemas independientes que se comunican mediante **canales de evento por
ScriptableObject**. Nadie guarda una referencia dura a nadie a quien no necesite estrictamente.

```
              ┌──────────────┐
              │ InputHandler │
              └───────┬──────┘
                     OnTapped (event C#)
                      │
                      ▼
              ┌────────────────────┐         SO_OnGameStarted ───────────┐
              │ GameStateMachine   │─────────SO_OnGameOver ─────────────┐│
              │  Menu/Playing/Over │─────────SO_OnGameReset ───────────┐││
              └─┬─────┬─────┬──────┘                                   │││
                │     │     │                                          │││
       FlipDir  │     │     │ StartMoving/StopMoving/ResetTo            │││
                ▼     ▼     ▼                                          │││
              ┌──────────────┐                                         │││
              │ BallControl. │── OnFell ─►(la state machine escucha)   │││
              └──────┬───────┘                                         │││
                     │ raises SO_OnDirectionChanged                    │││
                     │                                                 │││
                     │                ┌──────────────┐ ◄───────────────┘││
                     ├──escucha──────►│ PathGenerator│◄────reset ────────┘│
                     │  ballPos        └──────────────┘◄────game over ───┘
                     │
                     ▼
              ┌──────────────┐                          ┌──────────────┐
              │ CameraFollow │                          │ PaletteCtrl. │
              └──────────────┘                          └──────┬───────┘
                                                               │ SO_OnScoreChanged
                                                               │ (IntGameEventSO)
                                                               │
   Gem.OnTrigger ──► SO_OnGemCollected (int) ──► CoinsWallet ──┘─►SO_OnCoinsChanged
                                                  │
                                                  └──► ScoreManager (distancia) ──► SO_OnScoreChanged
                                                                                  ──► SO_OnBestScoreChanged

   Lado UI (sólo escucha, nunca conduce):
       UIController, AudioManager, BallSkinApplier, PaletteController, ShopPanel
       — todos sólo se suscriben a canales; ninguno alimenta lógica de gameplay.
```

**Invariantes clave** (lista completa en `zigzag_architecture.md`):

- La bola es **kinemática** (ADR-001): el movimiento es `transform.position +=`, la caída es una
  velocidad descendente hecha a mano. Sin `Rigidbody` en la bola.
- Los pools (`UnityEngine.Pool.ObjectPool<T>`) gestionan todo lo que spawnea en runtime. **Cero
  `Instantiate`/`Destroy` después del `Awake`** (ADR-002).
- El determinismo en la generación es opt-in: `GameConfigSO.GenerationSeed = 0` → semilla nueva por run;
  cualquier otro valor → mismo camino siempre (sólo debugging).
- La cámara se mueve **únicamente a lo largo del eje global forward** `(-1, 0, 1)/√2` (ADR-014). La bola
  serpentea lateralmente por encima.

---

<a name="es-patterns"></a>

### 7. Patrones de diseño (elegidos antes de picar código)

El catálogo de patrones se **decidió por adelantado**, antes del primer `.cs` commiteado. El orden
cronológico de la fase de diseño fue:

```
   1.  GDD (zigzag_gdd.md)              ─── qué tiene que hacer el juego
                │
                ▼
   2.  Arquitectura (zigzag_architecture.md) ─── cómo tiene que estructurarse
                │
                ▼
   3.  CLAUDE.md §6 — catálogo de patrones obligatorios ─── qué patrones usar, cuáles rechazar
                │
                ▼
   4.  Primer commit de código  ─── cada iteración desde entonces mapea limpiamente al catálogo
```

Una vez arrancó el código, cada spec de iteración empezaba por "¿qué patrones del catálogo usa esta
feature?". Si hacía falta un patrón nuevo, la spec tenía que justificarlo antes de escribir código. Por
eso el repo tiene cero managers ad-hoc, cero singletons y cero god-objects — se descartaron *antes* de
poder inventarse.

Los patrones que el prototipo realmente usa (y dónde vive cada uno):

| Patrón | Dónde vive | Por qué este patrón |
|--------|-----------|---------------------|
| **ScriptableObject como contenedor de datos** | [`GameConfigSO`](Assets/Code/Runtime/Data/GameConfigSO.cs), [`PaletteRulesSO`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteRulesSO.cs), [`BallSkinSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs), [`BallSkinCatalogSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs) | Los valores tunables viven en assets, no en código. `[SerializeField] private` + propiedades `get`-only fuerzan la encapsulación. Los diseñadores rebalancean sin recompilar. |
| **Event Channel (pub-sub SO)** | 15 assets `SO_*` bajo `Assets/Settings/Events/`, tipos base en [`Events/`](Assets/Code/Runtime/Events/) | Desacopla emisores de receptores — cada lado sólo conoce el asset. Sin `FindObjectOfType`, sin singletons. Añadir un listener nuevo es cero código. |
| **Observer (events C# nativos)** | [`BallController.OnDirectionChanged`/`OnFell`](Assets/Code/Runtime/Gameplay/Player/BallController.cs), [`InputHandler.OnTapped`](Assets/Code/Runtime/Input/InputHandler.cs) | Variante local del pub-sub: se usa cuando emisor y oyente viven en el mismo assembly y un asset SO sería ceremonial. `Register`/`Unregister` simétricos en `OnEnable`/`OnDisable`. |
| **Finite State Machine (FSM)** | [`GameStateMachine`](Assets/Code/Runtime/Core/GameStateMachine.cs) con `enum GameState { Menu, Playing, GameOver }` | Tres estados explícitos, transiciones explícitas, sin flags ocultos. Hand-rolled — un framework añadiría peso sin valor a esta escala. |
| **Object Pool** | [`PlatformPool`](Assets/Code/Runtime/Gameplay/World/PlatformPool.cs), [`GemPool`](Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs) vía `UnityEngine.Pool.ObjectPool<T>` | Cero `Instantiate`/`Destroy` en hot paths. Prewarming en `Awake`. Los cubos reciclan invisibles detrás de la bola. ADR-002. |
| **Template Method (genérico abstracto)** | [`GameEventSO<T>`](Assets/Code/Runtime/Events/GameEventSO.cs) → [`IntGameEventSO`](Assets/Code/Runtime/Events/IntGameEventSO.cs), [`StringGameEventSO`](Assets/Code/Runtime/Events/StringGameEventSO.cs) | Una sola implementación de `Register`/`Unregister`/`Raise`; los payloads concretos sólo declaran `T`. Añadir `Vector3GameEventSO` es una línea. |
| **Strategy (data-driven)** | [`BallSkinSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs) cambia un `Material`, [`PaletteRulesSO`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteRulesSO.cs) cambia rangos HSV | La "estrategia" se elige arrastrando otro asset, no instanciando otra clase. Workflow editorial puro. |
| **Catálogo (Repository simplificado)** | [`BallSkinCatalogSO.GetById(string)`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs) | Lookup centralizado sobre un array ordenado (orden de display = orden de tienda). Sin allocs de `Dictionary` en runtime. |
| **Composition Root / Bootstrap** | [`GameBootstrap`](Assets/Code/Runtime/Core/GameBootstrap.cs) con `[DefaultExecutionOrder(-1000)]` | Único sitio donde cada ref serializada se valida con `Debug.Assert`. La composición vive en la escena; el bootstrap sólo grita si falta algo antes del frame 1. |
| **MVP-lite (View ⇄ Presenter ⇄ Model)** | View: [`UIController`](Assets/Code/Runtime/UI/UIController.cs), [`ShopPanel`](Assets/Code/Runtime/UI/Shop/ShopPanel.cs). Presenter: canales SO. Model: [`ScoreManager`](Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs), [`CoinsWallet`](Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs), [`SkinInventory`](Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs) | La View sólo lee canales y refresca widgets; nunca tiene lógica de negocio. El Model nunca referencia a la View — empuja por canal. |
| **Composition over inheritance** | Cada `MonoBehaviour` hace una cosa; los colaboradores llegan por `[SerializeField]`. La bola **no** extiende nada; la state machine **no** extiende nada. | Inheritance sólo para verdadero `is-a`. El gameplay arcade rara vez cumple ese listón. |
| **Helpers puros (sin estado)** | [`ScoreCalculator`](Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs), [`CameraFollowMath`](Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs), [`PaletteSampler`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteSampler.cs) | `static class`, sin Unity lifecycle. Testeable en EditMode sin mocks. Separa la aritmética de los side-effects (raises, `PlayerPrefs`). |

**Anti-patrones rechazados explícitamente por adelantado** (justificación completa en [`CLAUDE.md`](CLAUDE.md) §6):

- `GameManager` dios
- `static` mutable state
- Singletons vía `Instance ??= this`
- `SendMessage` / `BroadcastMessage`
- `FindObjectOfType` / `GameObject.Find` fuera de bootstrap o código de editor
- `Resources.Load` en hot paths
- Lógica de negocio en editor scripts

Leer los planes de iteración en `docs/superpowers/plans/` confirma la disciplina: cada plan abre con los
patrones existentes que la feature reutiliza y una sección *Decisiones* listando las alternativas
descartadas — que "la tienda es un catálogo de SOs, no un enum" o "la música es un segundo AudioSource
en la cámara, no un MonoBehaviour nuevo" son el tipo de decisión a nivel de patrón que se tomó *antes* de
escribir código.

---

<a name="es-scripts"></a>

### 8. Catálogo de scripts

Cada script aparece listado por su **capa**, con la ruta del archivo y un párrafo describiendo cómo
usarlo. La forma de cada `MonoBehaviour` y `ScriptableObject` está dictada por [`CLAUDE.md`](CLAUDE.md) —
lee ese archivo una vez para entender las convenciones.

#### 7.1 — `Data/` (configuración)

| Script | Rol |
|--------|------|
| [`GameConfigSO`](Assets/Code/Runtime/Data/GameConfigSO.cs) | Fuente única de verdad para cada valor tunable de gameplay: velocidad, caída, ground check, generación del camino, probabilidad de gema, multiplicador de score, duración del freeze-frame, threshold de caída de plataformas, tamaños de pool. Asset: `SO_GameConfig.asset`. **Todos los campos privados + serializados, todas las propiedades get-only**. `OnValidate` clampa cada numérico a un rango seguro. |

**Cómo usar:** arrastra `SO_GameConfig.asset` al slot correspondiente de cualquier componente que necesite
tunear valores. Nunca lo modifiques desde código — eso rompe la encapsulación. Para rebalancear, edita el
asset en el Inspector.

#### 7.2 — `Events/` (comunicación entre sistemas)

| Script | Rol |
|--------|------|
| [`GameEventSO`](Assets/Code/Runtime/Events/GameEventSO.cs) | Canal SO sin payload. Los listeners llaman `Register(Action)` en `OnEnable` y `Unregister(Action)` en `OnDisable`. Quien dispara llama `Raise()`. |
| [`GameEventSO<T>`](Assets/Code/Runtime/Events/GameEventSO.cs) | Canal genérico abstracto con payload tipado. Las subclases definen tipos concretos. |
| [`IntGameEventSO`](Assets/Code/Runtime/Events/IntGameEventSO.cs) | `: GameEventSO<int>`. Usado para score, coins, valor de gemas. |
| [`StringGameEventSO`](Assets/Code/Runtime/Events/StringGameEventSO.cs) | `: GameEventSO<string>`. Usado para transportar IDs de skin. |

**Cómo usar:** para añadir un evento nuevo, crea un asset (`Right-click → Create → ZigZag → Events → Game Event`),
arrástralo al inspector del que dispara y del que escucha. Cero cambios de código para wirear un par nuevo.

```
   Lado raiser                   Lado listener
   ─────────────                 ─────────────
   [SerializeField]              [SerializeField]
   private GameEventSO            private GameEventSO
        _onSomething;                  _onSomething;

   void Cuando()                  void OnEnable() => _onSomething.Register(Handle);
   { _onSomething.Raise(); }      void OnDisable() => _onSomething.Unregister(Handle);

                                  void Handle() { /* ... */ }
```

#### 7.3 — `Input/`

| Script | Rol |
|--------|------|
| [`InputHandler`](Assets/Code/Runtime/Input/InputHandler.cs) | Wrapper fino sobre `UnityEngine.Input`. Dispara `OnTapped` (event C#) con click izquierdo, `Space` o primer touch (Unity mapea touch 0 → mouse 0 automáticamente). Dos mecanismos de supresión: `SO_OnShopOpened/Closed` bloquea todo input mientras la tienda está abierta; el guard por click `EventSystem.IsPointerOverGameObject()` descarta taps que cayeron en un UI raycast target. |

**Cómo usar:** monta el componente en un GameObject root llamado `InputHandler`. Wirea los canales de
tienda en el inspector (opcional — null-safe). Otros sistemas se suscriben a `OnTapped` vía event C# (en
este prototipo sólo lo hace la state machine).

#### 7.4 — `Core/` (flujo del juego)

| Script | Rol |
|--------|------|
| [`GameState`](Assets/Code/Runtime/Core/GameState.cs) | `enum GameState { Menu, Playing, GameOver }`. |
| [`GameStateMachine`](Assets/Code/Runtime/Core/GameStateMachine.cs) | El cerebro. Rutea `InputHandler.OnTapped` según el estado actual (Menu→Start, Playing→Flip, GameOver→ignora). Escucha `BallController.OnFell` y dispara la coroutine de *freeze-frame al morir*: `Time.timeScale = 0` durante `GameConfigSO.FreezeFrameOnDeathSeconds` segundos reales, luego raises `SO_OnGameOver`. Escucha `SO_OnRetryRequested` y resetea la bola + raises `SO_OnGameReset`. |
| [`GameBootstrap`](Assets/Code/Runtime/Core/GameBootstrap.cs) | `[DefaultExecutionOrder(-1000)]`. Asserta cada dependencia wireada en `Awake`, así una referencia que falta aparece antes del primer frame en vez de como `NullReferenceException` en runtime. Sin service location ni instanciación — sólo validación. |

**Cómo usar:** pon un `GameStateMachine` en la escena, wirea todas las refs serializadas (input, ball,
ball spawn, config, los 4 canales SO de ciclo de vida), y un `GameBootstrap` al lado con refs a cada
sistema que quieras asertado.

#### 7.5 — `Gameplay/Player/`

| Script | Rol |
|--------|------|
| [`BallController`](Assets/Code/Runtime/Gameplay/Player/BallController.cs) | La bola. Movimiento a velocidad constante por uno de dos ejes del mundo (`-X` o `+Z`), ground check por `Physics.Raycast`, caída hecha a mano, rotación visual sin slip (`ω = v/r`), `StartMoving`/`StopMoving`/`ResetTo(position)`/`FlipDirection()` idempotentes. Raises tres events C# — `OnDirectionChanged(Vector3)`, `OnFell`, `OnReset` (disparado dentro de `ResetTo`, lo consume `BallTrailColorizer` para limpiar el trail al respawn) — más un canal opcional `SO_OnDirectionChanged` para la capa de presentación (audio). |
| [`BallDeathBurst`](Assets/Code/Runtime/Gameplay/Player/BallDeathBurst.cs) | Construye un `ParticleSystem` hijo en `Awake` (sphere shape, world-space, 36 partículas, lifetime 0.65 s) y se suscribe a `BallController.OnFell`. Snapea el host del burst al punto de impacto antes de `Play(true)` para que el burst quede anclado donde la bola se salió del path, no donde acaba tras el freeze-frame. Además controla la visibilidad de la bola alrededor del momento de muerte: desactiva el `MeshRenderer` hermano al caer para que la bola muerta no quede visible bajo el UI de Game Over, y lo reactiva en `BallController.OnReset` al respawnear (el GameObject sigue activo, así todas las suscripciones de la bola sobreviven al ciclo). Skin sync opcional vía los slots `_catalog` + `_onSkinEquipped` — `null`-safe; si quedan vacíos, se usa el `_burstColor` autorizado en el inspector (blanco→naranja). |

**Cómo usar:** monta el componente en el GameObject de la bola (Unity Sphere primitive a escala 1).
Arrastra `SO_GameConfig` y el canal SO de dirección a sus slots. La state machine gestiona el ciclo de
vida — la bola nunca escucha al input directamente.

#### 7.6 — `Gameplay/World/` (generación de camino + reciclaje + colapso)

| Script | Rol |
|--------|------|
| [`Segment`](Assets/Code/Runtime/Gameplay/World/Segment.cs) | C# puro. Un tramo de cubos a lo largo de una dirección; expone `IReadOnlyList<GameObject> Cubes`. Lleva `FallTriggerIndex`, una watermark monotónica usada por `PathGenerator.TriggerFalls`. |
| [`PlatformPool`](Assets/Code/Runtime/Gameplay/World/PlatformPool.cs) | Wrapper sobre `UnityEngine.Pool.ObjectPool<GameObject>`. Prewarmea `GameConfigSO.PlatformPoolInitialSize` cubos en `Awake`. Es dueño de un `RuntimeMaterial` instanciado (clon del material del prefab) para que `PaletteController` pueda recolorear todos los cubos activos con un solo `SetColor`. Se destruye en `OnDestroy`. |
| [`PathGenerator`](Assets/Code/Runtime/Gameplay/World/PathGenerator.cs) | Spawnea y recicla segmentos alrededor de la bola. `EnsureAhead`: spawnea cubos hasta cubrir `GameConfigSO.AheadBuffer` (medido por `Vector3.Dot(lastCube - ball, GlobalForward)`). `RecycleBehind`: libera el segmento más antiguo cuando su último cubo está más allá de `BehindBuffer`. `TriggerFalls`: llama `PlatformFaller.Begin()` en los cubos que cruzaron `PlatformFallStartBehind`. Usa `System.Random` seedeado desde `GameConfigSO.GenerationSeed` para determinismo. Elige al azar la dirección de arranque por run (par espejo, no afecta al baseline de score) y aplica drift caps asimétricos para que el camino zigzaguee más ancho hacia el lado al que arranca. |
| [`PlatformFaller`](Assets/Code/Runtime/Gameplay/World/PlatformFaller.cs) | Uno por cubo. Integración de gravedad hecha a mano una vez se llama `Begin()`. Resetea estado en `OnDisable` (el pool desactiva el cubo antes de reusarlo). `MaxFallDistance = 60` capa la integración para que un cubo huérfano no caiga eternamente. |

**Cómo usar:** pon un GameObject `PlatformPool` y otro `PathGenerator` en la escena, dale al pool un
prefab (`P_PlatformCube`), dale al generator el pool + transform de la bola + canales de ciclo de vida.
`PlatformFaller` es un componente del propio prefab (default `_gravity = 18`).

#### 7.7 — `Gameplay/Collectibles/`

| Script | Rol |
|--------|------|
| [`Gem`](Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs) | `[RequireComponent(Collider, Rigidbody)]` (el Rigidbody es kinemático — Unity 2022 exige que al menos un lado de un trigger tenga Rigidbody). `OnTriggerEnter` con la bola → raises `SO_OnGemCollected(value)` + se devuelve al pool. `Initialize(value, pool)` lo llama el spawner en cada `Get()`. |
| [`GemPool`](Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs) | Gemelo de `PlatformPool`. Prewarmea `GameConfigSO.GemPoolInitialSize` instancias. |
| [`GemSpawner`](Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs) | Lo invoca `PathGenerator` cuando un segmento se finaliza. Dado aleatorio contra `GemSpawnProbability`; si pasa, elige un cubo aleatorio del segmento y parenta una gema encima a `GemHeightAboveCubeCenter`. `ReleaseGemsBehind(ballPos, GlobalForward, BehindBuffer)` barre gemas huérfanas una vez por frame para que el pool no crezca sin límite. |

#### 7.8 — `Gameplay/Economy/`

| Script | Rol |
|--------|------|
| [`CoinsWallet`](Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs) | Único dueño de `PlayerPrefs["Coins"]`. Escucha `SO_OnGemCollected(int)` (suma a wallet + sesión); escucha `SO_OnGameReset` (resetea sesión, wallet intacta). `TrySpend(int)` es la API pública de gasto que usa `SkinInventory`: devuelve `false` con cantidad no positiva o fondos insuficientes; en éxito deduce, persiste inmediatamente y raises `SO_OnCoinsChanged`. |

#### 7.9 — `Gameplay/Scoring/`

| Script | Rol |
|--------|------|
| [`ScoreCalculator`](Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs) | `static class` pura. `ComputeDistanceScore(ballPos, origin, forwardAxis, multiplier)` proyecta el desplazamiento sobre `(-1,0,1)/√2` y devuelve `Mathf.FloorToInt(progress) * multiplier`, clampeado a ≥ 0. Cubierto por 7 tests EditMode. |
| [`ScoreManager`](Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs) | `MonoBehaviour`. Recomputa `_distanceScore` en `Update`, raises `SO_OnScoreChanged` sólo cuando el entero cruza un valor nuevo (no cada frame). Persiste `BestScore` en `PlayerPrefs` al recibir `SO_OnGameOver`, raises `SO_OnBestScoreChanged` si mejoró. |

#### 7.10 — `Gameplay/CameraSystem/`

| Script | Rol |
|--------|------|
| [`CameraFollowMath`](Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs) | Helper estático puro. `ComputeDesiredPosition(...)` proyecta el delta del target sobre `GlobalForward` y devuelve la posición deseada con `Y` bloqueada. Cubierto por 7 tests EditMode. |
| [`CameraFollow`](Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs) | `MonoBehaviour`. `LateUpdate` delega en el helper de matemáticas y luego hace `Vector3.SmoothDamp` hacia el resultado con `GameConfigSO.CameraFollowSmoothTime`. Se suscribe a `SO_OnGameReset` y snapea el transform al origen capturado (reseteando `_smoothVelocity`) para que una run larga no haga slingshot visible cuando el jugador hace Retry. |

#### 7.11 — `Gameplay/Cosmetics/`

| Script | Rol |
|--------|------|
| [`BallSkinSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs) | Datos por skin: `Id` (estable, nunca renombrar), `DisplayName`, `Price`, `Material`. |
| [`BallSkinCatalogSO`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs) | Lista ordenada. La primera entrada es el default (`Price = 0`, siempre owned). `GetById(string)` para lookup. `OnValidate` chequea unicidad y el invariante `price = 0` en la primera. |
| [`SkinInventory`](Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs) | Único dueño de `PlayerPrefs["OwnedSkins"]` (CSV) y `["EquippedSkin"]` (id). Brokerea la compra (valida fondos vía `CoinsWallet.TrySpend`, auto-equipa al ganar) y los equip requests que dispara la tienda. Raises `SO_OnSkinEquipped` (string) + `SO_OnInventoryChanged` (parameterless) en cada mutación. |
| [`BallSkinApplier`](Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs) | Vive en la bola. Escucha `SO_OnSkinEquipped`, resuelve el skin vía el catalog y hace swap de `MeshRenderer.sharedMaterial`. Usa `sharedMaterial` deliberadamente (no `.material`, que haría heap-alloc y rompería el batching). |
| [`BallTrailColorizer`](Assets/Code/Runtime/Gameplay/Cosmetics/BallTrailColorizer.cs) | Dueño del aspecto del `TrailRenderer` de la bola: asigna en runtime un material válido garantizado con cascada de shader fallbacks (evita el placeholder magenta `Hidden/InternalErrorShader`), aplica los defaults autorizados de ancho/tiempo/distancia de vértice desde campos `[SerializeField, Range]` (evita la trampa de trail gigante cuando alguien toca la curva del inspector), y mantiene `startColor`/`endColor` alineados con el `BallSkinSO` equipado escuchando `SO_OnSkinEquipped`. También escucha `BallController.OnReset` y llama `_trail.Clear()` para que el trail de la caída no quede flotando tras el respawn. |

#### 7.12 — `Gameplay/Aesthetics/`

| Script | Rol |
|--------|------|
| [`PaletteRulesSO`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteRulesSO.cs) | Configuración del ciclo de paleta: paso por threshold (score), duración de la transición, rangos HSV, distancia mínima de hue al anterior, colores iniciales, nombre de la propiedad del shader (default `_Color` para Built-in, `_BaseColor` para URP — cambia aquí sin tocar código). |
| [`PaletteSampler`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteSampler.cs) | Helper estático puro. `Sample(rng, rules, previousHue)` devuelve la tupla `(platformColor, cameraColor, primaryHue)` donde la cámara usa el hue complementario. Hasta 8 reintentos para respetar la distancia mínima; degradación graceful al último sampleo. Cubierto por tests EditMode. |
| [`PaletteController`](Assets/Code/Runtime/Gameplay/Aesthetics/PaletteController.cs) | Escucha `SO_OnScoreChanged`. Cada `ScoreThresholdStep` puntos cruzados, lerpea `PlatformPool.RuntimeMaterial._Color` y `Camera.backgroundColor` hacia un par complementario nuevo durante `TransitionSeconds`. Cancela transiciones en vuelo; snap a paleta inicial en `SO_OnGameReset`. Determinista con `GameConfigSO.GenerationSeed != 0`. |

#### 7.13 — `UI/`

| Script | Rol |
|--------|------|
| [`UIController`](Assets/Code/Runtime/UI/UIController.cs) | Dueño de los tres paneles del juego (Menu, HUD, GameOver) y los togglea según los canales SO de ciclo de vida. Actualiza textos TMP en `SO_OnScoreChanged`, `SO_OnBestScoreChanged`, `SO_OnCoinsChanged`, `SO_OnSessionCoinsChanged`. El score del HUD anima con un count-up — la velocidad se re-deriva como `gap / _hudScoreCatchUpDuration` en cada cambio, corre sobre `Time.unscaledDeltaTime` para sobrevivir al freeze-frame de la muerte, y snapea hacia abajo al reset. El score del panel GameOver salta al valor final sin animación. Muestra `_newRecordBadge` si la run batió el best. `OnShopButtonClicked()` está wireado al botón SHOP y llama `_shopPanel.OpenShop()`. Escucha `SO_OnShopOpened` / `SO_OnShopClosed` para ocultar/restaurar el panel del Menu mientras el overlay de la tienda está abierto. |
| [`Shop/ShopRowView`](Assets/Code/Runtime/UI/Shop/ShopRowView.cs) | Pura presentación de una fila de tienda. `Bind(skin)` setea campos estáticos una vez; `Refresh(owned, equipped, canAfford)` cambia el label del botón entre `BUY {price}`, `EQUIP`, `EQUIPPED` y togglea `interactable`. Raises `SO_OnSkinPurchaseRequested` o `SO_OnSkinEquipRequested` al click, con el id del skin como payload. |
| [`Shop/ShopPanel`](Assets/Code/Runtime/UI/Shop/ShopPanel.cs) | Construye una `ShopRowView` por entrada del catalog en `Start`, dentro de un `VerticalLayoutGroup`. `OpenShop()` activa el root + raises `SO_OnShopOpened`; `CloseShop()` hace lo inverso. Refresca todas las filas en `SO_OnInventoryChanged` o `SO_OnCoinsChanged`. |

#### 7.14 — `Audio/`

| Script | Rol |
|--------|------|
| [`AudioManager`](Assets/Code/Runtime/Audio/AudioManager.cs) | `[RequireComponent(AudioSource)]`. Se suscribe a `SO_OnDirectionChanged` (sin payload), `SO_OnGemCollected` (payload int ignorado), `SO_OnGameOver`. Cada handler es `_audioSource.PlayOneShot(clip, volume)`. Los volúmenes son campos serializados por clip, no están en `GameConfigSO` (es ganancia de mixing, no diseño de juego). Clips `null` → no-op silencioso (te deja probar el wiring antes de que existan los assets). |

**Música de fondo.** El track de música en loop (`Assets/Audio/music.mp3`) **no** se gestiona por código.
Vive como un segundo componente `AudioSource` en el GameObject `Main Camera`, configurado directamente en
el Inspector: `Play On Awake = true`, `Loop = true`, `Volume = 0.036` (bajo a propósito, para que los
SFX sigan siendo el foco perceptual), 2D puro (sin spatialization). Tener la música separada del source
que `AudioManager` usa para `PlayOneShot` garantiza que los SFX nunca corten el track, y rebalancear el
mix es un cambio de un campo en el inspector — no de código. Para añadir un fade-in / fade-out en el
game over, introducir un `MusicController` mínimo que escuche `SO_OnGameOver` / `SO_OnGameReset` y
rampee `AudioSource.volume` — todavía no hecho (YAGNI).

#### 7.15 — Tests (`Assets/Code/Tests/EditMode/`)

| Fixture | Cubre |
|---------|-------|
| `ScoreCalculatorTests` | Proyección de distancia: progreso cero, sólo +Z, sólo -X, diagonal, clamp hacia atrás, multiplicador, multiplicador cero. |
| `CameraFollowMathTests` | Matemáticas de cámara: target estático, movimiento +Z, movimiento -X, perpendicular = cero, diagonal, Y-drop ignorado, `lockedY` sobrescribe. |
| `CoinsWalletTests` | `TrySpend`: camino feliz, fondos insuficientes, cantidad no positiva. |
| `SkinInventoryTests` | `ParseOwnedCsv`: todos los IDs conocidos, IDs desconocidos descartados, whitespace ignorado, CSV vacío o null devuelve set vacío. |
| `PaletteSamplerTests` | Aritmética de hue complementario, distancia circular, distancia mínima respetada, sampleo de rangos. |

---

<a name="es-events"></a>

### 9. Canales de eventos (flujo de datos)

Cada canal de evento es un asset `ScriptableObject` bajo `Assets/Settings/Events/`. El mismo asset se
arrastra al slot del inspector de cada sistema que interactúa con él — no hay acoplamiento de código
entre quien dispara y quien escucha.

| Canal | Tipo | Disparado por | Escuchado por |
|-------|------|---------------|---------------|
| `SO_OnGameStarted` | `GameEventSO` | `GameStateMachine` | `PathGenerator`, `PaletteController`, `UIController` |
| `SO_OnGameOver` | `GameEventSO` | `GameStateMachine` (tras el freeze-frame) | `PathGenerator`, `ScoreManager`, `UIController`, `AudioManager` |
| `SO_OnGameReset` | `GameEventSO` | `GameStateMachine` | `PathGenerator`, `CoinsWallet`, `ScoreManager`, `PaletteController`, `UIController` |
| `SO_OnRetryRequested` | `GameEventSO` | `UIController` (botón) | `GameStateMachine` |
| `SO_OnGemCollected` | `IntGameEventSO` | `Gem.OnTriggerEnter` | `CoinsWallet`, `AudioManager` |
| `SO_OnCoinsChanged` | `IntGameEventSO` | `CoinsWallet` | `UIController`, `ShopPanel` |
| `SO_OnSessionCoinsChanged` | `IntGameEventSO` | `CoinsWallet` | `UIController` |
| `SO_OnScoreChanged` | `IntGameEventSO` | `ScoreManager` | `UIController`, `PaletteController` |
| `SO_OnBestScoreChanged` | `IntGameEventSO` | `ScoreManager` | `UIController` |
| `SO_OnDirectionChanged` | `GameEventSO` | `BallController.FlipDirection` | `AudioManager` |
| `SO_OnSkinPurchaseRequested` | `StringGameEventSO` | `ShopRowView` | `SkinInventory` |
| `SO_OnSkinEquipRequested` | `StringGameEventSO` | `ShopRowView` | `SkinInventory` |
| `SO_OnSkinEquipped` | `StringGameEventSO` | `SkinInventory` | `BallSkinApplier`, `ShopPanel` |
| `SO_OnInventoryChanged` | `GameEventSO` | `SkinInventory` | `ShopPanel` |
| `SO_OnShopOpened` / `SO_OnShopClosed` | `GameEventSO` | `ShopPanel` | `InputHandler`, `UIController` (oculta el panel del Menu mientras el overlay de la tienda está abierto) |

---

<a name="es-iterations"></a>

### 10. Hoja de ruta por iteración

El devlog completo está en [`devlog.md`](devlog.md). Un párrafo por iteración:

| # | Fecha | Iteración | Resultado |
|---|-------|-----------|-----------|
| 1 | 2026-05-21 | **Baseline de movimiento** | Bola + Input + Config. La bola se mueve por `transform.position`, ground check por raycast, caída hecha a mano. |
| 1.5 | 2026-05-22 | **Addendum de layout** | El código migra de `Assets/_Project/` a `Assets/Code/` directamente. |
| 2 | 2026-05-22 | **Loop de partida** | `GameEventSO`, `GameStateMachine`, `UIController`, Menu → Playing → GameOver → Retry. Modelo de dirección reseteado a ejes de mundo (-X / +Z). |
| 3 | 2026-05-22 | **Camino procedural + pool** | `PathGenerator` + `PlatformPool` con `UnityEngine.Pool.ObjectPool<T>`, `System.Random` determinista, buffers ahead/behind medidos sobre `GlobalForward`. |
| 4 | 2026-05-22 | **Gemas + score + persistencia** | `Gem`/`GemPool`/`GemSpawner`, `ScoreCalculator` (puro + testeado), `ScoreManager` con best-score en `PlayerPrefs`, HUD wireado. |
| 4.1 | 2026-05-23 | **Split del wallet** | `CoinsWallet` queda como currency persistente; `ScoreManager` pasa a ser sólo distancia. Cimiento para la tienda. |
| 4.2 | 2026-05-24 | **Cámara forward-only** | `CameraFollowMath` (puro + testeado) + refactor de `CameraFollow`. La cámara avanza sólo por `GlobalForward`; la bola serpentea por encima. ADR-014. |
| 5 | 2026-05-25 | **Tienda + skins** | Catálogo de `BallSkinSO`, `SkinInventory` con persistencia PlayerPrefs, `BallSkinApplier`, overlay `ShopPanel`, `CoinsWallet.TrySpend`. Reemplaza al powerup imán descopeado. |
| 6 | 2026-05-25 | **Audio + freeze-frame + rolling** | Asmdef `Audio` nuevo + `AudioManager`. `Time.timeScale = 0` durante 0.1s reales al morir antes de mostrar GameOver. Rotación visual sin slip en la bola (`ω = v/r`). |
| 7 | 2026-05-25 | **Paleta cíclica** | `PaletteRulesSO` + `PaletteSampler` + `PaletteController`. Cada 50 puntos de score los colores de plataforma y cámara lerpean a un par complementario fresco. |
| 8 | 2026-05-26 | **Pulido final** | `PlatformFaller` (plataformas pasadas se desploman), config de build mobile-portrait 608×1080, version `0.9`, audio importado, `_distanceMultiplier` rebalanceado 3→1, música de fondo en loop como segundo `AudioSource` autónomo en el `Main Camera` (sin código). |
| 9 | 2026-05-27 | **Feedback de gema** | Burst procedural de partículas construido por código al recoger una gema (world-space, material estático compartido, cero assets nuevos); `GemSpawner` traquea el cubo-soporte de cada gema y conduce su Y en `LateUpdate` para que las gemas caigan al ritmo de las plataformas que colapsan, en vez de quedarse flotando. |
| 10 | 2026-05-27 | **Pulido final (trail + death burst + GlobalForward)** | `TrailRenderer` nativo sobre la bola con `BallTrailColorizer` (dueño del material runtime + ancho para esquivar la trampa de trail magenta/gigante, tinta al skin equipado, limpia en cada respawn); `BallDeathBurst` procedural en el punto de impacto (mirror del burst de `Gem`, skin sync opcional, oculta el `MeshRenderer` de la bola al morir y lo restaura en el respawn para que la bola muerta no quede visible bajo el UI de Game Over); `GameConfigSO.GlobalForward` consolidado como fuente única de verdad (cierra la deuda de iter 4.2); `CameraFollow` snapea al origen en `SO_OnGameReset`; HUD score animado con un count-up multiplier-agnóstico usando `Time.unscaledDeltaTime` (sobrevive al freeze-frame); el overlay de la tienda oculta el panel del Menu mientras está abierto. Versión del bundle subida `0.9 → 1.0.0`. |

---

<a name="es-testing"></a>

### 11. Tests

El repositorio incluye **24 tests EditMode** repartidos en 5 fixtures, cubriendo las funciones puras que
son más difíciles de validar a mano. Los tests PlayMode se saltaron deliberadamente — el comportamiento
en runtime se verifica jugando, y escribir PlayMode en una ventana de dos semanas habría desplazado
trabajo de feature.

```
Window → General → Test Runner → EditMode → Run All
```

Todos los tests siguen estructura AAA, una aserción lógica por test, los paths espejan la estructura de
runtime.

---

<a name="es-references"></a>

### 12. Referencias

| Tema                                | Ruta                                                           |
| ----------------------------------- | -------------------------------------------------------------- |
| Reglas del proyecto (obligatorias)  | [`CLAUDE.md`](CLAUDE.md)                                        |
| Game Design Document                | [`zigzag_gdd.md`](zigzag_gdd.md) ([EN](zigzag_gdd.en.md))       |
| Arquitectura (C4 + ADRs)            | [`zigzag_architecture.md`](zigzag_architecture.md) ([EN](zigzag_architecture.en.md)) |
| Specs por iteración                 | [`docs/superpowers/specs/`](docs/superpowers/specs/)            |
| Planes por iteración                | [`docs/superpowers/plans/`](docs/superpowers/plans/)            |
| Devlog                              | [`devlog.md`](devlog.md) ([EN](devlog.en.md))                   |
| Prompts de agentes (subagentes Claude) | [`.claude/agents/`](.claude/agents/)                         |
| Brief original Junior Game Developer | [`Junior Game Developer - Technical Test.pdf`](Junior%20Game%20Developer%20-%20Technical%20Test.pdf) |

**Referencias externas** (leídas durante la construcción):

- [Unity 2022.3 LTS — Object Pooling](https://docs.unity3d.com/2022.3/Documentation/Manual/object-pool.html)
- [Unity 2022.3 LTS — ScriptableObject](https://docs.unity3d.com/2022.3/Documentation/Manual/class-ScriptableObject.html)
- [Unity 2022.3 LTS — `Physics.Raycast`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.Raycast.html)
- [Unity 2022.3 LTS — `destroyCancellationToken`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour-destroyCancellationToken.html)
- [Game Programming Patterns — Robert Nystrom](https://gameprogrammingpatterns.com/) (la arquitectura de canales SO está inspirada por los capítulos *Observer* y *Service Locator*)
- [Ryan Hipple — Unite Austin 2017 *Game Architecture with Scriptable Objects*](https://www.youtube.com/watch?v=raQ3iHhE_Kk) (origen del patrón de canal de evento SO tal y como se usa aquí)
- [Anthropic Claude Code](https://www.anthropic.com/claude-code) — el pair programmer IA usado en este proyecto
- [Anthropic Claude Opus 4.7 announcement](https://www.anthropic.com/news) — el modelo subyacente

---

<div align="center">

Built with <strong>Claude Code · Claude Opus 4.7</strong> · Unity 2022.3 LTS · 2026<br/>
<sub><em>Code, documentation and this README — all authored through the same AI-assisted workflow.<br/>
Código, documentación y este README — todo escrito mediante el mismo workflow asistido por IA.</em></sub>

</div>

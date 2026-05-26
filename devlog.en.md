# ZigZag — Devlog

Chronological log of decisions and development iterations. Each entry describes **what was done**, **why** and **what remains pending**. Human-authored document; the code is the source of truth.

---

## 2026-05-21 — Iteration 1: base movement

### Goal

First playable script: the ball moves on its own diagonally across an XZ plane and changes direction on tap/click. Minimum viable to validate movement feel before adding procedural generation.

### Scope agreed after planning

- **Movement + Input + Config (minimum playable).** Three scripts and one configuration asset.
- **Tap-anywhere** as mobile input (same as the original ZigZag): any tap on the screen changes direction. On PC, left click or `Space` (editor shortcut) do the same with no additional code, thanks to Unity mapping the first touch to `Input.GetMouseButtonDown(0)`.

### What was implemented

1. **Folder structure** under `Assets/` with three `.asmdef` (the minimum needed):
   - `ZigZag.Runtime.Data` (no references)
   - `ZigZag.Runtime.Input` (no references)
   - `ZigZag.Runtime.Gameplay` (references Data and Input)

2. **`GameConfigSO`** (`Assets/Code/Runtime/Data/GameConfigSO.cs`)
   Only the subset of fields relevant for movement right now — `_initialSpeed`, `_acceleration`, `_maxSpeed`, `_fallSpeed`, `_fallThreshold`, `_groundCheckDistance`, `_groundLayerMask`. Mandatory pattern: `[SerializeField] private` + `get`-only property. `OnValidate` clamps negative values.

3. **`InputHandler`** (`Assets/Code/Runtime/Input/InputHandler.cs`)
   Thin layer on top of `UnityEngine.Input`. Exposes a single `event Action OnTapped`. Fires on left click, first touch (mobile) or `Space`. Alias `using UnityInput = UnityEngine.Input;` to avoid clashing with the project's own `ZigZag.Runtime.Input` namespace.

4. **`BallController`** (`Assets/Code/Runtime/Gameplay/Player/BallController.cs`)
   Core of the mechanic:
   - Two precomputed directions as `static readonly Vector3`: `(1,0,1).normalized` and `(-1,0,1).normalized`.
   - `Update` applies `transform.position += direction * speed * dt` while `grounded`. Accelerates up to `maxSpeed`.
   - When there is no ground (short `Physics.Raycast` downward with `LayerMask`), it keeps advancing horizontally and adds a vertical component `-fallSpeed`.
   - When crossing `fallThreshold`, it raises `OnFell` and stops.
   - `HandleTapped` flips the diagonal via a bool flag (avoids comparing vectors with `==`). Only changes while `IsMoving && IsGrounded` — falling off the path blocks input, same as in the original.
   - Subscription to `OnTapped` in `OnEnable` / unsubscription in `OnDisable`. Symmetric, no lambdas.
   - `Debug.Assert` in `Awake` to catch unassigned serialized references before it blows up with `NullReferenceException`.
   - `OnDrawGizmosSelected` to visualize the ground-check raycast in the scene (green = grounded, red = falling).

### Decisions made during implementation

- **No Rigidbody on the ball.** ADR-001 in the architecture document is clear: movement via `transform.position`, simulated fall. Pending implication: once gems appear, their trigger will need a Rigidbody (either on the gem or on the ball). To be revisited when implementing `Gem`. TODO in the Gem code when it arrives.
- **`groundCheckDistance` default 0.55.** With the ball (sphere radius 0.5) at `y=0.65` and the ground (cube Y scale = 0.3) at `y=0`, the center sits exactly 0.5 above the cube's top face. 0.55 gives minimal cushion so it doesn't oscillate between grounded/falling at the edge.
- **`_groundLayerMask` default `~0`** (all layers). The raycast from inside the ball's own collider does not self-hit (documented `Physics.Raycast` behavior), so it works for testing without having to create a "Ground" layer from day one. TODO: create `Ground` layer and configure the mask once `PathGenerator` exists.
- **Input disabled when not `IsGrounded`.** It makes no sense to change direction while falling — we block the flip in `HandleTapped`. Consistent with the reference game.
- **The controller does not start on its own.** `IsMoving` starts at `false`. An external call to `StartMoving()` is required to make it move. In this iteration that will be done from a temporary test script or from the inspector (via a provisional button). Once `GameStateMachine` exists, it will be the one to trigger it when entering the `Playing` state.

### Pending — manual setup in Unity (cannot be done via text)

1. Create scene `S_Main.unity` under `Assets/Scenes/` (or rename/reuse `SampleScene`).
2. Create asset `SO_GameConfig.asset` in `Assets/Settings/` (menu `Create → ZigZag → Game Config`).
3. In the scene:
   - **Provisional ground**: primitive Cube, scale `(30, 0.3, 30)`, position `(0, 0, 0)`.
   - **Ball**: primitive Sphere, position `(0, 0.65, 0)`. Add `BallController` component, drag `SO_GameConfig` and the `InputHandler` GameObject into its slots.
   - **Input**: empty GameObject named `InputHandler` with an `InputHandler` component.
   - **Camera**: eyeballed orientation, static for now.
4. **Temporary test**: until `GameStateMachine` exists, call `StartMoving()` from the component's context menu or via a small bootstrap MonoBehaviour that lives only during this iteration (do not commit it to `main` once the state machine arrives).

### Expected validation

- **PC**: Play in editor → the ball advances along the `(1,0,1)` diagonal. Click or Space → flips to `(-1,0,1)`. Goes off the cube → falls. Y < -2 → `OnFell` log (subscribe manually in a test script if you want to see it).
- **Mobile** (not tested in build yet, but the code is ready): tap on the screen = same effect as click.

### Next iteration (outline)

1. `GameEventSO` + `IntGameEventSO` (assembly `ZigZag.Runtime.Events`).
2. Minimum `GameStateMachine` + `GameBootstrap`.
3. Basic UI `S_Main` with Menu → Playing → GameOver.
4. Provisional path with hand-placed cubes (still no procedural generation).

Procedural generation and pooling will be tackled in iteration 3 (`PathGenerator` + `PlatformPool`), following the GDD §14 roadmap order.

---

## 2026-05-22 — Addendum to iteration 1

### Layout change: the `_Project/` prefix is removed

Decision revisited after seeing the created tree: code now lives directly under `Assets/Code/Runtime/<Layer>/<Feature>/`, without the `_Project/` wrapper. Same decision for `Assets/Settings/` and `Assets/Scenes/`.

- **Pros:** shorter paths, less nesting, simpler navigation in the Project window.
- **Cons accepted:** ZigZag content mixes alphabetically with any third-party package that might be imported into `Assets/` in the future. For a 2-week prototype with no expected external packages (brief: no Asset Store, no plugins), the cost is zero.

Updated accordingly:
- `CLAUDE.md` §3 (folder tree + rationale).
- `zigzag_architecture.en.md` §4 (tree) and §5 (asmdef paths table).
- `zigzag_gdd.en.md` (references in §13 success criteria, §15 closed decisions, §17 README).
- Five subagent prompts in `.claude/agents/` (paths in output templates).
- **Pending manually:** `.claude/agents/AGENTS.md` and `.claude/agents/unity-architect.md` — the Claude Code auto-mode classifier blocks editing agent prompts. You have to change the four references from `Assets/_Project/` to `Assets/` by hand (3 lines total). It's mechanical.

### Provisional script to test movement

`Assets/Code/Runtime/Gameplay/Player/BallAutoStarter.cs`:
- In `Start` it calls `_ball.StartMoving()`.
- Subscribes to `OnDirectionChanged` and `OnFell` and logs them (toggleable via `_verbose`) to debug the first playtest without needing to wire up the camera, UI, or state machine yet.
- Marked with `// TODO:` to delete once `GameStateMachine` appears in iteration 2.

Minimum scene setup to see it working (same cube+sphere setup described above): add a `_Bootstrap` GameObject with `BallAutoStarter`, drag the `BallController` into its slot. Play → the ball should start.

---

## 2026-05-22 — Iteration 2: close the loop (Menu → Playing → GameOver → Retry)

### Goal

Close the play cycle. With the loop closed, later iterations (procedural generation, gems, powerup) can be tested without Stop/Play; without it, all subsequent features are tested blind.

### What was implemented

1. **Events layer** — new asmdef `ZigZag.Runtime.Events` (no refs).
   - `GameEventSO` (parameterless) and `GameEventSO<T>` (abstract) in the same file — they are conceptual partners.
   - `IntGameEventSO : GameEventSO<int>` in a separate file (ready for iteration 4 when `_onScoreChanged` appears).

2. **Core layer** — new asmdef `ZigZag.Runtime.Core` (refs: Events, Data, Input, Gameplay).
   - `enum GameState { Menu, Playing, GameOver }`.
   - `GameStateMachine` `MonoBehaviour sealed`:
     - **Routes the tap** according to state: in Menu → `StartGame`; in Playing → `_ball.FlipDirection()`; in GameOver → ignore (only the Retry button responds).
     - Listens to `BallController.OnFell` (local C# event) and transitions to GameOver.
     - Listens to `SO_OnRetryRequested` (SO channel) and triggers the retry sequence.
     - Raises: `SO_OnGameStarted`, `SO_OnGameOver`, `SO_OnGameReset`.
   - Decision: **`GameBootstrap` is deferred to iteration 3** — with no pools to initialize and no service locator, it adds nothing. Each actor validates its refs with `Debug.Assert` in its own `Awake`.

3. **UI layer** — new asmdef `ZigZag.Runtime.UI` (ref: Events only).
   - `UIController` `MonoBehaviour sealed`:
     - Three `GameObject` panels (`_menuPanel`, `_hudPanel`, `_gameOverPanel`), are `SetActive`-toggled based on the incoming event.
     - `OnRetryButtonClicked()` is invoked from the Retry button's `onClick` (configured by inspector) and `Raise()`s the `SO_OnRetryRequested` channel.
   - The UI does **not** reference Core or Gameplay. Its only communication with Core is via SO channel.

4. **`BallController` refactor** — removed the direct subscription to `InputHandler.OnTapped`. It now exposes `public void FlipDirection()` and it's the state machine that calls it when state == Playing.
   - **Why:** if both the ball and the state machine subscribed to the same `OnTapped`, the first tap in Menu could simultaneously start the game **and** flip the direction (race condition driven by subscription order). Centralizing routing in the state machine eliminates the ambiguity.
   - Consequence: the `ZigZag.Runtime.Gameplay` asmdef no longer references `Input`.

5. **`BallAutoStarter` removed** (script + meta + component in scene + orphan field `_inputHandler` in BallController). The iteration 1 TODO is fulfilled here.

### Pending — manual setup in Unity

The code compiles independently, but the scene needs these steps in the editor before the loop is playable. All mechanical.

#### A. Create the 4 event ScriptableObjects in `Assets/Settings/Events/`

`Right-click → Create → ZigZag → Events → Game Event`, rename:
- `SO_OnGameStarted.asset`
- `SO_OnGameOver.asset`
- `SO_OnGameReset.asset`
- `SO_OnRetryRequested.asset`

#### B. In the `SampleScene.unity` scene

1. **GameObject `GameStateMachine`** (empty root):
   - Add `GameStateMachine` component.
   - Slots: drag `InputHandler` (from the Player GameObject) → `_inputHandler`; `BallController` (also from Player) → `_ball`; (see step 3) `BallSpawn` → `_ballSpawnPoint`; the 4 event SOs to their respective slots.

2. **GameObject `BallSpawn`** (empty root):
   - Position equal to the desired ball spawn — right now `(0, 0, 0)` to match the Player's position in the current scene.
   - Serves as a spawn marker; you drag it to the state machine's `_ballSpawnPoint` slot.

3. **Canvas + UI panels**:
   - `Right-click in hierarchy → UI → Canvas` (creates Canvas + EventSystem automatically).
   - Under the Canvas, create three empty GameObjects (with `RectTransform`): `MenuPanel`, `HUDPanel`, `GameOverPanel`. Each fills the whole Canvas (anchor stretch).
   - Inside each panel, add TextMeshPro UI (`UI → Text - TextMeshPro`):
     - `MenuPanel` → `Text: "ZIGZAG"` large + `Text: "Click to play"` medium.
     - `HUDPanel` → `Text: "Score: 0"` top-left corner. (Placeholder until iteration 4 when `ScoreManager` exists.)
     - `GameOverPanel` → `Text: "GAME OVER"` large + `Text: "Score: 0"` + `Button` with label `"RETRY"`.
   - Create a root GameObject `UIController` with the `UIController` component. Slots: drag the three panels + the 4 event SOs.
   - **Wire the Retry button:** select the button → `Button` component → `OnClick()` → `+` → drag the `UIController` GameObject → select function `UIController.OnRetryButtonClicked`.

4. **Provisional path**:
   - Remove (or disable) the current large `Cube` of `(scale 5,5,5)`.
   - Create ~12 cubes scaled to `(1, 0.3, 1)` placed by hand forming a zigzag following the two diagonals `(1,0,1).normalized` and `(-1,0,1).normalized`. First cube at `(0, 0, 0)`; each subsequent cube offset `~0.707` in X and Z relative to the previous one, alternating sign of X every N cubes (3–8).
   - Layer `Default` (the default `GroundLayerMask` is `~0`, so it works).
   - Group them as children of an empty GameObject `Path_Provisional` to keep them organized.
   - **TODO:** `Path_Provisional` disappears in iteration 3 once `PathGenerator` exists.

#### C. Verification

Play → the menu should appear. Click → ball moves. Click during movement → flip. Ball falls off the path → GameOver panel. Retry button → ball back to spawn + new run without Stop/Play.

### Next iteration (outline)

3. `PathGenerator` + `PlatformPool` (`UnityEngine.Pool.ObjectPool<T>`). Replaces `Path_Provisional` with seeded generation. `GameBootstrap` reappears to initialize the pool.

### Addendum after playtest

- **Retry returns to Menu, not autostart.** The original plan went directly to Playing after Retry; in playtest it feels abrupt and breaks the rhythm. Now `HandleRetryRequested` leaves the state in `Menu` and `UIController.HandleGameReset` shows the menu panel. An extra tap is required to start again, consistent with the first startup.
- **Initial diagonal changed to `(-1, 0, 1)`** (previously `(1, 0, 1)`). The provisional path was built in direction `-X, +Z`, so the ball has to start along that diagonal to climb the path and not fall off on the first step. Adjusted in `BallController.Awake` and `ResetTo`; the bool `_isOnLeftDiagonal` starts at `true` so that `FlipDirection` remains symmetric.
- **Pivot of the direction model: pure world axes, not 45° diagonals.** A second playtest reveals that the "45° diagonals" `(±1, 0, 1)` projected by the isometric camera (-45° Y) fall off the path because it was built with cubes aligned to the world axes (Ketchapp original style). Directions are now `AlongNegativeX = (-1, 0, 0)` and `AlongPositiveZ = (0, 0, 1)`. With the camera rotated, both world axes appear as diagonals on screen — same visual, correct geometry. The internal bool is renamed `_isOnXAxis`. GDD §5.1 and architecture §7.4 updated.

---

## 2026-05-22 — Iteration 3: procedural generation + pooling

### Goal

Replace `Path_Provisional` (hand-placed cubes) with a generator that produces an infinite path by recycling cubes via `UnityEngine.Pool.ObjectPool<T>`. No `Instantiate`/`Destroy` after the pool's `Awake`. GDD §14 day 3.

### What was implemented

1. **`GameConfigSO` extended** with `Path Generation` + `Pooling` blocks:
   - `_pathStartPosition = (-2, -3, 3)` — position of the first cube of the generated path.
   - `_cubeSize = (1, 5, 1)` — the user's cube, tall for better visuals.
   - `_segmentMinLength = 1`, `_segmentMaxLength = 5` — random segment in [1, 5] cubes.
   - `_aheadBuffer = 30`, `_behindBuffer = 10` — measured along the "global forward" axis `(-1, 0, 1)/√2`, the diagonal between the two directions the ball takes.
   - `_generationSeed = 0` — sentinel: each Retry uses a different seed (`Environment.TickCount`) so each run is random. Any value other than `0` enables deterministic mode (same path every time, useful for debugging).
   - `_platformPoolInitialSize = 50` — the pool prewarms these cubes in `Awake`.

2. **Sub-feature `Gameplay/World/`** (asmdef `ZigZag.Runtime.Gameplay` now references `Events`):
   - `Segment.cs` — pure C# class. Holds direction + internal list of cubes exposed as `IReadOnlyList<GameObject>` (CLAUDE §5: do not expose mutable collections).
   - `PlatformPool.cs` — wrapper around `ObjectPool<GameObject>`. Prewarms in `Awake` (Get + Release in a loop). The cubes are parented to `transform` to keep the hierarchy clean. `maxSize = 2× initialSize` to handle pressure spikes.
   - `PathGenerator.cs`:
     - `Start` → `InitializePath()` populates the path until it covers `AheadBuffer`. This happens before the run starts, so the menu already shows the path beneath the ball.
     - `Update` (only when `_isGenerating = true`) → `EnsureAhead()` + `RecycleBehind()`.
     - `EnsureAhead`: spawns while `Vector3.Dot(lastCubePos - ballPos, GlobalForward) < AheadBuffer`. Cap of `MaxCubesSpawnedPerFrame = 20` as a safety net against infinite loops.
     - `RecycleBehind`: if the last cube of the oldest segment is further than `BehindBuffer` behind (same dot product, inverted sign), releases all its cubes to the pool and discards the segment.
     - Determinism: `System.Random` with seed (not `UnityEngine.Random`, which is global). Reinstantiated on each `HandleGameReset` with the `CreateRandom()` rule: seed `0` → `Environment.TickCount` (random per run, default); seed != 0 → deterministic. CLAUDE.md §2 forbids `DateTime.Now` in gameplay; here the tick count is used **only** as the initial seed, the simulation that follows is fully deterministic from that seed, so the rule is respected in spirit (no non-determinism within the run).
     - Subscriptions: `_onGameStarted` → starts generation; `_onGameOver` → stops it; `_onGameReset` → clears path and rebuilds from `PathStartPosition`.

3. **`GameBootstrap` resurrected** (`Core` asmdef, `DefaultExecutionOrder(-1000)`):
   - Only validates refs (`PlatformPool`, `PathGenerator`, `GameStateMachine`). Does not instantiate or resolve; each component self-initializes in its `Awake`.
   - Does **not** reference `UIController` — that would force Core to reference UI and break the asmdef direction. UI validates itself.

### Technical decisions (local mini-ADRs for iter 3)

- **"Global forward" axis = `(-1, 0, 1)/√2`** as the single metric for ahead/behind. Alternative: track the distance traveled by the ball. Rejected: the dot product is O(1), stateless, and remains correct even if the ball zigzags.
- **`Vector3.Dot` instead of Manhattan or Euclidean distance.** Absolute distances conflate ahead and behind. The dot with the global axis gives a sign: positive = ahead, negative = behind.
- **`Queue<Segment>` for the active set.** Natural FIFO: the oldest segment is the most likely to be behind the ball. `Peek` is O(1) and lets us check the head without dequeuing.
- **Pool prewarmed in `Awake` with a Get/Release loop**, not with direct `Instantiate`, because `ObjectPool<T>` maintains its own internal counter. Calling `Instantiate` externally would leave the counter inconsistent.
- **Cap of 20 cubes per frame in `EnsureAhead`** as a safety net: at typical speed (5 u/s) the `AheadBuffer` (30) is consumed in 6 seconds, demanding ~5 spawns/second, well below the cap. Hitting the cap is a symptom of a bug, not real load.

### Pending — manual setup in Unity

1. **Create `Assets/Prefabs/P_PlatformCube`** as per the spec already submitted — cube scaled to `(1, 5, 1)`, no scripts, Static OFF, layer `Default`.
2. **In the scene**:
   - **Remove** `Path_Provisional` and its child cubes.
   - **Create GameObject `PlatformPool`** with the `PlatformPool` component. Drag `P_PlatformCube` to the `_platformPrefab` slot and `SO_GameConfig` to the `_config` slot.
   - **Create GameObject `PathGenerator`** with the `PathGenerator` component. Slots: `_config` (SO_GameConfig), `_pool` (the `PlatformPool` GameObject), `_ballTransform` (Player), `_onGameStarted`, `_onGameOver`, `_onGameReset` (the 3 event SOs).
   - **Create GameObject `Bootstrap`** with the `GameBootstrap` component. Slots: `_platformPool`, `_pathGenerator`, `_stateMachine` (the `GameStateMachine` GameObject).
   - **Move `BallSpawn`** to the position above the first cube: `(-2, 0, 3)` (X and Z match the first cube at `(-2, -3, 3)`; Y `0` leaves the ball with 0.5 clearance above the cube's top, which the 0.55 raycast covers).

3. **Verification**:
   - Play → the menu appears. **The path is already generated beneath the ball and visible ahead.** If only the first cube is visible, initialization didn't reach `AheadBuffer` — check refs.
   - Click → ball starts, the path grows ahead.
   - Tap during movement → ball turns, follows the next segment.
   - Look at the Profiler: zero `Instantiate` after frame 1.
   - Look at the hierarchy inside `PlatformPool`: the number of children is stable (~50–60), not growing without bound.
   - **Per-run randomness**: with `_generationSeed = 0` (default), each Retry must produce a different path. To verify deterministic mode (debugging), set `_generationSeed = 42` (or any int other than 0): two consecutive Retries will give identical paths.

### Next iteration (outline)

4. Gems (`Gem`, `GemSpawner`, `GemPool`) + `ScoreManager` with persistence (`PlayerPrefs`). HUD shows real score. GDD §14 day 4.

---

## 2026-05-22 — Iteration 4: gems, score and persistence

### Goal

Close the loop with purpose: the ball collects gems, the score rises (gems + distance), the best record persists between runs. GDD §14 day 4.

### What was implemented

1. **`GameConfigSO` extended** with three new blocks:
   - `Gems`: `_gemSpawnProbability = 0.3` (per segment), `_gemValue = 10`, `_gemHeightAboveCubeCenter = 3.2`.
   - `Score`: `_distanceMultiplier = 1`.
   - `Pooling`: `_gemPoolInitialSize = 20`.

2. **Sub-feature `Gameplay/Collectibles/`** (same asmdef `ZigZag.Runtime.Gameplay`):
   - `Gem.cs` — `MonoBehaviour` with `[RequireComponent(Collider, Rigidbody)]`. Trigger; when the ball enters, raises `SO_OnGemCollected(value)` and returns to the pool. `Initialize(value, pool)` pattern to inject dependencies on each pool `Get`. Defensive `Awake` forces `isKinematic=true`, `useGravity=false`, `isTrigger=true` in case the prefab is misconfigured, and a `LogError + enabled=false` if the event channel is missing (`Debug.Assert` is compiled out in release).
   - `GemPool.cs` — direct twin of `PlatformPool`. Same Get/Release prewarm in `Awake`, same `maxSize = 2× initialSize`.
   - `GemSpawner.cs` — `TryPopulateSegment(Segment)` with a die roll against `GemSpawnProbability`. Own `System.Random` RNG reset on `_onGameReset` (same seed as `PathGenerator`, independent instances). Maintains `List<GameObject> _activeGems` to release uncollected gems on reset (TODO: prune when endurance runs are introduced).

3. **Sub-feature `Gameplay/Scoring/`**:
   - `ScoreCalculator.cs` — pure static helper. `ComputeDistanceScore(ballPos, origin, forwardAxis, multiplier)` projects displacement onto `(-1,0,1)/√2` and returns `Mathf.FloorToInt(progress) * multiplier`, clamped at zero for negative progress. Covered by 7 EditMode tests.
   - `ScoreManager.cs` — `MonoBehaviour`. Accumulates `_gemScore` (sum in `HandleGemCollected`) + `_distanceScore` (recomputed in `Update`). Raises `_onScoreChanged` only when the integer total changes, not every frame. Persistence: `PlayerPrefs.GetInt("BestScore", 0)` in `Awake`; `SetInt + Save` in `SaveBestIfHigher`, called on `_onGameOver`. `HandleGameReset` also goes through `RecomputeAndBroadcast` to avoid emitting a spurious score-changed if it was already zero.

4. **`PathGenerator` modified** to invoke `_gemSpawner.TryPopulateSegment(_currentSegment)` right before `FlipDirection + StartNewSegment`, i.e. when a segment reaches its target length. Optional field (no assert) — if no spawner is wired, the path keeps generating without gems. The last segment from `InitializePath` is not finalized through this path and ends up without a gem — known minor; it's finalized as soon as the ball crosses it.

5. **`UIController` extended** with three `TextMeshProUGUI` (`_hudScoreText`, `_gameOverFinalScoreText`, `_bestScoreText`) and an optional `_newRecordBadge` GameObject. Subscribed to `SO_OnScoreChanged` and `SO_OnBestScoreChanged`. The badge uses two cooperating handlers (`HandleBestScoreChanged` + `HandleGameOver`) to resolve regardless of the order Unity fires `_onGameOver` subscribers — `_newBestSeenInThisRun` is reset in `HandleGameStarted`. Asmdef `ZigZag.Runtime.UI` adds reference `Unity.TextMeshPro`.

6. **`GameBootstrap` extended** to validate `_scoreManager`, `_gemPool`, `_gemSpawner` in `Awake`. No asmdef changes (all live in `ZigZag.Runtime.Gameplay`).

7. **EditMode test harness debuted** — first tests `.asmdef` of the project (`Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef`). 7 tests on `ScoreCalculator` (zero, progress along -X, along +Z, diagonal, backwards-clamp, multiplier, zero multiplier).

### Technical decisions (local mini-ADRs)

- **Gem requires a kinematic `Rigidbody`, not the ball.** ADR-001 mandates a ball with no Rigidbody. Unity 2022.3 requires at least one of the two colliders to have a Rigidbody to fire `OnTriggerEnter`. Solution: the gem carries it (`isKinematic=true, useGravity=false`) — the ball remains a static collider with a transform that moves.
- **Distance measured by projection onto `GlobalForward`, not by `position.z`.** GDD §7.2 proposed `position.z` when the path was diagonal `(1,0,1)`. After the rework to world axes `-X/+Z` (iter 2 addendum), `position.z` would ignore progress on `-X` segments. The projection `Dot(pos - origin, (-1,0,1)/√2)` captures both correctly.
- **`ScoreCalculator` as a pure `static class`.** Separating arithmetic from side effects (raises, PlayerPrefs) allows trivial EditMode tests and reduces `ScoreManager` to non-testable 1-liners (the event wires). YAGNI: no `IBestScoreStore` introduced — `PlayerPrefs` with key `"BestScore"` is the whole story.
- **`GemSpawner` with its own RNG.** Alternative: pass `PathGenerator`'s `System.Random`. Rejected because it couples the two systems. Each has its own independent `System.Random` seeded with the same `_config.GenerationSeed`; the sequences are consumed without contaminating each other and the run remains byte-for-byte reproducible by seed.
- **Score is broadcast only when the integer changes.** The projected `progress` is a float but the score is int; most frames `Mathf.FloorToInt` doesn't cross a threshold. Without this guard the HUD would reprint 60×/s.
- **NEW RECORD badge resolved without assuming subscriber order.** Unity does not guarantee the order in which `GameEventSO` listeners run. `ScoreManager.HandleGameOver` and `UIController.HandleGameOver` may fire in any order. The solution is a `_newBestSeenInThisRun` bool that the `_onBestScoreChanged` handler sets when it sees a new record, and two badge activation points (one in each handler) that check both the flag and `_gameOverPanel.activeSelf`. Functionally correct in both orderings.

### Pending — manual setup in Unity (still not done)

Fully covered in `docs/superpowers/plans/2026-05-22-iteration-4-gems-and-score.md` Task 12. Summary: create `P_Gem.prefab`, add GameObjects `GemPool / GemSpawner / ScoreManager`, wire UI texts (HUD + GameOver + NewRecordBadge), wire `GameBootstrap` with the new refs, tag the ball as `Player`, set default values in `SO_GameConfig`. Until the wiring is complete, the code compiles but the scene runs the iteration 3 flow (no gems, no real score, HUD shows the "Score: 0" placeholder).

### Next iteration (outline)

5. Magnet powerup (`IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool`). Attracts gems within radius `R` for `T` seconds. GDD §14 day 5. Proves that the architecture is extensible without touching `Gem`/`ScoreManager`.

---

## 2026-05-23 — Iteration 4.1: split gem coins ↔ distance score

### Goal

Conceptually and code-wise separate the two "score" sources combined in iteration 4: gems become persistent currency prepared for a future shop; `ScoreManager` becomes a pure distance tracker. Partially reopens the closed GDD §11 decision ("No coin system / shop out of scope") — the shop stays out, but the wallet infrastructure does come in.

### What was implemented

1. **New sub-feature `Gameplay/Economy/`** with `CoinsWallet.cs`. A single `MonoBehaviour` responsible for the PlayerPrefs key `"Coins"`. Subscribed to `SO_OnGemCollected` (adds to wallet + session) and `SO_OnGameReset` (resets session, wallet untouched). Persistence on every pickup. No `Spend` API — the shop iteration will add it.

2. **`ScoreManager` refactor**: deleted `_gemScore`, `_onGemCollected`, `HandleGemCollected`. `CurrentScore = _distanceScore` directly. Public names and PlayerPrefs key `"BestScore"` kept intact by user decision; only the semantics change (it now is pure distance).

3. **`GameConfigSO._gemValue`** default `10 → 1`. Tooltip clarified: "Coins awarded per gem collected. Powerups may temporarily override this multiplier at runtime."

4. **`UIController` extended** with `_hudCoinsText` and `_gameOverSessionCoinsText`. Two new channels: `SO_OnCoinsChanged` (HUD wallet) and `SO_OnSessionCoinsChanged` (GameOver panel `+N coins`).

5. **`GameBootstrap`** adds ref + assert for `_coinsWallet`.

6. **Docs**: GDD §5.5, §7.2 (split into 7.2.1 score and 7.2.2 coins), §10.2, §10.3, §11; architecture §6.2, new §7.17 `CoinsWallet` (GameBootstrap moves to §7.18), new ADR-013 "Wallet separate from score, persisted per pickup".

### Decisions made in this split

- **No renames** (explicit user instruction). `CurrentScore`, `BestScore`, `SO_OnScoreChanged` and the PlayerPrefs key `"BestScore"` are kept even though they now reflect only distance. Makes diffs in existing consumers null; the cost is that a new reviewer needs to read the updated XML doc to understand the semantics.
- **Persistence per pickup, not per GameOver.** `PlayerPrefs.SetInt + Save` is cheap (microseconds per write) and a hard quit mid-run shouldn't rob the player of coins. For `BestScore` the opposite decision (write only on GameOver) remains correct — a partial score has no value.
- **1 gem = 1 coin as the tunable default.** Future powerups will be able to temporarily multiply it. Design deferred to the first powerup that needs it — the suggested path (in the spec's future considerations) is for `GemSpawner` to consult a modifier service instead of reading `_config.GemValue` raw.
- **The PlayerPrefs key `"BestScore"` is not migrated.** Pre-iteration values may contain `distance + gems`. With no real players, migration code isn't worth it. Manual reset via `Edit → Clear All PlayerPrefs` if a clean baseline is desired.
- **No new tests for `CoinsWallet`.** It's `+=` + `PlayerPrefs.SetInt` with no invariants. When `TrySpend(int amount)` with a funds guard appears, that one will deserve EditMode tests.

### Pending — manual setup in Unity

1. **Create 2 event SOs** in `Assets/Settings/Events/`:
   - `SO_OnCoinsChanged.asset` (IntGameEventSO).
   - `SO_OnSessionCoinsChanged.asset` (IntGameEventSO).
2. **GameObject `CoinsWallet`** in scene with the new component. Slots: `_onGemCollected` (existing `SO_OnGemCollected`), `_onGameReset` (existing), the 2 new outbound SOs.
3. **HUD Canvas**: add a `TextMeshProUGUI` `Coins: 0` in a corner opposite the score (e.g. bottom-left).
4. **GameOver Canvas**: add a `TextMeshProUGUI` `+0 coins` below the final score.
5. **`UIController`**: drag the 2 new TMPs + the 2 new SOs into their slots.
6. **`GameBootstrap`**: drag the `CoinsWallet` GameObject into the new slot.
7. **`SO_GameConfig.asset`**: change `_gemValue` from 10 to 1.

### Next iteration (outline)

5. Magnet powerup (`IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool`). Attracts gems within radius `R` for `T` seconds. GDD §14 day 5.

Eventual: coin multiplier powerup (atomic with the magnet or separate). Design deferred until needed — see future considerations in the iteration 4.1 spec.

### Addendum (same day, post-wiring)

Semantic swap HUD ↔ GameOver after playtest:

- **HUD**: `+{sessionCoins}` (coins earned in the current run). Auto-resets on each Retry via `SO_OnSessionCoinsChanged.Raise(0)` that `CoinsWallet.HandleGameReset` already triggered.
- **GameOver**: `Coins: {totalCoins}` (persistent total wallet, what the player "has in their pocket" for a future shop).

Reason: the HUD shows in-run progress (immediate motivation); GameOver is the place where it makes sense to read the accumulated balance (preview of what can be spent). The initial split was inverted and it showed: during the run you don't need to see the persistent total, since you can't spend it yet.

Code changes (separate commit): `UIController._gameOverSessionCoinsText` → `_gameOverTotalCoinsText` (with `[FormerlySerializedAs]` to keep the existing scene wiring). Swapped which TMP each handler writes to. Spec §4.4, §5, §6 and new §12 updated.

---

## 2026-05-24 — Iteration 4.2: forward-only camera

### Goal

Fix the camera follow so it advances **only** along the global forward axis `(-1, 0, 1)/√2`, reproducing the behavior of the original ZigZag: the camera moves up on screen, the ball snakes laterally on top of it. The previous implementation followed target X and Z independently, which kept the ball centered and removed the visual oscillation.

### What was implemented

1. **`CameraFollowMath`** (`Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs`)
   Pure static helper. `ComputeDesiredPosition(cameraOrigin, targetOrigin, targetCurrent, forwardAxis, lockedY)` projects the target delta onto the forward axis and returns the desired position (with Y locked). No Unity lifecycle, testable in EditMode the same as `ScoreCalculator`.

2. **`CameraFollowMathTests`** (`Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs`)
   Seven tests: static target, pure +Z movement, pure -X movement, perpendicular movement (must yield zero), pure diagonal, Y fall (must not affect XZ), and verification that `lockedY` overrides `cameraOrigin.y`.

3. **`CameraFollow` refactor** (`Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs`)
   `_horizontalOffset` replaced by `_cameraOrigin` + `_targetOrigin`. `LateUpdate` delegates to `CameraFollowMath` and applies `SmoothDamp` toward the result. File-local constant `GlobalForward = (-1, 0, 1).normalized` (deliberately duplicated with `PathGenerator`; deduplicating is a separate iteration).

### Decisions made during implementation

- **No clamp on forward progress.** If the target moves backwards in forward (does not happen in normal gameplay — only when recapturing origins in `SetTarget`), the math supports it with no special case.
- **`GlobalForward` is not moved to `GameConfigSO`.** Single source of truth would be nice, but it drags `PathGenerator` and `ScoreManager` into the refactor. Out of scope for this iteration.
- **No change to `orthographicSize`.** The change may reveal visible lateral drift; if so, it's handled as separate tuning, not bundled with the camera change in the same commit.

### ADR

- **ADR-014** added: "Camera advances only along the global forward axis". See `zigzag_architecture.en.md`.
- **ADR-007** updated with cross-reference to ADR-014.

### Pending

- Tuning of `orthographicSize` if the manual verification justifies it.
- (Long term) Move `GlobalForward` into `GameConfigSO` as the single source of truth shared with `PathGenerator` and `ScoreManager`.

---

## 2026-05-25 — Iteration 6: audio + freeze frame on death

### Goal

First batch of functional polish skipping the magnet powerup (explicit scope decision, see memory `project_scope_magnet_skipped.md`). Three SFX wired via SO channels + 0.1 s hit-stop on death. Without touching particles or trail — those are left for a later iteration if requested.

### What was implemented

1. **`GameConfigSO` extended** with a `Game Feel` section:
   - `_freezeFrameOnDeathSeconds = 0.1f` (tunable, 0 disables the effect). Tooltip clarifies that it is `Time.timeScale = 0` real-time seconds and that the GameOver UI appears **after** the freeze.
   - `OnValidate` clamps to non-negative.

2. **`BallController` outbound channel**:
   - New `[SerializeField] private GameEventSO _onDirectionChangedChannel` (optional, null-safe). Raise inside `FlipDirection` right after the existing C# `event Action<Vector3>`.
   - The `event Action<Vector3>` is kept — still useful for local listeners in the same asmdef (for example, a future `TrailRenderer` driver that needs the concrete vector). The added SO channel is parameterless because audio only needs "something changed".
   - `ZigZag.Runtime.Gameplay` asmdef already referenced `Events`, no need to touch it.

3. **`GameStateMachine` freeze frame**:
   - New `[SerializeField] GameConfigSO _config` + assert in `Awake`.
   - `HandleBallFell` now performs the transition **synchronously** (`CurrentState = GameOver`) so that a second `OnFell` in the same frame is filtered by the guard, and then triggers `_endGameRoutine = StartCoroutine(EndGameRoutine())`.
   - `EndGameRoutine`: `Time.timeScale = 0` → `WaitForSecondsRealtime(duration)` → `Time.timeScale = 1` → `_onGameOver.Raise()`. The GameOver panel appears **after** the hit-stop, giving weight to the moment of impact.
   - Defensive `OnDisable`: stops the running coroutine and restores `Time.timeScale = 1f` if it was left at 0 (defends against scene unload mid-freeze).
   - Removed the private helper `EndGame()` — its only call (from `HandleBallFell`) now lives in the coroutine path, and keeping it would be a one-line wrapper with no value.

4. **New Audio layer** — asmdef `ZigZag.Runtime.Audio` referencing only `ZigZag.Runtime.Events`. Follows the presentation rule (UI/Audio/VFX only listens, never drives).
   - `AudioManager.cs` (`MonoBehaviour sealed`, `[DisallowMultipleComponent]`, `[RequireComponent(AudioSource)]`):
     - 3 SO channel slots: `_onDirectionChanged` (parameterless, the one `BallController` now raises), `_onGemCollected` (existing `IntGameEventSO`; the payload is discarded with `int _`), `_onGameOver` (existing `GameEventSO`).
     - 3 `AudioClip` slots + 3 `[Range(0,1)] float` for per-clip volume (default flip 0.7, gem/death 1.0).
     - `Awake` grabs the `AudioSource`, forces `playOnAwake=false` and `loop=false` in case the prefab is misconfigured.
     - Symmetric `OnEnable`/`OnDisable` (CLAUDE.md §7). `=>`-bodied handlers calling `PlayOneShot(clip, volume)`. `PlayOneShot` with `clip == null` → silent no-op, so you can test the wiring before the clips arrive.

### Technical decisions

- **Hit-stop before the raise, not after.** Alternative considered: immediate raise + freeze on top. Rejected because the UI panel would appear instantly, perceptually nullifying the freeze — all that would stay frozen is the path, with no narrative value. With the current order, the 100 ms of freeze are what separate "the ball fell" from "the panel appears", which is what the classic arcade game feel demands.
- **`PlayOneShot` with a single shared `AudioSource`**, not one `AudioSource` per clip. `PlayOneShot` allows overlaps and keeps the component count down. If at some point a SFX needs its own pitch/spatial settings, it gets promoted to its own `AudioSource` — not the case yet.
- **Parameterless SO channel for direction change**, not `Vector3GameEventSO`. Audio doesn't need the vector. If future VFX does need it, a second channel is added or a specific `Vector3GameEventSO` is introduced — YAGNI for now.
- **`AudioManager` is not asserted in `GameBootstrap`.** Consistent with the current rule: `UIController` is not asserted either, because that would force `Core → UI` and break the asmdef graph direction. Audio is "presentation" in the same layer as UI; it self-validates.
- **Per-clip volumes serialized on the `AudioManager`**, not on `GameConfigSO`. They are mixing gain, not game design — they live with whoever plays them. If an `AudioMixer` with groups (`SFX`, `Music`) is later needed, it slots between the `AudioSource` and the clips without touching this code.

### Pending — manual setup in Unity

1. **Create `SO_OnDirectionChanged.asset`** in `Assets/Settings/Events/` (`Create → ZigZag → Events → Game Event`).
2. **Drag it to the `_onDirectionChangedChannel` slot of the Player's `BallController`**.
3. **`SO_GameConfig.asset`**: the `Freeze Frame On Death Seconds` field will appear in the `Game Feel` section; value 0.1 (default is already 0.1, so just verify).
4. **Drag `SO_GameConfig` to the new `_config` slot of `GameStateMachine`** (new field, appears under `Dependencies`).
5. **Create GameObject `AudioManager`** in the scene with the `AudioManager` component + an `AudioSource` (added by `[RequireComponent]` automatically). Slots:
   - `_onDirectionChanged` → newly created `SO_OnDirectionChanged.asset`.
   - `_onGemCollected` → `SO_OnGemCollected.asset` (existing).
   - `_onGameOver` → `SO_OnGameOver.asset` (existing).
   - `_directionFlipClip`, `_gemCollectedClip`, `_gameOverClip` → 3 `AudioClip` to source (jsfxr / freesound CC0). Empty slot → no crash, that SFX just doesn't play.
6. **Source 3 clips** and drop them in `Assets/Audio/`. GDD §9 recommendations: 50 ms click, 200 ms chime, 300 ms low impact.

### Verification

- Play → click → ball flips → `directionFlipClip` plays (if assigned).
- Pick up a gem → `gemCollectedClip` plays.
- Fall off the path → 100 ms freeze (the ball is frozen mid-fall) → `gameOverClip` plays and the GameOver panel appears. Retry → the ball goes back to Menu without `Time.timeScale` getting stuck at 0 (if it does, there's a bug — check that `OnDisable` is not firing mid-coroutine).
- Profiler / Audio Mixer: 0 `AudioSource` instances created at runtime; the only one is the one on the `AudioManager` GameObject.

### Next iteration (outline)

Remaining visual polish (trail renderer on the ball, particles on gem/death) or skip it and close the deliverable (README + Windows build). Decision to be made at the start of the next session.

### Same-day addendum — ball rotation

Added visual rolling without slip in `BallController`:

- New `[SerializeField] private float _ballRadius = 0.5f`. Its own `Visual Rolling` section, separate from gameplay tuning because it is a presentation value (coupled to the sphere primitive's render, not to movement feel). `OnValidate` clamps to `>= 0.01` to avoid division by zero.
- `_rollAxis = Vector3.Cross(Vector3.up, CurrentDirection)`. For `(-1,0,0)` it yields `+Z`; for `(0,0,1)` it yields `+X`. Cached and recomputed in `Awake`, `ResetTo` and `FlipDirection` — the cross is done once per direction change, not per frame.
- `Update` applies `transform.Rotate(_rollAxis, CurrentSpeed * dt * Rad2Deg / _ballRadius, Space.World)` at the end of the tick, after the position/speed update. The formula is the no-slip rolling one: ω = v/r (rad/s). It applies both when the ball is grounded and while falling (until it crosses `FallThreshold` and `IsMoving` is set to `false`, at which point `Update` early-returns and the rotation freezes along with position).
- `ResetTo` now also resets `transform.rotation = Quaternion.identity` so every run starts with a clean orientation (invisible with a flat-color sphere, but important once there's a texture/skin with directional detail).

### Technical decision

- **No slerp/lerp on the rotation axis change.** When flipping direction the axis changes instantly (from +Z to +X or vice versa). A smooth transition between axes would give a "wobble" incoherent with the game's instantaneous change in linear direction. The ZigZag style rewards clean inputs; the rolling follows the same principle.
- **`_ballRadius` is not moved to `GameConfigSO`.** It is a presentation parameter tied to the visual GameObject, not to game feel. If someone changes the sphere's `transform.localScale`, they should also adjust this field in the same Inspector — keeping them together prevents drift between visual scale and angular speed.

### Pending — manual setup

None. The default `_ballRadius = 0.5f` works with Unity's sphere primitive at scale 1 without touching anything. If the visual changes size at some point, adjust the field on the `BallController` component.

---

## 2026-05-25 — Iteration 5: skins shop (in parallel with iter 6)

### Goal

Replace the descoped magnet powerup with a shop accessible from the Menu where the player spends coins from `CoinsWallet` to buy and equip cosmetic ball skins (material swap only). Proves that the SO + channels architecture handles a new feature without touching `Gem`, `ScoreManager` or `BallController`. Iter 5 lived on a branch parallel to iter 6 (game feel) — the `d1f348e` merge integrates both on the same day.

### What was implemented

1. **New `Cosmetics/` layer** inside `ZigZag.Runtime.Gameplay` (no dedicated asmdef — sub-feature):
   - `BallSkinSO` — per-skin data: `Id` (stable, persistence contract, never rename), `DisplayName`, `Price`, `Material`. `OnValidate` requires `Id` and `Material`.
   - `BallSkinCatalogSO` — ordered array of skins. The first is the default (`Price = 0`, always owned). `GetById` with manual loop (no LINQ, CLAUDE §8 hot-path rule). `OnValidate` checks ID uniqueness and `Price == 0` on the first entry.
   - `SkinInventory` — sole owner of the PlayerPrefs keys `"OwnedSkins"` (CSV) and `"EquippedSkin"` (id). Listens to `SO_OnSkinPurchaseRequested` (validates with `CoinsWallet.TrySpend` + auto-equips) and `SO_OnSkinEquipRequested` (changes equipped if already owned). Persistence on every mutation — an alt-F4 mid-shop doesn't steal a paid skin. `Start` raises `SO_OnSkinEquipped` with the currently equipped skin to render on the first frame.
   - `BallSkinApplier` — lives on the ball, listens to `SO_OnSkinEquipped` and does `MeshRenderer.sharedMaterial = skin.Material`. `sharedMaterial` deliberately: `.material` would instantiate heap allocs and break batching.
   - `AssemblyInfo.cs` with `[InternalsVisibleTo("ZigZag.Tests.EditMode")]` to test `ParseOwnedCsv`.

2. **`CoinsWallet.TrySpend(int amount)`** added — returns `bool`. Guards against `amount <= 0` and `TotalCoins < amount`. Persists and raises `SO_OnCoinsChanged` only on success. `SessionCoins` is not touched (spending is not a run event). Covered by 3 new EditMode tests (`CoinsWalletTests`).

3. **Shop UI**:
   - `ShopRowView` — pure presentation. `Bind(skin)` sets name/swatch (material color)/listener; `Refresh(owned, equipped, canAfford)` updates the button label (`BUY 50`, `EQUIP`, `EQUIPPED`) and `interactable`. Click raises `SO_OnSkinPurchaseRequested` or `SO_OnSkinEquipRequested` with the `Id` as payload.
   - `ShopPanel` — overlay over the Menu. `Start` builds one row per catalog entry inside a `VerticalLayoutGroup`. `OpenShop()` activates the root + raises `SO_OnShopOpened`; `CloseShop()` closes it + raises `SO_OnShopClosed`. Listens to `SO_OnInventoryChanged` and `SO_OnCoinsChanged` to refresh.
   - `UIController.OnShopButtonClicked()` added — wired to the Menu's SHOP button via inspector, calls `_shopPanel.OpenShop()`.

4. **`InputHandler` double UI ↔ gameplay suppression**:
   - New refs `_onShopOpened` / `_onShopClosed` → `_isBlocked` flag blocks ALL input (mouse + Space) while the shop is open.
   - Additional guard `EventSystem.current.IsPointerOverGameObject()` — if the click fell on a UI raycast target (Menu's SHOP button, GameOver's RETRY button, any future widget on top of gameplay), the tap is discarded. Space is unaffected (it has no position).
   - `ZigZag.Runtime.Input.asmdef` now references `Events`.

5. **`ZigZag.Runtime.UI.asmdef`** adds a reference to `ZigZag.Runtime.Gameplay` so that `ShopPanel`/`ShopRowView` can type `BallSkinSO`, `SkinInventory` and `CoinsWallet`.

6. **5 new SO channels** in `Assets/Settings/Events/`:
   - `SO_OnSkinPurchaseRequested` (String) — UI → Inventory.
   - `SO_OnSkinEquipRequested` (String) — UI → Inventory.
   - `SO_OnSkinEquipped` (String) — Inventory → BallSkinApplier + ShopPanel.
   - `SO_OnInventoryChanged` (parameterless) — Inventory → ShopPanel.
   - `SO_OnShopOpened` / `SO_OnShopClosed` (parameterless) — ShopPanel → InputHandler.

7. **Assets**: 5 materials (`M_BallSkin_Default/Red/Green/Blue/Gold`) + 5 `SO_Skin_*.asset` + `SO_BallSkinCatalog.asset` + `P_ShopRow` prefab.

8. **`GameBootstrap`** validates `_skinInventory` and `_ballSkinApplier` in `Awake` with `Debug.Assert` — consistent with the bootstrap's local validation rule.

9. **EditMode tests**:
   - `CoinsWalletTests` (3) — `TrySpend` deducts + raises / preserves on insufficient funds / fails on `amount <= 0`.
   - `SkinInventoryTests` (4) — `ParseOwnedCsv` with all IDs / with unknown IDs / with whitespace / with empty or null CSV. TearDown destroys the instantiated `BallSkinSO`s (fix for the leak detected in `c1f89ad`).

### Technical decisions

- **Catalog-of-SOs, not enum.** Adding a skin is creating an `.asset`, not recompiling. Cost: one indirection per lookup; payoff: pure editorial workflow.
- **`Id` separate from the asset's `name`.** The asset can be renamed without breaking PlayerPrefs; the `Id` is the contract.
- **Double UI/tap suppression.** The `_isBlocked` block covers Space; the `IsPointerOverGameObject()` guard covers clicks landing on buttons. Neither alone is enough — Space ignores the pointer; clicks outside the shop still need to pass through.
- **Auto-equip on purchase.** A skin you buy without knowing where to activate it is gratuitous friction. The classic Ketchapp UX (ZigZag gym, Crossy Road) auto-equips, so it's replicated.
- **No "Restore Purchases" button / no IAP.** Free skins paid for with in-game currency; the prototype does not touch real stores. If someday monetized, the SkinInventory stays; a separate service is added.

### Pending — manual setup in Unity

Covered in the plan `docs/superpowers/plans/2026-05-25-shop-and-ball-skins.md` (tasks 12–18). Summary: create the 5 event SOs + 5 skin SOs + catalog + `P_ShopRow` prefab, mount `ShopPanel` as a child of the Canvas with `VerticalLayoutGroup`, add the SHOP button on the MenuPanel, wire `BallSkinApplier` on the ball's GameObject, wire `SkinInventory` at root. If the new Bootstrap refs are left empty, the `Debug.Assert`s catch it at play.

### Verification

- Play → Menu visible → SHOP button opens overlay → 5 rows. Default = `EQUIPPED`, others with price.
- Click `BUY` with insufficient coins → button greyed, unresponsive.
- Collect gems until you have 50 coins → `BUY 50` becomes interactable. Click → coins drop, the row switches to `EQUIPPED`, the ball changes color in real time (`OnSkinEquipped` swaps the material).
- Close shop → tap on screen starts the run. Click on the SHOP button does not start the run (UI raycast guard).
- Quit & relaunch → coins and equipped skin persist.

---

## 2026-05-25 — Iteration 7: cyclic color palette

### Goal

Give the game visual identity without external assets. Every N points the path and background change to a pair of complementary colors, lerping smoothly. Zero cost in art (everything is HSV sampling), high value in player motivation (each threshold feels like a mini-milestone).

### What was implemented

1. **Sub-feature `Gameplay/Aesthetics/`** inside the `ZigZag.Runtime.Gameplay` asmdef:
   - `PaletteRulesSO` — configurable asset. `Timing` block (`ScoreThresholdStep = 50`, `TransitionSeconds = 1.5`), `HSV Sampling` (`SaturationRange = (0.55, 0.85)`, `ValueRange = (0.70, 0.95)`, `MinHueDistanceFromPrevious = 0.15` — avoids near-identical palettes), `Initial Colors` (boot platform and camera, matched to the project's current ones), `Shader` (`_Color` by default; switching to `_BaseColor` migrates to URP without touching code). `OnValidate` clamps everything.
   - `PaletteSampler` — internal, pure `static class`. `Sample(rng, rules, previousPrimaryHue)` returns `(platform, camera, primaryHue)` with the camera using the complementary hue (0.5 offset on the wheel). Loop of up to 8 attempts to respect `MinHueDistanceFromPrevious`; if not met, accepts the last sampled (graceful degradation). Helpers `ComplementHue` and `CircularDistance` exposed for tests.
   - `PaletteController` — `MonoBehaviour sealed`:
     - Listens to `SO_OnScoreChanged` (`IntGameEventSO`). Counts how many `ScoreThresholdStep`s have been crossed; when it rises by one new, triggers `TriggerSwap`.
     - `LerpRoutine` (coroutine) interpolates `_currentPlatformColor` → `targetPlatform` and `_currentCameraColor` → `targetCamera` over `TransitionSeconds`. Cancels an in-progress transition if another comes in (they don't accumulate).
     - Listens to `SO_OnGameReset` — back to initial colors, re-seeds `System.Random`, `_lastThresholdReached = 0`.
     - Listens to `SO_OnGameStarted` defensively — resets the threshold counter in case the order of GameReset handlers differs.
     - `Shader.PropertyToID` cached to avoid by-name lookup on every `SetColor`.

2. **`PlatformPool.RuntimeMaterial`** — new property. In `Awake` the pool clones the prefab's material (`new Material(prefabRenderer.sharedMaterial)`) and assigns it as `sharedMaterial` to every cube it creates. `PaletteController` mutates this runtime material — all pool cubes change color with a single `SetColor`. Destroyed in `OnDestroy` so it doesn't leak.

3. **Determinism consistent with `PathGenerator`**: `PaletteController` uses its own `System.Random` seeded with `GameConfigSO.GenerationSeed` (same sentinel: `0` → `Environment.TickCount`). Independent instances to avoid contaminating the generator's sequence.

### Technical decisions

- **Runtime material on the pool, not per cube.** If each cube instantiated its own `.material`, batching would break (60+ draw calls vs. 1) and a palette swap would require `N*SetColor` instead of just one.
- **Complementary, not analogous or triadic.** High contrast between path and background eases reading in motion. Analogous/triadic palettes work for static art, not for a reaction game.
- **`MinHueDistanceFromPrevious = 0.15`.** Empirically, below 0.12 two consecutive palettes feel "the same"; above 0.20 each jump is violent. 0.15 is the sweet spot.
- **Lerp platform and camera in the same coroutine.** Starting two independent lerps would drift if the `Time.deltaTime` aren't identical (they aren't if handlers come in at different points of the frame). A single coroutine guarantees sync.
- **No `PaletteController` assert in `GameBootstrap`.** Same reason as `UIController`/`AudioManager`: it is a presentation layer, self-validates.

### Pending — manual setup in Unity

1. Create `SO_PaletteRules.asset` (`Create → ZigZag → Aesthetics → Palette Rules`). Defaults are reasonable.
2. GameObject `PaletteController` with the component. Slots: `_camera` (Main Camera), `_platformPool` (the `PlatformPool` GameObject), `_rules` (`SO_PaletteRules`), `_config` (`SO_GameConfig`), `_onScoreChanged`, `_onGameReset`, `_onGameStarted`.

### Verification

Play → boot with initial colors (blue path + light grey background). When the score crosses 50 → ~1.5s lerp to a new palette. Every 50 points, a new change. Retry → back to initial colors with the same seed (if `GenerationSeed != 0`) or a new one (if 0).

---

## 2026-05-26 — Iteration 8: final polish (falling platforms + mobile build + audio assets)

### Goal

Close the visible loop for the deliverable: the platforms the ball has already crossed collapse (reinforcing the game's no-return feel), Windows build configured in mobile portrait format, audio clips imported, final score balance. One-day-session iteration with several small commits.

### What was implemented

1. **`PlatformFaller`** (`Assets/Code/Runtime/Gameplay/World/PlatformFaller.cs`):
   - `MonoBehaviour sealed [DisallowMultipleComponent]` added to the `P_PlatformCube` prefab.
   - Hand-rolled fall animation (no Rigidbody — the ball remains kinematic, and `PhysX` would collide with the ground raycast). `Begin()` starts; `Update` integrates gravity (`_gravity = 18` u/s² by default). Constant `MaxFallDistance = 60` prevents a cube that entered falling but is never recycled (because the game ended, for example) from collapsing forever consuming CPU.
   - `Begin()` is idempotent. `OnDisable` resets state — the pool deactivates the cube on `Release`, so the next time `Get()` pulls it and `PathGenerator` repositions it, the faller is clean.

2. **`GameConfigSO._platformFallStartBehind = 1.5f`** — distance (projected onto `GlobalForward`) behind the ball at which a cube begins to fall. 1.5 ≈ 2 cubes behind (each step contributes ~0.707 to the forward axis), enough for the cube to be visually out of focus before it starts falling.

3. **`PathGenerator.TriggerFalls()`** new step in `Update`:
   - Iterates `_segments` (the Queue) in order — the `foreach` over `Queue<T>` uses a struct enumerator, zero allocs per frame (CLAUDE §8 satisfied).
   - Per segment, starts from `Segment.FallTriggerIndex` (monotonic watermark) and advances until the first cube still ahead of the ball. This avoids rescanning already-triggered cubes.
   - For each cube behind the threshold, calls `PlatformFaller.Begin()`. `Begin()`'s idempotency covers the rare case of double trigger.
   - If a cube is still ahead (forwardOffset > -threshold), early-returns — progress along the path is monotonic, no need to keep looking.

4. **`Segment.FallTriggerIndex`** — new internal int + `AdvanceFallTrigger()`. Watermark over cubes already processed by `TriggerFalls`. Resets when the segment is discarded (the pool already recycles the cube, the watermark dies with the segment).

5. **`PathGenerator.RecycleBehind` also sweeps gems**: once per frame, after Dequeue, `GemSpawner.ReleaseGemsBehind(ballPos, GlobalForward, BehindBuffer)` picks up any uncollected gem left behind. Previously the gem pool could grow indefinitely if the player kept dodging gems.

6. **Mobile-portrait build config**:
   - `SampleScene` added to `EditorBuildSettings.scenes` with index 0 (the only one).
   - `PlayerSettings.companyName`/`productName` adjusted; default resolution 608 × 1080 (9:16 ratio, mobile portrait); fullscreen mode `Windowed`; target orientation `Portrait`. Version → `0.9`.

7. **Rebalance `_distanceMultiplier` `3 → 1`** — the final score feels more readable at 1 point/unit. Gems are still 1 coin each (separated in the wallet).

8. **Audio assets imported** into `Assets/Audio/`: 3 SFX (`directionChange.wav`, `coinPickup.wav`, `gameOver.wav`) + 1 background music track (`music.mp3`). Wired in the existing `AudioManager` slots.

9. **Font atlas regenerated** (`9a55183`) after adding new glyphs to the UI (text `SHOP`, `EQUIPPED`, `+N coins`, etc.). Without this, TMP texts would show `□` (placeholder).

10. **`QualitySettings` migrated to `serializedVersion: 3`** (automatic by Unity 2022.3 on open; the change is committed explicitly so the repo doesn't carry drift).

### Technical decisions

- **Hand-rolled fall, not Rigidbody.** Activating a Rigidbody on every pool cube adds PhysX overhead per cube (>50 active rigid bodies in any frame), interferes with the ball's ground-check raycast (the ball could detect a falling cube as "ground") and forces precisely-timed `useGravity` and `isKinematic` disables on recycle. Hand-rolled: 3 floats, a `transform.position.y -=` and a cap, no side effects.
- **`FallTriggerIndex` watermark per segment, not per cube.** Alternative: bool per cube (`_hasBeenTriggered`). The watermark is O(1) to advance (a `++`) and O(1) to check "have I already passed this cube" (`i < watermark`). Bool per cube would require Dictionary lookups and allocs or an extra component.
- **`MaxFallDistance = 60`** as a safety net — at 18 u/s², 60 units is ~2.5s of falling. Enough time for the pool to recycle the cube in normal flow; if the run ends just before the recycle, the cube stops off-camera instead of integrating forever.
- **Gems swept once per frame, outside the segments loop**, so complexity does not multiply by the number of segments discarded in a burst frame (in menu → playing transitions).
- **Portrait build 608×1080.** 9:16 ratio ≈ modern iPhone. The technical test brief didn't specify a platform, but the original ZigZag is mobile-first; a PC build in portrait is the option that best demonstrates the code is ready for mobile target without touching the input layer (the `InputHandler` already maps touch to mouse click via Unity).
- **`_distanceMultiplier = 1`.** In playtest with 3, scores climbed to 5 digits in 30 seconds; noisy reading. With 1, the milestones (50, 100, 200) coincide with the palette swaps and progress feels measurable.

### Pending — manual setup in Unity

1. **Add `PlatformFaller` to the `P_PlatformCube` prefab** (`Inspector → Add Component → Platform Faller`). Default `_gravity = 18`.
2. Verify in `SO_GameConfig.asset` that `Platform Fall Start Behind = 1.5`.
3. Verify the `AudioManager` slots point to the imported clips.

### Verification

- Play → the ball advances → cubes ~2 behind it collapse downward, leave the view in ~1s. The ball never lands on a falling cube (the threshold respects the ground check).
- Profiler: zero `Instantiate` after the first frame, zero allocs in `PathGenerator.Update`. The pool stays stable at ~50-60 active cubes.
- Windows build produces an `.exe` at 608×1080 portrait. Audio plays in build, not only in editor.
- HUD score rises ~1 point per unit of forward progress; palette swaps land exactly every 50 points.

### Next iteration (outline)

Close the deliverable: README (en/es), `.gitignore` verified, screenshots for delivery.

### Same-day addendum — background music

Background music added (`Assets/Audio/music.mp3`) **without touching code**. The music lives as a second `AudioSource` on the `Main Camera` GameObject, separate from the one already carrying `AudioManager` (which only fires SFX via `PlayOneShot`):

- `Audio Clip = music.mp3`.
- `Play On Awake = true` — the music starts on scene load, not waiting for the first tap.
- `Loop = true` — short track that cycles indefinitely.
- `Volume = 0.036` (~3.6%) — deliberately low so SFX (flip, gem, game-over) remain the perceptual focus. Raising it is an inspector-only value change, not a code change.
- `Spatialize = false`, `Spatial Blend = 0` — pure 2D, the listener (camera) doesn't pan it.
- No `OutputAudioMixerGroup` — the prototype doesn't use AudioMixer yet.

### Technical decisions

- **Separate `AudioSource` for music, not shared with `AudioManager`.** `AudioManager` uses `PlayOneShot` with per-clip volume; mixing looping music in the same source would force cutting the music every time a SFX plays (`PlayOneShot` doesn't interrupt what's already playing, but the source's default `clip` remains in play for future `Play()` calls). Two `AudioSource`s on the same GameObject is Unity's standard solution and keeps each audio stream isolated.
- **Music on `Main Camera`, not on a separate `Music` GameObject.** The camera already carries the `AudioListener` by default (any 2D `AudioSource` will sound the same from any position), and `AudioSource`s on the listener have direct lookup with no extra lookups. For 2D global music, mounting another GameObject adds nothing.
- **No fade-in / fade-out script.** YAGNI until playtest asks for it. If later it's needed to cut the music on GameOver and bring it back on Retry, a minimal `MusicController` listening to `SO_OnGameOver` / `SO_OnGameReset` and adjusting `AudioSource.volume` will be introduced.

### Pending — manual setup

None. The component is already in the scene and serializes with `SampleScene.unity`. If you want to rebalance the mix, just edit the `Volume` field of the music `AudioSource` on the `Main Camera` Inspector.

# ZigZag — Game Design Document

> Design document for a prototype developed as a **technical test** for a Junior Game Developer position. The detailed technical architecture lives in `zigzag_architecture.en.md`.

---

## 1. Executive summary

**Genre:** Endless runner / hyper-casual arcade.
**Mode:** Infinite run with escalating difficulty.
**Platform:** PC (Windows), playable with the mouse simulating a tap.
**Engine:** Unity **2022.3.62f2 LTS**, **Built-in Render Pipeline** (project default). // TODO: evaluate URP before the vertical slice (see `CLAUDE.md` §1).
**Language:** C# with Unity's .NET. **Code in English only** (identifiers, comments, logs, asset names, commits). Design documents in Spanish for author velocity.
**Development timeframe:** 2 weeks (simulated sprint).
**Reference:** ZigZag by Ketchapp (Google Play).

**Pitch:** a ball advances on its own in a zigzag pattern along a narrow path that is generated infinitely; a single tap inverts its direction; the challenge is not to fall off.

---

## 2. Test context and constraints

### 2.1 Framing

This development simulates a **real sprint** inside a studio:
- The Producer asks for a specific game to be implemented (ZigZag).
- The Artist imposes using **only Unity primitives**.
- The team has 2 weeks.

### 2.2 Explicit constraints (from the brief)

| Constraint | Implication |
|---|---|
| No Asset Store assets | All graphic, audio and code material is created or sourced outside the Store (CC0, in-house) |
| No external plugins | Only native Unity APIs. **Excludes:** DOTween, UniRx, Cinemachine if it requires an extra package, Odin Inspector, etc. |
| Playable with the mouse | The click simulates a mobile tap. A Windows build is the target |
| Deliverable: zip without the Library folder | Documented in section 17 |

### 2.3 Work split over 2 weeks

| Week | Focus | End-of-week result |
|---|---|---|
| **Week 1** | Architecture + functional build | Game playable end-to-end. Visually austere but **complete**, with reviewable code |
| **Week 2** | Polish + deliverable | Game feel, visuals, audio, shop + skins, README, final build |

**Guiding principle:** week 1 builds clean from day one. Week 2 does NOT fix week-1 code, it only adds polish on top.

---

## 3. High concept

The player does not control the ball's speed or position. They only control **when to turn**. All of the tension comes from that constraint: one input, two states, split-second decisions. Difficulty grows organically as speed increases with distance.

---

## 4. Design pillars

These pillars are the yardstick for every decision. If a feature does not reinforce one of them, it does not enter the prototype.

1. **A single input.** Left click / tap. No complex gestures, no combos.
2. **Instant readability.** On any frame, the player understands what is happening: the ball's position, its direction, where the edge is.
3. **Game feel over content.** A short game that feels excellent is better than a long, lukewarm one.
4. **Reasonable determinism.** Same seed → same path. For debugging and so the player doesn't feel cheated.
5. **Clean, reviewable code.** A senior must be able to read the code without pain. Applies on every commit, not at the end.

---

## 5. Core mechanics

### 5.1 Ball movement

- **XZ** plane (Y height fixed except when falling).
- **Constant** speed within a frame, accelerates over time.
- Current direction ∈ `{ (-1, 0, 0), (0, 0, 1) }` — pure world axes, not 45° diagonals. The isometric camera, rotated -45° on Y, projects those world axes as on-screen diagonals, reproducing the visual zigzag of the original Ketchapp game. The path is built from cubes aligned to world axes (90° turns in world space).
- **Kinematic ball:** no Rigidbody with real gravity. Detailed justification in the architecture document.
- Falling simulated in code (own `fallSpeed`) once there is no ground.

### 5.2 Input

- **A single action:** left click (mouse) or `Space` (editor debug).
- Effect: inverts the current direction. Instantaneous change, no turn animation.
- No double-click, no hold, no swipe.

### 5.3 Path generation

- The path is composed of consecutive **segments**.
- Each segment is a straight sequence of N cubes along one of the two diagonals.
- N random between **3 and 8** cubes.
- When a segment ends, the next one starts from the last cube on the **other** diagonal.
- The spawner always keeps ~30 units of path ahead of the ball.
- Cubes that fall ~10 units behind are returned to the pool.
- Generation with a configurable seed for reproducibility.

### 5.4 Falling and death

- If the ball leaves the path, its Y starts dropping based on `fallSpeed`.
- When `position.y < -2` → `GameOver`.
- "Am I on the path?" detection: short downward raycast every frame.

### 5.5 Gems

- Spawn: when generating a segment, with **30%** probability, a gem is placed on a random cube of that segment.
- Visual: `Cube` rotated 45° (looks like an octahedron), pink material with emission.
- Trigger collider. On `OnTriggerEnter` with the ball: **+1 coin** to the persistent wallet (does not add to the score), particles, return to the pool. The per-gem value (`_gemValue` in `GameConfigSO`) is configurable.
- They do not block or deflect the ball.

### 5.6 Shop + ball skins (replaces the originally planned magnet powerup)

- **Scope decision:** the magnet powerup was descoped in favor of a **cosmetic skins shop**. Justification: same demonstration of extensible architecture (catalog of SOs + event channels), with less code surface and a playable progression loop (collect gems → spend coins → unlock cosmetics).
- **Access:** **SHOP** button on the Menu panel. The overlay suspends gameplay tapping via the `SO_OnShopOpened` / `SO_OnShopClosed` channels.
- **Catalog:** 5 default skins (`Default`, `Red`, `Green`, `Blue`, `Gold`). The default is free and always owned; the others cost coins.
- **Persistence:** `PlayerPrefs` with the keys `OwnedSkins` (CSV) and `EquippedSkin`. Each purchase is persisted immediately (not deferred to a GameOver).
- **Application in-game:** `BallSkinApplier` lives on the ball, listens to `SO_OnSkinEquipped` and swaps the `sharedMaterial`.
- **Extensibility:** adding a new skin = create a `BallSkinSO.asset` and drag it into the `BallSkinCatalogSO`. Zero recompiles.

---

## 6. Game feel parameters — initial values

These are **starting points for tuning in week 2**, not final values. They all live in a `ScriptableObject GameConfig`.

| Parameter | Initial value | Notes |
|---|---|---|
| `initialSpeed` | 5 u/s | Speed at start |
| `acceleration` | 0.05 u/s² | Constant ramp while playing |
| `maxSpeed` | 12 u/s | Cap. Above this it is unplayable |
| `fallSpeed` | 9.8 u/s | Only after falling off the path |
| `cubeSize` | (1, 0.3, 1) | Flat platform |
| `segmentMinLength` | 3 | Cubes per segment (min) |
| `segmentMaxLength` | 8 | Cubes per segment (max) |
| `gemSpawnProbability` | 0.30 | Per segment, not per cube |
| `gemValue` | 1 coin | Coins per gem (rebalanced iter 4.1: 10 → 1) |
| `distanceMultiplier` | 1 pt / u forward | Rebalanced iter 8: 3 → 1 |
| `aheadBuffer` | 30 u | Path visible ahead |
| `behindBuffer` | 10 u | Before returning to the pool |
| `cameraFollowSmoothTime` | 0.15 s | For `SmoothDamp` |
| `freezeFrameOnDeath` | 0.1 s | Pause on death |

---

## 7. Systems

### 7.1 Game states

`enum GameState { Menu, Playing, GameOver }`

- **Menu:** scene loaded, ball visible and still on the first segment, "Click to play" text.
- **Playing:** the ball moves, score rises, generation is active.
- **GameOver:** freeze frame, panel with final score + best + Retry button.

### 7.2 Score and persistence

Two separate magnitudes, persisted independently.

#### 7.2.1 Score (distance only)

- **Calculation:** projection of the ball's position onto the global forward axis `(-1,0,1)/√2`, multiplied by `distanceMultiplier` and truncated with `Mathf.FloorToInt`.
- **Best score:** `PlayerPrefs.GetInt("BestScore", 0)` on load; overwritten on entering `GameOver` if the current run beats the previous one.
- Gems do **not** contribute to the score.

#### 7.2.2 Coins / Wallet (persistent)

- Each gem grants `_gemValue` coins (default `1`) to the wallet.
- The wallet is persisted as `PlayerPrefs.GetInt("Coins", 0)` and rewritten on each pickup (robust against abrupt shutdowns).
- Coins are not spent yet — they are the basis for a future shop, out of scope for this sprint (see §11, §12).
- A per-run counter (`SessionCoins`) is reset on `GameReset` and shown on the GameOver panel as `"+N coins"`. The total wallet persists.
- There are **no** profiles or multi-user saves. A single int per device for score, another for wallet.

### 7.3 Pooling

- `UnityEngine.Pool.ObjectPool<GameObject>` (native since Unity 2021).
- Separate pools for cubes and gems.
- Initial size: 50 cubes, 20 gems.
- **Mandatory.** No `Instantiate` / `Destroy` at runtime.

---

## 8. Art and visuals

### 8.1 Style

Minimalist flat shading. No textures, only flat color materials + spot emission on gems and trail.

### 8.2 Suggested palette

| Element | Color |
|---|---|
| Platforms (alternating) | `#5BA8E0` and `#3A7FB8` |
| Background | `#1A1A2E` |
| Ball | `#0A0A0A` |
| Gems | `#E91E63` with emission `#FF4081` |
| Trail | Pink → transparent gradient |
| UI text | `#FFFFFF` |
| Ball skins | `Default`, `Red`, `Green`, `Blue`, `Gold` (individual materials in `Assets/Art/`) |
| Cyclic palette (iter 7) | Complementary HSV sampling every 50 points — the platforms' `#5BA8E0` is only the boot color |

### 8.3 Primitives

- Platforms: scaled `Cube`.
- Ball: `Sphere`.
- Gems: `Cube` rotated 45° on X and Z (visual octahedron).
- Lighting: a single `Directional Light` + ambient. No realistic shadows.

### 8.4 Camera

- **Orthographic.**
- Fixed rotation `(30, -45, 0)` → isometric view.
- Follows the ball on XZ with `Vector3.SmoothDamp`. Not parented to the ball.
- `orthographicSize ≈ 6`.

### 8.5 Effects (week 2)

- Native `TrailRenderer` on the ball, lifetime `0.25 s` (tuned down from the original `~0.5 s` after playtest — shorter reads as "speed" rather than "drag"), width `0.2 → 0` (tapering to a point). The color auto-tints to the equipped skin when bought from the shop. The material is assigned at runtime with a shader-fallback cascade by `BallTrailColorizer` so an empty slot doesn't render magenta. Implemented in iter 10.
- `ParticleSystem` burst on picking up a gem. Built procedurally in `Gem.Awake` (iter 9). World-space so the burst stays at the pickup point even if the gem and its support cube fall.
- `ParticleSystem` burst on falling. Built procedurally in `BallDeathBurst.Awake` (iter 10). 36 white→orange particles in a sphere shape, anchored to the impact point before the freeze. Optional skin sync.
- 0.1s freeze frame on death (iter 6).

---

## 9. Audio

Three SFX, no music. Sources: freesound.org (CC0) or generated with sfxr/jsfxr.

| Event | Type |
|---|---|
| Click (direction change) | Short click, ~50ms |
| Gem collected | High-pitched chime, ~200ms |
| Death | Dry low-frequency impact, ~300ms |

---

## 10. UI / UX

### 10.1 Menu screen

- Large text: **"ZIGZAG"**.
- Medium blinking text: **"Click to play"**.
- Top right corner: **"Best: XX"**.
- Background: gameplay scene already generated, ball still.

### 10.2 In-game HUD

- Top left corner: **current score** large (distance only).
- Top right corner: **best score** small.
- Coins (persistent wallet) visible in a secondary corner (e.g. bottom left) with an icon or `Coins: N` label.
- (No powerups in this prototype — the slot was reassigned to the skins shop in the Menu, see §5.6.)

### 10.3 Game Over panel

- Semi-transparent panel over the frozen scene.
- **"GAME OVER"**.
- **"Score: XX"** (final distance).
- **"Best: YY"**. If there was a new record, "New record!" in pink.
- **"+N coins"** — coins earned in the run that just ended (the total wallet persists without needing to be shown here; it appears in the HUD during the next run).
- **"RETRY"** button.

All in TextMeshPro. Simple fades, no complex animations.

---

## 11. Justified design decisions

These are decisions the reviewer will see and must understand the why behind. The technical ones live in the architecture document; here are the **game design** ones.

| Decision | Justification |
|---|---|
| Infinite mode instead of levels | This is what ZigZag actually is. Procedural generation demonstrates technical capability relevant to the role |
| Shop + skins instead of magnet powerup | Decision taken in iter 5: the shop demonstrates extensibility (catalog of SOs + event channels) with less code and delivers a playable progression loop that the magnet didn't have |
| No tutorial | If it needs explanation, the design fails pillar #2 (instant readability) |
| Shop and currency spending out of scope this sprint | Gems grant persistent currency (`CoinsWallet`, key `"Coins"`), ready for a future shop. Spending, items and the shop UI go into a later iteration. Demonstrates extensible architecture without investing time in features the test does not require |
| Orthographic camera, not perspective | Matches the original. Perspective would add occlusion problems |
| Ball always the same color | The visual style takes precedence over customization |
| Score = distance + gems, no multipliers | Simple to read for the player. Multipliers are noise in a prototype |

---

## 12. Out of scope

Explicit list to resist scope creep:

- Main menu with settings, credits, languages.
- Powerups (magnet or others — descoped in iter 5; see §5.6).
- Achievements, online leaderboards, profiles, names.
- Tutorial, onboarding.
- Music.
- Ads, IAP.
- Discrete levels.
- Multiplayer.
- Localization (everything in English in the UI for international simplicity, or everything in Spanish, a decision to be taken before day 1).
- Complex ball animations.
- Achievement / daily-objective system.

---

## 13. Prototype success criteria

### 13.1 End of Week 1 ("functional" build)

- [ ] Complete loop Menu → Playing → GameOver → Retry works.
- [ ] Path generates infinitely without frame drops.
- [ ] Ball responds to the click within 1 frame.
- [ ] There is progressive acceleration.
- [ ] Current score and best score are displayed and persisted.
- [ ] Gems are picked up and award points.
- [ ] Skins shop works (purchase with coins, equip, persist).
- [ ] No `Instantiate`/`Destroy` at runtime.
- [ ] `Assets/Code/Runtime/...` structure and asmdefs created per `zigzag_architecture.en.md` §4–§5.
- [ ] Reviewable code: clear names, no magic numbers, C# events properly subscribed/unsubscribed.

### 13.2 End of Week 2 (deliverable build)

- [x] Everything above +
- [x] Visible trail behind the ball (iter 10 — native `TrailRenderer` + `BallTrailColorizer` syncing color to the equipped skin).
- [x] Particles on key events (iter 9 — procedural burst on gem pickup; iter 10 — procedural burst on fall in `BallDeathBurst`).
- [x] Camera with `SmoothDamp` configured correctly (iter 4.2 — `CameraFollow` + `CameraFollowMath`, forward-only per ADR-014; iter 10 — snap to origin on retry).
- [x] 3 SFX working (iter 6 — flip, gem, game over; iter 8 — assets imported).
- [x] Game feel tuned: an outsider plays 3 rounds in a row voluntarily.
- [x] 5–10 basic EditMode tests passing (24 actual EditMode tests across 5 fixtures — `ScoreCalculator`, `CameraFollowMath`, `CoinsWallet`, `SkinInventory`, `PaletteSampler`).
- [x] README.md complete (bilingual; see section 17).
- [ ] Windows build compiles without critical warnings (local verification pending at iter 10 close-out).
- [x] The project opens cleanly in Unity 2022.3.62f2 with no console errors.

---

## 14. Iteration roadmap

### Week 1 — Construction

| Day | Focus | Internal deliverable |
|---|---|---|
| 1 | Project setup + base architecture | Folder structure, `GameConfig` SO, `GameEvents` static class, empty scene |
| 2 | Core movement + input | Ball moves and turns on a hand-placed static path |
| 3 | Procedural generation + pooling | Infinite path, no frame drops |
| 4 | Gems + score + persistence | Basic loop: collect gems, score rises, best is saved |
| 5 | Game states (Menu / Playing / GameOver) + minimal UI | Complete loop Menu → GameOver → Retry functional |
| 6 | Shop + skins (replaces magnet powerup) | Skins catalog, `SkinInventory` with persistence, shop overlay in the Menu |
| 7 | Buffer / testing / week-1 bug fixes | Internal "functional" build |

### Week 2 — Polish

| Day | Focus | Internal deliverable |
|---|---|---|
| 8 | Camera with SmoothDamp + Trail Renderer | Movement "feels good" |
| 9 | Freeze frame on death + visual rolling + cyclic palette | Complete visual feedback |
| 10 | Game feel tuning (speed, acceleration, widths) | Difficulty curve adjusted |
| 11 | Audio (3 SFX) + final UI tweaks | Internal "polished" build |
| 12 | README.md + documentation | Complete documentation |
| 13 | Exhaustive testing + bugfixes | Deliverable-candidate build |
| 14 | Final build + zip preparation | Zip ready to send |

---

## 15. Closed decisions (do not re-debate)

To avoid mid-sprint doubts:

- **Unity 2022.3.62f2 LTS** (version from the brief).
- **Built-in Render Pipeline** (project default). URP would only be evaluated post-prototype.
- **Kinematic** ball, no Rigidbody with real gravity.
- **Fixed orthographic isometric** camera, follows with SmoothDamp, not parented.
- Pooling with native `UnityEngine.Pool.ObjectPool<T>`.
- Persistence with `PlayerPrefs`.
- Input with `Input.GetMouseButtonDown(0)` abstracted in `InputHandler`. **No** new Input System.
- Communication between systems: **hybrid** — `GameEventSO` (ScriptableObject Event Channels) for global events + C# `event Action<T>` for local events. **No** UnityEvents, **no** statics. Detail in `zigzag_architecture.en.md` §6 / ADR-004 / ADR-010.
- Configuration in `ScriptableObject GameConfigSO` with read-only properties (encapsulation mandatory).
- `Assets/Code/Runtime/...` structure with one `.asmdef` per folder + isolated `ZigZag.Editor` and `ZigZag.Tests.*`.
- A single scene (`S_Main.unity`).
- Skins shop (replaces the magnet powerup that was originally planned; see §5.6). `BallSkinSO` catalog + `SkinInventory` with PlayerPrefs persistence.
- Infinite mode (no levels).
- Build target: PC Windows.
- **Language:** code in English (identifiers, comments, logs, commits). Design docs (`zigzag_gdd.md`, `zigzag_architecture.md`) in Spanish; English mirror files `zigzag_gdd.en.md` and `zigzag_architecture.en.md` are kept in sync.

If a temptation to change one of these arises, re-read this section before touching anything.

---

## 16. Identified risks

| Risk | Mitigation |
|---|---|
| Scope creep | Section 12 + daily review of "does this fit in scope?" |
| Game feel tuning is underestimated | Reserve the entirety of week 2, do not shorten it |
| Pooling poorly implemented | Do it on day 3, do not postpone it |
| "Am I on the path?" detection unstable | Start with a simple raycast, do not over-complicate |
| Over-engineering in the architecture | Only abstract real axes of change (config, generation, cosmetics, input). The rest, direct |
| Audio consumes excessive time | Hard cap of 2h searching for / generating SFX |
| Windows build fails at the last minute | Build from day 5, do not wait until day 14 |
| Forgetting to unsubscribe events → memory leaks | Rule: `OnEnable` += / `OnDisable` -=. No exceptions (see the architecture document) |
| Wrong Unity version | Verify 2022.3.62f2 before any serious commit |

---

## 17. Deliverable

### 17.1 Zip contents

- Full Unity project **without the `Library/` folder**.
- `README.md` at the project root.
- `Builds/` folder with a Windows build (optional but recommended).

### 17.2 README.md structure

```markdown
# ZigZag — Junior Test Prototype

## How to open the project
- Unity version: 2022.3.62f2
- Main scene: Assets/Scenes/Main.unity

## How to play
- Left click or Space: change direction
- Don't fall off the path
- Collect gems (pink) — each one adds 1 coin to your persistent wallet
- Visit the shop (**SHOP** button on the menu) to spend coins on ball skins

## Main technical decisions
[Summary + link to the architecture document]

## Project structure
[Commented folder tree]

## What I would do next if I had more time
[Honesty about the prototype's limitations]
```

The last section is **important** for a test: it shows self-criticism and product vision.

### 17.3 Pre-send checklist

- [ ] `Library/` folder removed.
- [ ] `Temp/` folder removed.
- [ ] `Logs/` folder removed.
- [ ] `obj/` folder removed.
- [ ] `.vs/`, `.idea/` removed.
- [ ] Reasonable zip size (<50 MB expected).
- [ ] The zip decompresses and opens cleanly in Unity 2022.3.62f2.
- [ ] The scene runs without console errors.
- [ ] README present and complete.
- [ ] No Asset Store assets and no external plugins.

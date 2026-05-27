# Final Polish and Deliverable Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three remaining items of `zigzag_gdd.md` §13.2 (trail behind the ball, death-impact VFX, Windows deliverable build) plus one debt-pay-down refactor (deduplicate the `GlobalForward` constant) and ship the iteration-10 devlog entry.

**Architecture:** Trail = native `UnityEngine.TrailRenderer` on `P_Ball` + a 30-line `BallTrailColorizer` (Cosmetics layer) that listens to `SO_OnSkinEquipped` and re-tints the trail to match the equipped skin material. Death burst = `BallDeathBurst` (Player layer) that procedurally builds a one-shot `ParticleSystem` child in `Awake` (mirrors the existing `Gem` burst pattern verbatim) and subscribes to `BallController.OnFell` C# event in the same assembly — no new SO channel needed. `GlobalForward` is promoted to `GameConfigSO.GlobalForward` and consumed by `PathGenerator`, `CameraFollow`, `ScoreManager` and (re)used by the two test fixtures. Build + screenshots + README update + devlog entry close the deliverable.

**Tech Stack:** Unity 2022.3.62f2 LTS, C# .NET Standard 2.1, Built-in Render Pipeline, `UnityEngine.TrailRenderer`, `UnityEngine.ParticleSystem`, NUnit (EditMode tests). No new packages.

**Naming conventions to follow (CLAUDE.md §2, §4):**
- All identifiers, comments, log strings and commit messages in English.
- `[SerializeField] private _camelCase` for inspector exposure; `public PascalCase` get-only properties.
- Namespaces `ZigZag.Runtime.<Layer>.<Feature>`.
- Files end with newline, UTF-8 (no BOM).
- Commit messages: conventional-commits prefix, no body, no Claude co-author footer (per user memory `feedback_commit_style.md`).
- No `FIXME` / `XXX` — only `// TODO: <description> (<owner-or-context>)`.

**Spec reference:** none — this plan is the spec. The remaining items were already enumerated in the conversation that produced this plan; no separate `docs/superpowers/specs/2026-05-27-*.md` document was authored.

---

## File Structure

**New code files (2):**

| Path | Responsibility |
|------|----------------|
| `Assets/Code/Runtime/Gameplay/Cosmetics/BallTrailColorizer.cs` | Listens to `SO_OnSkinEquipped`, recolors a referenced `TrailRenderer` to the equipped skin's material color. |
| `Assets/Code/Runtime/Gameplay/Player/BallDeathBurst.cs` | Builds a procedural one-shot particle burst in `Awake`; subscribes to `BallController.OnFell` and plays the burst at the ball's world position. |

**Modified code files (5):**

| Path | What changes |
|------|--------------|
| `Assets/Code/Runtime/Data/GameConfigSO.cs` | Add `public static readonly Vector3 GlobalForward = new Vector3(-1, 0, 1).normalized`. |
| `Assets/Code/Runtime/Gameplay/World/PathGenerator.cs` | Drop local `GlobalForward` field; read `GameConfigSO.GlobalForward` instead. |
| `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs` | Same: drop local field, read from `GameConfigSO`. |
| `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs` | Same: drop local field, read from `GameConfigSO`. |
| `README.md` | New §5 "Build artifact" subsection + 3 screenshot embeds in §1 / §5. |

**New non-code artifacts:**

| Path | What |
|------|------|
| `Builds/Windows/ZigZag.exe` (+ `_Data/`, `UnityPlayer.dll`, etc.) | Player build produced from `File → Build And Run`, copied to repo. |
| `docs/screenshots/menu.png` | Menu state screenshot (608×1080 portrait). |
| `docs/screenshots/gameplay.png` | Mid-run gameplay screenshot. |
| `docs/screenshots/gameover.png` | GameOver panel screenshot. |
| `devlog.md` (append) + `devlog.en.md` (append) | Iteration-10 entry describing this work. |

**Scene wiring in `Assets/Scenes/S_Main.unity` (manual via Editor, instructions inline):**

- Add a `TrailRenderer` component to the `Player` (ball) GameObject.
- Add the `BallTrailColorizer` component to the same GameObject; wire its serialized refs.
- Add the `BallDeathBurst` component to the same GameObject; wire its `BallController` ref.

---

## Task 1: Add `GlobalForward` constant to `GameConfigSO`

**Files:**
- Modify: `Assets/Code/Runtime/Data/GameConfigSO.cs`

The existing duplication (3 copies of `new Vector3(-1, 0, 1).normalized` in `PathGenerator`, `CameraFollow` and `ScoreManager`) was flagged as a known debt by the iter 4.2 devlog. Single source of truth is `GameConfigSO` because it already lives in the `Data` asmdef which every gameplay layer references.

- [ ] **Step 1: Read the existing file**

Open `Assets/Code/Runtime/Data/GameConfigSO.cs` and locate the closing `}` of the `GameConfigSO` class (line 159 in the current revision).

- [ ] **Step 2: Add the constant inside the class, right after the `OnValidate` block**

Insert the following just before the closing brace of the `public sealed class GameConfigSO` block (after the `#endif` of `OnValidate`):

```csharp
        /// <summary>
        /// Unit vector that points "down the path" in world space: the diagonal
        /// between the two world axes the ball alternates along (-X and +Z).
        /// Single source of truth shared by <c>PathGenerator</c> (ahead/behind
        /// buffer math), <c>CameraFollow</c> (locked advance axis) and
        /// <c>ScoreManager</c> (distance projection).
        /// </summary>
        public static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
```

- [ ] **Step 3: Verify Unity compiles**

Switch to the Unity Editor. Wait for the compile spinner (bottom-right). Console must show **zero errors**.

- [ ] **Step 4: Run the existing EditMode tests**

Open `Window → General → Test Runner → EditMode`. Click `Run All`. **24 tests must pass** — no regression yet because nobody is reading the new constant.

- [ ] **Step 5: Commit**

```bash
git add Assets/Code/Runtime/Data/GameConfigSO.cs
git commit -m "refactor(data): add GameConfigSO.GlobalForward constant"
```

---

## Task 2: Replace `GlobalForward` in `PathGenerator` with the shared constant

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/World/PathGenerator.cs` (lines 55-58)

- [ ] **Step 1: Locate and read the duplication**

Open `Assets/Code/Runtime/Gameplay/World/PathGenerator.cs`. Line 57 declares:

```csharp
private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
```

- [ ] **Step 2: Delete the local field**

Remove **only** the `GlobalForward` line (line 57). Keep `AlongNegativeX`, `AlongPositiveZ` and `GlobalPerpendicular` — they are not duplicated anywhere else.

- [ ] **Step 3: Replace every reference inside the class**

Search the file for the bare identifier `GlobalForward` (it appears in `EnsureAhead`, `RecycleBehind` and `TriggerFalls`). Replace each with `GameConfigSO.GlobalForward`. The `GameConfigSO` symbol is already in scope (`using ZigZag.Runtime.Data;` is at the top of the file).

Concrete edit count: 5 occurrences (verify with a final grep: `grep -n "GlobalForward" Assets/Code/Runtime/Gameplay/World/PathGenerator.cs` — only the 5 call sites should remain after the field deletion; if a 6th line still shows the old field declaration, the deletion was incomplete).

- [ ] **Step 4: Verify Unity compiles, run tests**

Switch to Unity. Console must be clean. `Test Runner → EditMode → Run All` → 24 tests pass.

- [ ] **Step 5: Manual smoke test in Editor**

Press Play. Tap to start. Confirm the path generates ahead of the ball and recycles behind exactly as before. The behavior is byte-identical — this is a pure rename.

- [ ] **Step 6: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/World/PathGenerator.cs
git commit -m "refactor(world): consume GameConfigSO.GlobalForward in PathGenerator"
```

---

## Task 3: Replace `GlobalForward` in `CameraFollow`

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs` (lines 26-28)

- [ ] **Step 1: Delete the local field**

Open the file and remove the 3-line block (line 26-28):

```csharp
        // Mirrors PathGenerator.GlobalForward and ScoreManager's forward axis. Kept
        // local to avoid coupling CameraSystem to PathGenerator; a single source of
        // truth on GameConfigSO is a separate, future refactor.
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
```

- [ ] **Step 2: Replace the only call site**

Inside `LateUpdate`, the call to `CameraFollowMath.ComputeDesiredPosition` passes `GlobalForward` as its fourth argument. Replace `GlobalForward` with `GameConfigSO.GlobalForward`. The `GameConfigSO` symbol is already in scope (`using ZigZag.Runtime.Data;` at the top).

- [ ] **Step 3: Verify Unity compiles, run tests**

Switch to Unity. Console clean. `Test Runner → EditMode → Run All` → 24 tests pass.

- [ ] **Step 4: Manual smoke test in Editor**

Press Play. Tap to start. Confirm the camera tracks the ball along the diagonal exactly as before; the ball still serpentines laterally over the camera (ADR-014 behavior preserved).

- [ ] **Step 5: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs
git commit -m "refactor(camera): consume GameConfigSO.GlobalForward in CameraFollow"
```

---

## Task 4: Replace `GlobalForward` in `ScoreManager`

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`

- [ ] **Step 1: Locate the duplication**

Open `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`. Find the line that declares `GlobalForward` as a `static readonly Vector3` initialized to `new Vector3(-1f, 0f, 1f).normalized`. (Grep first: `grep -n "GlobalForward" Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`.)

- [ ] **Step 2: Delete the field, replace call sites**

Delete the field declaration. Replace every bare `GlobalForward` reference inside the class with `GameConfigSO.GlobalForward`. Verify `using ZigZag.Runtime.Data;` is at the top; if not, add it (the file already references `GameConfigSO` indirectly via `_config`, so the using is almost certainly there already).

- [ ] **Step 3: Verify Unity compiles, run tests**

`Test Runner → EditMode → Run All` → 24 tests pass. Crucially, the `ScoreCalculatorTests` fixture exercises the same forward axis: any drift in the axis value would surface as test failures.

- [ ] **Step 4: Manual smoke test**

Play → tap → confirm the HUD score increments at the same rate as before (~1 point per unit of forward progress with `_distanceMultiplier = 1`).

- [ ] **Step 5: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs
git commit -m "refactor(scoring): consume GameConfigSO.GlobalForward in ScoreManager"
```

---

## Task 5: Create `BallTrailColorizer` component

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Cosmetics/BallTrailColorizer.cs`

The trail is a native `UnityEngine.TrailRenderer` added to the ball in Task 6 (Inspector wiring). This script's only job is keeping the trail's `startColor` / `endColor` aligned with whatever skin is currently equipped, so the trail visually reads as "the ball's slipstream".

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Keeps a <see cref="TrailRenderer"/>'s color aligned with the currently
    /// equipped <see cref="BallSkinSO"/>. Lives on the ball next to
    /// <see cref="BallSkinApplier"/>; both react to the same skin-equipped
    /// channel so the swap is atomic from the player's perspective.
    /// </summary>
    /// <remarks>
    /// The trail color is sampled from the skin's <see cref="Material"/>:
    /// <c>_Color</c> (Built-in RP) is the only property read. The end color is
    /// the same hue with alpha 0 so the tail fades smoothly. Width, time and
    /// other curve parameters are authored on the <see cref="TrailRenderer"/>
    /// component itself in the Inspector and are not touched here.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BallTrailColorizer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Trail renderer whose start/end color tracks the equipped skin. Authored on the ball GameObject; widths and curves are configured directly on the component.")]
        private TrailRenderer _trail;

        [SerializeField, Tooltip("Catalog used to resolve a skin id into its material.")]
        private BallSkinCatalogSO _catalog;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: payload is the newly equipped skin id.")]
        private StringGameEventSO _onSkinEquipped;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Debug.Assert(_trail != null, $"{nameof(BallTrailColorizer)} requires a {nameof(TrailRenderer)} reference.", this);
            Debug.Assert(_catalog != null, $"{nameof(BallTrailColorizer)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_onSkinEquipped != null, $"{nameof(BallTrailColorizer)} requires {nameof(_onSkinEquipped)}.", this);
        }

        private void OnEnable()
        {
            if (_onSkinEquipped != null) _onSkinEquipped.Register(HandleSkinEquipped);
        }

        private void OnDisable()
        {
            if (_onSkinEquipped != null) _onSkinEquipped.Unregister(HandleSkinEquipped);
        }

        private void HandleSkinEquipped(string skinId)
        {
            if (_trail == null || _catalog == null) return;
            BallSkinSO skin = _catalog.GetById(skinId);
            if (skin == null || skin.Material == null) return;

            Color tint = skin.Material.HasProperty(ColorProperty)
                ? skin.Material.GetColor(ColorProperty)
                : skin.Material.color;

            Color start = new Color(tint.r, tint.g, tint.b, 1f);
            Color end = new Color(tint.r, tint.g, tint.b, 0f);
            _trail.startColor = start;
            _trail.endColor = end;
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity. Wait for compile. Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Cosmetics/BallTrailColorizer.cs Assets/Code/Runtime/Gameplay/Cosmetics/BallTrailColorizer.cs.meta
git commit -m "feat(cosmetics): add BallTrailColorizer to sync trail color to equipped skin"
```

---

## Task 6: Add and configure the `TrailRenderer` on the ball; wire `BallTrailColorizer`

**Files:**
- Modify (Unity scene): `Assets/Scenes/S_Main.unity` (no code change)

This is Inspector-only work. The TrailRenderer is a built-in Unity component; the only "decisions" here are the visual tuning values.

- [ ] **Step 1: Open the scene and select the ball GameObject**

`File → Open Scene → Assets/Scenes/S_Main.unity`. In the Hierarchy, select the `Player` GameObject (the one that already carries `BallController`, `BallSkinApplier`, MeshRenderer, etc.).

- [ ] **Step 2: Add a `TrailRenderer` component**

Inspector → `Add Component → Effects → Trail Renderer`.

- [ ] **Step 3: Configure the TrailRenderer's tuning fields**

Set the following values in the Inspector (these are deliberately chosen for the 0.5-radius sphere primitive at scale 1, mobile-portrait camera):

| Field | Value | Why |
|-------|-------|-----|
| `Time` | `0.35` | Short tail so the trail reads as "speed" not "drag". |
| `Min Vertex Distance` | `0.05` | Smoother curve at slow speed; cheap. |
| `Autodestruct` | `false` | The pool model doesn't destroy the ball. |
| `Emitting` | `true` | Default; gets toggled below if needed. |
| `Width Curve` (start → end) | `0.45 → 0.0` | Tapers from ball radius to a point. |
| `Color Gradient` | start RGBA `(1,1,1,1)` → end RGBA `(1,1,1,0)` | Placeholder; `BallTrailColorizer` overrides on first skin event. |
| `Material` | drag `Default-Line.mat` (built-in) OR any unlit additive `Material` | Built-in `Default-Line` is fine for the deliverable. |
| `Shadow Casting Mode` | `Off` | Trails don't cast shadows in arcade aesthetic. |
| `Receive Shadows` | unchecked | Same reason. |

- [ ] **Step 4: Add the `BallTrailColorizer` component**

Inspector → `Add Component → Ball Trail Colorizer` (the type name is auto-discovered after compile).

- [ ] **Step 5: Wire its serialized refs**

In the `BallTrailColorizer` component:
- `_trail`: drag the same `Player` GameObject (so the `TrailRenderer` is resolved on the same object). Easiest: lock the Inspector (padlock icon), then drag the `TrailRenderer` line from the Inspector into the `_trail` slot.
- `_catalog`: drag `Assets/Settings/SO_BallSkinCatalog.asset`.
- `_onSkinEquipped`: drag `Assets/Settings/Events/SO_OnSkinEquipped.asset`.

- [ ] **Step 6: Save the scene**

`File → Save` (or `Ctrl+S`).

- [ ] **Step 7: Verification in Play mode**

Press Play.
- Menu state: the ball sits still at spawn; the trail is invisible (no motion → nothing emits).
- Tap to start: a short white tail follows the ball as it moves.
- Open the shop, equip a different skin (e.g. Red): close the shop, tap to start, the tail tints red and the ball tints red simultaneously.
- Fall off the path: the trail follows the ball down and fades after `Time = 0.35` seconds.

- [ ] **Step 8: Commit the scene change**

```bash
git add Assets/Scenes/S_Main.unity
git commit -m "chore(scene): add TrailRenderer + BallTrailColorizer to Player"
```

---

## Task 7: Create `BallDeathBurst` component

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Player/BallDeathBurst.cs`

Mirrors the procedural-burst pattern from `Gem.BuildPickupBurst` (see `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs` lines 178-238) so the codebase only has one "build a burst from code" pattern. Key differences from Gem's burst:
- Tint defaults to white→orange (failure color), not gold.
- Particle count and speed higher (death is more energetic than pickup).
- Subscribes to `BallController.OnFell` (C# event, same assembly) rather than `OnTriggerEnter`.
- Plays at the ball's current world position via `_burst.transform.position = transform.position` before `Play(true)` — needed because the ball keeps moving downward during the freeze-frame.

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using ZigZag.Runtime.Gameplay.Player;

namespace ZigZag.Runtime.Gameplay.Player
{
    /// <summary>
    /// One-shot particle burst played when the ball falls off the path. Built
    /// procedurally in <see cref="Awake"/> so the prefab doesn't carry a
    /// per-instance <see cref="ParticleSystem"/> component, mirroring the
    /// pattern used by <c>Gem</c> for pickup feedback.
    /// </summary>
    /// <remarks>
    /// Lives on the same GameObject as <see cref="BallController"/> and
    /// subscribes to its C# <see cref="BallController.OnFell"/> event directly
    /// (same assembly — no SO channel needed). The burst is a child of this
    /// transform and simulates in world space, so it stays put at the impact
    /// point while the ball continues its visual fall through the
    /// <see cref="Data.GameConfigSO.FreezeFrameOnDeathSeconds"/> window.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallController))]
    public sealed class BallDeathBurst : MonoBehaviour
    {
        [Header("Burst Tuning")]
        [SerializeField, Tooltip("Tint applied to the death burst. White-to-orange contrasts cleanly against every skin and the cycling palette.")]
        private Color _burstColor = new Color(1f, 0.65f, 0.25f, 1f);

        [SerializeField, Range(8, 96), Tooltip("Number of particles emitted on death.")]
        private int _burstParticleCount = 36;

        [SerializeField, Range(0.1f, 2f), Tooltip("Lifetime of each particle in seconds.")]
        private float _burstLifetime = 0.65f;

        [SerializeField, Range(1f, 16f), Tooltip("Initial outward speed of each particle, units/second.")]
        private float _burstSpeed = 7f;

        [SerializeField, Range(0.05f, 0.6f), Tooltip("Starting size of each particle in world units.")]
        private float _burstParticleSize = 0.18f;

        private BallController _ball;
        private ParticleSystem _burst;

        // Shared across every BallDeathBurst instance (the prototype has one
        // ball, so in practice this is the only ball; statics still avoid the
        // per-instance Material alloc if anyone scenes-up more balls later).
        private static Material _sharedBurstMaterial;

        private void Awake()
        {
            _ball = GetComponent<BallController>();
            _burst = BuildDeathBurst();
        }

        private void OnEnable()
        {
            if (_ball != null) _ball.OnFell += HandleFell;
        }

        private void OnDisable()
        {
            if (_ball != null) _ball.OnFell -= HandleFell;
            if (_burst != null) _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void HandleFell()
        {
            if (_burst == null) return;
            // Snap the burst host to the ball's impact position so the burst
            // is anchored where the ball left the path, not where the ball
            // ends up after the freeze-frame.
            _burst.transform.position = transform.position;
            _burst.Play(true);
        }

        private ParticleSystem BuildDeathBurst()
        {
            GameObject host = new GameObject("DeathBurst");
            host.transform.SetParent(transform, worldPositionStays: false);
            host.transform.localPosition = Vector3.zero;

            ParticleSystem ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.25f;
            main.startLifetime = _burstLifetime;
            main.startSpeed = _burstSpeed;
            main.startSize = _burstParticleSize;
            main.startColor = _burstColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = 0.6f;
            main.maxParticles = Mathf.Max(_burstParticleCount * 2, 64);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)_burstParticleCount)
            });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetOrCreateBurstMaterial();
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        private static Material GetOrCreateBurstMaterial()
        {
            if (_sharedBurstMaterial != null) return _sharedBurstMaterial;

            Shader shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Particles/Unlit");
            if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning($"{nameof(BallDeathBurst)}: no suitable shader found for the death burst material; burst will render with the default error material.");
                return null;
            }

            _sharedBurstMaterial = new Material(shader) { name = "BallDeathBurst (runtime)" };
            return _sharedBurstMaterial;
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity, wait for compile. Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Player/BallDeathBurst.cs Assets/Code/Runtime/Gameplay/Player/BallDeathBurst.cs.meta
git commit -m "feat(player): add procedural BallDeathBurst on ball fall"
```

---

## Task 8: Add `BallDeathBurst` to the ball in the scene

**Files:**
- Modify (Unity scene): `Assets/Scenes/S_Main.unity` (no code change)

- [ ] **Step 1: Select the Player GameObject in the scene**

Open `Assets/Scenes/S_Main.unity` if not already open. Hierarchy → select `Player`.

- [ ] **Step 2: Add the component**

Inspector → `Add Component → Ball Death Burst`. The `[RequireComponent(typeof(BallController))]` attribute auto-validates the dependency.

- [ ] **Step 3: Leave the tuning fields at their defaults**

The serialized defaults (`_burstColor = (1, 0.65, 0.25, 1)`, `_burstParticleCount = 36`, `_burstLifetime = 0.65`, `_burstSpeed = 7`, `_burstParticleSize = 0.18`) are tuned for the existing sphere primitive at scale 1 and the existing freeze-frame duration (0.1 s). Do not override unless the visual review in Step 5 calls for it.

- [ ] **Step 4: Save the scene**

`File → Save`.

- [ ] **Step 5: Play-mode verification**

Press Play. Tap to start. Drive the ball off the path.
- At the moment the ball leaves the path, the freeze-frame kicks in (0.1 s).
- During and immediately after the freeze, a white-to-orange burst of 36 particles appears at the impact point, spreads outward at ~7 u/s, drifts down under partial gravity, and fades to alpha 0 over 0.65 s.
- The GameOver panel appears after the freeze, on top of the residual burst — both coexist for ~0.5 s.
- Retry → the burst is fully cleaned up (no leftover particles).

Open the Profiler (`Window → Analysis → Profiler`) → CPU module. Trigger 3 deaths in a row. The "DeathBurst" GameObject count should stay at 1 (single instance, reused), and no `Material` allocations should appear after the first death.

- [ ] **Step 6: Commit the scene change**

```bash
git add Assets/Scenes/S_Main.unity
git commit -m "chore(scene): add BallDeathBurst component to Player"
```

---

## Task 9: Produce the Windows build

**Files:**
- Create: `Builds/Windows/ZigZag.exe` (+ `ZigZag_Data/`, `UnityPlayer.dll`, `MonoBleedingEdge/`, `UnityCrashHandler64.exe` — all auto-generated)
- Modify: `.gitignore` (verify `Builds/` is NOT ignored — it is by default in some templates)

- [ ] **Step 1: Verify `.gitignore` does not exclude `Builds/`**

```bash
grep -n "Builds" .gitignore
```

If the output is empty, `Builds/` is tracked. If a line `Builds/` or `/Builds/` exists, the build artifact will not be committable; remove or comment that line and commit `.gitignore` separately before continuing.

- [ ] **Step 2: Open the Build Settings**

In Unity: `File → Build Settings`.

Confirm:
- `Scenes In Build` contains exactly one entry: `Assets/Scenes/S_Main.unity` (already done in iter 8).
- `Platform` is `Windows, Mac & Linux Standalone` → highlighted with the Unity icon.
- `Target Platform` = `Windows`, `Architecture` = `x86_64`.

- [ ] **Step 3: Set the output location and build**

Click `Build`. In the folder picker, navigate to the repo root and select / create `Builds/Windows/`. Use `ZigZag` as the executable name (Unity adds `.exe` automatically).

Wait for the build. Expected duration on a typical dev machine: 1-3 minutes. The console must show no errors. Warnings about shader stripping or `IL2CPP` non-applicability are expected and benign.

- [ ] **Step 4: Smoke-test the executable**

Double-click `Builds/Windows/ZigZag.exe`. Confirm:
- Window opens at 608×1080 portrait.
- Menu → tap → ball moves, trail follows, gems spawn, score increments.
- Drive off the path → freeze-frame + death burst plays, GameOver panel appears.
- Retry → loop restarts cleanly.
- Quit the window normally (no crash on close).

- [ ] **Step 5: Commit the build artifact**

```bash
git add Builds/Windows/
git commit -m "chore(build): ship Windows standalone v0.9 in Builds/Windows/"
```

> **Note:** if `Builds/Windows/` ends up >100 MB (Git LFS threshold on GitHub), strip the `*_BurstDebugInformation_DoNotShip/` folder before committing — it's debug-only and Unity recreates it on the next build. Run `du -sh Builds/Windows/*` to check.

---

## Task 10: Capture deliverable screenshots

**Files:**
- Create: `docs/screenshots/menu.png`
- Create: `docs/screenshots/gameplay.png`
- Create: `docs/screenshots/gameover.png`

- [ ] **Step 1: Create the screenshots directory**

```bash
mkdir -p docs/screenshots
```

- [ ] **Step 2: Capture the Menu state**

Run the built executable. While on the Menu screen, take a window screenshot:
- Windows native: `Win+Shift+S` → window mode → click the game window.
- Save as `docs/screenshots/menu.png` at 608×1080 (or the captured resolution — Unity's portrait window is already this size).

The shot should show: ZIGZAG title, "Click to play", visible path behind, default ball skin, SHOP button.

- [ ] **Step 3: Capture mid-gameplay**

Start a run. Mid-zigzag (3-5 seconds in, ideally after at least one palette swap if you can reach score 50), take another window screenshot. Save as `docs/screenshots/gameplay.png`.

The shot should show: HUD score + coins, the ball with its trail visible, at least one gem on the path, the path extending ahead.

- [ ] **Step 4: Capture the GameOver state**

Fall off the path. Wait for the GameOver panel to settle (after the freeze + the death burst's fade). Take a screenshot. Save as `docs/screenshots/gameover.png`.

The shot should show: "GAME OVER" header, final score, best score, total coins, RETRY button, and ideally the last remnants of the death burst still on screen.

- [ ] **Step 5: Verify file sizes are reasonable**

```bash
ls -lh docs/screenshots/
```

Each `.png` should be 100-500 KB. If any is >1 MB, it likely captured at >2× the intended resolution — re-shoot with the window not scaled by the OS.

- [ ] **Step 6: Commit**

```bash
git add docs/screenshots/
git commit -m "docs: add deliverable screenshots (menu, gameplay, game over)"
```

---

## Task 11: Update README with build artifact and screenshot embeds

**Files:**
- Modify: `README.md`

The README currently has §5 "How to run" with two subsections (Editor / Windows build). The Windows-build subsection tells the reader to build the project themselves; with the artifact now in the repo, it should also point them at the prebuilt `.exe`. Screenshots go in §1 (overview, one hero shot) and §5 (one per state for the "How to run" preview).

- [ ] **Step 1: Read the current §1 and §5 of the English README**

Open `README.md` and locate `<a name="en-overview"></a>` and `<a name="en-run"></a>` (around lines 40 and 173 respectively in the current revision — adjust if the file has been edited since).

- [ ] **Step 2: Insert the hero screenshot in §1 (English)**

Right after the closing `</a>` of `<a name="en-overview"></a>` and the `### 1. What is this` heading, insert:

```markdown
<div align="center">
  <img src="docs/screenshots/gameplay.png" alt="Mid-run gameplay" width="304" />
</div>
```

(Width 304 = half-resolution of the 608-wide portrait shot; renders cleanly on GitHub.)

- [ ] **Step 3: Insert the same in §1 (Spanish)**

Find `<a name="es-overview"></a>` / `### 1. Qué es esto` and insert the same `<div>` block right after the heading. Same image path.

- [ ] **Step 4: Add a prebuilt-binary subsection to §5 (English)**

In `### 5. How to run`, after the existing "From a Windows build:" subsection, add:

```markdown
**Try the prebuilt binary (Windows x64):**

A signed-as-development `0.9` build is checked in at [`Builds/Windows/ZigZag.exe`](Builds/Windows/ZigZag.exe). Double-click it — no install, no dependencies beyond the Visual C++ runtime that ships with Windows 10/11.

| Menu | Gameplay | Game over |
|------|----------|-----------|
| ![Menu](docs/screenshots/menu.png) | ![Gameplay](docs/screenshots/gameplay.png) | ![Game over](docs/screenshots/gameover.png) |
```

- [ ] **Step 5: Add the same to §5 (Spanish)**

Locate `<a name="es-run"></a>` / `### 5. Cómo ejecutarlo` and add the Spanish-translated equivalent after the existing Windows build instructions:

```markdown
**Probar el binario precompilado (Windows x64):**

Hay un build `0.9` listo en [`Builds/Windows/ZigZag.exe`](Builds/Windows/ZigZag.exe). Doble-click — sin instalador, sin dependencias más allá del runtime de Visual C++ que viene con Windows 10/11.

| Menú | Gameplay | Game over |
|------|----------|-----------|
| ![Menú](docs/screenshots/menu.png) | ![Gameplay](docs/screenshots/gameplay.png) | ![Game over](docs/screenshots/gameover.png) |
```

- [ ] **Step 6: Preview the README rendering**

Open `README.md` in your IDE's Markdown preview (VS Code: `Ctrl+Shift+V`). Confirm:
- Both hero images load.
- The 3-column screenshot table renders without overflow.
- Links to `Builds/Windows/ZigZag.exe` are clickable.

- [ ] **Step 7: Commit**

```bash
git add README.md
git commit -m "docs(readme): link prebuilt Windows binary and embed deliverable screenshots"
```

---

## Task 12: Append the iteration-10 entry to the devlog

**Files:**
- Modify: `devlog.md` (append)
- Modify: `devlog.en.md` (append)

- [ ] **Step 1: Append the Spanish entry to `devlog.md`**

Open `devlog.md` and append at the very end (after the closing of iter 9):

```markdown
---

## 2026-05-27 — Iteración 10: pulido final del entregable

### Objetivo

Cerrar los tres checkboxes pendientes de `zigzag_gdd.md` §13.2 — trail visible detrás de la bola, partículas en el momento de la muerte, build de Windows con capturas — más un refactor pequeño de deuda técnica: deduplicar la constante `GlobalForward` que vivía clonada en `PathGenerator`, `CameraFollow` y `ScoreManager`.

### Lo que se ha implementado

1. **`GameConfigSO.GlobalForward`** como única fuente de verdad para el eje diagonal `(-1, 0, 1)/√2`. `PathGenerator`, `CameraFollow` y `ScoreManager` dejan de declarar el suyo y leen el del config. Cierra la deuda explícita registrada en el devlog de iter 4.2. Los 24 tests EditMode pasan sin cambios — el axis es byte-idéntico.

2. **Trail Renderer en la bola** + `BallTrailColorizer` (sub-feature `Cosmetics/`):
   - El `TrailRenderer` es un componente nativo de Unity sobre el GameObject Player; tuning (Time = 0.35, Min Vertex Distance = 0.05, Width 0.45 → 0, sin sombras) editado en el inspector.
   - `BallTrailColorizer` escucha `SO_OnSkinEquipped`, resuelve el `BallSkinSO` vía catalog, lee `Material._Color` (con fallback a `Material.color` para shaders sin esa property) y aplica `startColor = (RGB, 1)` / `endColor = (RGB, 0)` al trail. El swap es atómico con el del material principal (`BallSkinApplier` reacciona al mismo evento), así que comprar un skin nuevo cambia bola **y** trail en el mismo frame.

3. **`BallDeathBurst`** (sub-feature `Player/`):
   - Mirror exacto del patrón de `Gem.BuildPickupBurst`: construye un `ParticleSystem` hijo en `Awake` (sphere shape radius 0.1, world-space, burst de 36 partículas a 7 u/s, lifetime 0.65 s, gradiente alpha 1→0), material compartido estático con cascada de shader fallbacks.
   - Se suscribe a `BallController.OnFell` (event C# directo — mismo asmdef, no hace falta canal SO). En `HandleFell` hace `_burst.transform.position = transform.position` antes de `Play(true)` para anclar el burst en el punto de impacto, no donde acabe la bola después del freeze-frame.
   - `OnDisable` para el `ParticleSystem` defensivamente — si la escena se descarga mid-fall el burst no queda emitiendo en limbo.

4. **Build de Windows entregable** en `Builds/Windows/ZigZag.exe`. 608×1080 portrait, version `0.9`, escena única `S_Main`. Smoke test: arranque limpio, loop completo, freeze + burst + GameOver visibles, retry funcional, cierre sin crash.

5. **Capturas** en `docs/screenshots/` — `menu.png`, `gameplay.png`, `gameover.png`. README enlaza al `.exe` y embebe las capturas en §1 (hero) y §5 (tabla de 3 estados) en ambos idiomas.

### Decisiones técnicas

- **Trail nativo + colorizador delgado, no componente custom todo-en-uno.** El `TrailRenderer` de Unity ya es la implementación correcta — duplicar su lógica en C# sería ceremonia gratis. El colorizador hace lo único que el componente nativo no sabe: "qué color toca según la skin equipada". 30 líneas, una responsabilidad.

- **Death burst con event C# directo, no canal SO.** El audio del game over sí usa canal SO porque escucha en otro asmdef (`ZigZag.Runtime.Audio`). El death burst vive en el mismo asmdef que `BallController`, así que el event C# local es la herramienta correcta — un canal SO sería overhead de configuración (un asset más que arrastrar) sin payoff. Coherente con la guía "SO para cross-asmdef, event C# para mismo asmdef" usada también por la state machine que escucha `OnFell`.

- **Burst no skin-aware.** El burst de la gema es siempre dorado (no skin-aware) y se decidió igual aquí: el color de la muerte (blanco→naranja) contrasta con cualquier skin y con cualquier paleta cíclica. Acoplarlo a la skin diluiría el peso visual del momento de impacto, que es lo único que el burst tiene que comunicar.

- **`_burst.transform.position = transform.position` en `HandleFell`, no en `LateUpdate`.** La bola sigue cayendo durante el freeze-frame (`Time.timeScale = 0` no para los `Update` que no respetan unscaled time, pero sí el progreso visual). Anclar el burst al punto de impacto en el momento del trigger lo deja exactamente donde la bola tocó el borde, no donde acaba tras los 0.1 s de freeze.

- **Material compartido estático tanto en `Gem` como en `BallDeathBurst`.** Mismo patrón = misma penalización (un `Material` por sesión, no por instancia). Las dos copias del helper `GetOrCreateBurstMaterial` están aceptadas como duplicación local; un helper común en `Utilities/` se justifica si aparece un tercer cliente del patrón, no antes.

### Pendiente

Ninguno para el entregable. Backlog opcional post-entrega: `MusicController` con fade-in/out en GameOver (explícitamente YAGNI en iter 8), prune de gemas recogidas en `_activeGems` durante run para endurance (TODO existente en `GemSpawner.cs:36`), PlayMode test del loop completo Menu → Playing → GameOver → Retry → Menu.

### Verificación final

- 24/24 tests EditMode en verde.
- Play en editor: trail visible, cambia de color al equipar skin, burst de impacto al caer, GameOver panel tras 0.1 s de freeze.
- `Builds/Windows/ZigZag.exe` arranca a 608×1080 portrait, juega 3 runs sin warnings críticos en `output_log.txt`.
- README enlaza `.exe` + 3 capturas; render limpio en GitHub.
```

- [ ] **Step 2: Append the English entry to `devlog.en.md`**

Append the equivalent translation at the end of `devlog.en.md`. Match the structure: heading with the same date, same five subsections (`Objective`, `What was implemented`, `Technical decisions`, `Pending`, `Final verification`). Translate each paragraph faithfully — the body is information-equivalent to the Spanish version, not a paraphrase. (If you skipped the English translation work earlier in the iteration cycle and `devlog.en.md` lags behind in coverage, just append iter 10 at the end and leave the lag intact.)

- [ ] **Step 3: Commit both files together**

```bash
git add devlog.md devlog.en.md
git commit -m "docs: log iteration 10 — final polish, build and screenshots"
```

---

## Task 13: Final verification pass

**Files:** none — verification only.

- [ ] **Step 1: All tests pass**

```bash
# From the Unity Editor:
# Window → General → Test Runner → EditMode → Run All
```

Expected: **24 tests passed, 0 failed**.

- [ ] **Step 2: Console is clean in Play mode**

Press Play. Play through one full cycle (menu → run → death → retry → shop → equip → run → death → retry → quit play mode). Console must contain **zero errors and zero warnings** (informational `Debug.Log` lines are fine and expected from the audio init path).

- [ ] **Step 3: Built executable runs end-to-end**

Run `Builds/Windows/ZigZag.exe`. Same end-to-end cycle as Step 2. Window closes cleanly.

- [ ] **Step 4: Git working tree is clean**

```bash
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 5: Tag the deliverable**

```bash
git tag -a v0.9-deliverable -m "v0.9 deliverable build with final polish"
```

(Do **not** push the tag automatically — the user decides when to publish.)

---

## Self-Review — Spec Coverage Check

This plan claims to close the following items from the analysis that preceded it:

| Item from analysis | Covered by tasks |
|--------------------|------------------|
| Trail Renderer behind the ball | Tasks 5 + 6 |
| `Builds/Windows/` empty → ship the `.exe` | Task 9 |
| Screenshots for the deliverable | Task 10 |
| Death VFX (medium priority) | Tasks 7 + 8 |
| `GlobalForward` duplicated in 3 files | Tasks 1-4 |
| Devlog entry for the iteration | Task 12 |

**Items explicitly out of scope** (mentioned in the analysis but deferred):
- `Assets/VFX/` empty folder — left alone; the death burst is procedural like the gem burst, so no asset folder is required.
- TODO in `GemSpawner.cs:36` (prune collected gems during play) — deferred until endurance runs become a real concern.
- Music fade-in/out via `MusicController` — explicit YAGNI in iter 8, still YAGNI.
- PlayMode tests — deliberate scope choice (README §11), unchanged.
- CI with `game-ci/unity-test-runner` — open TODO in CLAUDE.md §10, not deliverable-blocking.
- URP migration, mobile/console target definition, UniTask decision — open TODOs in CLAUDE.md §1 / §7, post-prototype work.

No spec gaps. No placeholders. Type and naming consistency verified across tasks (`BallTrailColorizer`, `BallDeathBurst`, `GameConfigSO.GlobalForward` are referenced with identical signatures everywhere they appear).

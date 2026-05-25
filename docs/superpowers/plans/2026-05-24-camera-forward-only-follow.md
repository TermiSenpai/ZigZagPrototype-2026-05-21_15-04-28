# Camera forward-only follow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the camera advance **only** along the global forward axis `(-1, 0, 1)/√2` (vertical-up in screen space under the -45° Y rotation), so the ball visibly serpentines left/right across the screen — matching the original Ketchapp ZigZag camera behavior — instead of being kept dead-center as it does today.

**Architecture:** Extract the projection math into a pure static helper `CameraFollowMath` (EditMode-testable, mirrors the existing `ScoreCalculator` pattern). Refactor `CameraFollow` to capture both the camera and target origins at init time, call the helper each `LateUpdate`, and `SmoothDamp` toward its result. The forward axis stays as a `static readonly Vector3` inside `CameraFollow` (same pattern as `PathGenerator.GlobalForward`); deduplication into `GameConfigSO` is intentionally out of scope.

**Tech Stack:** Unity 2022.3.62f2 LTS, C# (Unity 2022.3 / .NET Standard 2.1), NUnit 3 via Unity Test Framework (EditMode).

**Reference:** Brainstorm conversation 2026-05-24 (no separate spec doc — the change is scoped enough to plan directly from the discussion).

**Scope notes:**
- Code-only plan. No Unity scene edits required; the existing `CameraFollow` component on the scene camera keeps the same serialized fields.
- Lateral framing / `orthographicSize` tuning is **not** part of this plan. The change may surface that the ball now drifts wider on screen than before; if Task 3 reveals the ball leaving the frame on long runs, log it as a follow-up tuning task — do not adjust `orthographicSize` opportunistically here.
- Reset behavior (camera scrolling smoothly back to spawn after death) is unchanged by design — both today's code and the new code SmoothDamp back to the captured origin when the ball is reset to its spawn position.

---

## File Structure

**New files:**
- `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs` — pure static helper. One responsibility: given the camera/target origins, current target position, forward axis and locked Y, return the desired camera world position. No Unity lifecycle. Lives next to `CameraFollow.cs` because they change together.
- `Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs` — NUnit fixture covering the projection math (parallel structure to `Assets/Code/Tests/EditMode/Scoring/ScoreCalculatorTests.cs`).

**Modified files (code):**
- `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs` — replace `_horizontalOffset` with `_cameraOrigin` + `_targetOrigin`, delegate the desired-position math to `CameraFollowMath`, add the forward-axis constant. Public surface (`Target` get, `SetTarget(Transform)`, serialized fields) unchanged.

**Modified files (docs):**
- `zigzag_architecture.md` — expand §7.10 with the projection rule; append a short ADR-014 explaining the decision; touch ADR-007 with a one-line "see also ADR-014".
- `devlog.md` — new iteration entry "2026-05-24 — Iteración 4.2: cámara solo-forward".

**No asmdef changes.** `Assets/Code/Runtime/Gameplay/CameraSystem/` is already covered by `ZigZag.Runtime.Gameplay.asmdef` (no per-subfeature asmdef in this project). The test assembly `ZigZag.Tests.EditMode.asmdef` already references `ZigZag.Runtime.Gameplay`.

**`.meta` files for new folders:** when Unity imports the new test sub-folders (`Gameplay/`, `Gameplay/CameraSystem/`), it will auto-generate `.meta` files for them. After the first Unity import they must be staged and committed alongside the test code (precedent: commit `e5cd32e`). Task 1 calls this out explicitly.

---

## Task 1: Add `CameraFollowMath` with EditMode tests (TDD)

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs`
- Create: `Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs` with exactly this content:

```csharp
using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.CameraSystem;

namespace ZigZag.Tests.EditMode.Gameplay.CameraSystem
{
    /// <summary>
    /// Tests the pure projection used by <see cref="CameraFollow"/>: the desired camera
    /// world position must equal the camera origin shifted only along the global forward
    /// axis by the target's forward-projected displacement, with Y locked to the captured
    /// value. Lateral (perpendicular-to-forward) target motion must produce zero camera
    /// displacement — that is the whole point of the change.
    /// </summary>
    [TestFixture]
    public sealed class CameraFollowMathTests
    {
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
        private static readonly Vector3 CameraOrigin = new Vector3(10f, 8f, -4f);
        private static readonly Vector3 TargetOrigin = new Vector3(-2f, 0.65f, 3f);
        private const float LockedY = 8f;
        private const float Tolerance = 1e-4f;

        [Test]
        public void ComputeDesiredPosition_TargetAtOrigin_ReturnsCameraOriginWithLockedY()
        {
            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, TargetOrigin, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedAlongPositiveZ_AdvancesCameraAlongForward()
        {
            // +Z motion has a positive component along forward (-1,0,1)/√2.
            // Δ = (0,0,7); progress = 7/√2 ≈ 4.9497.
            // forwardOffset = progress * forward = (-3.5, 0, 3.5).
            Vector3 targetNow = TargetOrigin + new Vector3(0f, 0f, 7f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x - 3.5f).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z + 3.5f).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedAlongNegativeX_AdvancesCameraAlongForward()
        {
            // -X motion also has a positive component along (-1,0,1)/√2.
            // Δ = (-7,0,0); progress = 7/√2 ≈ 4.9497.
            // forwardOffset = (-3.5, 0, 3.5).
            Vector3 targetNow = TargetOrigin + new Vector3(-7f, 0f, 0f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x - 3.5f).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z + 3.5f).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedPerpendicularToForward_LeavesCameraAtOrigin()
        {
            // Perpendicular-to-forward direction is (1,0,1)/√2 (the OTHER ball diagonal).
            // Any motion along it has zero dot with forward → zero camera displacement.
            Vector3 perpendicular = new Vector3(1f, 0f, 1f).normalized;
            Vector3 targetNow = TargetOrigin + perpendicular * 10f;

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedDiagonally_AccumulatesBothAxes()
        {
            // Δ = (-7,0,7); progress = 14/√2 ≈ 9.8995.
            // forwardOffset = (-7, 0, 7).
            Vector3 targetNow = TargetOrigin + new Vector3(-7f, 0f, 7f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x - 7f).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z + 7f).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetYChangesDuringFall_DoesNotAffectCameraXZ()
        {
            // The ball drops in Y when it falls off the path. forward is XZ-only
            // (its Y component is 0), so a Y-only delta must not move the camera at all.
            Vector3 targetNow = TargetOrigin + new Vector3(0f, -5f, 0f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_LockedYOverridesCameraOriginY()
        {
            // The caller decides the Y plane the camera stays on. cameraOrigin.y is
            // not what we want to read — we want the explicitly-locked Y. Use a
            // distinct value to prove the function ignores cameraOrigin.y.
            const float explicitLockedY = 42f;
            Vector3 targetNow = TargetOrigin + new Vector3(0f, 0f, 7f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, explicitLockedY);

            Assert.That(desired.y, Is.EqualTo(explicitLockedY).Within(Tolerance));
        }
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail with the expected error**

Open Unity (project root `e:\ZigZagPrototype\ZigZagPrototype`), let it import, then open `Window → General → Test Runner`, switch to the `EditMode` tab and click `Run All`.

Expected: the new test class fails to compile because `CameraFollowMath` does not yet exist — Unity will report something like:

```
Assets\Code\Tests\EditMode\Gameplay\CameraSystem\CameraFollowMathTests.cs(N,N): error CS0246: The type or namespace name 'CameraFollowMath' could not be found
```

This is the red of TDD. Do not proceed if Unity reports any *other* error (e.g. asmdef misconfiguration, missing folder `.meta`).

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs` with exactly this content:

```csharp
using UnityEngine;

namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    /// <summary>
    /// Pure projection helper used by <see cref="CameraFollow"/>. Given the camera and
    /// target world positions captured at init time (origins), the target's current
    /// position, the global forward axis and the Y plane the camera must stay on,
    /// returns the desired camera world position.
    /// </summary>
    /// <remarks>
    /// The desired position is the camera origin shifted only along the forward axis
    /// by the projection of the target's displacement onto that axis. Perpendicular
    /// (lateral) target motion intentionally contributes zero camera displacement —
    /// this reproduces the original Ketchapp ZigZag behavior where the camera
    /// advances "upward" in screen space and the ball visibly serpentines across it.
    /// </remarks>
    public static class CameraFollowMath
    {
        /// <summary>
        /// Returns the camera position the follower should approach this frame.
        /// </summary>
        /// <param name="cameraOrigin">Camera world position captured at init time. The Y component is ignored — see <paramref name="lockedY"/>.</param>
        /// <param name="targetOrigin">Target world position captured at init time, used as the reference for the displacement.</param>
        /// <param name="targetCurrent">Target world position this frame.</param>
        /// <param name="forwardAxis">Unit vector representing forward progress. The path uses <c>(-1, 0, 1).normalized</c>.</param>
        /// <param name="lockedY">Y world coordinate the camera must remain on; overrides <c>cameraOrigin.y</c>.</param>
        public static Vector3 ComputeDesiredPosition(
            Vector3 cameraOrigin,
            Vector3 targetOrigin,
            Vector3 targetCurrent,
            Vector3 forwardAxis,
            float lockedY)
        {
            Vector3 delta = targetCurrent - targetOrigin;
            float forwardProgress = Vector3.Dot(delta, forwardAxis);
            Vector3 forwardOffset = forwardProgress * forwardAxis;

            return new Vector3(
                cameraOrigin.x + forwardOffset.x,
                lockedY,
                cameraOrigin.z + forwardOffset.z);
        }
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

In Unity's Test Runner (EditMode tab) click `Run All`.

Expected: all 7 `CameraFollowMathTests` pass, plus the 7 pre-existing `ScoreCalculatorTests` still pass. Total: 14 passing, 0 failing.

- [ ] **Step 5: Stage and commit**

Unity will have generated `.meta` files for the new files and folders during import (`CameraFollowMath.cs.meta`, `Gameplay.meta`, `Gameplay/CameraSystem.meta`, `CameraFollowMathTests.cs.meta`). Stage them explicitly — `git add -A` is forbidden by policy.

```bash
git add Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs
git add Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs.meta
git add Assets/Code/Tests/EditMode/Gameplay.meta
git add Assets/Code/Tests/EditMode/Gameplay/CameraSystem.meta
git add Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs
git add Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs.meta

git commit -m "test(camera): add CameraFollowMath with forward-only projection"
```

If any of the `.meta` paths reported by `git status` differ from the list above (e.g. Unity uses a different folder name casing), adjust the paths in the `git add` lines to match what `git status` shows. Do **not** add `.meta` files Unity has not generated yet.

---

## Task 2: Refactor `CameraFollow` to use the helper

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs`

- [ ] **Step 1: Replace the file contents**

Overwrite `Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs` with exactly this content:

```csharp
using UnityEngine;
using ZigZag.Runtime.Data;

namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    /// <summary>
    /// Smoothly follows a target transform by advancing the camera only along the
    /// global forward axis <c>(-1, 0, 1)/√2</c>. Lateral target motion is discarded
    /// by design — the ball visibly serpentines across the screen instead of being
    /// kept dead-center, reproducing the original Ketchapp ZigZag behavior.
    /// </summary>
    /// <remarks>
    /// The camera origin (its world XZ at init) and the target origin (the target's
    /// world position at init) are captured once at <see cref="Start"/> or whenever
    /// <see cref="SetTarget"/> is called. Each <see cref="LateUpdate"/> the desired
    /// position is recomputed from those origins via <see cref="CameraFollowMath"/>
    /// and reached with <see cref="Vector3.SmoothDamp(Vector3,Vector3,ref Vector3,float)"/>.
    /// The Y plane is locked to the camera's initial Y so the camera never chases
    /// the ball downward when it falls off the path (ADR-007 + ADR-014).
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        // Mirrors PathGenerator.GlobalForward and ScoreManager's forward axis. Kept
        // local to avoid coupling CameraSystem to PathGenerator; a single source of
        // truth on GameConfigSO is a separate, future refactor.
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;

        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of the SmoothDamp approach time.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Transform the camera follows. Only its motion along the global forward axis (-1,0,1)/√2 moves the camera.")]
        private Transform _target;

        private Vector3 _cameraOrigin;
        private Vector3 _targetOrigin;
        private float _lockedY;
        private Vector3 _smoothVelocity;
        private bool _originsCaptured;

        public Transform Target => _target;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(CameraFollow)} requires a {nameof(GameConfigSO)} reference.", this);
        }

        private void Start()
        {
            CaptureOrigins();
        }

        private void LateUpdate()
        {
            if (_target == null || _config == null || !_originsCaptured) return;

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                _cameraOrigin,
                _targetOrigin,
                _target.position,
                GlobalForward,
                _lockedY);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref _smoothVelocity,
                _config.CameraFollowSmoothTime);
        }

        /// <summary>
        /// Reassigns the follow target and recaptures the camera/target origins
        /// and locked Y from the current world state. Intended for runtime wiring
        /// from a bootstrapper.
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            _smoothVelocity = Vector3.zero;
            CaptureOrigins();
        }

        private void CaptureOrigins()
        {
            _cameraOrigin = transform.position;
            _lockedY = _cameraOrigin.y;

            if (_target == null)
            {
                _originsCaptured = false;
                return;
            }

            _targetOrigin = _target.position;
            _originsCaptured = true;
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles cleanly and EditMode tests still pass**

Switch to Unity. The editor recompiles on save. Watch the Console:

Expected: zero compile errors, zero new warnings. Then in Test Runner (EditMode) → `Run All`: 14 passing, 0 failing (no test code changed, but the runtime asmdef recompiled).

If a compile error appears about `_horizontalOffset` being referenced elsewhere, search the codebase — no other file should touch that private field. Fix any stray reference; if none is found and the error persists, force-reimport the script (`Assets → Reimport`).

- [ ] **Step 3: Stage and commit**

```bash
git add Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs

git commit -m "refactor(camera): advance only along global forward axis"
```

---

## Task 3: Manual verification in the Unity Editor

This task produces no commit. It exists to catch behavioral regressions the EditMode tests cannot see (`SmoothDamp` lag, scene wiring, ball going off-screen).

**Files:** none.

- [ ] **Step 1: Enter Play mode on `Assets/Scenes/SampleScene.unity`**

Open the scene, press Play.

- [ ] **Step 2: Confirm the camera advances only "upward" in screen space**

Click once to start the run. Observe: as the ball moves, the camera should scroll in a single screen direction (upward, due to the -45° Y camera rotation projecting `(-1,0,1)/√2` onto +screen-Y). The horizon should not pan left/right.

If the camera visibly slides horizontally as the ball changes direction, the refactor is wrong — re-read the math in `CameraFollowMath.cs` and confirm `GlobalForward = (-1, 0, 1).normalized` (not `(1, 0, 1)` or any other variant).

- [ ] **Step 3: Confirm the ball serpentines on screen**

While playing, watch the ball's screen position. It should oscillate side-to-side around the screen center each time you flip direction, instead of staying glued to the center as it used to.

- [ ] **Step 4: Confirm no regressions in fall / GameOver / Retry**

Let the ball fall off the path. Confirm:
- The camera does not chase Y downward (the ball drops out of frame, the camera stays on its Y plane).
- GameOver panel appears as before.
- Click `Retry`. The camera SmoothDamps back to its captured origin while the path rebuilds. This smooth scroll-back is the same behavior the previous implementation had — it is not a regression introduced by this plan.

- [ ] **Step 5: Note any lateral framing issues as a follow-up**

If the ball drifts wide enough during long runs that it visibly approaches or leaves the screen edge, do **not** change `orthographicSize` here. Instead, write down a one-line note for a follow-up tuning task: "Iteration 4.2 follow-up: lateral excursion exceeds frame at distance X; tune `orthographicSize` and/or bias `PathGenerator` segment lengths to keep the ball framed." Then stop and report it to the user before doing anything else.

If the ball stays comfortably framed across multiple runs, no follow-up needed.

---

## Task 4: Update architecture and devlog docs

**Files:**
- Modify: `zigzag_architecture.md` (§7.10, ADR-007 cross-reference, new ADR-014)
- Modify: `devlog.md` (new iteration entry)

- [ ] **Step 1: Replace §7.10 of `zigzag_architecture.md`**

Find the §7.10 block in `zigzag_architecture.md` (currently lines 583–596, starting with `### 7.10 `CameraFollow` ...`). Replace the whole block — from the heading through the line `Namespace \`CameraSystem\` (no \`Camera\`) para no colisionar con \`UnityEngine.Camera\`.` — with exactly this:

```markdown
### 7.10 `CameraFollow` y `CameraFollowMath` (`ZigZag.Runtime.Gameplay.CameraSystem`)

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

Namespace `CameraSystem` (no `Camera`) para no colisionar con `UnityEngine.Camera`.

**Regla de movimiento (ADR-014):** la cámara avanza **solo** a lo largo del eje global forward `(-1, 0, 1)/√2`. La componente perpendicular del desplazamiento del target se descarta — el frame se queda quieto lateralmente y la bola serpentea visiblemente por la pantalla, reproduciendo el comportamiento del ZigZag original. La Y se bloquea a la Y inicial de la cámara para que no persiga la bola en su caída. La matemática vive en `CameraFollowMath` (estática, sin Unity lifecycle, cubierta por tests EditMode en `Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs`).
```

- [ ] **Step 2: Add a cross-reference line to ADR-007**

Find ADR-007 in `zigzag_architecture.md` (currently around line 810). Locate its `**Consecuencias:**` line:

```markdown
**Consecuencias:** ~20 líneas propias. Más simple de revisar.
```

Replace it with:

```markdown
**Consecuencias:** ~20 líneas propias. Más simple de revisar. **Ver también ADR-014** — refinamiento del eje de seguimiento (solo forward).
```

- [ ] **Step 3: Append ADR-014 at the end of the ADR list**

Find the end of `zigzag_architecture.md` (after the last existing ADR, currently ADR-013). Append exactly this block at the end of the file, preceded by one blank line:

```markdown

### ADR-014 — Cámara avanza solo en el eje global forward

**Decisión:** `CameraFollow` proyecta el desplazamiento del target sobre `(-1, 0, 1)/√2` y solo aplica esa componente; la perpendicular se descarta. La Y queda bloqueada a la Y inicial de la cámara.

**Alternativas consideradas:**
- Seguir X y Z del target por separado (implementación original). Mantiene la bola centrada en pantalla; rompe el feel del ZigZag de Ketchapp donde la cámara solo "sube" y la bola serpentea.
- Seguir todo el delta pero con damping mucho más fuerte en la perpendicular. Más complejo de tunear, mismo resultado visual aproximado.

**Justificación:** el pilar #2 del GDD ("lectura instantánea") y el feel del juego de referencia exigen que el jugador perciba la oscilación lateral de la bola como información visual primaria. Si la cámara la compensa, la oscilación deja de leerse.

**Consecuencias:**
- La excursión lateral acumulada de la bola pasa a ser visible. Si supera el ancho del frustum hay que tunear `orthographicSize` o sesgar `PathGenerator` para acotar el drift. Esta calibración es un eje de cambio independiente y se aborda como tuning, no como código nuevo.
- El reset de la bola al spawn provoca un scroll-back suave de la cámara (mismo comportamiento que la implementación anterior; no es regresión).
- Matemática extraída a `CameraFollowMath` para test unitario en EditMode, siguiendo el patrón de `ScoreCalculator`.
```

- [ ] **Step 4: Append a devlog entry**

Open `devlog.md` and append exactly this block at the end of the file, preceded by one blank line:

```markdown

---

## 2026-05-24 — Iteración 4.2: cámara solo-forward

### Objetivo

Corregir el seguimiento de cámara para que avance **solo** a lo largo del eje global forward `(-1, 0, 1)/√2`, reproduciendo el comportamiento del ZigZag original: la cámara sube en pantalla, la bola serpentea lateralmente sobre ella. La implementación previa seguía X y Z del target por separado, lo que mantenía la bola centrada y eliminaba la oscilación visual.

### Lo que se ha implementado

1. **`CameraFollowMath`** (`Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollowMath.cs`)
   Helper estático puro. `ComputeDesiredPosition(cameraOrigin, targetOrigin, targetCurrent, forwardAxis, lockedY)` proyecta el delta del target sobre el eje forward y devuelve la posición deseada (con Y bloqueada). Sin Unity lifecycle, testeable en EditMode igual que `ScoreCalculator`.

2. **`CameraFollowMathTests`** (`Assets/Code/Tests/EditMode/Gameplay/CameraSystem/CameraFollowMathTests.cs`)
   Siete tests: target estático, movimiento +Z puro, movimiento -X puro, movimiento perpendicular (debe dar cero), diagonal pura, caída en Y (no debe afectar XZ), y verificación de que `lockedY` sobrescribe `cameraOrigin.y`.

3. **`CameraFollow` refactor** (`Assets/Code/Runtime/Gameplay/CameraSystem/CameraFollow.cs`)
   `_horizontalOffset` reemplazado por `_cameraOrigin` + `_targetOrigin`. `LateUpdate` delega en `CameraFollowMath` y aplica `SmoothDamp` hacia el resultado. Constante `GlobalForward = (-1, 0, 1).normalized` local al archivo (duplicada con `PathGenerator` deliberadamente; deduplicar es otra iteración).

### Decisiones tomadas durante la implementación

- **No clamp del progreso forward.** Si el target retrocede en forward (no ocurre en gameplay normal — solo al recapturar origins en `SetTarget`), la matemática lo soporta sin caso especial.
- **`GlobalForward` no se mueve a `GameConfigSO`.** Estaría bien tener única fuente de verdad, pero arrastra a `PathGenerator` y `ScoreManager` al refactor. Fuera de alcance de esta iteración.
- **Sin cambio en `orthographicSize`.** El cambio puede revelar drift lateral visible; si lo hace, se trata como tuning aparte, no se mete en el mismo commit que el cambio de cámara.

### ADR

- **ADR-014** añadido: "Cámara avanza solo en el eje global forward". Ver `zigzag_architecture.md`.
- **ADR-007** actualizado con cross-reference a ADR-014.

### Pendiente

- Tuning de `orthographicSize` si la verificación manual lo justifica.
- (Largo plazo) Mover `GlobalForward` a `GameConfigSO` como única fuente de verdad compartida con `PathGenerator` y `ScoreManager`.
```

- [ ] **Step 5: Stage and commit**

```bash
git add zigzag_architecture.md devlog.md

git commit -m "docs: log iteration 4.2 — camera forward-only follow (ADR-014)"
```

---

## Self-Review (record of checks done while writing this plan)

- **Spec coverage:** The change discussed in the brainstorm conversation has one core requirement (camera advances only along forward) and one explicit non-requirement (don't tune `orthographicSize`). Both are covered — the first by Tasks 1–2, the second by an explicit guard in the scope notes and Task 3 Step 5.
- **Placeholder scan:** No "TODO/TBD/implement later" left in any task. Every code block is complete and copy-pasteable. Every command shows expected output. The `git add` step in Task 1 contains a contingency for `.meta` path variance — explicit and bounded, not a placeholder.
- **Type consistency:** The helper signature `ComputeDesiredPosition(Vector3 cameraOrigin, Vector3 targetOrigin, Vector3 targetCurrent, Vector3 forwardAxis, float lockedY)` is identical in (a) the test file in Task 1 Step 1, (b) the implementation in Task 1 Step 3, (c) the call site in Task 2 Step 1, and (d) the architecture doc snippet in Task 4 Step 1. Field names in `CameraFollow` (`_cameraOrigin`, `_targetOrigin`, `_lockedY`, `_smoothVelocity`, `_originsCaptured`) are consistent across the refactor. `GlobalForward` spelled identically (PascalCase) wherever it appears. `Target` property kept as-is to preserve the public surface.

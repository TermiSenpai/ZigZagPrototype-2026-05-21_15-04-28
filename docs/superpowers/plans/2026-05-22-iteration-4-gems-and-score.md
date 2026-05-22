# Iteration 4 — Gems, Score & Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the reward loop — gems spawn on the procedural path, the player picks them up for points, the score climbs (gem value + distance progress), and the best score persists across runs via PlayerPrefs. HUD shows live score; GameOver panel shows final/best plus a "New Record" badge.

**Architecture:** Two new sub-features under `ZigZag.Runtime.Gameplay`: `Collectibles/` (`Gem`, `GemPool`, `GemSpawner`) mirroring the existing `World/` pool+spawner pattern, and `Scoring/` (`ScoreCalculator`, `ScoreManager`). Communication is strictly via SO event channels — three new `IntGameEventSO` assets (`SO_OnGemCollected`, `SO_OnScoreChanged`, `SO_OnBestScoreChanged`). `PathGenerator` is the only existing file that gains a new collaborator (it calls `_gemSpawner.TryPopulateSegment` when finalizing a segment). `UIController` gains TMP text refs to display score/best. `GameBootstrap` validates the new actors.

**Tech Stack:** Unity 2022.3.62f2 LTS, C# .NET Standard 2.1, `UnityEngine.Pool.ObjectPool<T>`, `PlayerPrefs`, TextMeshPro 3.0.7, NUnit (Unity Test Framework 1.1.33).

---

## Pre-flight

- Working tree must be clean before starting. Current `git status` shows `M Assets/Settings/SO_GameConfig.asset` from prior work. Commit or stash it first:
  ```bash
  git add Assets/Settings/SO_GameConfig.asset && git commit -m "chore: persist SO_GameConfig adjustments from iteration 3"
  ```
  (Or `git stash` if the change is exploratory.)
- Branch: `feat/iter4-gems-and-score`. Create with:
  ```bash
  git checkout -b feat/iter4-gems-and-score
  ```
- Unity Editor 2022.3.62f2 must be open on the project at least once during the iteration so compilation runs on file changes. EditMode tests can be invoked from CLI:
  ```bash
  "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -projectPath "e:\ZigZagPrototype\ZigZagPrototype" -runTests -testPlatform EditMode -testResults "Logs\editmode-results.xml" -quit
  ```
  Expected exit code: `0` = all pass, `2` = one or more failed.

---

## File Map

**Modified:**
- `Assets/Code/Runtime/Data/GameConfigSO.cs` — add Gems, Score, Pooling.GemPoolInitialSize fields + properties + OnValidate clamps.
- `Assets/Code/Runtime/Gameplay/World/PathGenerator.cs` — accept an optional `GemSpawner` reference; invoke `TryPopulateSegment(_currentSegment)` at segment finalization.
- `Assets/Code/Runtime/UI/UIController.cs` — add HUD score `TextMeshProUGUI` field + GameOver score/best/new-record refs; subscribe to `_onScoreChanged` and `_onBestScoreChanged`.
- `Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef` — add `Unity.TextMeshPro` reference.
- `Assets/Code/Runtime/Core/GameBootstrap.cs` — add `_scoreManager`, `_gemPool`, `_gemSpawner` validation fields.

**Created (runtime code):**
- `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs`
- `Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs`
- `Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs`
- `Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs`
- `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`

**Created (tests):**
- `Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef`
- `Assets/Code/Tests/EditMode/Scoring/ScoreCalculatorTests.cs`

**Created (Unity assets, made via editor menus — instructions in Task 12):**
- `Assets/Settings/Events/SO_OnGemCollected.asset` (`IntGameEventSO`)
- `Assets/Settings/Events/SO_OnScoreChanged.asset` (`IntGameEventSO`)
- `Assets/Settings/Events/SO_OnBestScoreChanged.asset` (`IntGameEventSO`)
- `Assets/Prefabs/P_Gem.prefab`

**Updated docs:**
- `devlog.md` — iteration 4 entry.

---

## Task 1: Extend `GameConfigSO` with gem, score and gem-pool fields

**Files:**
- Modify: `Assets/Code/Runtime/Data/GameConfigSO.cs`

Adds the data the new systems will read. No new behaviour yet; this is the foundation every other task depends on.

- [ ] **Step 1.1: Add the four new serialized fields under new headers**

Open `Assets/Code/Runtime/Data/GameConfigSO.cs`. After the existing `[Header("Pooling")]` block (around line 67-69), add the new headers and fields. The final inspector layout will be: Movement → Falling → Ground Check → Camera → Path Generation → **Gems** → **Score** → Pooling.

Insert this block **before** the existing `[Header("Pooling")]`:

```csharp
        [Header("Gems")]
        [SerializeField, Range(0f, 1f), Tooltip("Probability per finalized segment that a gem is placed on one of its cubes.")]
        private float _gemSpawnProbability = 0.3f;

        [SerializeField, Tooltip("Points awarded for each gem collected.")]
        private int _gemValue = 10;

        [SerializeField, Tooltip("Vertical offset above a cube's center where a gem sits. Pick a value clear of both the cube top and the ball radius so collection is reliable.")]
        private float _gemHeightAboveCubeCenter = 3.2f;

        [Header("Score")]
        [SerializeField, Tooltip("Points per unit of forward progress (measured along the global forward axis, -X+Z diagonal).")]
        private int _distanceMultiplier = 1;
```

Then **add a new serialized field inside** the existing `[Header("Pooling")]` block (just below `_platformPoolInitialSize`):

```csharp
        [SerializeField, Tooltip("Number of gem instances the gem pool prewarms on Awake.")]
        private int _gemPoolInitialSize = 20;
```

- [ ] **Step 1.2: Add the matching get-only properties**

Below the existing property block (after `public int PlatformPoolInitialSize => _platformPoolInitialSize;`), append:

```csharp
        public float GemSpawnProbability => _gemSpawnProbability;
        public int GemValue => _gemValue;
        public float GemHeightAboveCubeCenter => _gemHeightAboveCubeCenter;
        public int DistanceMultiplier => _distanceMultiplier;
        public int GemPoolInitialSize => _gemPoolInitialSize;
```

- [ ] **Step 1.3: Extend `OnValidate` with clamps for the new fields**

Inside the `#if UNITY_EDITOR ... OnValidate()` block (around line 89-105), add at the bottom:

```csharp
            if (_gemValue < 0) _gemValue = 0;
            if (_distanceMultiplier < 0) _distanceMultiplier = 0;
            if (_gemPoolInitialSize < 1) _gemPoolInitialSize = 1;
            if (_gemHeightAboveCubeCenter < 0f) _gemHeightAboveCubeCenter = 0f;
```

`_gemSpawnProbability` already has `[Range(0,1)]` so it self-clamps in the inspector — no extra logic needed.

- [ ] **Step 1.4: Switch to Unity Editor and wait for recompile**

Bring Unity Editor to the foreground. Watch the Console — there must be zero compile errors. The `SO_GameConfig.asset` keeps its existing values; new fields appear with their defaults.

- [ ] **Step 1.5: Commit**

```bash
git add Assets/Code/Runtime/Data/GameConfigSO.cs
git commit -m "feat(data): add gem, score and gem-pool fields to GameConfigSO"
```

---

## Task 2: Bootstrap the EditMode test harness

**Files:**
- Create: `Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef`
- Create: `Assets/Code/Tests/EditMode/.gitkeep` (only if directory would otherwise be empty before Task 3 runs)

The project has zero tests today. CLAUDE.md §10 mandates EditMode tests for pure C#. Standing up the harness now is a one-time, ~5-minute cost that unblocks Task 3.

- [ ] **Step 2.1: Create the asmdef file**

Create `Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef` with this content:

```json
{
    "name": "ZigZag.Tests.EditMode",
    "rootNamespace": "ZigZag.Tests.EditMode",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "ZigZag.Runtime.Data",
        "ZigZag.Runtime.Events",
        "ZigZag.Runtime.Gameplay"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Field-by-field rationale (do not deviate without reason):
- `includePlatforms: ["Editor"]` keeps tests out of player builds.
- `overrideReferences: true` + `precompiledReferences: ["nunit.framework.dll"]` is the standard Unity Test Framework idiom.
- `defineConstraints: ["UNITY_INCLUDE_TESTS"]` makes the assembly compile only when the Test Framework's define is active.
- The three `ZigZag.Runtime.*` refs are what Task 3 will need.

- [ ] **Step 2.2: Switch to Unity Editor and verify the Test Runner picks it up**

Bring Unity to the foreground. Open `Window → General → Test Runner`. Click the **EditMode** tab. The assembly `ZigZag.Tests.EditMode` should appear in the tree (empty until Task 3). No compile errors in Console.

- [ ] **Step 2.3: Commit**

```bash
git add Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef
git commit -m "test: bootstrap EditMode test assembly"
```

---

## Task 3: `ScoreCalculator` (pure static) + EditMode tests

**Files:**
- Test: `Assets/Code/Tests/EditMode/Scoring/ScoreCalculatorTests.cs`
- Create: `Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs`

Pure helper used by `ScoreManager` (Task 5). Splitting it out means `ScoreManager`'s only untested logic is event wiring + `PlayerPrefs` — both of which are 1-liners. Distance progress is measured along the global forward axis `(-1, 0, 1)/√2` (same axis the path generator uses for ahead/behind buffers), so progress is correctly accumulated whether the ball is going `-X` or `+Z`.

- [ ] **Step 3.1: Write the failing test**

Create `Assets/Code/Tests/EditMode/Scoring/ScoreCalculatorTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.Scoring;

namespace ZigZag.Tests.EditMode.Scoring
{
    [TestFixture]
    public sealed class ScoreCalculatorTests
    {
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
        private static readonly Vector3 Origin = new Vector3(-2f, 0f, 3f);

        [Test]
        public void ComputeDistanceScore_AtOrigin_ReturnsZero()
        {
            int score = ScoreCalculator.ComputeDistanceScore(Origin, Origin, GlobalForward, multiplier: 1);
            Assert.AreEqual(0, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedAlongPositiveZ_ReturnsPositiveProgress()
        {
            Vector3 ballPosition = new Vector3(-2f, 0f, 10f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            // Δ = (0, 0, 7); dot with (-1,0,1)/√2 = 7/√2 ≈ 4.949; floor = 4.
            Assert.AreEqual(4, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedAlongNegativeX_ReturnsPositiveProgress()
        {
            Vector3 ballPosition = new Vector3(-9f, 0f, 3f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            // Δ = (-7, 0, 0); dot with (-1,0,1)/√2 = 7/√2 ≈ 4.949; floor = 4.
            Assert.AreEqual(4, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedDiagonally_AccumulatesBothAxes()
        {
            Vector3 ballPosition = new Vector3(-9f, 0f, 10f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            // Δ = (-7, 0, 7); dot = 14/√2 ≈ 9.899; floor = 9.
            Assert.AreEqual(9, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedBackwards_ReturnsZero()
        {
            // Ball cannot actually move backwards in gameplay, but the API must clamp.
            Vector3 ballPosition = new Vector3(5f, 0f, -3f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            Assert.AreEqual(0, score);
        }

        [Test]
        public void ComputeDistanceScore_MultiplierScalesResult()
        {
            Vector3 ballPosition = new Vector3(-2f, 0f, 10f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 3);
            // Floor first, then multiply: 4 * 3 = 12.
            Assert.AreEqual(12, score);
        }

        [Test]
        public void ComputeDistanceScore_ZeroMultiplier_ReturnsZero()
        {
            Vector3 ballPosition = new Vector3(-2f, 0f, 100f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 0);
            Assert.AreEqual(0, score);
        }
    }
}
```

- [ ] **Step 3.2: Run tests, verify they fail at compile time**

From the project root:

```bash
"C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -projectPath "e:\ZigZagPrototype\ZigZagPrototype" -runTests -testPlatform EditMode -testResults "Logs\editmode-results.xml" -quit
```

Expected: non-zero exit code with compile error "The type or namespace name 'ScoreCalculator' could not be found". This proves the test file is being picked up by the runner.

- [ ] **Step 3.3: Write the minimal implementation**

Create `Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs`:

```csharp
using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Scoring
{
    /// <summary>
    /// Pure helpers for score arithmetic. No Unity lifecycle, no state — every method
    /// is deterministic given its inputs, which keeps the rules easy to unit-test in
    /// EditMode without touching MonoBehaviours or PlayerPrefs.
    /// </summary>
    public static class ScoreCalculator
    {
        /// <summary>
        /// Projects the ball's displacement from <paramref name="origin"/> onto
        /// <paramref name="forwardAxis"/> and floors the result into integer points,
        /// scaled by <paramref name="multiplier"/>. Negative progress is clamped to
        /// zero so a misconfigured spawn origin can never produce a negative score.
        /// </summary>
        /// <param name="ballPosition">Current world position of the ball.</param>
        /// <param name="origin">World position the ball started from (path spawn point).</param>
        /// <param name="forwardAxis">Unit vector representing forward progress. The path
        /// uses <c>(-1, 0, 1).normalized</c> — the diagonal between the two ball directions.</param>
        /// <param name="multiplier">Points per integer unit of progress.</param>
        public static int ComputeDistanceScore(Vector3 ballPosition, Vector3 origin, Vector3 forwardAxis, int multiplier)
        {
            float progress = Vector3.Dot(ballPosition - origin, forwardAxis);
            if (progress < 0f) progress = 0f;
            return Mathf.FloorToInt(progress) * multiplier;
        }
    }
}
```

- [ ] **Step 3.4: Run tests, verify they pass**

```bash
"C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -projectPath "e:\ZigZagPrototype\ZigZagPrototype" -runTests -testPlatform EditMode -testResults "Logs\editmode-results.xml" -quit
```

Expected: exit code `0`. Inspect `Logs\editmode-results.xml` — `<test-run total="7" passed="7" failed="0" />`.

- [ ] **Step 3.5: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Scoring/ScoreCalculator.cs Assets/Code/Tests/EditMode/Scoring/ScoreCalculatorTests.cs
git commit -m "feat(scoring): add ScoreCalculator with deterministic distance scoring"
```

---

## Task 4: Create the three new `IntGameEventSO` channel assets

**Files:**
- Create (via Unity menu): `Assets/Settings/Events/SO_OnGemCollected.asset`
- Create (via Unity menu): `Assets/Settings/Events/SO_OnScoreChanged.asset`
- Create (via Unity menu): `Assets/Settings/Events/SO_OnBestScoreChanged.asset`

These are needed before Tasks 5–10 can wire references in the inspector. The `IntGameEventSO` class already exists at `Assets/Code/Runtime/Events/IntGameEventSO.cs`; we are only creating instances.

- [ ] **Step 4.1: Create the three assets in Unity Editor**

In the Project window:
1. Navigate to `Assets/Settings/Events/`.
2. Right-click → `Create → ZigZag → Events → Int Event`.
3. Name the new asset `SO_OnGemCollected`.
4. Repeat for `SO_OnScoreChanged` and `SO_OnBestScoreChanged`.

After creation the folder should contain (alongside the four existing `SO_OnGame*` assets):
- `SO_OnGemCollected.asset` + `.meta`
- `SO_OnScoreChanged.asset` + `.meta`
- `SO_OnBestScoreChanged.asset` + `.meta`

- [ ] **Step 4.2: Verify on disk**

From the project root:

```bash
ls Assets/Settings/Events/SO_On{GemCollected,ScoreChanged,BestScoreChanged}.asset
```

Expected: all three paths listed without `No such file` errors.

- [ ] **Step 4.3: Commit**

```bash
git add Assets/Settings/Events/SO_OnGemCollected.asset Assets/Settings/Events/SO_OnGemCollected.asset.meta Assets/Settings/Events/SO_OnScoreChanged.asset Assets/Settings/Events/SO_OnScoreChanged.asset.meta Assets/Settings/Events/SO_OnBestScoreChanged.asset Assets/Settings/Events/SO_OnBestScoreChanged.asset.meta
git commit -m "feat(events): add gem-collected, score-changed and best-score-changed channels"
```

---

## Task 5: `ScoreManager` MonoBehaviour

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`

Owns the run's current score (gems + distance), persists the best to `PlayerPrefs`, and broadcasts both via `IntGameEventSO`. Distance accumulation runs in `Update` and only raises `_onScoreChanged` when the integer value actually changes — keeps UI from re-rendering 60×/s for nothing.

- [ ] **Step 5.1: Write the file**

Create `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`:

```csharp
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Scoring
{
    /// <summary>
    /// Accumulates the current run's score (gem points + distance progress), persists
    /// the all-time best to <see cref="PlayerPrefs"/>, and broadcasts both via SO event
    /// channels so the UI never holds a reference to this component.
    /// </summary>
    /// <remarks>
    /// Distance is computed every frame from the ball's transform but only raises
    /// <c>SO_OnScoreChanged</c> when the integer total moves — gem pickups are rare
    /// enough to always raise. The origin for distance is the path start position
    /// taken from <see cref="GameConfigSO.PathStartPosition"/>, so the progress axis
    /// matches what the <c>PathGenerator</c> uses for its buffers.
    ///
    /// Persistence: <c>PlayerPrefs.GetInt("BestScore", 0)</c> in <c>Awake</c>;
    /// <c>SetInt + Save</c> in <see cref="SaveBestIfHigher"/> (called on GameOver).
    /// ADR-003 in zigzag_architecture.md picks PlayerPrefs over a file-based store
    /// because a single int per device is the whole persistence story for the prototype.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScoreManager : MonoBehaviour
    {
        private const string BestScorePrefKey = "BestScore";

        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of distance multiplier and path start position.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Transform of the ball; distance progress is computed from this.")]
        private Transform _ballTransform;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: each raise adds the payload to current score.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Listened-to: starts distance tracking.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Listened-to: stops distance tracking and persists best if improved.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Listened-to: resets current score back to zero (best is preserved).")]
        private GameEventSO _onGameReset;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised whenever the integer score changes.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Raised on boot (loaded best) and whenever the best is overwritten.")]
        private IntGameEventSO _onBestScoreChanged;

        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;

        public int CurrentScore { get; private set; }
        public int BestScore { get; private set; }

        private int _gemScore;
        private int _distanceScore;
        private bool _isTracking;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(ScoreManager)} requires a {nameof(GameConfigSO)} reference.", this);
            Debug.Assert(_ballTransform != null, $"{nameof(ScoreManager)} requires a ball Transform reference.", this);
            Debug.Assert(_onGemCollected != null, $"{nameof(ScoreManager)} requires {nameof(_onGemCollected)}.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(ScoreManager)} requires {nameof(_onGameStarted)}.", this);
            Debug.Assert(_onGameOver != null, $"{nameof(ScoreManager)} requires {nameof(_onGameOver)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(ScoreManager)} requires {nameof(_onGameReset)}.", this);
            Debug.Assert(_onScoreChanged != null, $"{nameof(ScoreManager)} requires {nameof(_onScoreChanged)}.", this);
            Debug.Assert(_onBestScoreChanged != null, $"{nameof(ScoreManager)} requires {nameof(_onBestScoreChanged)}.", this);

            BestScore = PlayerPrefs.GetInt(BestScorePrefKey, 0);
        }

        private void OnEnable()
        {
            if (_onGemCollected != null) _onGemCollected.Register(HandleGemCollected);
            if (_onGameStarted != null) _onGameStarted.Register(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Register(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGemCollected != null) _onGemCollected.Unregister(HandleGemCollected);
            if (_onGameStarted != null) _onGameStarted.Unregister(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Unregister(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        private void Start()
        {
            // Broadcast loaded best so the menu/HUD can paint it before any run starts.
            _onBestScoreChanged.Raise(BestScore);
            _onScoreChanged.Raise(CurrentScore);
        }

        private void Update()
        {
            if (!_isTracking || _config == null || _ballTransform == null) return;

            int newDistanceScore = ScoreCalculator.ComputeDistanceScore(
                _ballTransform.position,
                _config.PathStartPosition,
                GlobalForward,
                _config.DistanceMultiplier);

            if (newDistanceScore == _distanceScore) return;

            _distanceScore = newDistanceScore;
            RecomputeAndBroadcast();
        }

        private void HandleGemCollected(int gemValue)
        {
            _gemScore += gemValue;
            RecomputeAndBroadcast();
        }

        private void HandleGameStarted()
        {
            _isTracking = true;
        }

        private void HandleGameOver()
        {
            _isTracking = false;
            SaveBestIfHigher();
        }

        private void HandleGameReset()
        {
            _isTracking = false;
            _gemScore = 0;
            _distanceScore = 0;
            CurrentScore = 0;
            _onScoreChanged.Raise(CurrentScore);
        }

        private void RecomputeAndBroadcast()
        {
            int total = _gemScore + _distanceScore;
            if (total == CurrentScore) return;
            CurrentScore = total;
            _onScoreChanged.Raise(CurrentScore);
        }

        private void SaveBestIfHigher()
        {
            if (CurrentScore <= BestScore) return;
            BestScore = CurrentScore;
            PlayerPrefs.SetInt(BestScorePrefKey, BestScore);
            PlayerPrefs.Save();
            _onBestScoreChanged.Raise(BestScore);
        }
    }
}
```

- [ ] **Step 5.2: Switch to Unity Editor and confirm compile**

Bring Unity to the foreground. Console should be clean. The new component appears in `Add Component → Scripts → ZigZag.Runtime.Gameplay.Scoring`.

- [ ] **Step 5.3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs
git commit -m "feat(scoring): add ScoreManager (gems + distance, PlayerPrefs best)"
```

---

## Task 6: `Gem` MonoBehaviour

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs`

Trigger collider on the gem. On enter, raises `SO_OnGemCollected` with the configured value and returns itself to the pool. Pool reference is supplied by `GemSpawner` via `Initialize`, so the gem stays decoupled from the pool component.

The ball has no Rigidbody (ADR-001). Per Unity 2022.3 trigger rules, **the gem prefab must carry a kinematic `Rigidbody`** so trigger events fire — that's a prefab-level decision documented in Task 12, not a script-level one.

- [ ] **Step 6.1: Write the file**

Create `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs`:

```csharp
using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// A pickup that rewards the player with <see cref="Value"/> points. The instance
    /// is pooled; it returns itself to the pool on collection rather than being
    /// destroyed.
    /// </summary>
    /// <remarks>
    /// Trigger detection relies on a kinematic <c>Rigidbody</c> on the gem prefab —
    /// the ball is Rigidbody-free (ADR-001) so the gem must be the "moving" side of
    /// the contact for Unity to dispatch <c>OnTriggerEnter</c>.
    ///
    /// The owning <see cref="GemPool"/> is injected via <see cref="Initialize"/>
    /// (called by <c>GemSpawner</c>) so this script never has to call back into the
    /// pool component by reference.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Gem : MonoBehaviour
    {
        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised with the gem's point value when the ball enters its trigger.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Tag of the GameObject considered the ball. Pickup ignores any other collider.")]
        private string _ballTag = "Player";

        public int Value { get; private set; }

        private GemPool _owningPool;
        private bool _collected;

        private void Awake()
        {
            Debug.Assert(_onGemCollected != null, $"{nameof(Gem)} requires {nameof(_onGemCollected)}.", this);

            Rigidbody rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            Collider col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        /// <summary>
        /// Called by <c>GemSpawner</c> after taking the gem out of the pool. Sets the
        /// reward value and the pool to release back to. Idempotent across reuses.
        /// </summary>
        public void Initialize(int value, GemPool owningPool)
        {
            Value = value;
            _owningPool = owningPool;
            _collected = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_collected) return;
            if (!other.CompareTag(_ballTag)) return;

            _collected = true;
            _onGemCollected.Raise(Value);

            if (_owningPool != null) _owningPool.Release(gameObject);
        }
    }
}
```

- [ ] **Step 6.2: Switch to Unity Editor and confirm compile**

The `GemPool` type is referenced here but doesn't exist yet — expect **one** compile error: "The type or namespace name 'GemPool' could not be found." This is expected and resolved by Task 7. Do not commit yet.

- [ ] **Step 6.3: Defer commit until Task 7**

Continue immediately to Task 7. The two files form one compilable unit.

---

## Task 7: `GemPool` MonoBehaviour

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs`

Mirrors `PlatformPool` exactly — same `ObjectPool<GameObject>` pattern, same `Awake` prewarm loop. Different prefab, different capacity field (`_config.GemPoolInitialSize`). Keeping them as two separate components (rather than a generic `<T>`) avoids open-generic serialization headaches and matches `ADR-002`.

- [ ] **Step 7.1: Write the file**

Create `Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs`:

```csharp
using UnityEngine;
using UnityEngine.Pool;
using ZigZag.Runtime.Data;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// Object pool for gem instances. Direct twin of <c>PlatformPool</c>: prewarms
    /// <see cref="GameConfigSO.GemPoolInitialSize"/> instances in <c>Awake</c> and
    /// exposes a minimal <see cref="Get"/> / <see cref="Release"/> surface so
    /// <c>GemSpawner</c> never sees the underlying <see cref="ObjectPool{T}"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GemPool : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Gem prefab spawned and returned by the pool. Must carry a Gem component, a kinematic Rigidbody and a trigger Collider.")]
        private GameObject _gemPrefab;

        [SerializeField, Tooltip("Source of the initial pool capacity.")]
        private GameConfigSO _config;

        private ObjectPool<GameObject> _pool;

        private void Awake()
        {
            Debug.Assert(_gemPrefab != null, $"{nameof(GemPool)} requires a gem prefab.", this);
            Debug.Assert(_config != null, $"{nameof(GemPool)} requires a {nameof(GameConfigSO)} reference.", this);
            if (_gemPrefab == null || _config == null) return;

            int capacity = _config.GemPoolInitialSize;
            _pool = new ObjectPool<GameObject>(
                createFunc: CreateInstance,
                actionOnGet: OnGet,
                actionOnRelease: OnRelease,
                actionOnDestroy: OnDestroyInstance,
                collectionCheck: false,
                defaultCapacity: capacity,
                maxSize: capacity * 2);

            Prewarm(capacity);
        }

        /// <summary>Borrows a gem from the pool. Caller is responsible for setting transform and calling <c>Gem.Initialize</c>.</summary>
        public GameObject Get()
        {
            return _pool != null ? _pool.Get() : null;
        }

        /// <summary>Returns a previously borrowed gem to the pool. The instance is deactivated.</summary>
        public void Release(GameObject gem)
        {
            if (_pool == null || gem == null) return;
            _pool.Release(gem);
        }

        private GameObject CreateInstance()
        {
            GameObject instance = Instantiate(_gemPrefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void OnGet(GameObject gem)
        {
            gem.SetActive(true);
        }

        private void OnRelease(GameObject gem)
        {
            gem.SetActive(false);
        }

        private void OnDestroyInstance(GameObject gem)
        {
            if (gem != null) Destroy(gem);
        }

        private void Prewarm(int count)
        {
            GameObject[] preheated = new GameObject[count];
            for (int i = 0; i < count; i++) preheated[i] = _pool.Get();
            for (int i = 0; i < count; i++) _pool.Release(preheated[i]);
        }
    }
}
```

- [ ] **Step 7.2: Switch to Unity Editor and confirm compile**

Both `Gem.cs` and `GemPool.cs` compile cleanly now. Console must be free of errors.

- [ ] **Step 7.3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs Assets/Code/Runtime/Gameplay/Collectibles/GemPool.cs
git commit -m "feat(collectibles): add Gem trigger pickup and GemPool"
```

---

## Task 8: `GemSpawner` MonoBehaviour

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs`

Public entry point: `TryPopulateSegment(Segment segment)`. With probability `_config.GemSpawnProbability`, picks one cube in the segment uniformly at random, takes a gem from the pool, places it `_config.GemHeightAboveCubeCenter` above the cube center, and initializes it. Uses its own seeded `System.Random` reset on `_onGameReset` for determinism (mirroring `PathGenerator`).

- [ ] **Step 8.1: Write the file**

Create `Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.World;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// Decides whether a freshly finalized segment receives a gem, and places it on
    /// a randomly chosen cube of that segment. Called by <c>PathGenerator</c> at the
    /// moment a segment reaches its target length — never on a per-frame basis.
    /// </summary>
    /// <remarks>
    /// Uses its own <see cref="System.Random"/> seeded from
    /// <see cref="GameConfigSO.GenerationSeed"/>, reset on every <c>_onGameReset</c>
    /// just like the path generator. Same seed → same gem layout on every Retry,
    /// independent of (but reproducible alongside) the path layout.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GemSpawner : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of gem spawn probability, gem value and height offset.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Pool gem instances are taken from.")]
        private GemPool _pool;

        [Header("Event Channels")]
        [SerializeField, Tooltip("Listened-to: reseeds the placement RNG so each run is reproducible from the seed.")]
        private GameEventSO _onGameReset;

        private System.Random _random;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(GemSpawner)} requires a {nameof(GameConfigSO)} reference.", this);
            Debug.Assert(_pool != null, $"{nameof(GemSpawner)} requires a {nameof(GemPool)} reference.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(GemSpawner)} requires {nameof(_onGameReset)}.", this);

            if (_config != null) _random = CreateRandom();
        }

        private void OnEnable()
        {
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        /// <summary>
        /// Rolls against <see cref="GameConfigSO.GemSpawnProbability"/> and, if it
        /// passes, places one gem on a uniformly random cube of <paramref name="segment"/>.
        /// Safe to call with a null or empty segment — does nothing in that case.
        /// </summary>
        public void TryPopulateSegment(Segment segment)
        {
            if (segment == null || segment.CubeCount == 0) return;
            if (_config == null || _pool == null || _random == null) return;

            if (_random.NextDouble() >= _config.GemSpawnProbability) return;

            int cubeIndex = _random.Next(0, segment.CubeCount);
            IReadOnlyList<GameObject> cubes = segment.Cubes;
            GameObject cube = cubes[cubeIndex];
            if (cube == null) return;

            GameObject gemGo = _pool.Get();
            if (gemGo == null) return;

            Vector3 position = cube.transform.position + Vector3.up * _config.GemHeightAboveCubeCenter;
            gemGo.transform.SetPositionAndRotation(position, Quaternion.identity);

            Gem gem = gemGo.GetComponent<Gem>();
            if (gem != null) gem.Initialize(_config.GemValue, _pool);
        }

        private void HandleGameReset()
        {
            if (_config != null) _random = CreateRandom();
        }

        private System.Random CreateRandom()
        {
            // Matches PathGenerator's sentinel: 0 = fresh seed each run via TickCount,
            // anything else = deterministic. Same int → reproducible gem layout. The
            // RNG instance is independent of PathGenerator's, so the two systems do
            // not consume each other's random sequence.
            int seed = _config.GenerationSeed != 0 ? _config.GenerationSeed : System.Environment.TickCount;
            return new System.Random(seed);
        }
    }
}
```

- [ ] **Step 8.2: Switch to Unity Editor and confirm compile**

Console clean. New component available.

- [ ] **Step 8.3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs
git commit -m "feat(collectibles): add GemSpawner with deterministic per-segment placement"
```

---

## Task 9: Hook `PathGenerator` to invoke `GemSpawner.TryPopulateSegment`

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/World/PathGenerator.cs`

The spawner is optional — if no `GemSpawner` is wired the path still generates correctly without gems. The hook fires at segment finalization (the moment a segment reaches its target cube count and a new one is about to start). The initial pre-populated segments also get gems this way, so the menu screen shows gems waiting to be collected.

- [ ] **Step 9.1: Add the optional spawner field**

In `Assets/Code/Runtime/Gameplay/World/PathGenerator.cs`:

1. At the top of the file, in the `using` block (around lines 1-4), append:

```csharp
using ZigZag.Runtime.Gameplay.Collectibles;
```

2. In the `[Header("Dependencies")]` block, immediately after `_ballTransform` (line ~39), add:

```csharp
        [SerializeField, Tooltip("Optional. If wired, finalized segments are offered to this spawner for gem placement.")]
        private GemSpawner _gemSpawner;
```

The field is intentionally not asserted in `Awake` — `PathGenerator` predates `GemSpawner` and must still run if the spawner is absent from the scene.

- [ ] **Step 9.2: Invoke `TryPopulateSegment` at segment finalization**

Locate the `SpawnNextCubeOrStartNewSegment` method (around line 178). The finalization branch is:

```csharp
            if (_currentSegment.CubeCount >= _currentSegmentTargetLength)
            {
                FlipDirection();
                StartNewSegment(isFirstSegment: false);
                return;
            }
```

Replace it with:

```csharp
            if (_currentSegment.CubeCount >= _currentSegmentTargetLength)
            {
                // Hand the just-finalized segment to the gem spawner before reassigning.
                if (_gemSpawner != null) _gemSpawner.TryPopulateSegment(_currentSegment);

                FlipDirection();
                StartNewSegment(isFirstSegment: false);
                return;
            }
```

- [ ] **Step 9.3: Also populate the last initialized segment**

After `InitializePath()` finishes, the most recent segment never reached its target length (the loop exits when `DistanceAhead >= AheadBuffer`), so it never went through the finalization branch. The ball will roll onto this segment without ever seeing a gem on it. Acceptable trade-off: the very last pending segment is gem-free until it gets finalized by a future cube. No code change needed — call this out as a known minor in the devlog (Task 13).

Skip this step's edit — it's intentionally a no-op. Check the checkbox to acknowledge the decision.

- [ ] **Step 9.4: Release uncollected gems on game reset**

Gems already release themselves on pickup (`Gem.OnTriggerEnter`), but any uncollected gem sitting on a recycled segment leaks. `GemSpawner` is the only system that knows what gems it placed, so it owns the cleanup. Track placed gems in a list; release the live ones on `_onGameReset` before reseeding.

Open `Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs` and make these three edits.

1. At the top of the file, add to the existing `using`s:

```csharp
using System.Collections.Generic;
```

2. In the field block, after `private System.Random _random;`, add:

```csharp
        // TODO: prune collected gems from this list during play if endurance runs
        // become a thing — today it only resets between runs.
        private readonly List<GameObject> _activeGems = new List<GameObject>(32);
```

3. In `TryPopulateSegment`, after `if (gem != null) gem.Initialize(_config.GemValue, _pool);`, append:

```csharp
            _activeGems.Add(gemGo);
```

4. Replace the existing `HandleGameReset`:

```csharp
        private void HandleGameReset()
        {
            for (int i = 0; i < _activeGems.Count; i++)
            {
                GameObject g = _activeGems[i];
                // Skip collected gems — they were already released by Gem.OnTriggerEnter
                // and the pool deactivates released instances, so activeSelf is false.
                if (g != null && g.activeSelf) _pool.Release(g);
            }
            _activeGems.Clear();

            if (_config != null) _random = CreateRandom();
        }
```

- [ ] **Step 9.5: Switch to Unity Editor and confirm compile**

Console clean.

- [ ] **Step 9.6: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/World/PathGenerator.cs Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs
git commit -m "feat(world): wire GemSpawner into PathGenerator segment finalization"
```

---

## Task 10: Extend `UIController` with score/best text + new-record badge

**Files:**
- Modify: `Assets/Code/Runtime/UI/UIController.cs`
- Modify: `Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef`

Adds three `TextMeshProUGUI` references and one optional GameObject for the "New Record" badge. Subscribes to `_onScoreChanged` and `_onBestScoreChanged`. New-record detection: track the best the UI was last told about; when GameOver fires and the next `_onBestScoreChanged` arrives in the same logical event, the badge is shown. Simpler equivalent used here: cache the best-at-game-over and compare to final score.

- [ ] **Step 10.1: Reference TextMeshPro in the UI asmdef**

Open `Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef`. Replace its contents with:

```json
{
    "name": "ZigZag.Runtime.UI",
    "rootNamespace": "ZigZag.Runtime.UI",
    "references": [
        "ZigZag.Runtime.Events",
        "Unity.TextMeshPro"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 10.2: Extend `UIController.cs` with score/best fields**

In `Assets/Code/Runtime/UI/UIController.cs`, add `using TMPro;` at the top:

```csharp
using TMPro;
using UnityEngine;
using ZigZag.Runtime.Events;
```

After the `[Header("Panels")]` block (the three `_menuPanel/_hudPanel/_gameOverPanel` fields), insert this new header and four fields:

```csharp
        [Header("Score Display")]
        [SerializeField, Tooltip("HUD text showing the current run's score during Playing.")]
        private TextMeshProUGUI _hudScoreText;

        [SerializeField, Tooltip("GameOver panel text showing the final score of the just-ended run.")]
        private TextMeshProUGUI _gameOverFinalScoreText;

        [SerializeField, Tooltip("GameOver and Menu text showing the persisted best score.")]
        private TextMeshProUGUI _bestScoreText;

        [SerializeField, Tooltip("GameObject toggled active when the just-ended run beat the previous best. Leave null if not used.")]
        private GameObject _newRecordBadge;
```

After the existing `[Header("Event Channels (Inbound)")]` fields, but before `[Header("Event Channels (Outbound)")]`, insert two more inbound channels:

```csharp
        [SerializeField, Tooltip("Listened-to: refreshes the HUD score text.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the best score text.")]
        private IntGameEventSO _onBestScoreChanged;
```

- [ ] **Step 10.3: Add state to track the new-record flag**

Below the existing fields, before `private void Awake()`, add:

```csharp
        private int _lastKnownBest;
        private bool _newBestSeenInThisRun;
```

These two fields together let the new-record decision survive Unity's unspecified subscriber order on `_onGameOver`: `ScoreManager.HandleGameOver` and `UIController.HandleGameOver` can fire in either order, and the badge still lights up exactly when warranted (see Step 10.6 for the logic).

- [ ] **Step 10.4: Extend `Awake` asserts**

Inside `Awake()`, append after the existing `Debug.Assert` lines:

```csharp
            Debug.Assert(_hudScoreText != null, $"{nameof(UIController)} requires {nameof(_hudScoreText)}.", this);
            Debug.Assert(_gameOverFinalScoreText != null, $"{nameof(UIController)} requires {nameof(_gameOverFinalScoreText)}.", this);
            Debug.Assert(_bestScoreText != null, $"{nameof(UIController)} requires {nameof(_bestScoreText)}.", this);
            Debug.Assert(_onScoreChanged != null, $"{nameof(UIController)} requires {nameof(_onScoreChanged)}.", this);
            Debug.Assert(_onBestScoreChanged != null, $"{nameof(UIController)} requires {nameof(_onBestScoreChanged)}.", this);
```

Note: `_newRecordBadge` is intentionally not asserted — it is optional.

- [ ] **Step 10.5: Wire subscriptions in `OnEnable` / `OnDisable`**

In `OnEnable`, append:

```csharp
            if (_onScoreChanged != null) _onScoreChanged.Register(HandleScoreChanged);
            if (_onBestScoreChanged != null) _onBestScoreChanged.Register(HandleBestScoreChanged);
```

In `OnDisable`, append:

```csharp
            if (_onScoreChanged != null) _onScoreChanged.Unregister(HandleScoreChanged);
            if (_onBestScoreChanged != null) _onBestScoreChanged.Unregister(HandleBestScoreChanged);
```

- [ ] **Step 10.6: Add the event handlers and replace the three lifecycle one-liners**

Below `OnRetryButtonClicked()`, before the existing `HandleGameStarted`, add the two new handlers:

```csharp
        private void HandleScoreChanged(int newScore)
        {
            if (_hudScoreText != null) _hudScoreText.text = $"Score: {newScore}";
            if (_gameOverFinalScoreText != null) _gameOverFinalScoreText.text = $"Score: {newScore}";
        }

        private void HandleBestScoreChanged(int newBest)
        {
            bool wasNewRecord = newBest > _lastKnownBest;
            _lastKnownBest = newBest;
            if (_bestScoreText != null) _bestScoreText.text = $"Best: {newBest}";

            if (!wasNewRecord) return;
            _newBestSeenInThisRun = true;

            // If we got here AFTER HandleGameOver (one of the two valid orderings),
            // the panel is already up; light the badge now.
            if (_newRecordBadge != null && _gameOverPanel != null && _gameOverPanel.activeSelf)
            {
                _newRecordBadge.SetActive(true);
            }
        }
```

Replace the existing three lifecycle handlers (`HandleGameStarted`, `HandleGameOver`, `HandleGameReset` — each currently a one-liner that calls a `Show*` helper) with these versions:

```csharp
        private void HandleGameStarted()
        {
            _newBestSeenInThisRun = false;
            if (_newRecordBadge != null) _newRecordBadge.SetActive(false);
            ShowHud();
        }

        private void HandleGameOver()
        {
            ShowGameOver();
            // If we got here AFTER HandleBestScoreChanged (the other valid ordering),
            // the flag is already true; light the badge now that the panel is up.
            if (_newBestSeenInThisRun && _newRecordBadge != null)
            {
                _newRecordBadge.SetActive(true);
            }
        }

        private void HandleGameReset()
        {
            _newBestSeenInThisRun = false;
            if (_newRecordBadge != null) _newRecordBadge.SetActive(false);
            ShowMenu();
        }
```

Why split the badge activation between two handlers: Unity does not guarantee subscriber order on a `GameEventSO`. When `_onGameOver` raises, `ScoreManager.HandleGameOver` (which calls `SaveBestIfHigher → _onBestScoreChanged.Raise`) and `UIController.HandleGameOver` (which calls `ShowGameOver`) can run in either order. The combination above lights the badge exactly when (a) the best moved up this run AND (b) the GameOver panel is visible, regardless of which handler ran first. `_newBestSeenInThisRun` is reset on `HandleGameStarted` so a best loaded at boot via `ScoreManager.Start` does not leak into the first run's badge state.

- [ ] **Step 10.7: Switch to Unity Editor and confirm compile**

Console clean. The new fields appear in the `UIController` inspector.

- [ ] **Step 10.8: Commit**

```bash
git add Assets/Code/Runtime/UI/UIController.cs Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef
git commit -m "feat(ui): add score/best HUD text and new-record badge"
```

---

## Task 11: Extend `GameBootstrap` validation

**Files:**
- Modify: `Assets/Code/Runtime/Core/GameBootstrap.cs`

`GameBootstrap` already asserts `PlatformPool`, `PathGenerator`, `GameStateMachine`. Iteration 4 adds three actors that deserve the same boot-time guard: `ScoreManager`, `GemPool`, `GemSpawner`. Adding a reference to `ScoreManager` requires Core to know the `Scoring` namespace; the `ZigZag.Runtime.Core` asmdef already references `ZigZag.Runtime.Gameplay`, which is the same assembly the new types live in — no asmdef change needed.

- [ ] **Step 11.1: Add the three new fields and asserts**

At the top of `Assets/Code/Runtime/Core/GameBootstrap.cs`, add to the existing `using`s:

```csharp
using ZigZag.Runtime.Gameplay.Collectibles;
using ZigZag.Runtime.Gameplay.Scoring;
```

Below the existing serialized fields (`_platformPool`, `_pathGenerator`, `_stateMachine`), append:

```csharp
        [SerializeField, Tooltip("Scene's score manager.")]
        private ScoreManager _scoreManager;

        [SerializeField, Tooltip("Scene's gem pool.")]
        private GemPool _gemPool;

        [SerializeField, Tooltip("Scene's gem spawner.")]
        private GemSpawner _gemSpawner;
```

In `Awake()`, after the existing three `Debug.Assert` calls, append:

```csharp
            Debug.Assert(_scoreManager != null, $"{nameof(GameBootstrap)} requires a {nameof(ScoreManager)} reference.", this);
            Debug.Assert(_gemPool != null, $"{nameof(GameBootstrap)} requires a {nameof(GemPool)} reference.", this);
            Debug.Assert(_gemSpawner != null, $"{nameof(GameBootstrap)} requires a {nameof(GemSpawner)} reference.", this);
```

- [ ] **Step 11.2: Switch to Unity Editor and confirm compile**

Console clean.

- [ ] **Step 11.3: Commit**

```bash
git add Assets/Code/Runtime/Core/GameBootstrap.cs
git commit -m "feat(core): extend GameBootstrap to validate ScoreManager, GemPool, GemSpawner"
```

---

## Task 12: Scene wiring + `P_Gem` prefab + playtest verification

**Files (manual, Unity editor):**
- Create: `Assets/Prefabs/P_Gem.prefab`
- Modify: `Assets/Scenes/SampleScene.unity` (via Unity, not by hand)
- Modify: `Assets/Settings/SO_GameConfig.asset` (set the new field values in the inspector)

All code changes are done. This task is the manual Unity wiring that makes the iteration playable. It is documented as code-equivalent precision so the engineer cannot misclick.

- [ ] **Step 12.1: Tag the ball with `Player`**

In the scene, select the ball GameObject. In the inspector, set its `Tag` to `Player`. If `Player` is not in the list, click `Tag → Add Tag → +` and create it, then re-select the ball and assign.

- [ ] **Step 12.2: Build `P_Gem.prefab`**

1. In the Hierarchy, right-click → `3D Object → Cube`. Rename it `P_Gem_tmp`.
2. Scale: `(0.4, 0.4, 0.4)`. Rotation: `(45, 0, 45)` — gives the octahedron silhouette per GDD §8.3.
3. **Remove the default `BoxCollider`** and add a fresh one — `Add Component → Box Collider`. Set `Is Trigger = true`. Size: `(1, 1, 1)` (local; with scale `0.4` the world trigger is ~0.4 wide, comfortable for ball radius `0.5`).
4. Add Component → `Rigidbody`. Set `Is Kinematic = true`, `Use Gravity = false`. (Both are also enforced by `Gem.Awake`; setting them on the prefab avoids one-frame flicker.)
5. Add Component → `Gem` (the script from Task 6).
6. In the `Gem` component, drag `Assets/Settings/Events/SO_OnGemCollected.asset` into the `_onGemCollected` slot. Leave `_ballTag` as `Player`.
7. Create a material `M_Gem` at `Assets/Art/Materials/M_Gem.mat`. Color: `#E91E63`. Emission: enabled, color `#FF4081`, intensity `1`. Assign it to the cube's `MeshRenderer`.
8. Drag `P_Gem_tmp` from the Hierarchy into `Assets/Prefabs/`. Unity creates `P_Gem.prefab`. Rename the prefab to `P_Gem` (drop `_tmp`). Delete the Hierarchy instance.

- [ ] **Step 12.3: Add `GemPool` and `GemSpawner` GameObjects to the scene**

1. Hierarchy → right-click → `Create Empty`. Rename `GemPool`. Add component `GemPool`. Drag `P_Gem.prefab` to `_gemPrefab`, `SO_GameConfig.asset` to `_config`.
2. Hierarchy → right-click → `Create Empty`. Rename `GemSpawner`. Add component `GemSpawner`. Drag `SO_GameConfig.asset` to `_config`, the `GemPool` GameObject to `_pool`, `SO_OnGameReset.asset` to `_onGameReset`.

- [ ] **Step 12.4: Wire `PathGenerator` to the spawner**

Select the existing `PathGenerator` GameObject. Drag the new `GemSpawner` into its `_gemSpawner` slot.

- [ ] **Step 12.5: Add `ScoreManager` to the scene**

1. Hierarchy → right-click → `Create Empty`. Rename `ScoreManager`. Add component `ScoreManager`.
2. Slots:
   - `_config` ← `SO_GameConfig.asset`
   - `_ballTransform` ← the ball GameObject (drag from Hierarchy)
   - `_onGemCollected` ← `SO_OnGemCollected.asset`
   - `_onGameStarted` ← `SO_OnGameStarted.asset`
   - `_onGameOver` ← `SO_OnGameOver.asset`
   - `_onGameReset` ← `SO_OnGameReset.asset`
   - `_onScoreChanged` ← `SO_OnScoreChanged.asset`
   - `_onBestScoreChanged` ← `SO_OnBestScoreChanged.asset`

- [ ] **Step 12.6: Add UI text + new-record badge**

Under the existing `Canvas`:

1. Inside `HUDPanel`, ensure there is a `TextMeshPro - Text (UI)` element. If the existing "Score: 0" placeholder is `UnityEngine.UI.Text` (legacy), delete it and create a TMP one: right-click `HUDPanel` → `UI → Text - TextMeshPro`. Position: top-left, anchored to top-left, position `(20, -20)`. Text: `Score: 0`. Font size: 48. Color: white. Name the GameObject `HUDScoreText`.
2. Inside `GameOverPanel`, similarly create three TMP texts:
   - `GameOverFinalScoreText` — center, text `Score: 0`, font size 48, white.
   - `BestScoreText` — center below, text `Best: 0`, font size 36, white.
   - `NewRecordBadge` — an empty GameObject under `GameOverPanel` containing one TMP text `NEW RECORD!` (font size 56, color `#FF4081`). Disable `NewRecordBadge` by default.
3. Select the `UIController` GameObject. New slots:
   - `_hudScoreText` ← `HUDPanel/HUDScoreText` TMP
   - `_gameOverFinalScoreText` ← `GameOverPanel/GameOverFinalScoreText` TMP
   - `_bestScoreText` ← `GameOverPanel/BestScoreText` TMP
   - `_newRecordBadge` ← `GameOverPanel/NewRecordBadge` GameObject
   - `_onScoreChanged` ← `SO_OnScoreChanged.asset`
   - `_onBestScoreChanged` ← `SO_OnBestScoreChanged.asset`

- [ ] **Step 12.7: Wire `GameBootstrap`**

Select the existing `Bootstrap` GameObject. New slots:
- `_scoreManager` ← `ScoreManager`
- `_gemPool` ← `GemPool`
- `_gemSpawner` ← `GemSpawner`

- [ ] **Step 12.8: Set sensible defaults on `SO_GameConfig.asset`**

Select `Assets/Settings/SO_GameConfig.asset`. New field values:
- `Gems → Gem Spawn Probability` = `0.3`
- `Gems → Gem Value` = `10`
- `Gems → Gem Height Above Cube Center` = `3.2` (cube Y-size is 5, so its top is `+2.5` above center; gem sphere collider sits at `+3.2`, ~0.7 above the cube top — comfortable margin)
- `Score → Distance Multiplier` = `1`
- `Pooling → Gem Pool Initial Size` = `20`

- [ ] **Step 12.9: Playtest the golden path**

Press Play.
1. Menu appears. `Score: 0` and `Best: 0` visible. **The path is already populated and a handful of gems are visible on it.** If no gems appear, check Console — most likely `SO_OnGemCollected` is null on the prefab or `GemSpawner._config` is null.
2. Click. Ball starts moving. HUD reads `Score: 0` → climbs slowly (distance multiplier × progress) → jumps by 10 when the ball touches a gem.
3. Steer onto gems. Each pickup: gem disappears, score jumps by 10.
4. Fall off the path. GameOver panel: `Score: <final>`, `Best: <max(prev best, final)>`. If `final > prev best`, `NEW RECORD!` shows.
5. Click `RETRY`. Menu appears again. `Score: 0`. `Best: <persisted>`. Path has been rebuilt with a new gem layout.
6. Stop and Play again. `Best: <persisted>` is loaded from PlayerPrefs.

- [ ] **Step 12.10: Playtest the edge cases**

- **Gem on the very first cube:** sometimes the spawn lands on cube 0 of the initial segment. The ball should pick it up on the first frame after `StartMoving` — verify no double-trigger or missed-trigger.
- **No gem available (pool exhausted):** set `Gem Pool Initial Size = 1` in `SO_GameConfig`, play, and let several segments finalize. Expect: at most one gem on screen at a time; the rest of the segments roll without gems. No `NullReferenceException`.
- **Deterministic seed:** set `GenerationSeed = 42`, play twice with the same seed, verify gem layout is identical run-to-run. Set back to `0` when done.
- **PlayerPrefs persistence:** beat the previous best, stop play, restart Unity, play again — `Best:` should show the score from the previous session. (Optional: clear with `PlayerPrefs.DeleteKey("BestScore")` from a temporary editor script if you want to test the zero case.)

- [ ] **Step 12.11: Inspect the Profiler for allocations**

`Window → Analysis → Profiler`. CPU Usage → Hierarchy. Search `Alloc`. During a steady-state run:
- `Update.ScriptRunBehaviourUpdate` should not show ScoreManager or PathGenerator allocations per frame.
- `OnTriggerEnter` → `Gem.OnTriggerEnter` may show one allocation per pickup (the event invocation list). Acceptable for the prototype.

If anything else allocates per frame, investigate before committing.

- [ ] **Step 12.12: Commit scene + prefab + config changes**

```bash
git add Assets/Prefabs/P_Gem.prefab Assets/Prefabs/P_Gem.prefab.meta Assets/Scenes/SampleScene.unity Assets/Settings/SO_GameConfig.asset Assets/Art/Materials/M_Gem.mat Assets/Art/Materials/M_Gem.mat.meta
git commit -m "feat(scene): wire gem pool, spawner, score manager and HUD score/best texts"
```

(If `Assets/Art/Materials/` does not yet exist as a tracked folder, the `.meta` for it is also added — include it in the commit.)

---

## Task 13: Devlog entry for iteration 4

**Files:**
- Modify: `devlog.md`

Records what was done, the decisions made, and what's pending for iteration 5.

- [ ] **Step 13.1: Append the iteration 4 entry**

Append to `devlog.md`:

```markdown
---

## 2026-05-22 — Iteración 4: gemas, score y persistencia

### Objetivo

Cerrar el loop con propósito: la bola recoge gemas, el score sube (gemas + distancia), el mejor record se persiste entre runs. GDD §14 día 4.

### Lo que se ha implementado

1. **`GameConfigSO` extendido** con tres bloques nuevos:
   - `Gems`: `_gemSpawnProbability = 0.3` (por tramo), `_gemValue = 10`, `_gemHeightAboveCubeCenter = 3.2`.
   - `Score`: `_distanceMultiplier = 1`.
   - `Pooling`: `_gemPoolInitialSize = 20`.

2. **Sub-feature `Gameplay/Collectibles/`** (mismo asmdef `ZigZag.Runtime.Gameplay`):
   - `Gem.cs` — `MonoBehaviour` con `[RequireComponent(Collider, Rigidbody)]`. Trigger; al entrar la bola raises `SO_OnGemCollected(value)` y se devuelve al pool. Patrón `Initialize(value, pool)` para inyectar dependencias en cada `Get` del pool.
   - `GemPool.cs` — gemelo directo de `PlatformPool`. Mismo prewarm Get/Release en `Awake`, mismo `maxSize = 2× initialSize`.
   - `GemSpawner.cs` — `TryPopulateSegment(Segment)` con dado contra `GemSpawnProbability`. RNG propio `System.Random` reseteado en `_onGameReset` (mismo seed que `PathGenerator`, instancias independientes). Mantiene `List<GameObject> _activeGems` para liberar gemas no recogidas al reset (TODO: prune cuando se introduzcan endurance runs).

3. **Sub-feature `Gameplay/Scoring/`**:
   - `ScoreCalculator.cs` — helper estático puro. `ComputeDistanceScore(ballPos, origin, forwardAxis, multiplier)` proyecta desplazamiento sobre `(-1,0,1)/√2` y devuelve `Mathf.FloorToInt(progress) * multiplier`, con clamp en cero para progreso negativo. Cubierto por EditMode tests.
   - `ScoreManager.cs` — `MonoBehaviour`. Acumula `_gemScore` (suma en `HandleGemCollected`) + `_distanceScore` (recomputado en `Update`). Solo raises `_onScoreChanged` cuando el total entero cambia, no cada frame. Persistencia: `PlayerPrefs.GetInt("BestScore", 0)` en `Awake`; `SetInt + Save` en `SaveBestIfHigher`, llamado al recibir `_onGameOver`.

4. **`PathGenerator` modificado** para invocar `_gemSpawner.TryPopulateSegment(_currentSegment)` justo antes de `FlipDirection + StartNewSegment`, es decir cuando un tramo alcanza su longitud objetivo. El último tramo de `InitializePath` no se finaliza por este camino y queda sin gema — known minor, se finaliza en cuanto la bola lo cruza.

5. **`UIController` extendido** con tres `TextMeshProUGUI` (`_hudScoreText`, `_gameOverFinalScoreText`, `_bestScoreText`) y un GameObject opcional `_newRecordBadge`. Suscrito a `SO_OnScoreChanged` y `SO_OnBestScoreChanged`. El badge se activa cuando `HandleBestScoreChanged` detecta que el nuevo best supera al anterior cacheado y el panel GameOver está activo. Asmdef `ZigZag.Runtime.UI` añade referencia `Unity.TextMeshPro`.

6. **`GameBootstrap` extendido** para validar `_scoreManager`, `_gemPool`, `_gemSpawner` en `Awake`. Sin cambios en asmdef (todos viven en `ZigZag.Runtime.Gameplay`).

7. **Test harness EditMode estrenado** — primer `.asmdef` de tests del proyecto (`Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef`). 7 tests sobre `ScoreCalculator` (cero, progreso por -X, por +Z, diagonal, backwards-clamp, multiplier, multiplier cero).

### Decisiones técnicas (mini-ADRs locales)

- **Gem requiere `Rigidbody` kinematic, no la bola.** ADR-001 manda bola sin Rigidbody. Unity 2022.3 exige que al menos uno de los dos colliders tenga Rigidbody para disparar `OnTriggerEnter`. Solución: la gema lo lleva (`isKinematic=true, useGravity=false`) — la bola sigue siendo collider estático con transform que se mueve.
- **Distancia medida por proyección sobre `GlobalForward`, no por `position.z`.** GDD §7.2 propuso `position.z` cuando el camino era diagonal `(1,0,1)`. Tras el rework a ejes mundo `-X/+Z` (iter 2 addendum), `position.z` ignoraría el progreso de los tramos `-X`. La proyección `Dot(pos - origin, (-1,0,1)/√2)` captura ambos correctamente.
- **`ScoreCalculator` como `static class` puro.** Separar la aritmética de los side-effects (raises, PlayerPrefs) permite tests EditMode triviales y deja a `ScoreManager` reducido a 1-liners no testeables (los wires de eventos). YAGNI: no se introduce `IBestScoreStore` — `PlayerPrefs` con clave `"BestScore"` es la historia completa.
- **`GemSpawner` con RNG propio.** Alternativa: pasar el `System.Random` de `PathGenerator`. Descartado porque acopla los dos sistemas. Cada uno tiene `System.Random` independiente seedeado con el mismo `_config.GenerationSeed`; las secuencias se consumen sin contaminarse y el run sigue siendo reproducible byte a byte por seed.
- **Score se broadcastea solo cuando el entero cambia.** El proyectado `progress` es float pero el score es int; la mayoría de frames `Mathf.FloorToInt` no cruza umbral. Sin esta guarda el HUD reprintearía 60×/s.

### Pendiente — setup manual en Unity

Cubierto íntegramente en `docs/superpowers/plans/2026-05-22-iteration-4-gems-and-score.md` Task 12. Resumen: crear `P_Gem.prefab`, añadir GameObjects `GemPool / GemSpawner / ScoreManager`, wire UI texts (HUD + GameOver + NewRecordBadge), wire `GameBootstrap` con las nuevas refs, etiquetar la bola con `Player`, fijar valores por defecto en `SO_GameConfig`.

### Próxima iteración (planteamiento)

5. Powerup imán (`IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool`). Atrae gemas en radio `R` durante `T` segundos. GDD §14 día 5. Demuestra que la arquitectura es extensible sin tocar `Gem`/`ScoreManager`.
```

- [ ] **Step 13.2: Commit**

```bash
git add devlog.md
git commit -m "docs: log iteration 4 — gems, score and persistence"
```

---

## Done criteria

Iteration 4 is complete when **all** of these hold:

1. `git status` is clean on `feat/iter4-gems-and-score`.
2. EditMode tests pass: `<test-run total="7" passed="7" failed="0" />`.
3. Manual playtest (Task 12 steps 12.9 and 12.10) all pass.
4. Unity Console is empty after a full Menu → Playing → GameOver → Retry → Playing loop.
5. Profiler shows no per-frame allocations attributable to ScoreManager, PathGenerator, GemSpawner.
6. `devlog.md` ends with the iteration 4 entry from Task 13.
7. PR (if applicable) cleanly merges into `main`.

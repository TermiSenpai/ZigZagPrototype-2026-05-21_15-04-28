# CLAUDE.md — ZigZagPrototype

Project-level guidance for Claude Code and any AI assistant working in this repository.
**These rules are mandatory and override conflicting defaults.**

---

## 1. Project Context

- **Engine:** Unity **2022.3.62f2 LTS**
- **Language:** C# (.NET Standard 2.1 / Unity 2022.3 compatible)
- **Render Pipeline:** Built-in (default for the project as of today). TODO: evaluate URP migration before vertical slice.
- **Target Platforms:** PC standalone first. TODO: define mobile/console targets.
- **Game Genre:** ZigZag-style arcade prototype (single-input, score-driven, runner/reflex).

---

## 2. Hard Rules (Non-Negotiable)

1. **English only.** Every identifier, comment, commit message, asset name, log string and `TODO` is written in English. No Spanish in code.
2. **Long-term work is `TODO:`** — never silently deferred. Format: `// TODO: <short description> (<owner-or-context>)`. Never `FIXME`, `XXX`, or untagged notes.
3. **Game design patterns are mandatory** for any non-trivial system (see §6). No ad-hoc managers, no god classes.
4. **Encapsulation is mandatory** (see §5). No `public` mutable fields. No exposed mutable state.
5. **Independence is mandatory.** Systems communicate through interfaces, events, or ScriptableObject channels — never via direct cross-system references when avoidable.
6. **Determinism in gameplay code.** No `DateTime.Now`, no unseeded `Random`, no frame-rate-dependent math without `Time.deltaTime` / `Time.fixedDeltaTime`.
7. **No code in `Update()` that can live elsewhere.** Cache, subscribe, pool, or move to `FixedUpdate`/coroutines as appropriate.
8. **Never edit `.meta` files by hand.** Never delete an asset without its `.meta`. Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build/`.
9. **Force-text serialization** for scenes and prefabs is assumed (set in `EditorSettings.asset`). Do not change it.
10. **Don't ask before reading.** Read the code first; only ask when ambiguity remains.

---

## 3. Directory Layout

All gameplay assets live under `Assets/_Project/` so they sort to the top and stay separate from third-party content.

```
Assets/
├── _Project/
│   ├── Art/                # Sprites, models, textures, materials
│   ├── Audio/              # Clips, mixers
│   ├── Code/
│   │   ├── Runtime/        # Runtime scripts (asmdef: ZigZag.Runtime)
│   │   │   ├── Core/       # Bootstrap, ServiceLocator, GameLoop
│   │   │   ├── Gameplay/   # Player, world, scoring
│   │   │   ├── Input/      # Input handling (abstraction over Input System)
│   │   │   ├── UI/         # HUD, menus
│   │   │   ├── Audio/      # Audio service
│   │   │   ├── Data/       # ScriptableObject data containers
│   │   │   ├── Events/     # ScriptableObject event channels
│   │   │   └── Utilities/  # Pure helpers, extensions
│   │   ├── Editor/         # Editor-only (asmdef: ZigZag.Editor, includePlatforms: Editor)
│   │   └── Tests/
│   │       ├── EditMode/   # asmdef: ZigZag.Tests.EditMode
│   │       └── PlayMode/   # asmdef: ZigZag.Tests.PlayMode
│   ├── Prefabs/
│   ├── Scenes/
│   ├── Settings/           # ScriptableObject configs, render assets
│   └── VFX/
├── Scenes/                 # Unity default; keep empty or remove SampleScene once _Project/Scenes exists
└── ...
```

- Every runtime folder under `Code/` has an **assembly definition (`.asmdef`)**.
- Editor code lives in its own `.asmdef` with `includePlatforms: [Editor]` to keep it out of player builds.
- Tests live in `.asmdef` files that reference `UnityEngine.TestRunner` and `UnityEditor.TestRunner` and set `defineConstraints: [UNITY_INCLUDE_TESTS]`.

TODO: Create the `_Project` skeleton with .asmdef files on first feature commit.

---

## 4. Naming Conventions

| Element              | Convention                       | Example                          |
| -------------------- | -------------------------------- | -------------------------------- |
| Namespace            | `ZigZag.<Layer>.<Feature>`       | `ZigZag.Runtime.Gameplay.Player` |
| Class / Struct / Enum| `PascalCase`                     | `PlayerController`               |
| Interface            | `IPascalCase`                    | `IDamageable`                    |
| Method / Property    | `PascalCase`                     | `ApplyDamage`                    |
| Private field        | `_camelCase`                     | `_rigidbody`                     |
| Serialized field     | `[SerializeField] private` + `_camelCase` | `[SerializeField] private float _moveSpeed;` |
| Constant             | `PascalCase` (not SCREAMING)     | `MaxLives`                       |
| Static readonly      | `PascalCase`                     | `DefaultGravity`                 |
| Local / parameter    | `camelCase`                      | `deltaTime`                      |
| Assembly             | `ZigZag.<Layer>[.<Feature>]`     | `ZigZag.Runtime.Gameplay`        |
| ScriptableObject asset | `SO_<Name>` (file), class `<Name>SO` or `<Name>Data` | `SO_PlayerStats`, `PlayerStatsSO` |
| Prefab                | `P_<Name>`                       | `P_Player`                       |
| Scene                 | `S_<Name>`                       | `S_MainMenu`, `S_Gameplay`       |
| Material              | `M_<Name>`                       | `M_PlayerBody`                   |

---

## 5. Encapsulation & API Hygiene

- **Never** `public` a mutable field. Use `[SerializeField] private` for inspector exposure and a `public` property (`{ get; private set; }` or get-only) for external reads.
- **Never** expose internal collections directly. Return `IReadOnlyList<T>` / `IReadOnlyDictionary<TK,TV>` or copies.
- **Default access** is the most restrictive that works: `private` → `internal` → `protected` → `public`.
- **`sealed` by default** for concrete classes that aren't designed for inheritance.
- **No singletons that mutate global state from `Awake`** — prefer `ServiceLocator` (registered by a bootstrapper) or scene-scoped composition root.
- **MonoBehaviours own behavior, not data.** Long-lived configuration lives in ScriptableObjects.
- **Dependency direction:** `UI` → `Gameplay` → `Core`. Never upward. Never sideways without an interface.
- **No `FindObjectOfType`, `GameObject.Find`, `SendMessage`, `BroadcastMessage`** outside of editor/bootstrap code. They are slow and break encapsulation.
- **Validate inputs at boundaries.** Public methods on services should guard against `null` and out-of-range arguments and either throw `ArgumentException`/`ArgumentNullException` or fail loudly with `Debug.LogError` + early return.

---

## 6. Design Patterns (use these by default)

| Pattern                | Use when…                                                      | Notes                                                                 |
| ---------------------- | -------------------------------------------------------------- | --------------------------------------------------------------------- |
| **ScriptableObject Architecture** | Configuration, tunable data, event channels, runtime sets | Default for any tunable value. Avoids singletons.                     |
| **Event Channel (SO)** | Cross-system communication without references                  | One SO asset per event; subscribers register in `OnEnable`/unregister in `OnDisable`. |
| **Service Locator**    | Accessing app-wide services (Audio, Save, Analytics)           | Registered by bootstrapper; never accessed in `Awake` of regular objects. |
| **State Machine**      | Player state, game state, AI                                   | Prefer a small hand-rolled FSM or a HFSM; avoid bringing in big frameworks for the prototype. |
| **Object Pool**        | Anything spawned/despawned at runtime (bullets, FX, obstacles) | Use `UnityEngine.Pool.ObjectPool<T>` (built into 2022 LTS).           |
| **Command**            | Undoable actions, input remapping, replay                      | Especially for the input layer.                                       |
| **Observer / Pub-Sub** | Loose coupling when SO channels are overkill                   | C# `event` or `Action<T>`; always unsubscribe.                        |
| **Strategy**           | Swappable behaviors (movement, scoring rules)                  | Inject via interface or SO.                                           |
| **Factory**            | Object creation with non-trivial setup                         | Combine with pools.                                                   |
| **MVP / MVVM-lite**    | UI screens                                                     | View (MonoBehaviour) ⇄ Presenter (plain C#) ⇄ Model (SO/data).        |
| **Composition over inheritance** | Always                                              | Inheritance only for true `is-a`; prefer small interfaces.            |

Anti-patterns to **reject on sight**: god `GameManager`, `static` mutable state, scene-spanning singletons created with `Instance ??= this`, `SendMessage`, `Resources.Load` in hot paths, business logic in editor scripts.

---

## 7. MonoBehaviour Discipline

- **Cache component lookups** in `Awake`. Never call `GetComponent` in `Update`/`FixedUpdate`.
- **Subscribe in `OnEnable`, unsubscribe in `OnDisable`.** Symmetric, every time. Avoids leaked handlers when objects are pooled.
- **Use `Awake` for self-init**, `Start` for cross-object init, `OnEnable`/`OnDisable` for subscriptions.
- **Physics in `FixedUpdate`**, input in `Update`, rendering-adjacent work in `LateUpdate`.
- **`[RequireComponent]`** any hard dependency. **`[DisallowMultipleComponent]`** when only one is valid.
- **`[SerializeField] private`** + inspector tooltip is the standard exposure idiom.
- **Validate serialized data** in `OnValidate` (editor-only) — catch misconfiguration before play.
- **Coroutines** are for time-based sequences; cancel them in `OnDisable` (`StopAllCoroutines` or stored handle).
- **Avoid `async void`.** If you use async, return `Task` / `UniTask` (TODO: decide if UniTask is added) and guard against `destroyCancellationToken` (Unity 2022.2+).
- **`destroyCancellationToken`** is the recommended cancellation source for async work tied to a MonoBehaviour lifecycle in 2022.3.

---

## 8. Performance Rules

- **No allocations in `Update`.** No `new List<T>()`, no LINQ, no `string` concatenation, no `foreach` over `IEnumerable<T>` boxed enumerators, no `params` calls.
- **Use `for` over `foreach`** in hot paths. `foreach` over `List<T>` is fine; over interfaces is not.
- **Avoid `Camera.main`** in hot paths — cache it.
- **Avoid `Transform.Find`, `tag` string compares in hot paths** — use cached references or hashed IDs.
- **Pool everything that spawns** more than once per second. Use `UnityEngine.Pool.ObjectPool<T>`.
- **String hashing for `Animator`:** `Animator.StringToHash("Speed")` cached in `static readonly int`.
- **Physics queries:** prefer non-allocating variants (`Physics.RaycastNonAlloc`, `OverlapSphereNonAlloc`) with cached buffers.
- **`SetActive` is cheaper than instantiate/destroy** — that's the whole point of pooling.
- **Profile before optimizing.** The Profiler and Frame Debugger are the source of truth; assumptions are not.

---

## 9. Logging & Diagnostics

- Use `Debug.Log` only behind a conditional or a project-level logger. Verbose logs in hot paths are forbidden.
- Errors that indicate a bug: `Debug.LogError`. Warnings that the user can fix: `Debug.LogWarning`. Anything else, consider whether it should exist at all.
- Use `Debug.Assert` / `UnityEngine.Assertions.Assert` for invariants in development.
- Strip logs from release builds with `[Conditional("UNITY_EDITOR")]` or `[Conditional("DEVELOPMENT_BUILD")]` on custom logger methods.

---

## 10. Testing

- **EditMode tests** for pure C# (data, math, state machines, services with mocked Unity boundaries).
- **PlayMode tests** for MonoBehaviour interactions, physics, coroutines.
- **No mocks of Unity types** — wrap them behind interfaces and mock the interface.
- **Arrange-Act-Assert** structure, one logical assertion per test, descriptive test names: `Method_State_ExpectedResult`.
- Test files mirror the runtime structure under `Assets/_Project/Code/Tests/<EditMode|PlayMode>/`.
- TODO: add a CI job (GitHub Actions + `game-ci/unity-test-runner`) once the repo has its first runtime scripts.

---

## 11. Source Control

- **Force Text** asset serialization (already the default in 2022 LTS — verify in `EditorSettings.asset`).
- **Commit `.meta` files** with their assets, never separately.
- **Never commit** `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build/`, `*.csproj`, `*.sln` (the project's `.gitignore` already handles this — don't undo it).
- **Atomic commits.** One conceptual change per commit. Imperative-mood, English commit subjects (`Add player input service`, not `Added` or `Adds`).
- **Branches:** `main` is the stable trunk. Work on `feat/<short>`, `fix/<short>`, `chore/<short>`.

---

## 12. Required Skeleton — `MonoBehaviour` Template

Every new MonoBehaviour should look like this until proven otherwise:

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

## 13. Required Skeleton — ScriptableObject Event Channel

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

TODO: generic `GameEventSO<T>` once typed payloads are needed.

---

## 14. When Working on a Task

1. **Read first.** Locate the system, read its public surface, read its tests.
2. **Choose the right pattern** from §6 — don't invent one.
3. **Write the smallest change** that satisfies the requirement. No speculative abstractions.
4. **Encapsulate.** Default to `private`. Add public surface only with justification.
5. **Add or update tests** if the change is testable.
6. **Mark unfinished work** with `// TODO:` and a one-line reason. Never leave commented-out code.
7. **Update CLAUDE.md** when a rule changes — it is the source of truth.

---

## 15. When in Doubt

- **A pattern fits poorly?** Default to the simplest composition (interface + plain C# class).
- **A system feels too coupled?** Insert a ScriptableObject event channel.
- **Performance feels off?** Open the Profiler before writing a single optimization.
- **An asset's purpose is unclear?** Look at its `.asset`/`.prefab` references via the Project window's "Find References in Scene".

---

## 16. Agents

Specialized subagents live in [`.claude/agents/`](.claude/agents/). Use them via the `Agent` tool. See [`.claude/agents/AGENTS.md`](.claude/agents/AGENTS.md) for the catalog and dispatch guide.

# Agents Catalog — ZigZagPrototype

Specialized subagents for Unity 2022.3.62f2 LTS development.
Each agent enforces a slice of [`CLAUDE.md`](../../CLAUDE.md). Dispatch them via the `Agent` tool with `subagent_type: <agent-name>`.

---

## Dispatch Map

| You're about to…                                              | Use agent                    |
| ------------------------------------------------------------- | ---------------------------- |
| Design a new system, pick patterns, set module boundaries     | `unity-architect`            |
| Write or modify a gameplay script (player, world, scoring…)   | `unity-gameplay-programmer`  |
| Diagnose frame time, GC, draw calls, physics cost             | `unity-performance-profiler` |
| Review C# changes against project rules                       | `unity-code-reviewer`        |
| Add EditMode / PlayMode tests                                 | `unity-test-author`          |
| Build custom inspectors, editor windows, build hooks          | `unity-editor-tooling`       |
| Build a HUD, menu, screen, or any UI                          | `unity-ui-developer`         |

---

## Agents

### [`unity-architect`](unity-architect.md)
Designs systems with explicit pattern choice (ScriptableObject Architecture, SO Event Channel, Service Locator, State Machine, Object Pool, Command, Observer, Strategy, Factory, MVP). Returns API sketch + file list + dependency direction + asmdef split.

### [`unity-gameplay-programmer`](unity-gameplay-programmer.md)
Writes complete, compiling C# files following the §12 MonoBehaviour template — sealed types, private serialized fields, symmetric `OnEnable`/`OnDisable`, no `GetComponent` in `Update`, no allocations in hot paths.

### [`unity-performance-profiler`](unity-performance-profiler.md)
Measure-first optimizer. Refuses to change code without a Profiler-backed hypothesis. Ranks fixes by expected impact and prescribes the verification step.

### [`unity-code-reviewer`](unity-code-reviewer.md)
Strict, citation-driven reviewer. Walks an 8-section checklist against `CLAUDE.md` and returns findings grouped by severity with `file:line` and concrete fixes.

### [`unity-test-author`](unity-test-author.md)
Writes EditMode tests (pure C#) and PlayMode tests (MonoBehaviour/physics) with proper `.asmdef` references, AAA structure, deterministic seeds, and `[TearDown]` cleanup. Refuses to test code that should be extracted to plain C# first.

### [`unity-editor-tooling`](unity-editor-tooling.md)
Builds editor-only tools (custom inspectors, property drawers, EditorWindows, asset post-processors, build hooks) in editor-only `.asmdef`s — never leaks into player builds, never mutates assets without `Undo`.

### [`unity-ui-developer`](unity-ui-developer.md)
Implements UI with MVP-lite separation (View ⇄ Presenter ⇄ Model/SO) on uGUI + TextMeshPro. Enforces canvas batching discipline, raycast hygiene, and no business logic in views.

---

## Typical Workflows

### New feature

1. `unity-architect` → design the system, get the file list and pattern choice.
2. `unity-gameplay-programmer` (and/or `unity-ui-developer`) → implement.
3. `unity-test-author` → add EditMode/PlayMode tests.
4. `unity-code-reviewer` → audit before merge.
5. `unity-performance-profiler` → only if the feature touches a hot path.

### Bug fix

1. `unity-code-reviewer` on the suspect file to surface latent issues.
2. `unity-gameplay-programmer` (or relevant agent) to apply the fix.
3. `unity-test-author` to add a regression test.

### Optimization pass

1. `unity-performance-profiler` to identify and verify the bottleneck.
2. `unity-gameplay-programmer` to apply the prescribed change.
3. `unity-performance-profiler` again to confirm the improvement.

---

## Conventions for All Agents

- **English only**, in every output — identifiers, comments, prose, commit messages.
- **TODOs** for deferred work: `// TODO: <imperative description>`.
- **Citations** as `file_path:line_number` in markdown link format: `[Foo.cs:42](Assets/_Project/Code/Runtime/Foo.cs#L42)`.
- **Refuse out-of-scope changes**, but log them as `TODO:` follow-ups in the response.
- **Never** silently break encapsulation, introduce mutable globals, or skip the rules in `CLAUDE.md`.

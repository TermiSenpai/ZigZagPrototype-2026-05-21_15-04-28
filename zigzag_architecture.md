# ZigZag — Arquitectura Técnica

> Documento técnico complementario al GDD ([`zigzag_gdd.md`](zigzag_gdd.md)). Recoge decisiones de arquitectura, firmas de clases, sistema de eventos, convenciones de código y trade-offs (ADRs ligeros).
>
> **Alineado con [`CLAUDE.md`](CLAUDE.md) y [`.claude/agents/AGENTS.md`](.claude/agents/AGENTS.md). Si surge cualquier conflicto, manda `CLAUDE.md`.**

**Versión de Unity:** 2022.3.62f2 LTS
**Render pipeline:** Built-in (default del proyecto). // TODO: evaluate URP migration before vertical slice (CLAUDE §1).
**Lenguaje del código:** C# (.NET Standard 2.1 / Unity 2022.3).
**Idioma:** el **código** (identificadores, comentarios, `TODO:`, logs, nombres de asset, mensajes de commit) está **exclusivamente en inglés**. Este documento de diseño está en castellano por velocidad del autor, según se explicita en [`CLAUDE.md` §2.1](CLAUDE.md).

---

## 1. Propósito del documento

Este documento existe para:

1. Dejar **explícitas las decisiones técnicas** que un revisor verá al abrir el código.
2. Servir como **contrato de las clases principales antes de escribirlas** (diseño primero, código después).
3. Documentar **convenciones** que se aplicarán uniformemente.
4. Justificar trade-offs en formato de mini-ADRs.

No es documentación de API generada del código. No reemplaza los comentarios `///` en el código mismo.

---

## 2. Visión general de la arquitectura

### 2.1 Filosofía

- **Componentes pequeños con responsabilidad única** (SRP). Cada `MonoBehaviour` hace una cosa.
- **Comunicación por eventos**, no por llamadas directas entre sistemas no relacionados.
- **Configuración fuera del código**, en `ScriptableObject`.
- **Inyección por inspector**, nunca `FindObjectOfType` ni `GameObject.Find` en runtime.
- **Pooling de todo lo instanciable**, sin `Instantiate` / `Destroy` durante el gameplay.
- **Determinismo en gameplay**: nada de `DateTime.Now` ni `UnityEngine.Random` sin seed para generación.

### 2.2 Capas y dirección de dependencias

```
┌────────────────────────────────────────────────────────────┐
│  Presentación   UI · Audio · VFX                            │  → solo escucha eventos
└────────────────────────────────────────────────────────────┘
                          ▲
┌────────────────────────────────────────────────────────────┐
│  Coordinación   Core (GameStateMachine, GameBootstrap)      │  → orquesta gameplay
└────────────────────────────────────────────────────────────┘
                          ▲
┌────────────────────────────────────────────────────────────┐
│  Gameplay   Player · World · Collectibles · Powerups ·      │
│             Scoring · CameraSystem                          │  → publica eventos
└────────────────────────────────────────────────────────────┘
                          ▲
┌────────────────────────────────────────────────────────────┐
│  Datos / Input   GameConfigSO · GameEventSO · InputHandler  │  → no depende de nadie
└────────────────────────────────────────────────────────────┘
```

Regla: **UI → Gameplay → Core → Data**. Nunca hacia arriba. Nunca lateral sin interfaz o evento.

### 2.3 Reglas duras (heredadas de CLAUDE.md §2)

Sirven de checklist permanente:

1. **Inglés** en todo el código.
2. Trabajo diferido como `// TODO: <descripción> (<contexto>)`. **Nunca** `FIXME`, `XXX` ni notas sin tag.
3. **Patrones obligatorios** para sistemas no triviales (ver §6).
4. **Encapsulación obligatoria** — nunca `public` mutable.
5. **Independencia** — interfaces, eventos o `GameEventSO`, nunca referencias cruzadas innecesarias.
6. **Determinismo** en código de gameplay.
7. **`Update()` mínimo** — cache, suscripciones, pooling, mover a `FixedUpdate` o coroutines.
8. **Nunca** editar `.meta` a mano ni commitear `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`.
9. **Force-text serialization** asumido para escenas y prefabs (no tocar `EditorSettings.asset`).
10. **Leer antes de preguntar.**

---

## 3. Convenciones de código (alineadas a CLAUDE.md §4 y §5)

### 3.1 Naming — única tabla de referencia

| Elemento                  | Convención                                | Ejemplo                                                |
| ------------------------- | ----------------------------------------- | ------------------------------------------------------ |
| Namespace                 | `ZigZag.<Layer>.<Feature>`                | `ZigZag.Runtime.Gameplay.Player`                       |
| Clase / Struct / Enum     | `PascalCase`                              | `BallController`                                       |
| Interface                 | `IPascalCase`                             | `IPowerup`                                             |
| Método / Propiedad        | `PascalCase`                              | `StartMoving`, `CurrentSpeed`                          |
| Campo privado             | `_camelCase`                              | `_rigidbody`                                           |
| Campo serializado         | `[SerializeField] private` + `_camelCase` | `[SerializeField] private float _forwardSpeed;`        |
| Constante                 | `PascalCase` (**no** SCREAMING)           | `MaxLives`, `DefaultGravity`                           |
| Static readonly           | `PascalCase`                              | `DefaultGravity`                                       |
| Local / parámetro         | `camelCase`                               | `deltaTime`                                            |
| Assembly                  | `ZigZag.<Layer>[.<Feature>]`              | `ZigZag.Runtime.Gameplay`                              |
| Evento C#                 | `On<Sustantivo><Pasado>`                  | `OnDirectionChanged`, `OnGemCollected`                 |
| Asset `ScriptableObject`  | `SO_<Nombre>` (file), clase `<Nombre>SO`  | `SO_GameConfig.asset`, `GameConfigSO`                  |
| Prefab                    | `P_<Nombre>`                              | `P_Player`, `P_PlatformCube`                           |
| Escena                    | `S_<Nombre>`                              | `S_Main`                                               |
| Material                  | `M_<Nombre>`                              | `M_Platform_A`                                         |

**Sin opcionalidad.** Estas son las convenciones. Si en algún punto algo no encaja, se discute y se actualiza este documento — no se aplican variantes ad-hoc.

### 3.2 Reglas de oro

1. **Sin magic numbers.** Toda constante numérica configurable vive en `GameConfigSO`.
2. **Sin `Instantiate` / `Destroy` en runtime.** Solo durante setup inicial (`Awake` / `Start` de `GameBootstrap`).
3. **`event` keyword obligatorio** en eventos C# públicos. Nunca `public Action<T> OnX`.
4. **Suscripción en `OnEnable`, desuscripción en `OnDisable`.** Simétrico. Sin excepciones.
5. **Invocación de eventos con `?.Invoke(...)`.** Nunca `.Invoke(...)` directo.
6. **Sin lambdas en suscripciones a eventos.** No se pueden desuscribir.
7. **`[SerializeField] private`** para exponer al inspector. Nunca `public` mutable.
8. **`sealed`** por defecto en clases concretas. Herencia solo cuando hay un `is-a` real.
9. **Una clase, una responsabilidad.** ~200 líneas suele indicar que toca partir.
10. **Comentarios `///`** en APIs públicas de clases reutilizables. Comentarios de cuerpo solo cuando el código no se explica solo.
11. **Validar entradas en boundaries.** Métodos públicos de servicios chequean `null` / rango y, o bien lanzan `ArgumentException` / `ArgumentNullException`, o bien hacen `Debug.LogError` + early return.

### 3.3 Validación de referencias serializadas

Patrón estándar para detectar configuraciones incorrectas en runtime:

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

`OnValidate` corre en el editor y aborta misconfiguraciones antes del play. `Debug.Assert` falla en runtime con stack trace en lugar de soltar un `NullReferenceException` 50 frames después.

---

## 4. Estructura de carpetas

Alineada a [`CLAUDE.md` §3](CLAUDE.md). Todos los assets del juego cuelgan de `Assets/_Project/` para no mezclarse con third-party.

```
Assets/
├── _Project/
│   ├── Art/                             # Sprites, modelos, texturas, materiales
│   ├── Audio/                           # Clips, mixers
│   ├── Code/
│   │   ├── Runtime/
│   │   │   ├── Core/                    # GameBootstrap, GameStateMachine, GameState (enum)
│   │   │   ├── Gameplay/
│   │   │   │   ├── Player/              # BallController
│   │   │   │   ├── World/               # PathGenerator, Segment, PlatformPool
│   │   │   │   ├── Collectibles/        # Gem, GemSpawner, GemPool
│   │   │   │   ├── Powerups/            # IPowerup, MagnetPowerup, PowerupManager, PowerupPool
│   │   │   │   ├── Scoring/             # ScoreManager, ScorePersistence
│   │   │   │   └── CameraSystem/        # CameraFollow
│   │   │   ├── Input/                   # InputHandler
│   │   │   ├── UI/                      # UIController, MenuPanel, HUDPanel, GameOverPanel
│   │   │   ├── Audio/                   # AudioManager
│   │   │   ├── Data/                    # GameConfigSO
│   │   │   ├── Events/                  # GameEventSO, GameEventSO<T>, IntGameEventSO, ...
│   │   │   └── Utilities/               # Helpers puros, extensiones
│   │   ├── Editor/                      # Tools editor-only
│   │   └── Tests/
│   │       ├── EditMode/
│   │       └── PlayMode/
│   ├── Prefabs/                         # P_Ball, P_PlatformCube, P_Gem, P_Magnet, ...
│   ├── Scenes/                          # S_Main.unity
│   ├── Settings/                        # SO_GameConfig.asset + assets de eventos SO_*
│   └── VFX/
└── Scenes/                              # Default de Unity. Borrar SampleScene cuando S_Main exista
```

**Prefijo `_Project/`:** se ordena alfabéticamente al principio y separa contenido propio de paquetes.

---

## 5. Assembly Definitions (.asmdef)

Una `.asmdef` por carpeta de Runtime, según [`CLAUDE.md` §3](CLAUDE.md). El coste de setup es one-shot; la ganancia es compilaciones incrementales rápidas y aplicación dura de la dirección de dependencias.

| asmdef                         | Path                                              | Referencias internas                            | Notas                                                              |
| ------------------------------ | ------------------------------------------------- | ----------------------------------------------- | ------------------------------------------------------------------ |
| `ZigZag.Runtime.Core`          | `Assets/_Project/Code/Runtime/Core/`              | Events, Data                                    |                                                                    |
| `ZigZag.Runtime.Data`          | `Assets/_Project/Code/Runtime/Data/`              | —                                               | Sólo `ScriptableObject` de configuración.                          |
| `ZigZag.Runtime.Events`        | `Assets/_Project/Code/Runtime/Events/`            | —                                               | `GameEventSO` y variantes tipadas.                                 |
| `ZigZag.Runtime.Input`         | `Assets/_Project/Code/Runtime/Input/`             | Events                                          |                                                                    |
| `ZigZag.Runtime.Gameplay`      | `Assets/_Project/Code/Runtime/Gameplay/`          | Core, Data, Events, Input, Utilities            | Todas las features de gameplay viven en sub-namespaces.            |
| `ZigZag.Runtime.UI`            | `Assets/_Project/Code/Runtime/UI/`                | Core, Data, Events                              | TextMeshPro. **Nunca** referencia Gameplay directamente.           |
| `ZigZag.Runtime.Audio`         | `Assets/_Project/Code/Runtime/Audio/`             | Data, Events                                    |                                                                    |
| `ZigZag.Runtime.Utilities`     | `Assets/_Project/Code/Runtime/Utilities/`         | —                                               | Pure C#, helpers, extensions.                                      |
| `ZigZag.Editor`                | `Assets/_Project/Code/Editor/`                    | Cualquier asmdef de Runtime                     | `includePlatforms: [Editor]`. **Nunca** entra al player build.     |
| `ZigZag.Tests.EditMode`        | `Assets/_Project/Code/Tests/EditMode/`            | Runtime + `UnityEngine.TestRunner`              | `defineConstraints: [UNITY_INCLUDE_TESTS]`.                        |
| `ZigZag.Tests.PlayMode`        | `Assets/_Project/Code/Tests/PlayMode/`            | Runtime + `UnityEngine.TestRunner`              | `defineConstraints: [UNITY_INCLUDE_TESTS]`.                        |

**Verificación post-setup:** desde cualquier asmdef de Runtime no debe poder hacerse `using ZigZag.Editor.*`. Si Unity compila eso, la asmdef de editor está mal configurada.

---

## 6. Sistema de eventos (decisión final: híbrido)

`CLAUDE.md` §6 lista **Event Channel (SO)** como patrón por defecto para comunicación cross-system, y **`static` mutable state** como anti-patrón explícito. Por tanto la decisión es:

- **Eventos globales / cross-system → `GameEventSO` (ScriptableObject Event Channel).** Permite que UI escuche a Gameplay sin tener referencia, y el listado de eventos vive como assets que un revisor puede explorar.
- **Eventos locales (un componente expone, otro suscribe en el mismo sistema) → `event Action<T>` en el componente emisor.** Patrón Observer de C# puro. CLAUDE.md §6 lo permite explícitamente "cuando los SO channels son overkill".

Ningún `static` mutable. Ningún singleton.

### 6.1 `GameEventSO` (skeleton, alineado a `CLAUDE.md` §13)

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

Variante con payload (creada al primer uso, no antes):

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

### 6.2 Catálogo de eventos

**Globales (assets `SO_*.asset` en `Assets/_Project/Settings/Events/`):**

| Asset                       | Tipo                | Disparado por                | Suscriptores típicos                              |
| --------------------------- | ------------------- | ---------------------------- | ------------------------------------------------- |
| `SO_OnGameStarted`          | `GameEventSO`       | `GameStateMachine`           | `BallController`, `PathGenerator`, `UIController` |
| `SO_OnGameOver`             | `GameEventSO`       | `GameStateMachine`           | `BallController`, `PathGenerator`, `UIController`, `AudioManager`, `ScoreManager`, `PowerupManager` |
| `SO_OnGameReset`            | `GameEventSO`       | `GameStateMachine`           | Todos los sistemas con estado mutable             |
| `SO_OnScoreChanged`         | `IntGameEventSO`    | `ScoreManager`               | `UIController` (HUD)                              |
| `SO_OnBestScoreChanged`     | `IntGameEventSO`    | `ScoreManager`               | `UIController` (Menu, GameOver)                   |
| `SO_OnGemCollected`         | `IntGameEventSO`    | `Gem`                        | `ScoreManager`, `AudioManager`, VFX               |
| `SO_OnPowerupActivated`     | `GameEventSO`       | `PowerupManager`             | `UIController` (HUD indicator), `AudioManager`    |
| `SO_OnPowerupExpired`       | `GameEventSO`       | `PowerupManager`             | `UIController`                                    |

**Locales (`event` C# en el componente emisor):**

- `InputHandler.OnTapped` — `BallController` y `AudioManager` (opcional) lo escuchan.
- `BallController.OnDirectionChanged` — telemetría/feedback inmediato. Privado al sistema Gameplay.
- `BallController.OnFell` — `GameStateMachine` lo escucha para hacer la transición a `GameOver`.

Si un evento local se necesita en más de dos sistemas, sube a `GameEventSO`.

### 6.3 Reglas de suscripción

- `event` keyword obligatorio para C# events públicos.
- `GameEventSO` se inyecta por inspector: `[SerializeField] private GameEventSO _onGameStarted;`.
- Suscripción en `OnEnable`, desuscripción en `OnDisable`. Simétrico.
- Sin lambdas en suscripciones (no se pueden desuscribir → memory leaks).
- Invocación de C# events con `?.Invoke(...)`.

### 6.4 Flujos clave

**Inicio de partida:**
```
Usuario click en Menu
  └─> InputHandler.OnTapped (evento C# local)
       └─> GameStateMachine.HandleMenuTap()
            └─> SO_OnGameStarted.Raise()
                 ├─> BallController: arranca movimiento
                 ├─> ScoreManager: resetea contadores
                 ├─> PathGenerator: empieza generación
                 ├─> UIController: oculta menú, muestra HUD
                 └─> AudioManager: (opcional) ambient
```

**Recogida de gema:**
```
Bola entra en trigger de Gema
  └─> Gem.OnTriggerEnter
       ├─> GemPool.Release(this)
       └─> SO_OnGemCollected.Raise(value)
            ├─> ScoreManager: suma puntos
            │    └─> SO_OnScoreChanged.Raise(newScore)
            │         └─> UIController: actualiza HUD
            ├─> AudioManager: PlayPickup
            └─> VFX: spawn partículas
```

**Game Over:**
```
Bola sale del camino y position.y < threshold
  └─> BallController.OnFell (evento C# local)
       └─> GameStateMachine.HandleBallFell()
            └─> SO_OnGameOver.Raise()
                 ├─> ScoreManager.SaveBestIfHigher → SO_OnBestScoreChanged.Raise
                 ├─> PathGenerator: detiene generación
                 ├─> BallController: detiene movimiento
                 ├─> PowerupManager: limpia powerup activo
                 ├─> UIController: panel GameOver
                 └─> AudioManager: PlayDeath
```

---

## 7. Catálogo de clases

Para cada clase: responsabilidad, dependencias y firma pública. No incluye implementación, eso es código.

### 7.1 `GameConfigSO` (`ZigZag.Runtime.Data`)

**Responsabilidad:** contenedor único de parámetros configurables. Encapsulado: campos privados, lectura por propiedad.

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
        [SerializeField] private int _generationSeed = 0;        // 0 = aleatorio en runtime

        [Header("Gems")]
        [SerializeField, Range(0f, 1f)] private float _gemSpawnProbability = 0.3f;
        [SerializeField] private int _gemValue = 10;

        [Header("Powerups")]
        [SerializeField, Range(0f, 1f)] private float _magnetSpawnProbability = 0.05f;
        [SerializeField] private float _magnetDuration = 5f;
        [SerializeField] private float _magnetRadius = 4f;
        [SerializeField] private float _magnetAttractSpeed = 8f;

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
        [SerializeField] private int _powerupPoolInitialSize = 5;

        // Propiedades de sólo lectura — encapsulación obligatoria (CLAUDE §5).
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
        public float MagnetSpawnProbability   => _magnetSpawnProbability;
        public float MagnetDuration           => _magnetDuration;
        public float MagnetRadius             => _magnetRadius;
        public float MagnetAttractSpeed       => _magnetAttractSpeed;
        public int DistanceMultiplier         => _distanceMultiplier;
        public float CameraFollowSmoothTime   => _cameraFollowSmoothTime;
        public float CameraOrthographicSize   => _cameraOrthographicSize;
        public float FreezeFrameOnDeath       => _freezeFrameOnDeath;
        public int PlatformPoolInitialSize    => _platformPoolInitialSize;
        public int GemPoolInitialSize         => _gemPoolInitialSize;
        public int PowerupPoolInitialSize     => _powerupPoolInitialSize;
    }
}
```

### 7.2 `GameStateMachine` (`ZigZag.Runtime.Core`)

**Responsabilidad:** mantiene el estado actual (`Menu | Playing | GameOver`) y dispara los `GameEventSO` correspondientes. Reemplaza el viejo `static class GameEvents`.

**Dependencias inyectadas (Inspector):**
- `GameConfigSO _config`
- `InputHandler _inputHandler`
- `GameEventSO _onGameStarted, _onGameOver, _onGameReset`
- `BallController _ball` (sólo para escuchar su `OnFell` local)

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

**Eventos a los que se suscribe:**
- `InputHandler.OnTapped` (sólo válido en `Menu`)
- `BallController.OnFell`

### 7.3 `InputHandler` (`ZigZag.Runtime.Input`)

**Responsabilidad:** abstrae la captura de input. ADR-006 fija `UnityEngine.Input` clásico.

```csharp
namespace ZigZag.Runtime.Input
{
    [DisallowMultipleComponent]
    public sealed class InputHandler : MonoBehaviour
    {
        public event Action OnTapped;
        // Update: si Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) → OnTapped?.Invoke();
    }
}
```

### 7.4 `BallController` (`ZigZag.Runtime.Gameplay.Player`)

**Responsabilidad:** mueve la bola, alterna dirección, detecta caída.

**Dependencias inyectadas:**
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

        public Vector3 CurrentDirection { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool IsMoving { get; private set; }

        public void StartMoving();
        public void StopMoving();
        public void ResetTo(Vector3 position);
    }
}
```

**Direcciones internas:** `(Vector3.right + Vector3.forward).normalized` y `(Vector3.left + Vector3.forward).normalized`.

### 7.5 `PathGenerator` (`ZigZag.Runtime.Gameplay.World`)

**Responsabilidad:** genera y despawnea tramos por delante / por detrás de la bola.

**Dependencias inyectadas:**
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

**Notas internas:**
- Cola `Queue<Segment>` de tramos activos.
- `System.Random` con seed para reproducibilidad (no `UnityEngine.Random`, que es global).
- En `Update`, si el último cubo está a menos de `AheadBuffer` de la bola, genera tramo.
- Devuelve al pool los cubos `BehindBuffer` detrás.

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

`class` (no `struct`): contiene una `List<GameObject>`, evita problemas de copia por valor. Expuesta como `IReadOnlyList<GameObject>` (CLAUDE §5 — no exponer colecciones mutables).

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

**Eventos:** suscrito a `SO_OnGemCollected` (suma score), `SO_OnGameOver` (llama `SaveBestIfHigher`). Persistencia: `PlayerPrefs.GetInt("BestScore", 0)` en `Awake`, `SetInt` + `Save` al guardar.

### 7.10 `CameraFollow` (`ZigZag.Runtime.Gameplay.CameraSystem`)

```csharp
namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    [DisallowMultipleComponent]
    public sealed class CameraFollow : MonoBehaviour
    {
        public void SetTarget(Transform target);
    }
}
```

Namespace `CameraSystem` (no `Camera`) para no colisionar con `UnityEngine.Camera`.

### 7.11 `IPowerup` (`ZigZag.Runtime.Gameplay.Powerups`)

```csharp
namespace ZigZag.Runtime.Gameplay.Powerups
{
    public interface IPowerup
    {
        string Id { get; }
        float Duration { get; }
        void Activate(BallController ball);
        void Deactivate();
        void Tick(float deltaTime);
    }
}
```

### 7.12 `MagnetPowerup` (`ZigZag.Runtime.Gameplay.Powerups`)

Clase pura, no `MonoBehaviour`. Activada por `PowerupManager`.

```csharp
public sealed class MagnetPowerup : IPowerup
{
    public string Id => "magnet";
    public float Duration { get; }
    public MagnetPowerup(GameConfigSO config) { ... }
    public void Activate(BallController ball);
    public void Deactivate();
    public void Tick(float deltaTime);
}
```

**Funcionamiento:** `Tick` consulta un registro de gemas activas (mantenido por `GemPool` o `GemSpawner`) en radio y las mueve con `Vector3.MoveTowards`.

### 7.13 `PowerupManager` (`ZigZag.Runtime.Gameplay.Powerups`)

```csharp
[DisallowMultipleComponent]
public sealed class PowerupManager : MonoBehaviour
{
    public IPowerup ActivePowerup { get; private set; }
    public float TimeRemaining { get; private set; }
    public bool IsActive => ActivePowerup != null;

    public void Activate(IPowerup powerup);
    public void DeactivateCurrent();
}
```

Suscrito a `SO_OnGameOver` para limpiar el powerup activo y a un evento de pickup local del prefab de powerup.

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

Suscrito a `SO_OnGameStarted`, `SO_OnGameOver`, `SO_OnScoreChanged`, `SO_OnBestScoreChanged`.

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

Suscrito a `InputHandler.OnTapped` (local), `SO_OnGemCollected`, `SO_OnGameOver`.

### 7.16 Pools (`ZigZag.Runtime.Gameplay.World` / `.Collectibles` / `.Powerups`)

Wrappers ligeros sobre `UnityEngine.Pool.ObjectPool<T>`.

```csharp
[DisallowMultipleComponent]
public sealed class PlatformPool : MonoBehaviour
{
    public GameObject Get();
    public void Release(GameObject platform);
}
```

Mismo patrón para `GemPool` y `PowerupPool`. Internamente: `ObjectPool<GameObject>` con `createFunc`, `actionOnGet`, `actionOnRelease`, `actionOnDestroy`.

### 7.17 `GameBootstrap` (`ZigZag.Runtime.Core`)

**Responsabilidad:** punto de entrada por escena. Resuelve referencias, inicializa pools, llama a `UIController.ShowMenu`. Sustituye al pattern Service Locator para este alcance (un único punto de composición es suficiente con una escena).

```csharp
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class GameBootstrap : MonoBehaviour
{
    // referencias serializadas a todos los sistemas
    // Awake: validar refs, inicializar pools, set inicial UI
}
```

---

## 8. Decisiones técnicas justificadas (ADRs)

Cada decisión es defendible. Si en la review preguntan "¿por qué X?", la respuesta está aquí.

### ADR-001 — Bola cinemática (sin Rigidbody con gravedad)

**Contexto:** la bola se mueve a velocidad constante en dos diagonales fijas y "cae" cuando se sale del camino.

**Decisión:** mover por `transform.position += dir * speed * dt`. Sin Rigidbody. La caída se simula con `fallSpeed` propio.

**Alternativas:**
- Rigidbody dinámico con gravedad: fricción de PhysX, colisiones que frenan, no determinista entre máquinas.
- Rigidbody kinemático: mejor pero sigue añadiendo coste sin beneficio.

**Consecuencias:** movimiento determinista. La detección de "estoy en suelo" hay que hacerla a mano (raycast hacia abajo).

### ADR-002 — `UnityEngine.Pool.ObjectPool<T>`

**Decisión:** usar el pool nativo de Unity (2021+).

**Alternativas:** pool propio (más código), plugins externos (prohibidos por el brief).

**Consecuencias:** menos código, API estándar. Pequeña pérdida de control sobre internos del pool, irrelevante.

### ADR-003 — `PlayerPrefs` para best score

**Decisión:** `PlayerPrefs.GetInt` / `SetInt` con clave `"BestScore"`.

**Alternativas:** JSON con `JsonUtility` (sobreingeniería para un int), SQLite (absurdo).

**Consecuencias:** trivial. Sin perfiles, irrelevante para el alcance.

### ADR-004 — Eventos híbridos: `GameEventSO` global + `event` C# local

**Contexto:** se necesita comunicación cross-system desacoplada y, además, eventos puntuales entre componentes adyacentes.

**Decisión:**
- **`GameEventSO`** para eventos consumidos por más de un sistema (Score, GameOver, GemCollected, etc.).
- **`event Action<T>` C#** para eventos publicados por un componente y consumidos por otro del mismo sistema (`InputHandler.OnTapped`, `BallController.OnFell`).

**Alternativas rechazadas:**
- `static class GameEvents` con `event` estáticos: viola la regla de `CLAUDE.md` §6 ("`static` mutable state" como anti-patrón) y arrastra ciclo de vida acoplado al dominio de la aplicación, problemático si se recarga la escena.
- `UnityEvent` por inspector: reflexión, ~5–10× más lento, allocations.
- Solo `GameEventSO` para todo: crear un asset para `InputHandler.OnTapped` o `BallController.OnFell` es overkill (CLAUDE §6 admite C# events "cuando los SO channels son overkill").

**Consecuencias:**
- Type-safe en compilación.
- Sin estado estático mutable.
- Los eventos globales se ven como assets navegables en el editor (autodocumentado para el revisor).
- Coste: disciplina dura de suscripción/desuscripción simétrica. Mitigado por la regla `OnEnable` / `OnDisable`.

### ADR-005 — `ScriptableObject` para configuración con propiedades read-only

**Decisión:** todos los parámetros del juego en `GameConfigSO`. Campos `[SerializeField] private`, lectura por propiedad. Encapsulación obligatoria (CLAUDE §5).

**Alternativas rechazadas:**
- Constantes en código (recompilar para tunear).
- JSON cargado en runtime (IO innecesaria).
- `[SerializeField]` repartidos por MonoBehaviours (dispersión).
- Campos `public` mutables en el SO (rompe encapsulación — CLAUDE §5).

**Consecuencias:** un único asset para tunear todo. Iteración rápida en semana 2. Cero coste en runtime.

### ADR-006 — Input clásico (`UnityEngine.Input`) en lugar del new Input System

**Decisión:** `Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)` encapsulado en `InputHandler`.

**Alternativas:** new Input System (más potente pero requiere setup de Action Maps; sin beneficio para 1 botón).

**Consecuencias:** menos código, sin dependencias. Migración futura trivial porque está encapsulado.

### ADR-007 — Cámara `SmoothDamp` manual (no Cinemachine)

**Decisión:** `CameraFollow` propio con `Vector3.SmoothDamp`.

**Alternativas:** Cinemachine (paquete extra, en duda con el brief), hija de la bola (jitter).

**Consecuencias:** ~20 líneas propias. Más simple de revisar.

### ADR-008 — Una sola escena

**Decisión:** todo en `S_Main.unity`. Estados gestionados por `GameStateMachine` + `UIController`.

**Alternativas:** escenas separadas con `SceneManager.LoadScene` (recarga, pérdida de continuidad visual).

**Consecuencias:** transiciones instantáneas. La UI muestra/oculta paneles.

### ADR-009 — Interfaz `IPowerup` aunque solo haya un powerup

**Decisión:** definir `IPowerup`, `MagnetPowerup` la implementa.

**Justificación:** "tipos de powerups" es eje de cambio probable. Coste ~10 líneas. Añadir un segundo powerup pasa a ser una clase nueva sin tocar `PowerupManager`.

**Consecuencias:** pensamiento extensible sin sobreingeniería.

### ADR-010 — `GameStateMachine` MonoBehaviour como coordinador (no estática, no singleton)

**Decisión:** una clase `GameStateMachine` instanciada en la escena (referenciada por `GameBootstrap`). Reemplaza el patrón "static class GameEvents".

**Alternativas rechazadas:**
- `static class` con eventos estáticos: CLAUDE §6 lista "`static` mutable state" como anti-patrón.
- Singleton MonoBehaviour `Instance ??= this`: CLAUDE §5 lo prohíbe explícitamente.

**Consecuencias:** ciclo de vida ligado a la escena (lo que queremos). Suscripciones a `GameEventSO` se hacen en `OnEnable`, se liberan en `OnDisable`. Sin estado global.

### ADR-011 — Una asmdef por carpeta de Runtime

**Decisión:** seguir CLAUDE.md §3 al pie de la letra. Una `.asmdef` por carpeta de Runtime + Editor + Tests separadas.

**Alternativas:**
- Una sola `ZigZag.Runtime.asmdef`: simple de empezar, pero rompe la barrera dura entre capas (UI podría llamar a Gameplay directamente sin que Unity lo impida).
- Sin asmdef: imposible separar editor code del player build.

**Consecuencias:** setup inicial de ~20 minutos. Compilaciones incrementales rápidas. Dirección de dependencias enforced por el compilador.

### ADR-012 — `sealed` por defecto en clases concretas

**Decisión:** todas las clases concretas que no estén diseñadas para herencia se declaran `sealed`.

**Justificación:** CLAUDE §5 lo prescribe. Reduce superficie de cambios accidentales y permite mejor inlining.

**Consecuencias:** si en el futuro hay que extender una clase, se quita `sealed` con justificación.

---

## 9. SOLID aplicado (concreción)

| Principio                          | Aplicación en este proyecto                                                                                                       |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| **S** — Single Responsibility      | `BallController` mueve; `ScoreManager` cuenta; `UIController` muestra; `PathGenerator` genera. Ninguno hace dos cosas.            |
| **O** — Open/Closed                | Añadir un powerup nuevo = clase nueva que implementa `IPowerup`. `PowerupManager` no se toca.                                     |
| **L** — Liskov Substitution        | Cualquier `IPowerup` es intercambiable en `PowerupManager.Activate(...)`.                                                         |
| **I** — Interface Segregation      | `IPowerup` tiene 4 métodos, no más. Sin interfaces "fat".                                                                         |
| **D** — Dependency Inversion       | `BallController` depende de `GameConfigSO` (abstracción de datos). Sistemas se inyectan por inspector, no se crean internamente. |

---

## 10. Performance rules (alineadas a `CLAUDE.md` §8)

Reglas fijas. No es "optimizemos cuando moleste"; es la disciplina por defecto. Profile antes de cambiar nada.

- **Sin allocations en `Update` / `FixedUpdate`.** Nada de `new List<T>()`, LINQ, `string` concat, `foreach` sobre `IEnumerable<T>`, `params`.
- **`for` antes que `foreach`** en hot paths. `foreach` sobre `List<T>` es aceptable; sobre interfaz no.
- **Cache `Camera.main`** en `Awake`. Nunca leerlo en `Update`.
- **Cache transforms y componentes** en `Awake`. Cero `GetComponent` en `Update` / `FixedUpdate`.
- **`Animator.StringToHash`** en `static readonly int` (no aplica todavía — no hay Animator — pero queda como regla).
- **Physics non-alloc:** `Physics.RaycastNonAlloc`, `OverlapSphereNonAlloc` con buffers cacheados.
- **`SetActive` en lugar de `Instantiate`/`Destroy`** (lo cual es justo el pooling).
- **Sin `tag` string compares en hot paths.** Cache de `LayerMask` o referencia directa.
- **El Profiler manda.** Cualquier optimización requiere medida previa.

---

## 11. Logging & diagnósticos (alineado a `CLAUDE.md` §9)

- `Debug.Log` solo detrás de condicional o de un logger del proyecto. Logs verbosos en hot paths prohibidos.
- `Debug.LogError` para bugs reales; `Debug.LogWarning` para configuraciones recuperables; nada más a menos que sume.
- `Debug.Assert` / `UnityEngine.Assertions.Assert` para invariantes en development.
- Métodos de un logger custom marcados con `[Conditional("UNITY_EDITOR")]` o `[Conditional("DEVELOPMENT_BUILD")]` para que se eliminen en release.

---

## 12. Tests (alcance prototipo)

Tests **básicos** (decisión explícita: prioridad baja en un prototipo de 2 semanas; objetivo es demostrar disciplina, no cobertura exhaustiva).

### 12.1 Qué se testea

**EditMode (pure C#):**
- `GameConfigSO` — propiedades read-only devuelven los valores serializados.
- `ScoreManager` — aritmética: gemas suman correctamente; reset deja en 0; `SaveBestIfHigher` solo sobrescribe si mejora.
- `MagnetPowerup` — `Tick` decrementa `TimeRemaining`; expira en `Duration` ticks.

**PlayMode (MonoBehaviour / coroutines):**
- `BallController` — al disparar `OnTapped`, `CurrentDirection` se invierte en el frame siguiente.
- `PathGenerator` — tras N segundos, hay al menos un tramo activo y ninguno duplicado.

### 12.2 Convenciones

- AAA: Arrange, Act, Assert. Un assert lógico por test.
- Naming: `Method_State_ExpectedResult` — `ScoreManager_AfterGemCollected_AddsValueToScore`.
- Sin mocks de tipos Unity. Si algo necesita mock, se extrae a interfaz C# y se mockea la interfaz.
- Seeds fijas en tests deterministas (`PathGenerator` con seed = 42).
- `[TearDown]` para destruir GameObjects creados en PlayMode.

### 12.3 Objetivo cuantitativo

5–10 tests entre ambos modos. Suficiente para que un revisor vea disciplina sin que el sprint se desangre.

---

## 13. Source control (alineado a `CLAUDE.md` §11)

- **`main`** = stable. Trabajo en `feat/<short>`, `fix/<short>`, `chore/<short>`.
- **Commits atómicos.** Un cambio conceptual por commit. Subject en **inglés**, imperativo: `Add ball controller`, no `Added` ni `Adds`.
- **Force-text serialization** verificada en `EditorSettings.asset` (default en 2022 LTS).
- **`.meta` con su asset**, nunca por separado.
- **`.gitignore`** ya gestiona `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build/`, `*.csproj`, `*.sln`. No tocar.
- **PRs / merges** opcionales para un proyecto solo; aun así, mantener el formato como si hubiese revisor.

---

## 14. MonoBehaviour template (canónico)

Idéntico a [`CLAUDE.md` §12](CLAUDE.md). Todo MonoBehaviour nuevo arranca de esta plantilla y solo se desvía con justificación:

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

## 15. Agentes especializados

Los subagentes están definidos en [`.claude/agents/`](.claude/agents/) y catalogados en [`AGENTS.md`](.claude/agents/AGENTS.md).

| Voy a…                                                       | Agente                          |
| ------------------------------------------------------------ | ------------------------------- |
| Diseñar un sistema, elegir patrón, fijar boundaries          | `unity-architect`               |
| Escribir / modificar un script de gameplay                   | `unity-gameplay-programmer`     |
| Diagnosticar frame time, GC, draw calls, físicas             | `unity-performance-profiler`    |
| Revisar cambios C# contra las reglas del proyecto            | `unity-code-reviewer`           |
| Añadir tests EditMode / PlayMode                             | `unity-test-author`             |
| Crear inspectores custom, EditorWindows, hooks de build      | `unity-editor-tooling`          |
| Construir HUD, menú, pantalla, prompt                        | `unity-ui-developer`            |

**Flujos típicos:**

- **Nueva feature:** `unity-architect` → `unity-gameplay-programmer` (y/o `unity-ui-developer`) → `unity-test-author` → `unity-code-reviewer` → `unity-performance-profiler` (solo si toca hot path).
- **Bugfix:** `unity-code-reviewer` (audit) → `unity-gameplay-programmer` (fix) → `unity-test-author` (regresión).
- **Optimización:** `unity-performance-profiler` (medir) → `unity-gameplay-programmer` (aplicar) → `unity-performance-profiler` (verificar).

---

## 16. Riesgos técnicos identificados y mitigaciones

| Riesgo                                                                  | Mitigación                                                                                                  |
| ----------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Memory leaks por eventos no desuscritos                                 | Regla `OnEnable` / `OnDisable` simétrica, aplicada uniformemente.                                           |
| Frame drops por `Instantiate` no detectado                              | Profiler abierto periódicamente en semana 1. Pool desde día 3.                                              |
| Detección de "estoy en suelo" inestable                                 | Raycast con `LayerMask` específica de plataformas.                                                          |
| Cámara con jitter                                                       | `SmoothDamp` parametrizado, no hija de la bola.                                                             |
| Determinismo roto entre máquinas                                        | Generación con `System.Random` con seed (no `UnityEngine.Random`).                                          |
| Powerup activo al hacer GameOver no se limpia                           | `PowerupManager` se suscribe a `SO_OnGameOver` y limpia.                                                    |
| `GameEventSO` con suscripciones residuales tras recargar la escena      | `OnEnable` / `OnDisable` en cada listener garantiza limpieza. En una sola escena no se recarga, sin riesgo. |
| Score `int` puede overflow en partidas eternas                          | `long` no necesario para un endless realista. Si pasa, es un buen problema.                                 |
| asmdef mal configuradas haciendo que el editor code entre al player     | Test manual: build de Windows con `ZigZag.Editor` en `includePlatforms: [Editor]`.                          |

---

## 17. Checklist de calidad de código (pre-entrega)

Antes de empaquetar el zip:

- [ ] Cero warnings en consola al abrir el proyecto.
- [ ] Cero errores al ejecutar `S_Main`.
- [ ] Todos los eventos C# públicos usan `event` keyword.
- [ ] Todas las suscripciones tienen su desuscripción correspondiente (grep `+=` y `-=`).
- [ ] Cero `Instantiate` / `Destroy` en métodos de gameplay (solo `Awake` / `Start` / `GameBootstrap`).
- [ ] Cero `FindObjectOfType`, `GameObject.Find`, `GameObject.FindWithTag` en runtime loops.
- [ ] Cero magic numbers; todos los valores configurables vienen de `GameConfigSO`.
- [ ] Naming consistente con la tabla de §3.1 en todo el proyecto.
- [ ] Una clase = un fichero, mismo nombre.
- [ ] Namespaces 3-niveles aplicados consistentemente.
- [ ] `sealed` aplicado por defecto en clases concretas.
- [ ] Comentarios `///` en APIs públicas de clases principales.
- [ ] `SO_GameConfig.asset` presente y poblado.
- [ ] Todos los `GameEventSO.asset` creados y referenciados por sus suscriptores/emitters.
- [ ] Sin código muerto, sin `Debug.Log` sobrantes.
- [ ] **Sin `FIXME`, sin `XXX`, sin notas sin tag.** Cualquier deferral es `// TODO: <descripción> (<contexto>)` (`CLAUDE.md` §2.2).
- [ ] 5–10 tests EditMode/PlayMode pasando.
- [ ] asmdef de Editor con `includePlatforms: [Editor]` verificado (no entra al build).
- [ ] Build Windows compila sin warnings críticos.

---

## 18. Lo que NO está en este documento (y dónde encontrarlo)

- **Diseño de juego (mecánicas, parámetros, alcance):** [`zigzag_gdd.md`](zigzag_gdd.md).
- **Reglas globales del proyecto:** [`CLAUDE.md`](CLAUDE.md).
- **Catálogo de subagentes:** [`.claude/agents/AGENTS.md`](.claude/agents/AGENTS.md).
- **Implementación concreta:** el código.
- **Builds y configuración de Unity:** `README.md` (TODO crear).

---

## 19. Cómo usar este documento durante el desarrollo

1. **Antes de empezar un día:** mirar qué clase toca y revisar su sección en §7.
2. **Antes de hacer commit:** verificar §3 (convenciones) + §17 (checklist).
3. **Cuando surja una duda arquitectónica:** consultar ADRs (§8) o añadir uno nuevo si la decisión es nueva.
4. **Al final de cada semana:** repasar §17 completo.
5. **Si una regla de este documento entra en conflicto con `CLAUDE.md`:** gana `CLAUDE.md` y se actualiza este documento.

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
│  Gameplay   Player · World · Collectibles · Economy ·       │
│             Scoring · Cosmetics · Aesthetics · CameraSystem │  → publica eventos
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

### 2.4 Patrones de diseño elegidos antes de picar código

`CLAUDE.md` §6 fija el catálogo de patrones obligatorios para sistemas no triviales y los anti-patrones a rechazar de plano. **Ese catálogo se escribió antes del primer `.cs` del repositorio** — los patrones no son hallazgos retrospectivos, son decisiones tomadas durante el diseño (este documento + el GDD) y aplicadas después al picar código.

Patrones efectivamente aplicados en la implementación (mapeo patrón → clases reales del repo):

| Patrón | Dónde vive | Por qué se eligió |
|--------|-----------|-------------------|
| **ScriptableObject como contenedor de datos** | `GameConfigSO`, `PaletteRulesSO`, `BallSkinSO`, `BallSkinCatalogSO` | Configuración editable sin recompilar, encapsulación natural (`private + serialized + get-only`), tunable por diseñador, hot-reload en editor. |
| **Event Channel (SO Pub-Sub)** | 15 assets `SO_*` bajo `Assets/Settings/Events/` | Comunicación cross-system sin que sender y receiver se referencien directamente. Sustituye singletons y `FindObjectOfType`. |
| **Observer (event C# nativo)** | `BallController.OnDirectionChanged` / `OnFell`, `InputHandler.OnTapped`, `Gem` callbacks | Variante local del pub-sub: cuando emisor y oyente viven en la misma capa/asmdef y no merece la pena ceremonialmente un asset SO. Suscripción simétrica en `OnEnable`/`OnDisable`. |
| **Finite State Machine (FSM)** | `GameStateMachine` con `enum GameState { Menu, Playing, GameOver }` | Tres estados, transiciones explícitas. Una FSM hand-rolled es más legible que arrastrar un framework. |
| **Object Pool** | `PlatformPool`, `GemPool` (vía `UnityEngine.Pool.ObjectPool<T>` de 2022 LTS) | Cero `Instantiate`/`Destroy` en hot path. Prewarming en `Awake`. ADR-002. |
| **Template Method (jerarquía genérica)** | `GameEventSO<T>` abstract → `IntGameEventSO`, `StringGameEventSO` | Una sola implementación de Register/Unregister/Raise; los payloads concretos sólo declaran el tipo. |
| **Strategy (data-driven)** | `BallSkinSO` (intercambio de `Material`), `PaletteRulesSO` (intercambio de rangos HSV) | La "estrategia" se selecciona arrastrando otro asset, no instanciando otra clase. Workflow editorial puro. |
| **Catálogo (Repository simplificado)** | `BallSkinCatalogSO` con `GetById(string)` | Lookup centralizado sin Dictionary corriendo en runtime; el array serializado preserva orden de display = orden de tienda. |
| **Composition Root / Bootstrap** | `GameBootstrap` con `[DefaultExecutionOrder(-1000)]` | Único punto de validación de refs serializadas vía `Debug.Assert`. No instancia ni resuelve nada — la composición ya está en la escena; el bootstrap sólo grita si falta algo antes del primer frame. |
| **MVP-lite (View ⇄ Presenter ⇄ Model)** | `UIController` (View), canales SO (Presenter), `ScoreManager`/`CoinsWallet`/`SkinInventory` (Model) | La View sólo lee y actualiza widgets; nunca contiene lógica de negocio. El Model nunca conoce a la View — empuja por canal. |
| **Composition over inheritance** | Cada `MonoBehaviour` tiene una responsabilidad. La bola no extiende nada; recibe colaboradores por `[SerializeField]`. | Inheritance solo cuando hay verdadero `is-a`; en gameplay arcade rara vez ocurre. |
| **Pure helpers (sin estado)** | `ScoreCalculator`, `CameraFollowMath`, `PaletteSampler` | `static class` sin Unity lifecycle. Testeable en EditMode sin mocks; separa la aritmética de los side-effects (raises, persistencia). |

Anti-patrones explícitamente evitados (sólo lista — la justificación está en `CLAUDE.md` §6):

- God `GameManager`.
- Singletons globales (`Instance ??= this`).
- `static` mutable state.
- `SendMessage`, `BroadcastMessage`, `FindObjectOfType`, `GameObject.Find` fuera de bootstrap/editor.
- Lógica de negocio en editor scripts.
- `Resources.Load` en hot paths.

El orden cronológico fue: GDD → este documento de arquitectura → `CLAUDE.md` con el catálogo de patrones obligatorios → primer commit de código. Cada iteración posterior añade clases que encajan en uno de los patrones de la tabla; si una iteración requiere un patrón nuevo, se introduce explícitamente en su spec con justificación (los planes de `docs/superpowers/plans/` lo documentan).

---

## 3. Convenciones de código (alineadas a CLAUDE.md §4 y §5)

### 3.1 Naming — única tabla de referencia

| Elemento                  | Convención                                | Ejemplo                                                |
| ------------------------- | ----------------------------------------- | ------------------------------------------------------ |
| Namespace                 | `ZigZag.<Layer>.<Feature>`                | `ZigZag.Runtime.Gameplay.Player`                       |
| Clase / Struct / Enum     | `PascalCase`                              | `BallController`                                       |
| Interface                 | `IPascalCase`                             | `IDamageable`                                          |
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

Alineada a [`CLAUDE.md` §3](CLAUDE.md). **Layout plano sin prefijo `_Project/`** — paths más cortos, navegación más simple. El trade-off (mezcla alfabética con paquetes third-party si se importan en el futuro) se asume.

```
Assets/
├── Art/                                 # Sprites, modelos, texturas, materiales
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
│   │   └── Utilities/                   # Helpers puros, extensiones
│   ├── Editor/                          # Tools editor-only (asmdef con includePlatforms: [Editor])
│   └── Tests/
│       ├── EditMode/
│       └── PlayMode/
├── Prefabs/                             # P_Ball, P_PlatformCube, P_Gem, P_ShopRow
├── Scenes/                              # S_Main.unity
├── Settings/                            # SO_GameConfig.asset + assets de eventos SO_*
└── VFX/
```

**Naming de asmdef independiente del path:** los archivos `.asmdef` se llaman `ZigZag.<Layer>.<Feature>` (e.g. `ZigZag.Runtime.Data`) sin importar dónde estén físicamente. El nombre del assembly define el contrato de namespace; la ruta solo organiza ficheros.

---

## 5. Assembly Definitions (.asmdef)

Una `.asmdef` por carpeta de Runtime, según [`CLAUDE.md` §3](CLAUDE.md). El coste de setup es one-shot; la ganancia es compilaciones incrementales rápidas y aplicación dura de la dirección de dependencias.

| asmdef                         | Path                                              | Referencias internas                            | Notas                                                              |
| ------------------------------ | ------------------------------------------------- | ----------------------------------------------- | ------------------------------------------------------------------ |
| `ZigZag.Runtime.Core`          | `Assets/Code/Runtime/Core/`                       | Events, Data                                    |                                                                    |
| `ZigZag.Runtime.Data`          | `Assets/Code/Runtime/Data/`                       | —                                               | Sólo `ScriptableObject` de configuración.                          |
| `ZigZag.Runtime.Events`        | `Assets/Code/Runtime/Events/`                     | —                                               | `GameEventSO` y variantes tipadas.                                 |
| `ZigZag.Runtime.Input`         | `Assets/Code/Runtime/Input/`                      | Events                                          |                                                                    |
| `ZigZag.Runtime.Gameplay`      | `Assets/Code/Runtime/Gameplay/`                   | Core, Data, Events, Input, Utilities            | Todas las features de gameplay viven en sub-namespaces.            |
| `ZigZag.Runtime.UI`            | `Assets/Code/Runtime/UI/`                         | Core, Data, Events                              | TextMeshPro. **Nunca** referencia Gameplay directamente.           |
| `ZigZag.Runtime.Audio`         | `Assets/Code/Runtime/Audio/`                      | Data, Events                                    |                                                                    |
| `ZigZag.Runtime.Utilities`     | `Assets/Code/Runtime/Utilities/`                  | —                                               | Pure C#, helpers, extensions.                                      |
| `ZigZag.Editor`                | `Assets/Code/Editor/`                             | Cualquier asmdef de Runtime                     | `includePlatforms: [Editor]`. **Nunca** entra al player build.     |
| `ZigZag.Tests.EditMode`        | `Assets/Code/Tests/EditMode/`                     | Runtime + `UnityEngine.TestRunner`              | `defineConstraints: [UNITY_INCLUDE_TESTS]`.                        |
| `ZigZag.Tests.PlayMode`        | `Assets/Code/Tests/PlayMode/`                     | Runtime + `UnityEngine.TestRunner`              | `defineConstraints: [UNITY_INCLUDE_TESTS]`.                        |

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

**Globales (assets `SO_*.asset` en `Assets/Settings/Events/`):**

| Asset                       | Tipo                | Disparado por                | Suscriptores típicos                              |
| --------------------------- | ------------------- | ---------------------------- | ------------------------------------------------- |
| `SO_OnGameStarted`          | `GameEventSO`       | `GameStateMachine`           | `BallController`, `PathGenerator`, `UIController` |
| `SO_OnGameOver`             | `GameEventSO`       | `GameStateMachine`           | `PathGenerator`, `UIController`, `AudioManager`, `ScoreManager` |
| `SO_OnGameReset`            | `GameEventSO`       | `GameStateMachine`           | Todos los sistemas con estado mutable             |
| `SO_OnRetryRequested`       | `GameEventSO`       | `UIController` (botón Retry) | `GameStateMachine`                                |
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
                 ├─> BallController: detiene movimiento (vía GameStateMachine.StopMoving)
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
        public event Action OnReset;       // disparado dentro de ResetTo (iter 10)

        public Vector3 CurrentDirection { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool IsMoving { get; private set; }

        public void StartMoving();
        public void StopMoving();
        public void ResetTo(Vector3 position);
    }
}
```

**Direcciones internas:** `new Vector3(-1f, 0f, 0f)` (puro -X) y `new Vector3(0f, 0f, 1f)` (puro +Z). Los nombres internos `AlongNegativeX` y `AlongPositiveZ` reflejan el eje mundo, no la apariencia en pantalla. La ilusión de zigzag 45° la produce la cámara isométrica (rotación -45° Y) que proyecta ambos ejes como diagonales en pantalla — mismo truco que el ZigZag original de Ketchapp. El path se construye con cubos alineados a los ejes (giros de 90° en world space).

**Eventos C# locales (iter 10):** `OnReset` se añade junto a `OnDirectionChanged`/`OnFell`. Se dispara desde `ResetTo(position)` justo al final, después de recolocar la bola y los flags. Consumidor único actual: `BallTrailColorizer` lo usa para llamar `_trail.Clear()` y evitar la línea recta visible entre el punto de muerte y el spawn (el `TrailRenderer` interpolaría la teleportación como si fuera movimiento). Es event C# local porque el consumidor vive en el mismo asmdef (`ZigZag.Runtime.Gameplay`) — un canal SO sería ceremonia (ver ADR-004).

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

**Snap al origen en retry (iter 10):** la cámara se suscribe a `SO_OnGameReset` y, al recibirlo, mueve su `transform.position` a `(_cameraOrigin.x, _lockedY, _cameraOrigin.z)` y resetea `_smoothVelocity = Vector3.zero`. Sin esto, después de una run larga la cámara estaba lejos del origen y el `SmoothDamp` del siguiente run hacía un slingshot visible de varias unidades de mundo hacia atrás antes de quedarse quieta sobre el menú. El handler es null-safe sobre el canal y guard contra `!_originsCaptured` (no captura todavía → no hay nada a lo que volver).

**Fuente de `GlobalForward` (iter 10):** la constante se lee desde `GameConfigSO.GlobalForward`. Antes vivía duplicada en `PathGenerator`, `CameraFollow` y `ScoreManager`; el ADR-015 cierra la deuda explícita registrada en el devlog de iter 4.2.

> **Nota — secciones 7.11 a 7.13 retiradas y luego repobladas.** Originalmente contenían `IPowerup`, `MagnetPowerup` y `PowerupManager`. El powerup imán se descopeó en la iteración 5; en su lugar entró la tienda de skins (iter 5) y, más tarde, los componentes cosméticos delgados que pintan trail y burst de muerte según el skin equipado (iter 10). Se reusan los números 7.11/7.12/7.13 para esos componentes. Las secciones siguientes (7.14 `UIController` en adelante) conservan su numeración histórica para no romper referencias cruzadas con el devlog (`§7.17 CoinsWallet`, `§7.18 GameBootstrap`).

### 7.11 `BallSkinApplier`, `BallTrailColorizer`, `BallDeathBurst` (capa Cosmetics + Player)

Tres componentes delgados que reaccionan al canal `SO_OnSkinEquipped` (string payload = skin id) y mantienen la presentación de la bola coherente sin acoplarse al `BallController`.

```csharp
namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    [DisallowMultipleComponent]
    public sealed class BallSkinApplier : MonoBehaviour
    {
        // Vive en la bola, escucha SO_OnSkinEquipped, hace MeshRenderer.sharedMaterial = skin.Material.
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallController))]
    public sealed class BallTrailColorizer : MonoBehaviour
    {
        // Iter 10. Autoritativo sobre la apariencia del TrailRenderer:
        //  - material (cascada de shader fallbacks → evita magenta de InternalErrorShader)
        //  - width, time, minVertexDistance (defaults reproducibles; evita la trampa
        //    de Width Curve del Inspector que escaló el trail a varias unidades de mundo)
        //  - startColor/endColor tintados al equipar skin (mismo canal que BallSkinApplier)
        //  - _trail.Clear() en BallController.OnReset (sin esto, el respawn pinta una línea
        //    recta desde el punto de muerte hasta el spawn)
        // El material es estático compartido por instancia (una alocación por sesión).
    }
}

namespace ZigZag.Runtime.Gameplay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallController))]
    public sealed class BallDeathBurst : MonoBehaviour
    {
        // Iter 10. ParticleSystem hijo construido en Awake (sphere shape, world-space, 36
        // partículas, lifetime 0.65 s, alpha 1→0). Suscrito a BallController.OnFell (event C#);
        // en HandleFell snapea el host al punto de impacto antes de Play(true), así el burst
        // queda anclado donde la bola se salió del path, no donde acaba tras el freeze-frame.
        // Skin sync opcional vía slots _catalog + _onSkinEquipped (null-safe). Material estático
        // compartido. Patrón mirror de Gem.BuildPickupBurst — misma cascada de shader fallbacks.
        //
        // Iter 10 addendum: además es dueño de la visibilidad de la bola en el momento de
        // muerte. En HandleFell desactiva MeshRenderer.enabled (la bola muerta no debe quedar
        // visible bajo el panel GameOver); en HandleReset (consumiendo BallController.OnReset)
        // lo reactiva. Sólo el renderer, NO gameObject.SetActive(false) — apagar el GO mataría
        // las suscripciones de BallSkinApplier, BallTrailColorizer y la state machine, y el
        // siguiente Retry no llegaría a la bola.
    }
}
```

**Decisiones:**

- **Trail nativo + colorizer delgado, no componente custom.** El `TrailRenderer` de Unity ya es la implementación correcta. Lo único que el componente nativo no sabe hacer es elegir color según skin equipado, asignar material seguro y limpiar al respawn — y eso lo cubre el colorizer en ~120 líneas con docstring.
- **El colorizer es dueño del ancho del trail**, no sólo del color, como respuesta al incidente "trail magenta y gigante" detectado en el primer build de iter 10. Los campos `[SerializeField, Range]` reemplazan la `Width Curve` del Inspector (que es un `AnimationCurve` con dos keys editables — un drag accidental rompe la build sin warning de compilación).
- **`BallDeathBurst` con event C# directo, no canal SO.** Audio escucha `SO_OnGameOver` desde otro asmdef, así que ahí el canal SO es obligatorio. El death burst vive en el mismo asmdef que `BallController` y el event local basta — coherente con ADR-004.
- **Skin sync opcional en el burst.** Los slots `_catalog`/`_onSkinEquipped` son null-safe. Default blanco→naranja contrasta con cualquier skin y cualquier paleta cíclica; con el catálogo wireado, el feedback gana coherencia visual con la bola. No es obligatorio para que el burst funcione.
- **Ocultar la bola con `MeshRenderer.enabled = false`, no con `SetActive(false)`** (iter 10 addendum). El burst es el único componente que conoce el momento exacto del impacto, así que la responsabilidad de ocultar la bola muerta vive aquí, no en `BallController`. Apagar el GameObject completo mataría las suscripciones de `BallSkinApplier`, `BallTrailColorizer` y los handlers que la bola registra en `OnEnable`; el siguiente `SO_OnGameReset` no llegaría y el respawn quedaría roto silenciosamente. Apagar sólo el renderer mantiene el ciclo de eventos intacto; la restauración engancha a `BallController.OnReset` para que el mesh vuelva en el frame del respawn, no antes.

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

Suscrito a `SO_OnGameStarted`, `SO_OnGameOver`, `SO_OnScoreChanged`, `SO_OnBestScoreChanged`, `SO_OnCoinsChanged`, `SO_OnSessionCoinsChanged`, `SO_OnShopOpened`, `SO_OnShopClosed`.

**Count-up animado del HUD (iter 10):** `HandleScoreChanged` no pinta el entero crudo. Setea `_targetHudScore` y re-deriva `_hudCountUpSpeed = gap / _hudScoreCatchUpDuration` (default 0.5 s). En `Update` interpola `_displayedHudScore` con `Mathf.MoveTowards` sobre `Time.unscaledDeltaTime` — `unscaledDeltaTime` es deliberado para que el freeze-frame de la muerte (`Time.timeScale = 0`) no congele la animación, lo que se notaría como un tirón visible al llegar al panel GameOver. `_lastShownHudScore` evita reescribir el `TextMeshProUGUI.text` cuando el entero a mostrar no cambia (sin esto, el TMP regenera mesh 60 veces por segundo). Snap-down inmediato si el target es menor (caso del reset a 0). El score del panel GameOver sigue saltando al valor final sin animar. La velocidad multiplier-agnóstica garantiza que cualquier rebalanceo de `_distanceMultiplier` no cambia el feel — el HUD siempre tarda lo mismo en alcanzar el nuevo total.

**Shop oculta el Menu (iter 10):** `HandleShopOpened`/`HandleShopClosed` togglean `_menuPanel.SetActive`. Antes, el overlay de la tienda se solapaba visualmente con el panel del menú; ahora desaparece mientras la tienda está abierta. Los canales `SO_OnShopOpened`/`SO_OnShopClosed` ya existían (los disparaba `ShopPanel` para suprimir taps en `InputHandler`); el `UIController` se engancha como segundo listener — el canal pasa de 1 a 2 oyentes sin código nuevo del lado del raiser.

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

### 7.16 Pools (`ZigZag.Runtime.Gameplay.World` / `.Collectibles`)

Wrappers ligeros sobre `UnityEngine.Pool.ObjectPool<T>`.

```csharp
[DisallowMultipleComponent]
public sealed class PlatformPool : MonoBehaviour
{
    public GameObject Get();
    public void Release(GameObject platform);
}
```

Mismo patrón para `GemPool`. Internamente: `ObjectPool<GameObject>` con `createFunc`, `actionOnGet`, `actionOnRelease`, `actionOnDestroy`.

### 7.17 `CoinsWallet` (`ZigZag.Runtime.Gameplay.Economy`)

**Responsabilidad:** acumular monedas otorgadas por las gemas y persistir la wallet entre runs. Único punto que toca la `PlayerPrefs` key `"Coins"`.

**Dependencias inyectadas:**
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

**Eventos:** suscrito a `SO_OnGemCollected` (suma a wallet + session) y `SO_OnGameReset` (resetea `SessionCoins`, `TotalCoins` intacto). Persistencia: `PlayerPrefs.SetInt + Save` en cada pickup — los datos del jugador no deben perderse por un cierre brusco a media run.

### 7.18 `GameBootstrap` (`ZigZag.Runtime.Core`)

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

**Consecuencias:** ~20 líneas propias. Más simple de revisar. **Ver también ADR-014** — refinamiento del eje de seguimiento (solo forward).

### ADR-008 — Una sola escena

**Decisión:** todo en `S_Main.unity`. Estados gestionados por `GameStateMachine` + `UIController`.

**Alternativas:** escenas separadas con `SceneManager.LoadScene` (recarga, pérdida de continuidad visual).

**Consecuencias:** transiciones instantáneas. La UI muestra/oculta paneles.

### ADR-009 — Powerups fuera de scope (decisión revertida)

**Decisión final:** los powerups (originalmente: imán) **no entran en el prototipo**. El slot se reasignó a la iteración 5 "Tienda + skins" (ver `project_scope_magnet_skipped` y devlog iter 5).

**Justificación:** dos semanas no daban para un sistema de powerups completo *más* el polish visual. La tienda demuestra el mismo punto (extensibilidad de la arquitectura sin tocar gameplay) con menos superficie de código y entrega valor jugable inmediato (skins desbloqueables).

**Consecuencias:** el número ADR-009 queda ocupado por esta nota para preservar la numeración del resto de ADRs (citados desde el devlog). No existen `IPowerup`, `MagnetPowerup` ni `PowerupManager` en el repo.

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

### ADR-013 — Wallet de coins separada del score, persistida por pickup

**Contexto:** las gemas inicialmente sumaban a un único `CurrentScore` combinado con la distancia. El usuario pide que las gemas sean currency persistente (preparada para una tienda futura) y que el score refleje solo distancia.

**Decisión:**
- Nueva clase `CoinsWallet` (sub-feature `Gameplay/Economy/`, mismo asmdef `ZigZag.Runtime.Gameplay`). Único punto del proyecto que toca la `PlayerPrefs` key `"Coins"`.
- `ScoreManager` deja de escuchar `SO_OnGemCollected`. `CurrentScore` = distancia.
- Persistencia de coins **en cada pickup**, no en GameOver.

**Alternativas rechazadas:**
- Mantener score combinado y exponer un getter `CoinsOnly`: oculta la separación, mezcla responsabilidades, contradice la dirección hacia tienda.
- Persistir solo en GameOver: un cierre brusco (alt-F4, crash del editor) robaría coins al jugador. Las coins son currency real, no un número volátil.
- Renombrar `Gem` → `Coin`: churn alto mid-prototype; el visual sigue siendo una gema (octaedro rosa). La nomenclatura "el collectible es una gema, lo que otorga son coins" es consistente con cómo otros juegos modelan el tema.
- Crear `ICurrency` + `CurrencyService` genérico: YAGNI con una sola currency.

**Consecuencias:**
- Score y wallet evolucionan de forma independiente; añadir cosméticos pagados con coins (iteración 5: tienda + skins) no toca `ScoreManager`.
- Dos PlayerPrefs keys (`"BestScore"`, `"Coins"`) en lugar de una. Trivial.
- Cuando llegue la tienda, `CoinsWallet` añade `bool TrySpend(int)` con guard de fondos suficientes, sin tocar el resto del sistema.

---

## 9. SOLID aplicado (concreción)

| Principio                          | Aplicación en este proyecto                                                                                                       |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| **S** — Single Responsibility      | `BallController` mueve; `ScoreManager` cuenta; `UIController` muestra; `PathGenerator` genera. Ninguno hace dos cosas.            |
| **O** — Open/Closed                | Añadir un skin nuevo = crear un `.asset` `BallSkinSO` + arrastrar al catalog. `SkinInventory`, `BallSkinApplier` y `ShopPanel` no se tocan. Añadir un canal de evento nuevo = `GameEventSO<T>` con el `T` adecuado, sin tocar el dispatcher.                                     |
| **L** — Liskov Substitution        | Cualquier `GameEventSO<T>` concreto (`IntGameEventSO`, `StringGameEventSO`) es intercambiable donde se espera un canal con ese payload. El consumidor sólo conoce la base abstracta. |
| **I** — Interface Segregation      | `GameEventSO` tiene 3 métodos (`Raise`, `Register`, `Unregister`) y `GameEventSO<T>` añade los tipados. Sin interfaces "fat".     |
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
- `ScoreCalculator` — proyección de distancia sobre `GlobalForward`, clamp en cero, multiplier.
- `CameraFollowMath` — proyección de seguimiento sobre el eje forward, Y bloqueada, perpendicular descartada.
- `CoinsWallet.TrySpend` — éxito, fondos insuficientes, cantidad no positiva.
- `SkinInventory.ParseOwnedCsv` — IDs conocidos, IDs desconocidos descartados, whitespace ignorado, CSV vacío/null.
- `PaletteSampler` — hue complementario, distancia circular, distancia mínima respetada.

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
| Coroutine de freeze-frame deja `Time.timeScale = 0` tras unload         | `GameStateMachine.OnDisable` detiene la coroutine y restaura `Time.timeScale = 1f` defensivamente.          |
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

### ADR-014 — Cámara avanza solo en el eje global forward

**Decisión:** `CameraFollow` proyecta el desplazamiento del target sobre `(-1, 0, 1)/√2` y solo aplica esa componente; la perpendicular se descarta. La Y queda bloqueada a la Y inicial de la cámara.

**Alternativas consideradas:**
- Seguir X y Z del target por separado (implementación original). Mantiene la bola centrada en pantalla; rompe el feel del ZigZag de Ketchapp donde la cámara solo "sube" y la bola serpentea.
- Seguir todo el delta pero con damping mucho más fuerte en la perpendicular. Más complejo de tunear, mismo resultado visual aproximado.

**Justificación:** el pilar #2 del GDD ("lectura instantánea") y el feel del juego de referencia exigen que el jugador perciba la oscilación lateral de la bola como información visual primaria. Si la cámara la compensa, la oscilación deja de leerse.

**Consecuencias:**
- La excursión lateral acumulada de la bola pasa a ser visible. Si supera el ancho del frustum hay que tunear `orthographicSize` o sesgar `PathGenerator` para acotar el drift. Esta calibración es un eje de cambio independiente y se aborda como tuning, no como código nuevo.
- El reset de la bola al spawn provoca un scroll-back suave de la cámara (mismo comportamiento que la implementación anterior; no es regresión). **Actualización iter 10:** ese scroll-back deja de ser suave y pasa a ser un snap instantáneo al origen vía `CameraFollow.HandleGameReset` — una run larga acumula varias unidades de progreso forward y el `SmoothDamp` del run siguiente generaba un slingshot visible. Snap + reset de `_smoothVelocity` lo elimina.
- Matemática extraída a `CameraFollowMath` para test unitario en EditMode, siguiendo el patrón de `ScoreCalculator`.

### ADR-015 — `GameConfigSO.GlobalForward` como única fuente de verdad

**Decisión:** la constante `Vector3 GlobalForward = new Vector3(-1, 0, 1).normalized` vive como `public static readonly` en `GameConfigSO`. `PathGenerator`, `CameraFollow` y `ScoreManager` la leen desde ahí en lugar de declarar su propia copia.

**Alternativas consideradas:**
- Mantener una copia local por consumidor (estado previo a iter 10). Acoplamiento cero entre módulos, drift garantizado el día que alguien retoque uno sin los otros — la deuda quedó registrada explícitamente en el devlog de iter 4.2.
- Promoverla a una `static class GameConstants` en un asmdef propio. Más limpio teóricamente, pero suma un asmdef para una sola constante; `GameConfigSO` ya es referenciado por toda la capa de gameplay.

**Justificación:** la constante define la geometría del path y aparece como argumento de `ScoreCalculator.ComputeDistanceScore`, `CameraFollowMath.ComputeDesiredPosition` y las cuentas de `EnsureAhead`/`RecycleBehind`/`TriggerFalls`. Tener una sola fuente garantiza que cualquier cambio (improbable, pero pensable — un eje girado a 60°/120° en un futuro level pack) se aplica en un solo sitio.

**Consecuencias:**
- Cuatro commits secuenciales (`dc72c52` `93f34c7` `33c743b` `f460d21`) consolidan la migración. Diffs pequeños por consumidor; el axis es byte-idéntico, los 24 tests EditMode pasan sin modificar.
- Cierra el TODO explícito de iter 4.2.
- Coste de runtime cero: `static readonly Vector3` se inicializa una vez al cargar el tipo y se referencia como cualquier campo; no requiere instancia de `GameConfigSO`.

# ZigZag — Devlog

Registro cronológico de decisiones e iteraciones de desarrollo. Cada entrada describe **qué se hizo**, **por qué** y **qué queda pendiente**. Documento de autoría humana; el código es la fuente de verdad.

---

## 2026-05-21 — Iteración 1: movimiento base

### Objetivo

Primer script jugable: la bola se mueve sola en diagonal sobre un plano XZ y cambia de dirección al tap/click. Mínimo viable para validar el feel del movimiento antes de meter generación procedural.

### Alcance acordado tras planificación

- **Movimiento + Input + Config (mínimo jugable).** Tres scripts y una asset de configuración.
- **Tap-anywhere** como input móvil (igual que el ZigZag original): cualquier toque en pantalla cambia dirección. En PC, click izquierdo o `Space` (atajo de editor) hacen lo mismo, sin código adicional, gracias a que Unity mapea el primer touch a `Input.GetMouseButtonDown(0)`.

### Lo que se ha implementado

1. **Estructura de carpetas** bajo `Assets/` con tres `.asmdef` (mínimas necesarias):
   - `ZigZag.Runtime.Data` (sin referencias)
   - `ZigZag.Runtime.Input` (sin referencias)
   - `ZigZag.Runtime.Gameplay` (referencia Data e Input)

2. **`GameConfigSO`** (`Assets/Code/Runtime/Data/GameConfigSO.cs`)
   Solo el subset de campos relevantes para movimiento ahora — `_initialSpeed`, `_acceleration`, `_maxSpeed`, `_fallSpeed`, `_fallThreshold`, `_groundCheckDistance`, `_groundLayerMask`. Patrón obligatorio: `[SerializeField] private` + propiedad `get`-only. `OnValidate` clampa valores negativos.

3. **`InputHandler`** (`Assets/Code/Runtime/Input/InputHandler.cs`)
   Capa fina sobre `UnityEngine.Input`. Expone un único `event Action OnTapped`. Dispara con click izquierdo, primer touch (móvil) o `Space`. Alias `using UnityInput = UnityEngine.Input;` para evitar colisión con el namespace propio `ZigZag.Runtime.Input`.

4. **`BallController`** (`Assets/Code/Runtime/Gameplay/Player/BallController.cs`)
   Núcleo de la mecánica:
   - Dos direcciones precalculadas como `static readonly Vector3`: `(1,0,1).normalized` y `(-1,0,1).normalized`.
   - `Update` aplica `transform.position += direction * speed * dt` mientras esté `grounded`. Acelera hasta `maxSpeed`.
   - Cuando no hay suelo (`Physics.Raycast` corto hacia abajo con `LayerMask`), sigue avanzando en horizontal y suma una componente vertical `-fallSpeed`.
   - Al cruzar `fallThreshold`, dispara `OnFell` y se detiene.
   - `HandleTapped` invierte la diagonal vía bool flag (evita comparar vectores con `==`). Solo cambia si está `IsMoving && IsGrounded` — caer del camino bloquea el input, igual que en el original.
   - Suscripción a `OnTapped` en `OnEnable` / desuscripción en `OnDisable`. Simétrico, sin lambdas.
   - `Debug.Assert` en `Awake` para detectar referencias serializadas sin asignar antes de que petarde con `NullReferenceException`.
   - `OnDrawGizmosSelected` para visualizar el raycast de ground check en la escena (verde = grounded, rojo = falling).

### Decisiones tomadas durante la implementación

- **Sin Rigidbody en la bola.** ADR-001 del documento de arquitectura es claro: movimiento por `transform.position`, caída simulada. Implicación pendiente: cuando aparezcan las gemas, su trigger necesitará Rigidbody (en la gema o en la bola). Se revisará al implementar `Gem`. TODO en el código de Gem cuando llegue.
- **`groundCheckDistance` default 0.55.** Con la bola (sphere radius 0.5) en `y=0.65` y el suelo (cubo escala Y=0.3) en `y=0`, el centro queda exactamente a 0.5 del techo del cubo. 0.55 da cushion mínimo para no oscilar entre grounded/falling en el borde.
- **`_groundLayerMask` default `~0`** (todas las layers). El raycast desde dentro del propio collider de la bola no se autoimpacta (comportamiento documentado de `Physics.Raycast`), así que sirve para test sin tener que crear una capa "Ground" desde el primer día. TODO: crear capa `Ground` y configurar la mask cuando exista `PathGenerator`.
- **Input deshabilitado cuando no `IsGrounded`.** No tiene sentido cambiar de dirección mientras caes — bloqueamos el flip en `HandleTapped`. Coherente con el juego de referencia.
- **El controller no arranca solo.** `IsMoving` empieza en `false`. Hace falta una llamada externa a `StartMoving()` para que se mueva. En esta iteración eso se hará desde un script de test temporal o desde el inspector (vía botón provisional). Cuando exista `GameStateMachine`, será él quien lo dispare al entrar en estado `Playing`.

### Pendiente — setup manual en Unity (no se puede via texto)

1. Crear escena `S_Main.unity` en `Assets/Scenes/` (o renombrar/reusar `SampleScene`).
2. Crear asset `SO_GameConfig.asset` en `Assets/Settings/` (menú `Create → ZigZag → Game Config`).
3. En la escena:
   - **Ground provisional**: Cube primitivo, scale `(30, 0.3, 30)`, posición `(0, 0, 0)`.
   - **Ball**: Sphere primitivo, posición `(0, 0.65, 0)`. Añadir componente `BallController`, arrastrar `SO_GameConfig` y el GameObject del `InputHandler` a sus slots.
   - **Input**: GameObject vacío llamado `InputHandler` con componente `InputHandler`.
   - **Cámara**: orientación a ojo, por ahora estática.
4. **Test temporal**: hasta que exista `GameStateMachine`, llamar `StartMoving()` desde el menú contextual del componente o un pequeño MonoBehaviour-arranque que viva solo durante esta iteración (no commitearlo a `main` cuando llegue la state machine).

### Validación esperada

- **PC**: Play en editor → la bola avanza en diagonal `(1,0,1)`. Click o Space → invierte a `(-1,0,1)`. Sale del cubo → cae. Y < -2 → log de `OnFell` (suscribirse manualmente en un script de test si se quiere ver).
- **Móvil** (no testado en build aún, pero el código está listo): tap en pantalla = mismo efecto que click.

### Próxima iteración (planteamiento)

1. `GameEventSO` + `IntGameEventSO` (assembly `ZigZag.Runtime.Events`).
2. `GameStateMachine` + `GameBootstrap` mínimo.
3. UI básica `S_Main` con Menu → Playing → GameOver.
4. Path provisional con cubos colocados a mano (todavía sin generación procedural).

Generación procedural y pooling se atacarán en la iteración 3 (`PathGenerator` + `PlatformPool`), siguiendo el orden del roadmap del GDD §14.

---

## 2026-05-22 — Addendum a iteración 1

### Cambio de layout: se elimina el prefijo `_Project/`

Decisión revisada tras ver el árbol creado: el código vive ahora directamente bajo `Assets/Code/Runtime/<Layer>/<Feature>/`, sin el wrapper `_Project/`. Misma decisión para `Assets/Settings/` y `Assets/Scenes/`.

- **Pros:** paths más cortos, menos anidamiento, navegación más simple en el Project window.
- **Cons asumidos:** ZigZag content se mezcla alfabéticamente con cualquier paquete third-party que se importe a `Assets/` en el futuro. Para un prototipo de 2 semanas sin packages externos esperados (brief: sin Asset Store, sin plugins) es coste cero.

Actualizado en consecuencia:
- `CLAUDE.md` §3 (árbol de carpetas + justificación).
- `zigzag_architecture.md` §4 (árbol) y §5 (tabla de paths de asmdef).
- `zigzag_gdd.md` (referencias en §13 criterios de éxito, §15 decisiones cerradas, §17 README).
- Cinco prompts de subagentes en `.claude/agents/` (paths en plantillas de salida).
- **Pendiente manual:** `.claude/agents/AGENTS.md` y `.claude/agents/unity-architect.md` — el auto-mode classifier de Claude Code bloquea editar agent prompts. Tienes que cambiar las cuatro referencias a `Assets/_Project/` por `Assets/` a mano (3 líneas en total). Es mecánico.

### Script provisional para testear el movimiento

`Assets/Code/Runtime/Gameplay/Player/BallAutoStarter.cs`:
- En `Start` llama `_ball.StartMoving()`.
- Se suscribe a `OnDirectionChanged` y `OnFell` y los loguea (toggleable con `_verbose`) para depurar el primer playtest sin tener que enchufar todavía la cámara, la UI o la state machine.
- Marcado con `// TODO:` para borrarlo cuando aparezca `GameStateMachine` en la iteración 2.

Setup mínimo en escena para verlo funcionar (mismo cubo+sphere del setup descrito arriba): añadir un GameObject `_Bootstrap` con `BallAutoStarter`, arrastrar el `BallController` a su slot. Play → la bola debería arrancar.

---

## 2026-05-22 — Iteración 2: cerrar el loop (Menu → Playing → GameOver → Retry)

### Objetivo

Cerrar el ciclo de partida. Con loop cerrado, iteraciones posteriores (generación procedural, gemas, powerup) se prueban sin Stop/Play; sin él, todas las features siguientes se testean a ciegas.

### Lo que se ha implementado

1. **Capa Events** — nuevo asmdef `ZigZag.Runtime.Events` (sin refs).
   - `GameEventSO` (parameterless) y `GameEventSO<T>` (abstract) en el mismo fichero — son partners conceptuales.
   - `IntGameEventSO : GameEventSO<int>` en fichero separado (lista para iteración 4 cuando aparezca `_onScoreChanged`).

2. **Capa Core** — nuevo asmdef `ZigZag.Runtime.Core` (refs: Events, Data, Input, Gameplay).
   - `enum GameState { Menu, Playing, GameOver }`.
   - `GameStateMachine` `MonoBehaviour sealed`:
     - **Rutea el tap** según estado: en Menu → `StartGame`; en Playing → `_ball.FlipDirection()`; en GameOver → ignora (sólo botón Retry).
     - Escucha `BallController.OnFell` (evento C# local) y transiciona a GameOver.
     - Escucha `SO_OnRetryRequested` (canal SO) y dispara la secuencia de retry.
     - Raises: `SO_OnGameStarted`, `SO_OnGameOver`, `SO_OnGameReset`.
   - Decisión: **`GameBootstrap` se difiere a iteración 3** — sin pools que inicializar y sin service locator, no aporta nada. Cada actor valida sus refs con `Debug.Assert` en su propio `Awake`.

3. **Capa UI** — nuevo asmdef `ZigZag.Runtime.UI` (ref: sólo Events).
   - `UIController` `MonoBehaviour sealed`:
     - Tres `GameObject` panels (`_menuPanel`, `_hudPanel`, `_gameOverPanel`), se hacen `SetActive` según el evento que llega.
     - `OnRetryButtonClicked()` se invoca desde el `onClick` del botón Retry (configurado por inspector) y `Raise()` el canal `SO_OnRetryRequested`.
   - El UI **no** referencia Core ni Gameplay. La única comunicación con Core es por canal SO.

4. **Refactor de `BallController`** — eliminada la suscripción directa a `InputHandler.OnTapped`. Ahora expone `public void FlipDirection()` y es la state machine quien la llama cuando estado == Playing.
   - **Por qué:** si la bola y el state machine se suscribieran ambos al mismo `OnTapped`, el primer tap en Menu podría a la vez iniciar la partida **y** voltear la dirección (race según orden de suscripción). Centralizar el routing en la state machine elimina la ambigüedad.
   - Consecuencia: el asmdef `ZigZag.Runtime.Gameplay` ya no referencia `Input`.

5. **`BallAutoStarter` eliminado** (script + meta + componente en escena + campo orphan `_inputHandler` en BallController). El TODO de iteración 1 se cumple aquí.

### Pendiente — setup manual en Unity

El código compila independiente, pero la escena necesita estos pasos en el editor antes de que el loop sea jugable. Todos son mecánicos.

#### A. Crear los 4 ScriptableObject de eventos en `Assets/Settings/Events/`

`Right-click → Create → ZigZag → Events → Game Event`, renombrar:
- `SO_OnGameStarted.asset`
- `SO_OnGameOver.asset`
- `SO_OnGameReset.asset`
- `SO_OnRetryRequested.asset`

#### B. En la escena `SampleScene.unity`

1. **GameObject `GameStateMachine`** (root vacío):
   - Añadir componente `GameStateMachine`.
   - Slots: arrastrar `InputHandler` (el del GameObject Player) → `_inputHandler`; `BallController` (también del Player) → `_ball`; (ver paso 3) `BallSpawn` → `_ballSpawnPoint`; los 4 SO de eventos a sus respectivos slots.

2. **GameObject `BallSpawn`** (root vacío):
   - Posición igual al spawn deseado de la bola — ahora mismo `(0, 0, 0)` para que coincida con la posición del Player en la escena actual.
   - Sirve como marker de spawn; lo arrastras al slot `_ballSpawnPoint` del state machine.

3. **Canvas + paneles UI**:
   - `Right-click en jerarquía → UI → Canvas` (crea Canvas + EventSystem automáticamente).
   - Bajo el Canvas, crear tres GameObjects vacíos (con `RectTransform`): `MenuPanel`, `HUDPanel`, `GameOverPanel`. Cada uno ocupa el Canvas entero (anchor stretch).
   - Dentro de cada panel, añadir TextMeshPro UI (`UI → Text - TextMeshPro`):
     - `MenuPanel` → `Text: "ZIGZAG"` grande + `Text: "Click to play"` mediano.
     - `HUDPanel` → `Text: "Score: 0"` esquina superior izquierda. (Placeholder hasta iteración 4 cuando exista `ScoreManager`.)
     - `GameOverPanel` → `Text: "GAME OVER"` grande + `Text: "Score: 0"` + `Button` con label `"RETRY"`.
   - Crear GameObject root `UIController` con el componente `UIController`. Slots: arrastrar los tres paneles + los 4 SO de eventos.
   - **Wire del botón Retry:** seleccionar el botón → componente `Button` → `OnClick()` → `+` → arrastrar el GameObject `UIController` → seleccionar función `UIController.OnRetryButtonClicked`.

4. **Path provisional**:
   - Eliminar (o desactivar) el `Cube` actual grande de `(scale 5,5,5)`.
   - Crear ~12 cubos escalados a `(1, 0.3, 1)` colocados a mano formando un zigzag siguiendo las dos diagonales `(1,0,1).normalized` y `(-1,0,1).normalized`. El primer cubo en `(0, 0, 0)`; cada cubo siguiente desplazado `~0.707` en X y Z respecto al anterior, alternando signo de X cada N cubos (3–8).
   - Layer `Default` (la `GroundLayerMask` por defecto es `~0`, así sirve).
   - Agruparlos como hijos de un GameObject vacío `Path_Provisional` para tenerlos ordenados.
   - **TODO:** `Path_Provisional` desaparece en iteración 3 cuando exista `PathGenerator`.

#### C. Verificación

Play → debería aparecer el menú. Click → bola se mueve. Click durante movimiento → flip. Bola cae fuera del path → panel GameOver. Botón Retry → bola al spawn + partida nueva sin Stop/Play.

### Próxima iteración (planteamiento)

3. `PathGenerator` + `PlatformPool` (`UnityEngine.Pool.ObjectPool<T>`). Reemplaza `Path_Provisional` por generación con seed. Reaparece `GameBootstrap` para inicializar el pool.

### Addendum tras playtest

- **Retry vuelve a Menu, no autostart.** El plan original iba directo a Playing tras Retry; en playtest se siente brusco y rompe el ritmo. Ahora `HandleRetryRequested` deja el estado en `Menu` y `UIController.HandleGameReset` muestra el panel del menú. Hace falta un tap adicional para arrancar de nuevo, consistente con el primer arranque.
- **Diagonal inicial cambiada a `(-1, 0, 1)`** (antes `(1, 0, 1)`). El path provisional se construyó en dirección `-X, +Z`, así que la bola tiene que arrancar por esa diagonal para subir por el camino y no salirse al primer paso. Ajustado en `BallController.Awake` y `ResetTo`; el bool `_isOnLeftDiagonal` arranca en `true` para que `FlipDirection` siga siendo simétrico.
- **Pivote del modelo de dirección: ejes mundo puros, no diagonales 45°.** Segundo playtest revela que las "diagonales 45°" `(±1, 0, 1)` proyectadas por la cámara isométrica (-45° Y) salen del path porque éste se construyó con cubos alineados a los ejes mundo (estilo Ketchapp original). Las direcciones ahora son `AlongNegativeX = (-1, 0, 0)` y `AlongPositiveZ = (0, 0, 1)`. Con la cámara rotada, ambos ejes mundo aparecen como diagonales en pantalla — mismo visual, geometría correcta. El bool interno se renombra `_isOnXAxis`. GDD §5.1 y arquitectura §7.4 actualizados.

---

## 2026-05-22 — Iteración 3: generación procedural + pooling

### Objetivo

Reemplazar el `Path_Provisional` (cubos colocados a mano) por un generador que produce camino infinito reciclando cubos con `UnityEngine.Pool.ObjectPool<T>`. Sin `Instantiate`/`Destroy` después del `Awake` del pool. GDD §14 día 3.

### Lo que se ha implementado

1. **`GameConfigSO` extendido** con bloque `Path Generation` + `Pooling`:
   - `_pathStartPosition = (-2, -3, 3)` — posición del primer cubo del path generado.
   - `_cubeSize = (1, 5, 1)` — el cubo del usuario, alto para mejor visual.
   - `_segmentMinLength = 1`, `_segmentMaxLength = 5` — tramo random en [1, 5] cubos.
   - `_aheadBuffer = 30`, `_behindBuffer = 10` — medidos a lo largo del eje "global forward" `(-1, 0, 1)/√2`, la diagonal entre las dos direcciones que toma la bola.
   - `_generationSeed = 0` — sentinela: cada Retry usa una seed distinta (`Environment.TickCount`) para que cada run sea aleatoria. Cualquier valor distinto de `0` activa modo determinista (mismo camino siempre, útil para debugging).
   - `_platformPoolInitialSize = 50` — el pool prewarmea estos cubos en `Awake`.

2. **Sub-feature `Gameplay/World/`** (asmdef `ZigZag.Runtime.Gameplay` ahora referencia `Events`):
   - `Segment.cs` — clase pura C#. Lleva dirección + lista interna de cubos expuesta como `IReadOnlyList<GameObject>` (CLAUDE §5: no exponer colecciones mutables).
   - `PlatformPool.cs` — wrapper sobre `ObjectPool<GameObject>`. Prewarmea en `Awake` (Get + Release en loop). Los cubos son parented a `transform` para mantener la jerarquía limpia. `maxSize = 2× initialSize` por si hay picos de presión.
   - `PathGenerator.cs`:
     - `Start` → `InitializePath()` puebla el camino hasta cubrir `AheadBuffer`. Esto pasa antes de que arranque la partida, así el menú ya muestra path debajo de la bola.
     - `Update` (solo cuando `_isGenerating = true`) → `EnsureAhead()` + `RecycleBehind()`.
     - `EnsureAhead`: spawna mientras `Vector3.Dot(lastCubePos - ballPos, GlobalForward) < AheadBuffer`. Cap de `MaxCubesSpawnedPerFrame = 20` como red de seguridad contra loops infinitos.
     - `RecycleBehind`: si el último cubo del segmento más antiguo está a más de `BehindBuffer` por detrás (mismo dot product, signo invertido), libera todos sus cubos al pool y descarta el segmento.
     - Determinismo: `System.Random` con seed (no `UnityEngine.Random`, que es global). Reinstanciado en cada `HandleGameReset` con la regla de `CreateRandom()`: seed `0` → `Environment.TickCount` (random por run, default); seed != 0 → determinista. CLAUDE.md §2 prohíbe `DateTime.Now` en gameplay; aquí el tick count se usa **solo** como semilla inicial, la simulación que sigue es totalmente determinista a partir de esa semilla, así que la regla se respeta en espíritu (no hay non-determinism dentro de la run).
     - Suscripciones: `_onGameStarted` → arranca generación; `_onGameOver` → la para; `_onGameReset` → limpia path y rebuild desde `PathStartPosition`.

3. **`GameBootstrap` resucitado** (`Core` asmdef, `DefaultExecutionOrder(-1000)`):
   - Solo valida refs (`PlatformPool`, `PathGenerator`, `GameStateMachine`). No instancia ni resuelve; cada componente se autoinicializa en su `Awake`.
   - **No** referencia `UIController` — eso forzaría a Core a referenciar UI y romper la dirección de asmdefs. La UI se valida a sí misma.

### Decisiones técnicas (mini-ADRs locales a iter 3)

- **Eje "global forward" = `(-1, 0, 1)/√2`** como métrica única para ahead/behind. Alternativa: rastrear distancia recorrida por la bola. Descartado: el dot product es O(1), sin estado, y se mantiene correcto aunque la bola haga zigzag.
- **`Vector3.Dot` en lugar de distancia Manhattan o Euclídea.** Las distancias absolutas confunden ahead y behind. El dot con el eje global da signo: positivo = ahead, negativo = behind.
- **`Queue<Segment>` para el conjunto activo.** FIFO natural: el segmento más antiguo es el más probable a estar detrás de la bola. `Peek` es O(1) y permite chequear el comienzo sin sacar.
- **Pool prewarmeado en `Awake` con loop Get/Release**, no con `Instantiate` directo, porque el `ObjectPool<T>` mantiene su propio contador interno. Llamar `Instantiate` por fuera dejaría el contador inconsistente.
- **Cap de 20 cubos por frame en `EnsureAhead`** como red de seguridad: a velocidad típica (5 u/s) el `AheadBuffer` (30) se consume en 6 segundos, lo que pide ~5 spawns/segundo, muy por debajo del cap. Si llega al cap es síntoma de bug, no de carga real.

### Pendiente — setup manual en Unity

1. **Crear `Assets/Prefabs/P_PlatformCube`** según la spec ya enviada — cubo escalado a `(1, 5, 1)`, sin scripts, Static OFF, layer `Default`.
2. **En la escena**:
   - **Borrar** el `Path_Provisional` y sus cubos hijos.
   - **Crear GameObject `PlatformPool`** con el componente `PlatformPool`. Arrastrar `P_PlatformCube` al slot `_platformPrefab` y `SO_GameConfig` al slot `_config`.
   - **Crear GameObject `PathGenerator`** con el componente `PathGenerator`. Slots: `_config` (SO_GameConfig), `_pool` (el GameObject `PlatformPool`), `_ballTransform` (Player), `_onGameStarted`, `_onGameOver`, `_onGameReset` (los 3 SO de eventos).
   - **Crear GameObject `Bootstrap`** con el componente `GameBootstrap`. Slots: `_platformPool`, `_pathGenerator`, `_stateMachine` (el GameObject `GameStateMachine`).
   - **Mover `BallSpawn`** a la posición sobre el primer cubo: `(-2, 0, 3)` (X y Z coinciden con el primer cubo en `(-2, -3, 3)`; Y `0` deja la bola con holgura de 0.5 sobre el top del cubo, que el raycast de 0.55 cubre).

3. **Verificación**:
   - Play → menú aparece. **El path ya está generado debajo de la bola y se ve hacia delante.** Si solo se ve el primer cubo, la inicialización no llegó al `AheadBuffer` — comprobar refs.
   - Click → bola arranca, el path crece por delante.
   - Tap durante movimiento → bola gira, sigue el siguiente tramo.
   - Mirar el Profiler: cero `Instantiate` después del frame 1.
   - Mirar la jerarquía dentro de `PlatformPool`: el número de hijos es estable (~50–60), no crece sin parar.
   - **Aleatoriedad por run**: con `_generationSeed = 0` (default), cada Retry debe producir un camino distinto. Para verificar el modo determinista (debugging), poner `_generationSeed = 42` (o cualquier int distinto de 0): dos Retry consecutivos darán paths idénticos.

### Próxima iteración (planteamiento)

4. Gemas (`Gem`, `GemSpawner`, `GemPool`) + `ScoreManager` con persistencia (`PlayerPrefs`). HUD muestra score real. GDD §14 día 4.

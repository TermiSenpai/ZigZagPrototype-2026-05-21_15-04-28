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
   - `Gem.cs` — `MonoBehaviour` con `[RequireComponent(Collider, Rigidbody)]`. Trigger; al entrar la bola raises `SO_OnGemCollected(value)` y se devuelve al pool. Patrón `Initialize(value, pool)` para inyectar dependencias en cada `Get` del pool. `Awake` defensivo fuerza `isKinematic=true`, `useGravity=false`, `isTrigger=true` por si el prefab está mal configurado, y un `LogError + enabled=false` si falta el canal de evento (los `Debug.Assert` se compilan fuera en release).
   - `GemPool.cs` — gemelo directo de `PlatformPool`. Mismo prewarm Get/Release en `Awake`, mismo `maxSize = 2× initialSize`.
   - `GemSpawner.cs` — `TryPopulateSegment(Segment)` con dado contra `GemSpawnProbability`. RNG propio `System.Random` reseteado en `_onGameReset` (mismo seed que `PathGenerator`, instancias independientes). Mantiene `List<GameObject> _activeGems` para liberar gemas no recogidas al reset (TODO: prune cuando se introduzcan endurance runs).

3. **Sub-feature `Gameplay/Scoring/`**:
   - `ScoreCalculator.cs` — helper estático puro. `ComputeDistanceScore(ballPos, origin, forwardAxis, multiplier)` proyecta desplazamiento sobre `(-1,0,1)/√2` y devuelve `Mathf.FloorToInt(progress) * multiplier`, con clamp en cero para progreso negativo. Cubierto por 7 EditMode tests.
   - `ScoreManager.cs` — `MonoBehaviour`. Acumula `_gemScore` (suma en `HandleGemCollected`) + `_distanceScore` (recomputado en `Update`). Solo raises `_onScoreChanged` cuando el total entero cambia, no cada frame. Persistencia: `PlayerPrefs.GetInt("BestScore", 0)` en `Awake`; `SetInt + Save` en `SaveBestIfHigher`, llamado al recibir `_onGameOver`. `HandleGameReset` también pasa por `RecomputeAndBroadcast` para no emitir un score-changed espurio si ya estaba a cero.

4. **`PathGenerator` modificado** para invocar `_gemSpawner.TryPopulateSegment(_currentSegment)` justo antes de `FlipDirection + StartNewSegment`, es decir cuando un tramo alcanza su longitud objetivo. Campo opcional (sin assert) — si no hay spawner enganchado el path sigue generándose sin gemas. El último tramo de `InitializePath` no se finaliza por este camino y queda sin gema — known minor, se finaliza en cuanto la bola lo cruza.

5. **`UIController` extendido** con tres `TextMeshProUGUI` (`_hudScoreText`, `_gameOverFinalScoreText`, `_bestScoreText`) y un GameObject opcional `_newRecordBadge`. Suscrito a `SO_OnScoreChanged` y `SO_OnBestScoreChanged`. El badge usa dos handlers cooperando (`HandleBestScoreChanged` + `HandleGameOver`) para resolverse independientemente del orden en que Unity dispare a los suscriptores de `_onGameOver` — `_newBestSeenInThisRun` se resetea en `HandleGameStarted`. Asmdef `ZigZag.Runtime.UI` añade referencia `Unity.TextMeshPro`.

6. **`GameBootstrap` extendido** para validar `_scoreManager`, `_gemPool`, `_gemSpawner` en `Awake`. Sin cambios en asmdef (todos viven en `ZigZag.Runtime.Gameplay`).

7. **Test harness EditMode estrenado** — primer `.asmdef` de tests del proyecto (`Assets/Code/Tests/EditMode/ZigZag.Tests.EditMode.asmdef`). 7 tests sobre `ScoreCalculator` (cero, progreso por -X, por +Z, diagonal, backwards-clamp, multiplier, multiplier cero).

### Decisiones técnicas (mini-ADRs locales)

- **Gem requiere `Rigidbody` kinematic, no la bola.** ADR-001 manda bola sin Rigidbody. Unity 2022.3 exige que al menos uno de los dos colliders tenga Rigidbody para disparar `OnTriggerEnter`. Solución: la gema lo lleva (`isKinematic=true, useGravity=false`) — la bola sigue siendo collider estático con transform que se mueve.
- **Distancia medida por proyección sobre `GlobalForward`, no por `position.z`.** GDD §7.2 propuso `position.z` cuando el camino era diagonal `(1,0,1)`. Tras el rework a ejes mundo `-X/+Z` (iter 2 addendum), `position.z` ignoraría el progreso de los tramos `-X`. La proyección `Dot(pos - origin, (-1,0,1)/√2)` captura ambos correctamente.
- **`ScoreCalculator` como `static class` puro.** Separar la aritmética de los side-effects (raises, PlayerPrefs) permite tests EditMode triviales y deja a `ScoreManager` reducido a 1-liners no testeables (los wires de eventos). YAGNI: no se introduce `IBestScoreStore` — `PlayerPrefs` con clave `"BestScore"` es la historia completa.
- **`GemSpawner` con RNG propio.** Alternativa: pasar el `System.Random` de `PathGenerator`. Descartado porque acopla los dos sistemas. Cada uno tiene `System.Random` independiente seedeado con el mismo `_config.GenerationSeed`; las secuencias se consumen sin contaminarse y el run sigue siendo reproducible byte a byte por seed.
- **Score se broadcastea solo cuando el entero cambia.** El proyectado `progress` es float pero el score es int; la mayoría de frames `Mathf.FloorToInt` no cruza umbral. Sin esta guarda el HUD reprintearía 60×/s.
- **Badge de NEW RECORD resuelto sin asumir orden de suscriptores.** Unity no garantiza el orden en que los listeners de un `GameEventSO` se ejecutan. `ScoreManager.HandleGameOver` y `UIController.HandleGameOver` pueden dispararse en cualquier orden. La solución es un bool `_newBestSeenInThisRun` que el handler de `_onBestScoreChanged` marca cuando ve un récord nuevo, y dos puntos de activación del badge (uno en cada handler) que comprueban tanto el flag como `_gameOverPanel.activeSelf`. Funcionalmente correcto en ambos órdenes.

### Pendiente — setup manual en Unity (todavía sin hacer)

Cubierto íntegramente en `docs/superpowers/plans/2026-05-22-iteration-4-gems-and-score.md` Task 12. Resumen: crear `P_Gem.prefab`, añadir GameObjects `GemPool / GemSpawner / ScoreManager`, wire UI texts (HUD + GameOver + NewRecordBadge), wire `GameBootstrap` con las nuevas refs, etiquetar la bola con `Player`, fijar valores por defecto en `SO_GameConfig`. Hasta que se complete la wiring, el código compila pero la escena ejecuta el flujo iteración 3 (sin gemas, sin score real, HUD muestra "Score: 0" placeholder).

### Próxima iteración (planteamiento)

5. Powerup imán (`IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool`). Atrae gemas en radio `R` durante `T` segundos. GDD §14 día 5. Demuestra que la arquitectura es extensible sin tocar `Gem`/`ScoreManager`.

---

## 2026-05-23 — Iteración 4.1: split gem coins ↔ distance score

### Objetivo

Separar conceptualmente y en el código las dos fuentes de "puntuación" combinadas en iteración 4: las gemas pasan a ser currency persistente preparada para una tienda futura; el `ScoreManager` queda como tracker de distancia puro. Reabre parcialmente la decisión cerrada del GDD §11 ("Sin sistema de monedas / shop fuera de scope") — la tienda sigue fuera, pero la infraestructura de wallet sí entra.

### Lo que se ha implementado

1. **Nueva sub-feature `Gameplay/Economy/`** con `CoinsWallet.cs`. `MonoBehaviour` único responsable de la PlayerPrefs key `"Coins"`. Suscrito a `SO_OnGemCollected` (suma a wallet + session) y `SO_OnGameReset` (resetea session, wallet intacta). Persistencia en cada pickup. Sin API `Spend` — la añade la iteración de tienda.

2. **`ScoreManager` refactor**: borrado `_gemScore`, `_onGemCollected`, `HandleGemCollected`. `CurrentScore = _distanceScore` directo. Nombres públicos y PlayerPrefs key `"BestScore"` intactos por decisión del usuario; solo cambia la semántica (ahora es distancia pura).

3. **`GameConfigSO._gemValue`** default `10 → 1`. Tooltip aclarado: "Coins awarded per gem collected. Powerups may temporarily override this multiplier at runtime."

4. **`UIController` extendido** con `_hudCoinsText` y `_gameOverSessionCoinsText`. Dos canales nuevos: `SO_OnCoinsChanged` (HUD wallet) y `SO_OnSessionCoinsChanged` (panel GameOver `+N coins`).

5. **`GameBootstrap`** añade ref + assert de `_coinsWallet`.

6. **Docs**: GDD §5.5, §7.2 (separada en 7.2.1 score y 7.2.2 coins), §10.2, §10.3, §11; arquitectura §6.2, nuevo §7.17 `CoinsWallet` (GameBootstrap pasa a §7.18), nuevo ADR-013 "Wallet separada del score, persistida por pickup".

### Decisiones tomadas en este split

- **Sin renames** (instrucción explícita del usuario). `CurrentScore`, `BestScore`, `SO_OnScoreChanged` y la PlayerPrefs key `"BestScore"` se mantienen aunque ahora reflejen solo distancia. Hace que diffs en consumidores existentes sean nulos; el coste es que un revisor nuevo necesita leer el XML doc actualizado para entender la semántica.
- **Persistencia por pickup, no por GameOver.** `PlayerPrefs.SetInt + Save` es barato (microsegundos por escritura) y un cierre brusco a media run no debe robarle coins al jugador. Para `BestScore` la decisión opuesta (escribir solo en GameOver) sigue siendo correcta — un score parcial no tiene valor.
- **1 gema = 1 moneda como default tunable.** Powerups futuros podrán multiplicarlo temporalmente. Diseño aplazado al primer powerup que lo necesite — el path sugerido (en future considerations del spec) es que `GemSpawner` consulte un servicio de modificadores en lugar de leer `_config.GemValue` raw.
- **No se migra la PlayerPrefs key `"BestScore"`.** Valores pre-iteración pueden contener `distancia + gemas`. Sin jugadores reales, no merece código de migración. Manual reset desde `Edit → Clear All PlayerPrefs` si se quiere baseline limpio.
- **Sin tests nuevos para `CoinsWallet`.** Es `+=` + `PlayerPrefs.SetInt` sin invariantes. Cuando aparezca `TrySpend(int amount)` con guard de fondos, ese sí merece tests EditMode.

### Pendiente — setup manual en Unity

1. **Crear 2 SO de eventos** en `Assets/Settings/Events/`:
   - `SO_OnCoinsChanged.asset` (IntGameEventSO).
   - `SO_OnSessionCoinsChanged.asset` (IntGameEventSO).
2. **GameObject `CoinsWallet`** en escena con el componente nuevo. Slots: `_onGemCollected` (existente `SO_OnGemCollected`), `_onGameReset` (existente), los 2 outbound SOs nuevos.
3. **Canvas HUD**: añadir `TextMeshProUGUI` `Coins: 0` en una esquina contraria al score (ej. inferior izquierda).
4. **Canvas GameOver**: añadir `TextMeshProUGUI` `+0 coins` debajo del score final.
5. **`UIController`**: arrastrar los 2 TMP nuevos + los 2 SOs nuevos a sus slots.
6. **`GameBootstrap`**: arrastrar el GameObject `CoinsWallet` al nuevo slot.
7. **`SO_GameConfig.asset`**: cambiar `_gemValue` de 10 a 1.

### Próxima iteración (planteamiento)

5. Powerup imán (`IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool`). Atrae gemas en radio `R` durante `T` segundos. GDD §14 día 5.

Eventual: powerup multiplicador de coins (atomic con el imán o separado). Diseño aplazado hasta que se necesite — ver future considerations del spec de iteración 4.1.

### Addendum (mismo día, post-wiring)

Swap semántico HUD ↔ GameOver tras playtest:

- **HUD**: `+{sessionCoins}` (coins ganadas en la run actual). Reseteo automático en cada Retry vía `SO_OnSessionCoinsChanged.Raise(0)` que `CoinsWallet.HandleGameReset` ya disparaba.
- **GameOver**: `Coins: {totalCoins}` (wallet total persistente, lo que el jugador "tiene en el bolsillo" para una futura tienda).

Razón: el HUD muestra progreso de la run en curso (motivación inmediata); el GameOver es el lugar donde tiene sentido leer el balance acumulado (preview de lo que podrá gastarse). El reparto inicial estaba al revés y se notaba: durante la partida no necesitas ver el total persistente, ya que no puedes gastarlo todavía.

Cambios de código (commit aparte): `UIController._gameOverSessionCoinsText` → `_gameOverTotalCoinsText` (con `[FormerlySerializedAs]` para no romper el wire de escena ya hecho); swap de a qué TMP escribe cada handler. Spec §4.4, §5, §6 y nuevo §12 actualizados.

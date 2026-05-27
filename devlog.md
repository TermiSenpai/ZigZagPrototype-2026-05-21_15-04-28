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

---

## 2026-05-25 — Iteración 6: audio + freeze frame al morir

### Objetivo

Primera tanda de polish funcional saltándose el powerup imán (decisión explícita de scope, ver memoria `project_scope_magnet_skipped.md`). Tres SFX enganchados por canales SO + hit-stop de 0.1 s en la muerte. Sin tocar particles ni trail — esos quedan para una iteración posterior si se pide.

### Lo que se ha implementado

1. **`GameConfigSO` extendido** con sección `Game Feel`:
   - `_freezeFrameOnDeathSeconds = 0.1f` (tunable, 0 desactiva el efecto). Tooltip aclara que es `Time.timeScale = 0` real-time seconds y que la UI de GameOver aparece **después** del freeze.
   - `OnValidate` clampa a no-negativos.

2. **`BallController` outbound channel**:
   - Nuevo `[SerializeField] private GameEventSO _onDirectionChangedChannel` (opcional, null-safe). Raise dentro de `FlipDirection` justo después del `event Action<Vector3>` C# existente.
   - El `event Action<Vector3>` se mantiene — sigue siendo útil para listeners locales en el mismo asmdef (por ejemplo, un futuro `TrailRenderer` driver que necesite el vector concreto). El canal SO añadido es parameterless porque audio solo necesita "algo cambió".
   - Asmdef `ZigZag.Runtime.Gameplay` ya referenciaba `Events`, no hace falta tocarlo.

3. **`GameStateMachine` freeze frame**:
   - Nuevo `[SerializeField] GameConfigSO _config` + assert en `Awake`.
   - `HandleBallFell` ahora hace el transition **síncrono** (`CurrentState = GameOver`) para que un segundo `OnFell` en el mismo frame se filtre por el guard, y luego dispara `_endGameRoutine = StartCoroutine(EndGameRoutine())`.
   - `EndGameRoutine`: `Time.timeScale = 0` → `WaitForSecondsRealtime(duration)` → `Time.timeScale = 1` → `_onGameOver.Raise()`. El panel de GameOver aparece **después** del hit-stop, lo que da peso al momento de impacto.
   - `OnDisable` defensivo: detiene la coroutine en curso y restaura `Time.timeScale = 1f` si quedó a 0 (defiende contra unload de escena mid-freeze).
   - Eliminado el helper privado `EndGame()` — su única llamada (desde `HandleBallFell`) ahora vive en el camino coroutine, y mantenerlo sería un wrapper de una línea sin valor.

4. **Nueva capa Audio** — asmdef `ZigZag.Runtime.Audio` referenciando solo `ZigZag.Runtime.Events`. Sigue la regla de la presentación (UI/Audio/VFX solo escucha, nunca conduce).
   - `AudioManager.cs` (`MonoBehaviour sealed`, `[DisallowMultipleComponent]`, `[RequireComponent(AudioSource)]`):
     - 3 slots de canal SO: `_onDirectionChanged` (parameterless, el que `BallController` ahora raise), `_onGemCollected` (`IntGameEventSO` existente; el payload se descarta con `int _`), `_onGameOver` (`GameEventSO` existente).
     - 3 `AudioClip` slots + 3 `[Range(0,1)] float` para volume per-clip (default flip 0.7, gem/death 1.0).
     - `Awake` agarra el `AudioSource`, fuerza `playOnAwake=false` y `loop=false` por si el prefab está mal configurado.
     - `OnEnable`/`OnDisable` simétricos (CLAUDE.md §7). Handlers `=>`-bodied a `PlayOneShot(clip, volume)`. `PlayOneShot` con `clip == null` → no-op silencioso, para poder probar el wiring sin tener los clips aún.

### Decisiones técnicas

- **Hit-stop antes del raise, no después.** Alternativa considerada: raise inmediato + freeze por encima. Descartada porque el panel UI aparecería instantáneamente, anulando perceptualmente el freeze — lo único que quedaría congelado sería el path, sin valor narrativo. Con el orden actual, los 100 ms del freeze son lo que separa "la bola se cayó" de "aparece el panel", que es lo que pide el game feel arcade clásico.
- **`PlayOneShot` con un solo `AudioSource` compartido**, no un `AudioSource` por clip. `PlayOneShot` permite solapes y mantiene el componente count abajo. Si en algún momento un SFX necesita pitch/spatial settings propios, se promueve a su propio `AudioSource` — todavía no es el caso.
- **Canal SO parameterless para direction change**, no `Vector3GameEventSO`. Audio no necesita el vector. Si la VFX futura sí lo necesita, se añade un segundo canal o se introduce un `Vector3GameEventSO` específico — YAGNI por ahora.
- **`AudioManager` no se asserta en `GameBootstrap`.** Coherente con la regla actual: `UIController` tampoco se asserta porque eso forzaría `Core → UI` y rompería la dirección del grafo de asmdefs. Audio es "presentación" en la misma capa que UI; se autovalida.
- **Volumes per-clip serializados en el `AudioManager`**, no en `GameConfigSO`. Son ganancia de mixing, no game design — viven con quien los reproduce. Si más adelante hace falta un `AudioMixer` con grupos (`SFX`, `Music`), se mete entre el `AudioSource` y los clips sin tocar este código.

### Pendiente — setup manual en Unity

1. **Crear `SO_OnDirectionChanged.asset`** en `Assets/Settings/Events/` (`Create → ZigZag → Events → Game Event`).
2. **Arrastrarlo al slot `_onDirectionChangedChannel` del `BallController`** del Player.
3. **`SO_GameConfig.asset`**: el campo `Freeze Frame On Death Seconds` aparecerá en la sección `Game Feel`; valor 0.1 (default ya es 0.1, así que basta con verificar).
4. **Arrastrar `SO_GameConfig` al nuevo slot `_config` del `GameStateMachine`** (campo nuevo, aparece bajo `Dependencies`).
5. **Crear GameObject `AudioManager`** en escena con el componente `AudioManager` + un `AudioSource` (lo añade `[RequireComponent]` solo). Slots:
   - `_onDirectionChanged` → `SO_OnDirectionChanged.asset` recién creado.
   - `_onGemCollected` → `SO_OnGemCollected.asset` (existente).
   - `_onGameOver` → `SO_OnGameOver.asset` (existente).
   - `_directionFlipClip`, `_gemCollectedClip`, `_gameOverClip` → 3 `AudioClip` que hay que conseguir (jsfxr / freesound CC0). Slot vacío → no crashea, simplemente no suena ese SFX.
6. **Conseguir 3 clips** y dejarlos en `Assets/Audio/`. Recomendaciones GDD §9: click 50 ms, tintineo 200 ms, impacto grave 300 ms.

### Verificación

- Play → click → la bola flipea → suena `directionFlipClip` (si está asignado).
- Recoger gema → suena `gemCollectedClip`.
- Caer del path → freeze de 100 ms (la bola se queda congelada mid-fall) → suena `gameOverClip` y aparece el panel GameOver. Retry → la bola vuelve a Menu sin que el `Time.timeScale` se quede a 0 (si se queda, hay un bug — comprobar que `OnDisable` no se está disparando mid-coroutine).
- Profiler / Audio Mixer: 0 instances de `AudioSource` creados en runtime; el único que existe es el del GameObject `AudioManager`.

### Próxima iteración (planteamiento)

Polish visual restante (trail renderer en la bola, particles en gem/death) o saltarlo y cerrar deliverable (README + build de Windows). Decisión a tomar al inicio de la siguiente sesión.

### Addendum mismo día — rotación de la bola

Añadido rolling visual sin slip en `BallController`:

- Nuevo `[SerializeField] private float _ballRadius = 0.5f`. Sección `Visual Rolling` propia, separada de gameplay tuning porque es un valor de presentación (acoplado al render del sphere primitive, no al feel del movimiento). `OnValidate` clampa a `>= 0.01` para evitar división por cero.
- `_rollAxis = Vector3.Cross(Vector3.up, CurrentDirection)`. Para `(-1,0,0)` da `+Z`; para `(0,0,1)` da `+X`. Cacheado y recomputado en `Awake`, `ResetTo` y `FlipDirection` — el cross se hace una vez por cambio de dirección, no por frame.
- `Update` aplica `transform.Rotate(_rollAxis, CurrentSpeed * dt * Rad2Deg / _ballRadius, Space.World)` al final del tick, después del position/speed update. La fórmula es la del rolling sin deslizamiento: ω = v/r (rad/s). Se aplica tanto cuando la bola está grounded como mientras cae (hasta que cruza `FallThreshold` y `IsMoving` se pone a `false`, momento en que `Update` early-returns y la rotación se congela junto con la posición).
- `ResetTo` ahora también resetea `transform.rotation = Quaternion.identity` para que cada run empiece con orientación limpia (invisible con un sphere de color plano, pero importante en cuanto haya textura/skin con detalle direccional).

### Decisión técnica

- **Sin slerp/lerp en el cambio de eje de rotación.** Al flipear dirección el eje cambia de golpe (de +Z a +X o viceversa). Una transición suave entre ejes daría un "wobble" visual incoherente con el cambio instantáneo de dirección lineal del juego. El estilo ZigZag premia inputs limpios; el rolling sigue el mismo principio.
- **No se mueve `_ballRadius` a `GameConfigSO`.** Es un parámetro de presentación atado al GameObject visual, no al game feel. Si alguien cambia el `transform.localScale` del sphere, también debe ajustar este campo en el mismo Inspector — mantenerlos juntos previene drift entre escala visual y velocidad angular.

### Pendiente — setup manual

Ninguno. El default `_ballRadius = 0.5f` funciona con el sphere primitive de Unity a escala 1 sin tocar nada. Si el visual cambia de tamaño en algún momento, ajustar el campo en el componente `BallController`.

---

## 2026-05-25 — Iteración 5: tienda de skins (en paralelo con iter 6)

### Objetivo

Reemplazar el powerup imán descopeado por una tienda accesible desde el Menu donde el jugador gasta las coins de `CoinsWallet` para comprar y equipar skins cosméticos de la bola (solo swap de material). Prueba que la arquitectura SO + canales aguanta una feature nueva sin tocar `Gem`, `ScoreManager` ni `BallController`. Iter 5 vivió en una rama paralela a iter 6 (game-feel) — el merge `d1f348e` integra ambas el mismo día.

### Lo que se ha implementado

1. **Capa `Cosmetics/` nueva** dentro de `ZigZag.Runtime.Gameplay` (sin asmdef propio — sub-feature):
   - `BallSkinSO` — datos por skin: `Id` (estable, contrato de persistencia, nunca renombrar), `DisplayName`, `Price`, `Material`. `OnValidate` exige `Id` y `Material`.
   - `BallSkinCatalogSO` — array ordenado de skins. El primero es el default (`Price = 0`, siempre owned). `GetById` con loop manual (no LINQ, regla de hot-path CLAUDE §8). `OnValidate` chequea unicidad de IDs y `Price == 0` en el primero.
   - `SkinInventory` — único dueño de las PlayerPrefs keys `"OwnedSkins"` (CSV) y `"EquippedSkin"` (id). Escucha `SO_OnSkinPurchaseRequested` (valida con `CoinsWallet.TrySpend` + auto-equipa) y `SO_OnSkinEquipRequested` (cambia equipado si ya owned). Persistencia en cada mutación — un alt-F4 a mitad de tienda no roba un skin pagado. `Start` raises `SO_OnSkinEquipped` con el equipado actual para pintar al primer frame.
   - `BallSkinApplier` — vive en la bola, escucha `SO_OnSkinEquipped` y hace `MeshRenderer.sharedMaterial = skin.Material`. `sharedMaterial` deliberado: `.material` instanciaría heap-allocs y rompería batching.
   - `AssemblyInfo.cs` con `[InternalsVisibleTo("ZigZag.Tests.EditMode")]` para testear `ParseOwnedCsv`.

2. **`CoinsWallet.TrySpend(int amount)`** añadido — devuelve `bool`. Guarda contra `amount <= 0` y `TotalCoins < amount`. Persiste y raise `SO_OnCoinsChanged` solo en éxito. `SessionCoins` no se toca (gastar no es un evento de run). Cubierto por 3 tests EditMode nuevos (`CoinsWalletTests`).

3. **UI Shop**:
   - `ShopRowView` — pure presentation. `Bind(skin)` setea name/swatch (color del material)/listener; `Refresh(owned, equipped, canAfford)` actualiza el label del botón (`BUY 50`, `EQUIP`, `EQUIPPED`) y `interactable`. Click raises `SO_OnSkinPurchaseRequested` o `SO_OnSkinEquipRequested` con el `Id` como payload.
   - `ShopPanel` — overlay sobre el Menu. `Start` construye una fila por entrada del catalog dentro de un `VerticalLayoutGroup`. `OpenShop()` activa el root + raises `SO_OnShopOpened`; `CloseShop()` lo cierra + raises `SO_OnShopClosed`. Escucha `SO_OnInventoryChanged` y `SO_OnCoinsChanged` para refresh.
   - `UIController.OnShopButtonClicked()` añadido — wired al botón SHOP del Menu via inspector, llama `_shopPanel.OpenShop()`.

4. **`InputHandler` doble supresión UI ↔ gameplay**:
   - Nuevas refs `_onShopOpened` / `_onShopClosed` → `_isBlocked` flag bloquea TODO input (mouse + Space) mientras la tienda está abierta.
   - Guard adicional `EventSystem.current.IsPointerOverGameObject()` — si el click cayó sobre un UI raycast target (botón SHOP del menu, botón RETRY de GameOver, cualquier widget futuro encima del gameplay), el tap se descarta. Space no se ve afectado (no tiene posición).
   - `ZigZag.Runtime.Input.asmdef` ahora referencia `Events`.

5. **`ZigZag.Runtime.UI.asmdef`** añade referencia a `ZigZag.Runtime.Gameplay` para que `ShopPanel`/`ShopRowView` puedan tipar `BallSkinSO`, `SkinInventory` y `CoinsWallet`.

6. **5 canales SO nuevos** en `Assets/Settings/Events/`:
   - `SO_OnSkinPurchaseRequested` (String) — UI → Inventory.
   - `SO_OnSkinEquipRequested` (String) — UI → Inventory.
   - `SO_OnSkinEquipped` (String) — Inventory → BallSkinApplier + ShopPanel.
   - `SO_OnInventoryChanged` (parameterless) — Inventory → ShopPanel.
   - `SO_OnShopOpened` / `SO_OnShopClosed` (parameterless) — ShopPanel → InputHandler.

7. **Assets**: 5 materiales (`M_BallSkin_Default/Red/Green/Blue/Gold`) + 5 `SO_Skin_*.asset` + `SO_BallSkinCatalog.asset` + prefab `P_ShopRow`.

8. **`GameBootstrap`** valida `_skinInventory` y `_ballSkinApplier` en `Awake` con `Debug.Assert` — coherente con la regla de validación local del bootstrap.

9. **Tests EditMode**:
   - `CoinsWalletTests` (3) — `TrySpend` deduce + raise / preserva en insuficiencia / falla en `amount <= 0`.
   - `SkinInventoryTests` (4) — `ParseOwnedCsv` con todos los IDs / con IDs desconocidos / con whitespace / con CSV vacío o null. TearDown destruye los `BallSkinSO` instanciados (fix de leak detectado en `c1f89ad`).

### Decisiones técnicas

- **Catalog-of-SOs, no enum.** Añadir un skin es crear un `.asset`, no recompilar. Coste: una indirección por lookup; payoff: editorial workflow puro.
- **`Id` separado de `name` del asset.** El asset puede renombrarse sin romper PlayerPrefs; el `Id` es el contrato.
- **Doble supresión UI/tap.** El bloqueo por `_isBlocked` cubre Space; el guard `IsPointerOverGameObject()` cubre clicks que caen sobre buttons. Ninguno solo basta — Space ignora pointer; los clicks fuera del shop sí tienen que pasar.
- **Auto-equipa al comprar.** Un skin que compras sin saber dónde activarlo es fricción gratuita. La UX clásica de Ketchapp (gym ZigZag, Crossy Road) auto-equipa, así que se replica.
- **Sin botón "Restore Purchases" / sin IAP.** Skins gratis pagadas con currency in-game; el prototipo no toca stores reales. Si algún día se monetiza, el SkinInventory se mantiene; se añade un servicio aparte.

### Pendiente — setup manual en Unity

Cubierto en el plan `docs/superpowers/plans/2026-05-25-shop-and-ball-skins.md` (tasks 12–18). Resumen: crear los 5 SO de eventos + 5 skins SO + catalog + prefab `P_ShopRow`, montar `ShopPanel` como hijo del Canvas con `VerticalLayoutGroup`, añadir botón SHOP en MenuPanel, wirear `BallSkinApplier` en el GameObject de la bola, wirear `SkinInventory` en root. Si las refs nuevas del Bootstrap quedan vacías, los `Debug.Assert` lo cazan al play.

### Verificación

- Play → Menu visible → botón SHOP abre overlay → 5 filas. Default = `EQUIPPED`, otras con precio.
- Click `BUY` con coins insuficientes → botón gris, no responde.
- Recoger gemas hasta tener 50 coins → `BUY 50` se vuelve interactable. Click → coins bajan, fila pasa a `EQUIPPED`, la bola cambia de color en tiempo real (el `OnSkinEquipped` rasiona el material).
- Cerrar shop → tap en pantalla arranca la run. Click sobre el botón SHOP no arranca la run (guard de UI raycast).
- Quit & relaunch → coins y skin equipado persisten.

---

## 2026-05-25 — Iteración 7: paleta de color cíclica

### Objetivo

Dar identidad visual al juego sin assets externos. Cada N puntos el path y el fondo cambian a un par de colores complementarios, lerpeando suave. Coste cero en arte (todo es HSV sampling), valor alto en motivación al jugador (cada threshold se siente como un mini-hito).

### Lo que se ha implementado

1. **Sub-feature `Gameplay/Aesthetics/`** dentro del asmdef `ZigZag.Runtime.Gameplay`:
   - `PaletteRulesSO` — asset configurable. Bloques `Timing` (`ScoreThresholdStep = 50`, `TransitionSeconds = 1.5`), `HSV Sampling` (`SaturationRange = (0.55, 0.85)`, `ValueRange = (0.70, 0.95)`, `MinHueDistanceFromPrevious = 0.15` — evita paletas casi idénticas), `Initial Colors` (platform y camera de boot, matcheados a los actuales del proyecto), `Shader` (`_Color` por defecto; cambiar a `_BaseColor` migra a URP sin tocar código). `OnValidate` clampa todo.
   - `PaletteSampler` — `static class` interna y pura. `Sample(rng, rules, previousPrimaryHue)` devuelve `(platform, camera, primaryHue)` con la cámara usando el hue complementario (offset 0.5 en el círculo). Loop de hasta 8 intentos para respetar `MinHueDistanceFromPrevious`; si no se cumple, acepta el último sampleado (degradación graceful). Helpers `ComplementHue` y `CircularDistance` expuestos para tests.
   - `PaletteController` — `MonoBehaviour sealed`:
     - Escucha `SO_OnScoreChanged` (`IntGameEventSO`). Cuenta cuántos `ScoreThresholdStep` se han cruzado; cuando sube uno nuevo, dispara `TriggerSwap`.
     - `LerpRoutine` (coroutine) interpola `_currentPlatformColor` → `targetPlatform` y `_currentCameraColor` → `targetCamera` durante `TransitionSeconds`. Cancela una transición en curso si entra otra (no se acumulan).
     - Escucha `SO_OnGameReset` — vuelve a colores iniciales, re-seed del `System.Random`, `_lastThresholdReached = 0`.
     - Escucha `SO_OnGameStarted` defensivamente — reset del threshold counter en caso de que el orden de los handlers en GameReset difiera.
     - `Shader.PropertyToID` cacheado para evitar el lookup por nombre en cada `SetColor`.

2. **`PlatformPool.RuntimeMaterial`** — nueva propiedad. En `Awake` el pool clona el material del prefab (`new Material(prefabRenderer.sharedMaterial)`) y lo asigna como `sharedMaterial` a cada cubo creado. El `PaletteController` muta este material runtime — todos los cubos del pool cambian de color con un solo `SetColor`. Se destruye en `OnDestroy` para no fugar.

3. **Determinismo coherente con `PathGenerator`**: `PaletteController` usa su propio `System.Random` seedeado con `GameConfigSO.GenerationSeed` (mismo sentinel: `0` → `Environment.TickCount`). Instancias independientes para no contaminar la secuencia del generator.

### Decisiones técnicas

- **Material runtime en el pool, no en cada cubo.** Si cada cubo instanciara su propio `.material`, el batching se rompería (60+ draw calls vs. 1) y un palette swap pediría `N*SetColor` en lugar de uno solo.
- **Complementario, no análogo ni triádico.** El contraste alto entre path y fondo facilita el read en movimiento. Análogos/triádicos sirven para arte estático, no para un juego de reacción.
- **`MinHueDistanceFromPrevious = 0.15`.** Empíricamente, por debajo de 0.12 dos paletas consecutivas se sienten "iguales"; por encima de 0.20 cada salto es violento. 0.15 es el punto dulce.
- **Lerp del platform Y del camera en la misma coroutine.** Iniciar dos lerps independientes daría drift si los `Time.deltaTime` no son idénticos (no lo son si entran handlers en distintos puntos del frame). Una sola coroutine garantiza sync.
- **Sin assert de `PaletteController` en `GameBootstrap`.** Misma razón que con `UIController`/`AudioManager`: es capa de presentación, se autovalida.

### Pendiente — setup manual en Unity

1. Crear `SO_PaletteRules.asset` (`Create → ZigZag → Aesthetics → Palette Rules`). Defaults son razonables.
2. GameObject `PaletteController` con el componente. Slots: `_camera` (Main Camera), `_platformPool` (el GameObject `PlatformPool`), `_rules` (`SO_PaletteRules`), `_config` (`SO_GameConfig`), `_onScoreChanged`, `_onGameReset`, `_onGameStarted`.

### Verificación

Play → arranque con colores iniciales (azul path + gris claro fondo). Cuando el score cruza 50 → lerp de ~1.5s a una paleta nueva. Cada 50 puntos, cambio nuevo. Retry → vuelve a colores iniciales con la misma seed (si `GenerationSeed != 0`) o una nueva (si es 0).

---

## 2026-05-26 — Iteración 8: pulido final (plataformas que caen + build móvil + audio assets)

### Objetivo

Cerrar el loop visible para el deliverable: las plataformas que la bola ya cruzó se desploman (lo que refuerza el sentido de no-retorno del juego), build de Windows configurado en formato móvil portrait, clips de audio importados, balance final de score. Iteración con un día de sesión, varios commits pequeños.

### Lo que se ha implementado

1. **`PlatformFaller`** (`Assets/Code/Runtime/Gameplay/World/PlatformFaller.cs`):
   - `MonoBehaviour sealed [DisallowMultipleComponent]` añadido al prefab `P_PlatformCube`.
   - Anima caída hand-rolled (no Rigidbody — la bola sigue siendo kinematic, y `PhysX` colisionaría con el ground raycast). `Begin()` arranca; `Update` integra gravedad (`_gravity = 18` u/s² por defecto). Constante `MaxFallDistance = 60` evita que un cubo que entró en caída pero nunca se recicla (porque el game ha acabado, por ejemplo) se desplome para siempre consumiendo CPU.
   - `Begin()` es idempotente. `OnDisable` resetea estado — el pool desactiva el cubo al `Release`, así que la próxima vez que `Get()` lo saca y `PathGenerator` lo reposiciona, el faller está limpio.

2. **`GameConfigSO._platformFallStartBehind = 1.5f`** — distancia (proyectada sobre `GlobalForward`) detrás de la bola a la que un cubo empieza a caer. 1.5 ≈ 2 cubos detrás (cada step contribuye ~0.707 al eje forward), suficiente para que el cubo esté visualmente fuera del foco antes de empezar a caer.

3. **`PathGenerator.TriggerFalls()`** nuevo paso en `Update`:
   - Recorre `_segments` (la Queue) en orden — el `foreach` sobre `Queue<T>` usa un enumerator struct, cero allocs por frame (CLAUDE §8 cumplido).
   - Por segmento, parte de `Segment.FallTriggerIndex` (watermark monotónico) y avanza hasta el primer cubo todavía adelante de la bola. Eso evita rescanear cubos ya disparados.
   - Para cada cubo detrás del threshold, llama `PlatformFaller.Begin()`. Idempotencia del `Begin()` cubre el caso raro de doble trigger.
   - Si un cubo está aún adelante (forwardOffset > -threshold), early-return — el progreso a lo largo del path es monotónico, no hace falta seguir mirando.

4. **`Segment.FallTriggerIndex`** — nuevo entero interno + `AdvanceFallTrigger()`. Watermark sobre los cubos ya procesados por `TriggerFalls`. Se resetea cuando el segment se descarta (el pool ya recicla el cubo, el watermark muere con el segmento).

5. **`PathGenerator.RecycleBehind` también barre gemas**: una vez por frame, después del Dequeue, `GemSpawner.ReleaseGemsBehind(ballPos, GlobalForward, BehindBuffer)` recoge cualquier gema no recogida que quedó detrás. Antes el gem pool podía crecer indefinidamente si el jugador esquivaba siempre las gemas.

6. **Build config mobile-portrait**:
   - `SampleScene` añadida al `EditorBuildSettings.scenes` con index 0 (única).
   - `PlayerSettings.companyName`/`productName` ajustados; resolución default 608 × 1080 (ratio 9:16, mobile portrait); fullscreen mode `Windowed`; orientación target `Portrait`. Version → `0.9`.

7. **Rebalanceo `_distanceMultiplier` `3 → 1`** — el score final se siente más legible con 1 punto/unit. Las gemas siguen siendo 1 coin/each (separadas en wallet).

8. **Audio assets importados** en `Assets/Audio/`: 3 SFX (`directionChange.wav`, `coinPickup.wav`, `gameOver.wav`) + 1 música de fondo (`music.mp3`). Wired en `AudioManager` slots existentes.

9. **Atlas de fuente regenerado** (`9a55183`) tras añadir glyphs nuevos al UI (texto `SHOP`, `EQUIPPED`, `+N coins`, etc.). Sin esto, los TMP texts mostrarían `□` (placeholder).

10. **`QualitySettings` migrados a `serializedVersion: 3`** (auto por Unity 2022.3 al abrir; commit del cambio explícito para que el repo no tenga drift).

### Decisiones técnicas

- **Caída hand-rolled, no Rigidbody.** Activar Rigidbody en cada cubo del pool añade overhead de PhysX por cubo (>50 cuerpos rígidos activos en cualquier frame), interfiere con el raycast de ground check de la bola (la bola podría detectar como "ground" un cubo cayendo) y obliga a desactivar `useGravity` y `isKinematic` con timing preciso al recycle. Hand-rolled: 3 floats, un `transform.position.y -=` y un cap, sin side effects.
- **Watermark `FallTriggerIndex` por segmento, no por cubo.** Alternativa: bool por cubo (`_hasBeenTriggered`). El watermark es O(1) para avanzar (un `++`) y O(1) para chequear si "ya pasé este cubo" (`i < watermark`). Bool por cubo pediría lookup y allocs de Dictionary o componente extra.
- **`MaxFallDistance = 60`** como red de seguridad — a 18 u/s², 60 unidades son ~2.5s de caída. Suficiente tiempo para que el pool recicle el cubo en flujo normal; si la run acaba justo antes del recycle, el cubo se queda quieto fuera de cámara en lugar de seguir integrando para siempre.
- **Gemas barridas una vez por frame, fuera del loop de segmentos**, para que la complejidad no se multiplique por el número de segmentos descartados en un frame ráfaga (en transiciones de menu → playing).
- **Build portrait 608×1080.** Ratio 9:16 ≈ iPhone moderno. El brief del test técnico no especificaba plataforma, pero el ZigZag original es mobile-first; un build de PC en portrait es la opción que mejor demuestra que el código está listo para target móvil sin tocar nada del input layer (el `InputHandler` ya mapea touch a mouse click vía Unity).
- **`_distanceMultiplier = 1`.** En playtest con 3 los scores subían a 5 cifras en 30 segundos; lectura ruidosa. Con 1, los hitos (50, 100, 200) coinciden con los palette swaps y el progreso se siente medible.

### Pendiente — setup manual en Unity

1. **Añadir `PlatformFaller` al prefab `P_PlatformCube`** (`Inspector → Add Component → Platform Faller`). Default `_gravity = 18`.
2. Verificar en `SO_GameConfig.asset` que `Platform Fall Start Behind = 1.5`.
3. Verificar slots del `AudioManager` apuntan a los clips importados.

### Verificación

- Play → la bola avanza → los cubos que quedan ~2 detrás se desploman hacia abajo, salen de la vista en ~1s. La bola nunca cae sobre un cubo que está cayendo (el threshold respeta el ground check).
- Profiler: cero `Instantiate` después del primer frame, cero allocs en `Update` del `PathGenerator`. El pool sigue estable en ~50-60 cubos activos.
- Build de Windows produce un `.exe` con resolución 608×1080 portrait. Audio suena en build, no solo en editor.
- HUD score sube ~1 punto por unit de progreso forward; los palette swaps caen exactamente cada 50 puntos.

### Próxima iteración (planteamiento)

Cerrar el deliverable: README (en/es), `.gitignore` verificado, capturas para la entrega.

### Addendum mismo día — música de fondo

Añadida música de fondo (`Assets/Audio/music.mp3`) **sin tocar código**. La música vive como un segundo `AudioSource` en el GameObject `Main Camera`, separada del que ya lleva el `AudioManager` (que sólo dispara SFX por `PlayOneShot`):

- `Audio Clip = music.mp3`.
- `Play On Awake = true` — la música arranca al cargar la escena, sin esperar al primer tap.
- `Loop = true` — track corto que cicla indefinidamente.
- `Volume = 0.036` (~3.6%) — punto bajo deliberado para que los SFX (flip, gema, game-over) sigan siendo el foco perceptual. Subirlo es un cambio de un valor en el inspector, no de código.
- `Spatialize = false`, `Spatial Blend = 0` — 2D pura, el listener (cámara) no genera paneo.
- Sin `OutputAudioMixerGroup` — el prototipo no usa AudioMixer todavía.

### Decisiones técnicas

- **`AudioSource` separado para música, no compartido con `AudioManager`.** El `AudioManager` usa `PlayOneShot` con volumen per-clip; mezclar música looping en el mismo source forzaría a cortar la música cada vez que suene un SFX (`PlayOneShot` no interrumpe lo que ya suena, pero el `clip` por defecto del source sí queda en juego para futuras `Play()`). Dos `AudioSource` en el mismo GameObject es la solución estándar de Unity y mantiene cada flujo de audio aislado.
- **Música en el `Main Camera`, no en un GameObject `Music` aparte.** La cámara ya lleva el `AudioListener` por defecto (cualquier `AudioSource` 2D sonará igual desde cualquier posición), y los `AudioSource` en el listener tienen lookup directo sin lookups extra. Para una música 2D global no aporta nada montar otro GameObject.
- **Sin script de fade-in / fade-out.** YAGNI hasta que el playtest lo pida. Si más adelante hace falta cortar la música al GameOver y volverla al Retry, se introducirá un `MusicController` mínimo que escuche `SO_OnGameOver` / `SO_OnGameReset` y ajuste `AudioSource.volume`.

### Pendiente — setup manual

Ninguno. El componente ya está en la escena y se serializa con `SampleScene.unity`. Si quieres rebalancear el mix, sólo edita el campo `Volume` del `AudioSource` de música en el Inspector del `Main Camera`.

---

## 2026-05-27 — Iteración 9: feedback de gema (burst de partículas + caída con plataforma)

### Objetivo

Cerrar dos huecos visibles tras los playtests de la iter 8:

1. **Recoger una gema no se sentía.** El pickup era un `SetActive(false)` instantáneo: la gema desaparecía sin transición y sólo quedaba el SFX. La memoria del jugador apenas registraba el evento.
2. **Las gemas se quedaban flotando** cuando el cubo que las soportaba colapsaba (`PlatformFaller`). El cubo caía, la gema se quedaba clavada en el aire — rompía el principio de "lo que el jugador ya pasó deja de existir" que la iter 8 había instalado.

Cero assets nuevos (ni VFX prefab, ni shaders nuevos): todo se construye en runtime desde código para mantener el repo lean.

### Lo que se ha implementado

1. **Burst procedural de partículas en `Gem`** (`Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs`):
   - `Awake` construye un `ParticleSystem` hijo (`PickupBurst`) con todos los módulos configurados por código: sphere shape (radius 0.05), `simulationSpace = World` (las partículas no siguen a la gema cuando se recicla), burst único de `_burstParticleCount = 18` partículas a `_burstSpeed = 4.5` u/s, `startLifetime = 0.45 s`, `gravityModifier = 0.4`, `size-over-lifetime` curva 1→0, `color-over-lifetime` gradiente alpha 1→0. Renderer en modo Billboard.
   - 5 campos `[SerializeField]` exponen el tuning (`_burstColor`, `_burstParticleCount`, `_burstLifetime`, `_burstSpeed`, `_burstParticleSize`) — todos con `[Range]` y tooltip. Defaults pensados para el sphere primitive a escala 0.4 del prefab.
   - `OnTriggerEnter` ahora: raise del evento existente, **desactiva `MeshRenderer.enabled` y `Collider.enabled`** (no `SetActive(false)`, porque desactivar el GO mataría el `ParticleSystem` antes de que reprodujera), `_burst.Play(true)`, arranca coroutine `ReleaseAfterBurst` que hace `WaitForSeconds(_burstLifetime)` antes de soltar al pool.
   - `Initialize(value, pool)` resetea estado defensivo: cancela coroutine pendiente, re-enable renderer/collider. Cubre el caso raro de que el pool re-sirva la gema antes de que termine el burst (pool stress).
   - `OnDisable` también para la coroutine y limpia el `ParticleSystem` (`StopEmittingAndClear`) — el pool desactiva la gema al hacer `Release`, así que sin esto un burst en curso al reciclar se quedaría flotando.
   - **Material compartido estático** (`_sharedBurstMaterial`) — una sola alocación de `Material` por sesión, no una por gema. Lazy init en el primer `Awake` que lo necesita. Búsqueda de shader con cascada de fallbacks: `Particles/Standard Unlit` → `Particles/Unlit` → `Mobile/Particles/Alpha Blended` → `Legacy Shaders/Particles/Alpha Blended` → `Sprites/Default`. Cualquier instalación de Unity 2022.3 tiene al menos uno.
   - `[RequireComponent(typeof(MeshRenderer))]` añadido — el script ya lo asumía implícitamente.

2. **Tracking de cubo-soporte en `GemSpawner`** (`Assets/Code/Runtime/Gameplay/Collectibles/GemSpawner.cs`):
   - Lista paralela `_supportCubes` (tamaño igual a `_activeGems`). En `TryPopulateSegment`, cuando se elige el cubo aleatorio del segmento, se guarda la referencia del cubo junto con la gema.
   - **Nuevo `LateUpdate`**: por cada gema activa, escribe `gem.position.y = supportCube.position.y + _config.GemHeightAboveCubeCenter`. `LateUpdate` (no `Update`) garantiza leer la posición del cubo **después** de que `PlatformFaller.Update` haya integrado la gravedad ese frame, así la gema y el cubo se mueven sincronizados sin lag de un frame.
   - Skip silencioso si `gem`, `cube` o cualquiera de los dos está `!activeSelf` — el cubo puede haberse reciclado a una posición lejana, no queremos que la gema le siga.
   - `RemoveTrackingAt(int i)` privado: pequeño helper que sincroniza `RemoveAt` entre las dos listas. Se llama desde los tres puntos donde antes había `_activeGems.RemoveAt(i)` (null, inactiva, behind-buffer). `HandleGameReset` añade `_supportCubes.Clear()` después del `_activeGems.Clear()`.

### Decisiones técnicas

- **`LateUpdate` con write a `transform.position`, no parenting cube→gem.** La opción obvia era `gem.transform.SetParent(cube.transform)`. Se descartó porque el cubo tiene `localScale = (1, 5, 1)` (no uniforme) y la gema viene con `rotation = (45, 0, 45)`. Unity no puede representar la combinación (rotación arbitraria + escala no uniforme del padre) en un `Vector3 localScale`; el resultado visual es una gema *deformada* — aplastada o estirada — el famoso bug de "shear" que sólo se nota cuando el padre no-uniforme entra en escena. Conducir sólo el Y por código evita el problema entero, y es O(N) trivial con N ≤ 50 gemas activas.
- **Read-after-faller-write garantizado por `LateUpdate`.** `PlatformFaller.Update` corre en la fase `Update`; `GemSpawner.LateUpdate` corre después en el mismo frame. La gema lee siempre la Y del cubo tras la integración de gravedad, sin frame de retraso visible.
- **Tracking O(N) en una lista paralela, no Dictionary.** N suele ser ≤ 30 gemas activas en cualquier momento. `Dictionary<GameObject, GameObject>` añadiría allocs de boxing-por-GetHashCode y overhead que la traversal lineal no tiene. Las dos `List<GameObject>` se mantienen en lock-step desde un único helper (`RemoveTrackingAt`), así el invariante de tamaños no se rompe.
- **Burst construido en `Awake` por código, no en el prefab.** Añadir el `ParticleSystem` al prefab `P_Gem` lo acopla al editor: cualquier cambio de tuning pediría editar el prefab y commitearlo. Con el burst en código, las propiedades expuestas en el inspector del componente `Gem` se aplican a cada Get del pool, y dos gemas distintas pueden coexistir con tunings diferentes sin variantes de prefab.
- **`MeshRenderer.enabled = false` + `Collider.enabled = false`, no `SetActive(false)`.** Si se desactiva el GO entero, el `ParticleSystem` hijo deja de simular y el burst se corta a medio fade. Apagar sólo los componentes "visibles" mantiene el sistema activo el tiempo justo para que `ReleaseAfterBurst` lo devuelva al pool.
- **Coroutine de release, no `Invoke("ReleaseToPool", lifetime)`.** Una coroutine es cancelable con `StopCoroutine` cuando `Initialize` se llama mientras hay un release pendiente (gema re-servida del pool antes de terminar). `Invoke` no es trivial de cancelar selectivamente.
- **Material compartido estático.** Cada `new Material(shader)` es una alocación que sobrevive la sesión. Compartirla entre todas las gemas significa que el palette tinting (el `_burstColor` del primer Awake) se aplica a todas — aceptado: el contrato del burst es "amarillo dorado uniforme", no skin-aware. Si hace falta tinting por gema se reintroduce `MaterialPropertyBlock` (`MPB`) — YAGNI por ahora.

### Pendiente — setup manual en Unity

Ninguno. El `ParticleSystem` se construye en `Awake` de cada gema, así que con sólo reabrir la escena las gemas existentes en el pool ya tienen su hijo `PickupBurst`. El prefab `P_Gem` no requiere cambios (sólo el `.prefab` recibió un bump menor de meta para registrar el `[RequireComponent(MeshRenderer)]`, ya cumplido por el sphere primitive).

### Verificación

- Play → recoger gema → la gema desaparece y un burst dorado se queda en el espacio durante ~0.45s, encogiendo y fundiendo a alpha 0. El SFX de coin sigue sonando exactamente en el mismo frame (sin lag).
- Recoger una gema en una plataforma que está colapsando → el burst se queda en el aire en el punto del pickup (simulación world-space), no se hunde con el cubo.
- Dejar pasar una gema sin recogerla → la plataforma colapsa → la gema cae con la plataforma a la misma velocidad, manteniendo su offset Y relativo, hasta que `RecycleBehind` la libere por estar fuera del `BehindBuffer`.
- Profiler: cero `Instantiate` en pickup (el burst se construye una vez en `Awake`); el `Material` aparece en el snapshot exactamente una vez (no `N`). `GemSpawner.LateUpdate` consume <0.05ms con 30 gemas activas.

### Próxima iteración (planteamiento)

Si entra otra sesión: trail renderer en la bola (la única feature de polish visual aún en backlog) o cierre definitivo del deliverable. Decisión a tomar al inicio.

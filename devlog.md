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

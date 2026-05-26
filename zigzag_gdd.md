# ZigZag — Game Design Document

> Documento de diseño para un prototipo desarrollado como **test técnico** para una posición Junior Game Developer. La arquitectura técnica detallada vive en `zigzag_architecture.md`.

---

## 1. Resumen ejecutivo

**Género:** Endless runner / arcade hipercasual.
**Modo:** Partida infinita con dificultad creciente.
**Plataforma:** PC (Windows), jugable con ratón simulando tap.
**Motor:** Unity **2022.3.62f2 LTS**, **Render Pipeline Built-in** (default del proyecto). // TODO: evaluar URP antes del vertical slice (ver `CLAUDE.md` §1).
**Lenguaje:** C# con .NET de Unity. **Código en inglés exclusivamente** (identificadores, comentarios, logs, nombres de asset, commits). Documentos de diseño en castellano para velocidad del autor.
**Plazo de desarrollo:** 2 semanas (sprint simulado).
**Referencia:** ZigZag de Ketchapp (Google Play).

**Pitch:** una bola avanza sola en zigzag sobre un camino estrecho que se genera infinitamente; un solo toque invierte su dirección; el reto es no caerse.

---

## 2. Contexto y restricciones del test

### 2.1 Encuadre

Este desarrollo simula un **sprint real** dentro de un estudio:
- El Producer pide implementar un juego concreto (ZigZag).
- El Artista impone usar **solo primitivas de Unity**.
- El equipo tiene 2 semanas.

### 2.2 Restricciones explícitas (del brief)

| Restricción | Implicación |
|---|---|
| Sin assets de la Asset Store | Todo material gráfico, sonoro y de código se crea o se obtiene fuera de la Store (CC0, propio) |
| Sin plugins externos | Solo APIs nativas de Unity. **Excluye:** DOTween, UniRx, Cinemachine si requiere paquete extra, Odin Inspector, etc. |
| Jugable con ratón | El click simula el tap móvil. Build de Windows es el target |
| Entregable: zip sin carpeta Library | Documentado en sección 17 |

### 2.3 División del trabajo en 2 semanas

| Semana | Foco | Resultado al final |
|---|---|---|
| **Semana 1** | Arquitectura + construcción funcional | Juego jugable de principio a fin. Visualmente austero pero **completo** y con código revisable |
| **Semana 2** | Pulido + entregable | Game feel, visuales, audio, tienda + skins, README, build final |

**Principio rector:** la semana 1 construye limpio desde el día uno. La semana 2 NO arregla código de la semana 1, solo añade polish encima.

---

## 3. High concept

El jugador no controla la velocidad ni la posición de la bola. Solo controla **cuándo girar**. Toda la tensión nace de esa restricción: un input, dos estados, decisiones de fracciones de segundo. La dificultad crece de forma orgánica al aumentar la velocidad con la distancia.

---

## 4. Pilares de diseño

Estos pilares son la vara de medir para cualquier decisión. Si una feature no refuerza uno de estos, no entra al prototipo.

1. **Un solo input.** Click izquierdo / tap. Sin gestos complejos, sin combos.
2. **Lectura instantánea.** En cualquier frame, el jugador entiende qué pasa: posición de la bola, dirección, dónde está el borde.
3. **Game feel sobre contenido.** Mejor un juego corto que se sienta excelente, que uno largo tibio.
4. **Determinismo razonable.** Misma seed → mismo camino. Para depurar y para que el jugador no sienta trampa.
5. **Código limpio y revisable.** Un senior debe poder leer el código sin dolor. Aplica en cada commit, no al final.

---

## 5. Mecánicas core

### 5.1 Movimiento de la bola

- Plano **XZ** (altura Y fija salvo al caer).
- Velocidad **constante** dentro de un frame, acelera con el tiempo.
- Dirección actual ∈ `{ (-1, 0, 0), (0, 0, 1) }` — ejes mundo puros, no diagonales 45°. La cámara isométrica rotada -45° Y proyecta esos ejes mundo como diagonales en pantalla, reproduciendo el visual zigzag del original Ketchapp. El path se construye con cubos alineados a los ejes mundo (giros de 90° en world space).
- **Bola cinemática:** sin Rigidbody con gravedad real. Justificación detallada en el documento de arquitectura.
- Caída simulada por código (`fallSpeed` propio) cuando ya no hay suelo.

### 5.2 Input

- **Una sola acción:** click izquierdo (ratón) o `Space` (debug en editor).
- Efecto: invierte la dirección actual. Cambio instantáneo, sin animación de giro.
- Sin doble-click, sin hold, sin swipe.

### 5.3 Generación del camino

- El camino se compone de **tramos** (segments) consecutivos.
- Cada tramo es una secuencia recta de N cubos en una de las dos diagonales.
- N aleatorio entre **3 y 8** cubos.
- Al terminar un tramo, el siguiente arranca desde el último cubo en la **otra** diagonal.
- El spawner mantiene siempre ~30 unidades de camino por delante de la bola.
- Cubos que quedan ~10 unidades por detrás se devuelven al pool.
- Generación con seed configurable para reproducibilidad.

### 5.4 Caída y muerte

- Si la bola sale del camino, su Y empieza a bajar según `fallSpeed`.
- Cuando `position.y < -2` → `GameOver`.
- Detección de "estoy en el camino": raycast corto hacia abajo cada frame.

### 5.5 Gemas

- Spawn: al generar un tramo, con probabilidad **30%**, una gema se coloca sobre un cubo aleatorio del tramo.
- Visual: `Cube` rotado 45° (queda como octaedro), material rosa con emission.
- Trigger collider. Al `OnTriggerEnter` con la bola: **+1 moneda** a la wallet persistente (no suma al score), partículas, vuelta al pool. El valor por gema (`_gemValue` en `GameConfigSO`) es configurable.
- No bloquean ni desvían a la bola.

### 5.6 Tienda + skins de bola (reemplaza al powerup imán originalmente planeado)

- **Decisión de scope:** el powerup imán se descopeó en favor de una **tienda de skins cosméticos**. Justificación: misma demostración de arquitectura extensible (catálogo de SOs + canales de eventos), con menos superficie de código y un loop de progresión jugable (recoger gemas → gastar coins → desbloquear cosméticos).
- **Acceso:** botón **SHOP** en el panel de Menu. El overlay suspende el tap de juego mediante canales `SO_OnShopOpened` / `SO_OnShopClosed`.
- **Catálogo:** 5 skins por defecto (`Default`, `Red`, `Green`, `Blue`, `Gold`). El default es gratis y está siempre owned; los demás cuestan coins.
- **Persistencia:** `PlayerPrefs` con las keys `OwnedSkins` (CSV) y `EquippedSkin`. Cada compra se persiste inmediatamente (no se difiere a un GameOver).
- **Aplicación al juego:** `BallSkinApplier` vive en la bola, escucha `SO_OnSkinEquipped` y hace swap del `sharedMaterial`.
- **Extensibilidad:** añadir un skin nuevo = crear un `BallSkinSO.asset` y arrastrarlo al `BallSkinCatalogSO`. Cero recompilaciones.

---

## 6. Parámetros de game feel — valores iniciales

Estos son **puntos de partida para tunear en semana 2**, no valores finales. Viven todos en un `ScriptableObject GameConfig`.

| Parámetro | Valor inicial | Notas |
|---|---|---|
| `initialSpeed` | 5 u/s | Velocidad al empezar |
| `acceleration` | 0.05 u/s² | Subida constante mientras juegas |
| `maxSpeed` | 12 u/s | Tope. Por encima es injugable |
| `fallSpeed` | 9.8 u/s | Solo cuando ya cayó del camino |
| `cubeSize` | (1, 0.3, 1) | Plataforma plana |
| `segmentMinLength` | 3 | Cubos por tramo (mín) |
| `segmentMaxLength` | 8 | Cubos por tramo (máx) |
| `gemSpawnProbability` | 0.30 | Por tramo, no por cubo |
| `gemValue` | 1 coin | Coins por gema (rebalanceado iter 4.1: 10 → 1) |
| `distanceMultiplier` | 1 pt / u forward | Rebalanceado iter 8: 3 → 1 |
| `aheadBuffer` | 30 u | Camino visible por delante |
| `behindBuffer` | 10 u | Antes de devolver al pool |
| `cameraFollowSmoothTime` | 0.15 s | Para `SmoothDamp` |
| `freezeFrameOnDeath` | 0.1 s | Pausa al morir |

---

## 7. Sistemas

### 7.1 Estados del juego

`enum GameState { Menu, Playing, GameOver }`

- **Menu:** escena cargada, bola visible quieta sobre el primer tramo, texto "Click to play".
- **Playing:** la bola se mueve, score sube, generación activa.
- **GameOver:** freeze frame, panel con score final + best + botón Retry.

### 7.2 Score y persistencia

Dos magnitudes separadas, persistidas independientemente.

#### 7.2.1 Score (solo distancia)

- **Cálculo:** proyección de la posición de la bola sobre el eje global forward `(-1,0,1)/√2`, multiplicada por `distanceMultiplier` y truncada con `Mathf.FloorToInt`.
- **Best score:** `PlayerPrefs.GetInt("BestScore", 0)` al cargar; se sobrescribe al entrar en `GameOver` si la run actual supera el anterior.
- Las gemas **no** contribuyen al score.

#### 7.2.2 Coins / Wallet (persistente)

- Cada gema otorga `_gemValue` monedas (default `1`) a la wallet.
- La wallet se persiste como `PlayerPrefs.GetInt("Coins", 0)` y se reescribe en cada pickup (robusto frente a cierre brusco).
- Las monedas no se gastan todavía — son la base para una tienda futura, fuera del scope de este sprint (ver §11, §12).
- Per-run counter (`SessionCoins`) se resetea en `GameReset` y se muestra en el panel GameOver como `"+N coins"`. La wallet total persiste.
- **No** hay perfiles ni saves multiusuario. Un único int por device para score, otro para wallet.

### 7.3 Pooling

- `UnityEngine.Pool.ObjectPool<GameObject>` (nativo desde Unity 2021).
- Pools separados para cubos y gemas.
- Tamaño inicial: 50 cubos, 20 gemas.
- **Obligatorio.** Sin `Instantiate` / `Destroy` en runtime.

---

## 8. Arte y visuales

### 8.1 Estilo

Flat shading minimalista. Sin texturas, solo materiales de color plano + emission puntual en gemas y trail.

### 8.2 Paleta sugerida

| Elemento | Color |
|---|---|
| Plataformas (alternadas) | `#5BA8E0` y `#3A7FB8` |
| Fondo | `#1A1A2E` |
| Bola | `#0A0A0A` |
| Gemas | `#E91E63` con emission `#FF4081` |
| Trail | Degradado rosa → transparente |
| UI texto | `#FFFFFF` |
| Skins de bola | `Default`, `Red`, `Green`, `Blue`, `Gold` (materiales individuales en `Assets/Art/`) |
| Paleta cíclica (iter 7) | Sampleo HSV complementario cada 50 puntos — el `#5BA8E0` de plataformas es solo el color de boot |

### 8.3 Primitivas

- Plataformas: `Cube` escalado.
- Bola: `Sphere`.
- Gemas: `Cube` rotado 45° en X y Z (octaedro visual).
- Iluminación: una `Directional Light` + ambient. Sin sombras realistas.

### 8.4 Cámara

- **Ortográfica.**
- Rotación fija `(30, -45, 0)` → vista isométrica.
- Sigue a la bola en XZ con `Vector3.SmoothDamp`. No se hace hija de la bola.
- `orthographicSize ≈ 6`.

### 8.5 Efectos (semana 2)

- `TrailRenderer` en la bola, vida ~0.5s, color rosa fade.
- `ParticleSystem` burst al coger gema.
- `ParticleSystem` burst al caer.
- Freeze frame de 0.1s al morir.

---

## 9. Audio

Tres SFX, sin música. Fuentes: freesound.org (CC0) o generados con sfxr/jsfxr.

| Evento | Tipo |
|---|---|
| Click (cambio de dirección) | Click corto, ~50ms |
| Gema recogida | Tintineo agudo, ~200ms |
| Muerte | Impacto seco grave, ~300ms |

---

## 10. UI / UX

### 10.1 Pantalla Menu

- Texto grande: **"ZIGZAG"**.
- Texto medio parpadeando: **"Click to play"**.
- Esquina superior derecha: **"Best: XX"**.
- Fondo: escena del juego ya generada, bola quieta.

### 10.2 HUD durante partida

- Esquina superior izquierda: **score actual** grande (solo distancia).
- Esquina superior derecha: **best score** pequeño.
- Coins (wallet persistente) visible en una esquina secundaria (ej. inferior izquierda) con icono o etiqueta `Coins: N`.
- (Sin powerups en este prototipo — el slot se reasignó a la tienda de skins en el Menu, ver §5.6.)

### 10.3 Panel Game Over

- Panel semi-transparente sobre escena congelada.
- **"GAME OVER"**.
- **"Score: XX"** (distancia final).
- **"Best: YY"**. Si hubo nuevo récord, "¡Nuevo récord!" en rosa.
- **"+N coins"** — monedas ganadas en la run que acaba de terminar (la wallet total persiste sin necesidad de mostrarse aquí; se ve en el HUD durante la próxima run).
- Botón **"RETRY"**.

Todo con TextMeshPro. Fades simples, sin animaciones complejas.

---

## 11. Decisiones de diseño justificadas

Estas son decisiones que el revisor verá y debe entender el porqué. Las técnicas detalladas viven en el documento de arquitectura; aquí las de **diseño de juego**.

| Decisión | Justificación |
|---|---|
| Modo infinito en lugar de niveles | Es lo que es ZigZag realmente. La generación procedural demuestra capacidad técnica relevante para el rol |
| Tienda + skins en lugar de powerup imán | Decisión tomada en iter 5: la tienda demuestra extensibilidad (catálogo de SOs + canales de eventos) con menos código y entrega un loop de progresión jugable que el imán no tenía |
| Sin tutorial | Si necesita explicación, el diseño falla en el pilar #2 (lectura instantánea) |
| Tienda y gasto de currency fuera de scope este sprint | Las gemas otorgan currency persistente (`CoinsWallet`, key `"Coins"`), lista para una tienda futura. El gasto, los items y la UI de shop entran en una iteración posterior. Demuestra arquitectura extensible sin invertir tiempo en features que el test no requiere |
| Cámara ortográfica, no perspectiva | Coincide con el original. Perspectiva añadiría problemas de oclusión |
| Bola siempre del mismo color | El estilo visual prima sobre la personalización |
| Score = distancia + gemas, sin multiplicadores | Lectura simple para el jugador. Multiplicadores son ruido en un prototipo |

---

## 12. Fuera de alcance

Lista explícita para resistir scope creep:

- Menú principal con settings, créditos, idiomas.
- Powerups (imán u otros — descopeado en iter 5; ver §5.6).
- Logros, leaderboards online, perfiles, nombres.
- Tutorial, onboarding.
- Música.
- Anuncios, IAP.
- Niveles discretos.
- Multijugador.
- Localización (todo en inglés en UI por simplicidad internacional, o todo en español, decisión a tomar antes del día 1).
- Animaciones complejas de la bola.
- Sistema de logros / objetivos diarios.

---

## 13. Criterios de éxito

### 13.1 Final de Semana 1 (build "functional")

- [ ] Loop completo Menu → Playing → GameOver → Retry funciona.
- [ ] Camino se genera infinitamente sin frame drops.
- [ ] Bola responde al click dentro de 1 frame.
- [ ] Hay aceleración progresiva.
- [ ] Score actual y best score se muestran y persisten.
- [ ] Gemas se recogen y dan puntos.
- [ ] Tienda de skins funciona (compra con coins, equipa, persiste).
- [ ] Sin `Instantiate`/`Destroy` en runtime.
- [ ] Estructura `Assets/Code/Runtime/...` y asmdefs creados según `zigzag_architecture.md` §4–§5.
- [ ] Código revisable: nombres claros, sin magic numbers, eventos C# bien suscritos/desuscritos.

### 13.2 Final de Semana 2 (build entregable)

- [ ] Todo lo anterior +
- [ ] Trail visible detrás de la bola.
- [ ] Partículas en eventos clave.
- [ ] Cámara con `SmoothDamp` configurado correctamente.
- [ ] 3 SFX funcionando.
- [ ] Game feel tuneado: alguien ajeno juega 3 partidas seguidas voluntariamente.
- [ ] 5–10 tests básicos EditMode/PlayMode pasando (ver `zigzag_architecture.md` §12).
- [ ] README.md completo (ver sección 17).
- [ ] Build de Windows compila sin warnings críticos.
- [ ] El proyecto se abre limpio en Unity 2022.3.62f2 sin errores en consola.

---

## 14. Roadmap detallado

### Semana 1 — Construcción

| Día | Foco | Entregable interno |
|---|---|---|
| 1 | Setup del proyecto + arquitectura base | Estructura de carpetas, `GameConfig` SO, `GameEvents` static class, escena vacía |
| 2 | Core movimiento + input | Bola se mueve y gira sobre camino estático puesto a mano |
| 3 | Generación procedural + pooling | Camino infinito, sin frame drops |
| 4 | Gemas + score + persistencia | Loop básico: recoges gemas, score sube, best se guarda |
| 5 | Estados del juego (Menu / Playing / GameOver) + UI mínima | Loop completo Menu → GameOver → Retry funcional |
| 6 | Tienda + skins (reemplaza al powerup imán) | Catálogo de skins, `SkinInventory` con persistencia, overlay de shop en el Menu |
| 7 | Buffer / testing / fix de bugs de semana 1 | Build interno "functional" |

### Semana 2 — Pulido

| Día | Foco | Entregable interno |
|---|---|---|
| 8 | Cámara con SmoothDamp + Trail Renderer | El movimiento "se siente bien" |
| 9 | Freeze frame al morir + rolling visual + paleta cíclica | Feedback visual completo |
| 10 | Tuning de game feel (velocidad, aceleración, anchos) | Curva de dificultad ajustada |
| 11 | Audio (3 SFX) + ajustes finales de UI | Build interno "polished" |
| 12 | README.md + documentación | Documentación completa |
| 13 | Testing exhaustivo + bugfixes | Build candidato a entregar |
| 14 | Build final + preparación del zip | Zip listo para enviar |

---

## 15. Decisiones cerradas (no re-debatir)

Para evitar dudas a mitad de sprint:

- **Unity 2022.3.62f2 LTS** (versión del brief).
- **Render Pipeline Built-in** (default del proyecto). URP solo se evaluaría post-prototipo.
- Bola **kinemática**, sin Rigidbody con gravedad real.
- Cámara **ortográfica isométrica fija**, sigue con SmoothDamp, no hija.
- Pooling con `UnityEngine.Pool.ObjectPool<T>` nativo.
- Persistencia con `PlayerPrefs`.
- Input con `Input.GetMouseButtonDown(0)` abstraído en `InputHandler`. **No** new Input System.
- Comunicación entre sistemas: **híbrido** — `GameEventSO` (ScriptableObject Event Channels) para eventos globales + `event Action<T>` C# para eventos locales. **No** UnityEvents, **no** estáticos. Detalle en `zigzag_architecture.md` §6 / ADR-004 / ADR-010.
- Configuración en `ScriptableObject GameConfigSO` con propiedades read-only (encapsulación obligatoria).
- Estructura `Assets/Code/Runtime/...` con una `.asmdef` por carpeta + `ZigZag.Editor` y `ZigZag.Tests.*` aisladas.
- Una sola escena (`S_Main.unity`).
- Tienda de skins (reemplaza al powerup imán que estaba originalmente planeado; ver §5.6). Catálogo de `BallSkinSO` + `SkinInventory` con persistencia PlayerPrefs.
- Modo infinito (no niveles).
- Target build: PC Windows.
- **Idioma:** código en inglés (identificadores, comentarios, logs, commits). Docs de diseño (`zigzag_gdd.md`, `zigzag_architecture.md`) en castellano.

Si surge tentación de cambiar una de estas, releer esta sección antes de tocar nada.

---

## 16. Riesgos identificados

| Riesgo | Mitigación |
|---|---|
| Scope creep | Sección 12 + revisión diaria de "¿esto entra en alcance?" |
| Tuning de game feel se subestima | Reservar semana 2 entera, no recortarla |
| Pooling mal implementado | Hacerlo el día 3, no posponerlo |
| Detección de "estoy en el camino" inestable | Empezar con raycast simple, no complicar |
| Sobreingeniería en arquitectura | Solo abstraer ejes de cambio reales (config, generación, cosméticos, input). Resto, directo |
| Audio consume tiempo excesivo | Cap duro de 2h buscando/generando SFX |
| Build de Windows falla a última hora | Buildear desde el día 5, no esperar al 14 |
| Olvido de desuscribir eventos → memory leaks | Regla: `OnEnable` += / `OnDisable` -=. Sin excepciones (ver documento de arquitectura) |
| Versión de Unity equivocada | Verificar 2022.3.62f2 antes de cualquier commit serio |

---

## 17. Entregable

### 17.1 Contenido del zip

- Proyecto Unity completo **sin carpeta `Library/`**.
- `README.md` en la raíz del proyecto.
- Carpeta `Builds/` con build de Windows (opcional pero recomendado).

### 17.2 Estructura del README.md

```markdown
# ZigZag — Prototipo Junior Test

## Cómo abrir el proyecto
- Versión de Unity: 2022.3.62f2
- Escena principal: Assets/Scenes/Main.unity

## Cómo jugar
- Click izquierdo o Space: cambiar dirección
- No te caigas del camino
- Recoge gemas (rosa) — cada una suma 1 moneda a tu wallet persistente
- Visita la tienda (botón **SHOP** del menú) para gastar las monedas en skins de bola

## Decisiones técnicas principales
[Resumen + link al documento de arquitectura]

## Estructura del proyecto
[Árbol de carpetas comentado]

## Qué haría a continuación si tuviera más tiempo
[Honestidad sobre limitaciones del prototipo]
```

La última sección es **importante** para un test: muestra autocrítica y visión de producto.

### 17.3 Checklist pre-envío

- [ ] Carpeta `Library/` eliminada.
- [ ] Carpeta `Temp/` eliminada.
- [ ] Carpeta `Logs/` eliminada.
- [ ] Carpeta `obj/` eliminada.
- [ ] `.vs/`, `.idea/` eliminadas.
- [ ] Tamaño del zip razonable (<50 MB esperable).
- [ ] El zip se descomprime y abre limpio en Unity 2022.3.62f2.
- [ ] La escena se ejecuta sin errores en consola.
- [ ] README presente y completo.
- [ ] Sin assets de Asset Store ni plugins externos.

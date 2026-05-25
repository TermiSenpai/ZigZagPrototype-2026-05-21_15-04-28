# Split: coins de gemas ↔ score por distancia

**Fecha:** 2026-05-23
**Iteración asociada:** 4.1 (incremento sobre iteración 4)
**Autor:** Brainstorm sesión 2026-05-23.

---

## 1. Contexto

Estado actual (iteración 4): `ScoreManager` combina **dos fuentes** en un único `CurrentScore` (`int`) que se persiste como `BestScore` en `PlayerPrefs`:

- `_gemScore` — incrementado al raise de `SO_OnGemCollected` con el `Gem.Value`.
- `_distanceScore` — proyección de la posición del ball sobre el eje global forward `(-1,0,1)/√2`.

`Gem` raises `SO_OnGemCollected(int value)` al `OnTriggerEnter` con la bola.

El GDD §11 y §12 cerraron explícitamente "Sin sistema de monedas / shop — fuera de scope". Este spec **reabre parcialmente esa decisión**: introducimos **wallet persistente** de monedas (currency) pero **sin tienda** — el gasto y la UI de shop quedan fuera, listos para una iteración futura.

---

## 2. Objetivo

Separar conceptualmente y en el código las dos magnitudes:

- **Score** = solo distancia recorrida.
- **Coins** = currency persistente acumulada al recoger gemas, **1 gema = 1 moneda** por defecto.

El score `BestScore` solo refleja distancia. Las coins persisten entre runs como wallet futura para una tienda. Las coins acumuladas **no pueden gastarse** todavía — no hay tienda, no hay items, no hay `Spend()` API.

---

## 3. Restricciones del usuario

1. **Sin renames.** `ScoreManager`, `CurrentScore`, `BestScore`, `SO_OnScoreChanged`, `SO_OnBestScoreChanged` y la PlayerPrefs key `"BestScore"` se mantienen — solo cambia su semántica (ahora reflejan solo distancia).
2. **`Gem` se mantiene** como nombre del collectible. Lo que otorga es ahora "coins", no "puntos".
3. **Tienda fuera de scope.** Solo se prepara la infraestructura (wallet, persistencia, HUD).
4. **1 gema = 1 moneda** como regla por defecto, configurable vía `GameConfigSO._gemValue`. Esta tunabilidad permite que powerups futuros (ej. multiplicador temporal) la modifiquen sin tocar código de gameplay — ver §10 future considerations.

---

## 4. Arquitectura propuesta

### 4.1 Nuevo componente: `CoinsWallet`

**Ubicación:** `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs`
**Asmdef:** `ZigZag.Runtime.Gameplay` (existente; nueva sub-feature `Economy`).
**Tipo:** `[DisallowMultipleComponent] sealed class CoinsWallet : MonoBehaviour`.

**Responsabilidad:** acumular monedas ganadas en la run actual y la wallet persistente entre runs. Es el único punto del proyecto que toca la PlayerPrefs key `"Coins"`.

**Dependencias (Inspector):**
- `IntGameEventSO _onGemCollected` (inbound — se suma payload a la wallet y a la session).
- `GameEventSO _onGameReset` (inbound — resetea `SessionCoins`; **no** toca `TotalCoins`).
- `IntGameEventSO _onCoinsChanged` (outbound — total wallet).
- `IntGameEventSO _onSessionCoinsChanged` (outbound — coins de esta run).

**API pública:**
```csharp
public int TotalCoins { get; private set; }
public int SessionCoins { get; private set; }
```

Sin `Spend(int)` — fuera de scope. Cuando se implemente la tienda se añadirá con su validación de fondos suficientes y su evento `SO_OnCoinsChanged` correspondiente.

**Ciclo de vida:**
- `Awake` → `TotalCoins = PlayerPrefs.GetInt("Coins", 0)`. `Debug.Assert` sobre cada ref serializada.
- `OnEnable` → registra handlers; `OnDisable` → desregistra.
- `Start` → broadcast inicial de `_onCoinsChanged.Raise(TotalCoins)` y `_onSessionCoinsChanged.Raise(0)` para pintar el HUD desde el menú.
- `HandleGemCollected(int value)` → `TotalCoins += value; SessionCoins += value; PlayerPrefs.SetInt + Save; raise ambos eventos`.
- `HandleGameReset()` → `SessionCoins = 0; raise _onSessionCoinsChanged.Raise(0)`. `TotalCoins` intacto.

**Persistencia en cada pickup, no en GameOver:**
`PlayerPrefs.SetInt + Save` es barato; un cierre brusco a media run (alt-F4, crash del editor) no debe robar coins al jugador. Decisión consistente con la dirección estratégica: las coins son currency real, el score es un número volátil.

### 4.2 Modificación: `ScoreManager`

Borra todo lo relacionado con gemas:
- Elimina campo `_onGemCollected` (serializado).
- Elimina campo privado `_gemScore`.
- Elimina handler `HandleGemCollected`.
- `RecomputeAndBroadcast` deja de sumar: `int total = _distanceScore`.

Mantiene:
- Nombres `CurrentScore`, `BestScore`, eventos, PlayerPrefs key `"BestScore"`.
- Documentación XML actualizada: "Tracks the current run's distance score and persists the all-time best..."

### 4.3 Modificación: `GameConfigSO`

- `_gemValue` default `10` → `1`. Tooltip actualizado: "Coins awarded per gem collected. Powerups may temporarily override this multiplier at runtime — see GDD §5.5."
- Sin renames de campos ni propiedades.

### 4.4 Modificación: `UIController`

Nuevos campos serializados:
- `[SerializeField] private TextMeshProUGUI _hudCoinsText` — HUD durante partida, muestra **coins de la run actual** con formato `+{SessionCoins}`.
- `[SerializeField] private TextMeshProUGUI _gameOverTotalCoinsText` — panel GameOver, muestra **total wallet persistente** con formato `Coins: {TotalCoins}`.
- `[SerializeField] private IntGameEventSO _onCoinsChanged`.
- `[SerializeField] private IntGameEventSO _onSessionCoinsChanged`.

Nuevos handlers (mismo patrón que el resto):
- `HandleSessionCoinsChanged(int session)` → `_hudCoinsText.text = $"+{session}"`.
- `HandleCoinsChanged(int total)` → `_gameOverTotalCoinsText.text = $"Coins: {total}"`.

Suscritos en `OnEnable`, desuscritos en `OnDisable`. Asserts en `Awake`.

**Justificación del reparto:** el HUD enseña "lo que llevas ganado en esta run" (motivación de seguir jugando, lectura inmediata para el jugador); el GameOver enseña "lo que tienes acumulado para la tienda" (preview del balance que podrá gastarse). El run-counter en HUD se resetea automáticamente al `_onGameReset` porque `CoinsWallet` raise `_onSessionCoinsChanged(0)`.

### 4.5 Modificación: `GameBootstrap`

Añadir ref serializada `[SerializeField] private CoinsWallet _coinsWallet` + `Debug.Assert` correspondiente.

### 4.6 `Gem.cs`

Sin cambios funcionales. Comentario XML aclara que `Value` representa coins, no puntos de score.

---

## 5. Nuevos assets

- `Assets/Settings/Events/SO_OnCoinsChanged.asset` (`IntGameEventSO`).
- `Assets/Settings/Events/SO_OnSessionCoinsChanged.asset` (`IntGameEventSO`).
- En escena: GameObject `CoinsWallet` con el componente, wires del Inspector.
- En Canvas HUD: TMP `+0` (run counter) en esquina secundaria al score actual.
- En Canvas GameOver: TMP `Coins: 0` (total wallet) debajo del score final.
- En `SO_GameConfig`: bajar `_gemValue` 10 → 1.
- En `GameBootstrap`: arrastrar `CoinsWallet` al nuevo slot.

---

## 6. Flujo de eventos resultante

**Recogida de gema:**
```
Bola ↦ Gem.OnTriggerEnter
  └─> Gem releases self to pool
  └─> SO_OnGemCollected.Raise(Value)    // Value default 1
       └─> CoinsWallet.HandleGemCollected(1)
            ├─> TotalCoins += 1; SessionCoins += 1
            ├─> PlayerPrefs.SetInt("Coins", TotalCoins) + Save
            ├─> SO_OnCoinsChanged.Raise(TotalCoins)
            │    └─> UIController updates GameOver "Coins: N" (no-op si no está visible)
            └─> SO_OnSessionCoinsChanged.Raise(SessionCoins)
                 └─> UIController updates HUD "+N"
```

`ScoreManager` ya **no** está suscrito a `SO_OnGemCollected`.

**Game Reset:**
```
Retry pressed
  └─> SO_OnGameReset.Raise()
       ├─> ScoreManager.HandleGameReset  (resetea distance score, igual que antes)
       ├─> CoinsWallet.HandleGameReset
       │    ├─> SessionCoins = 0
       │    └─> SO_OnSessionCoinsChanged.Raise(0)
       │         (resetea el HUD "+0"; TotalCoins intacto, el GameOver no se actualiza porque _onCoinsChanged no se raise)
       └─> GemSpawner, PathGenerator, UIController ... (sin cambios)
```

**Game Over:**
Sin cambios para coins (la wallet ya está persistida en cada pickup). `ScoreManager.SaveBestIfHigher` solo compara distance score.

---

## 7. Migración

- PlayerPrefs key `"BestScore"` queda intacta. Valores pre-iteración pueden contener combinado (score+gemas) ligeramente inflado. Sin cleanup automático.
- Si se quiere reset limpio, manual desde el editor: `Edit → Clear All PlayerPrefs` o eliminación selectiva. Decisión: no se incluye código de migración (prototipo, sin jugadores reales).

---

## 8. Documentación a actualizar

- **`zigzag_gdd.md`**:
  - §5.5 — aclarar que "gemas otorgan monedas; las monedas son currency persistente, no entran en el score".
  - §7.2 — separar en `7.2.1 Score (distancia)` y `7.2.2 Coins (wallet persistente)`. Actualizar las viñetas que hablan de "score = distancia + gemas".
  - §10.2 — HUD muestra `Score: X` y `Coins: Y`.
  - §10.3 — GameOver muestra distancia final + `+N coins`.
  - §11 — reformular la fila "Sin sistema de monedas / shop": "Tienda fuera de scope este sprint; gemas otorgan currency persistente como base para una tienda futura".
  - §12 — quitar "Sin sistema de monedas" o ajustar a "Sin tienda / sin items / sin gasto de currency".
- **`zigzag_architecture.md`**:
  - §6.2 — añadir filas `SO_OnCoinsChanged` y `SO_OnSessionCoinsChanged` al catálogo.
  - §7 — nueva sub-sección `7.17 CoinsWallet`.
  - Nueva **ADR-013** — "Wallet de coins separada del score. PlayerPrefs por pickup."
- **`devlog.md`**: nueva entrada `## 2026-05-23 — Iteración 4.1: split gem coins ↔ distance score`.

---

## 9. Tests

- **EditMode** existentes (`ScoreCalculator`) siguen pasando sin cambios — el cálculo de distancia no se altera.
- **No** se añaden tests para `CoinsWallet`. Razón: la lógica actual es `+=` + `PlayerPrefs.SetInt`, sin invariantes que merezcan cobertura. Cuando se introduzca `Spend(int)` con validación de fondos, ese método sí merece tests.

---

## 10. Future considerations (no implementar ahora)

Notas para evitar reabrir este spec cuando vuelva el tema:

- **Powerup multiplicador de coins (planeado por el usuario):** un powerup futuro podría duplicar/triplicar el valor de cada gema durante T segundos. Implementación esperada: el powerup llama `_config.SetGemValueOverride(int newValue, float duration)` o expone una API en el `Gem` directamente. Como el `Gem.Value` se setea por `Initialize(int, GemPool)` en cada `Get` del pool, lo correcto es que `GemSpawner.TryPopulateSegment` consulte un servicio de modificadores activos al asignar el value en lugar de leer `_config.GemValue` raw. **Diseñar cuando llegue el primer powerup que lo necesite, no antes.**
- **Tienda:** `Shop` panel con items que llaman `CoinsWallet.Spend(int)`. Requiere añadir `Spend` con guard `if (TotalCoins < amount) return false` y raise de `SO_OnCoinsChanged`. Probable adición de `SO_OnPurchaseSucceeded` / `SO_OnPurchaseFailed` para feedback de UI.
- **Migración de wallet a cloud save:** fuera de scope a perpetuidad para un prototipo de test técnico.
- **Anti-cheat de PlayerPrefs:** trivial editar `regedit` para regalarse coins. Asumimos jugador honesto — el alcance del prototipo no justifica obfuscación ni hashing.

---

## 11. Resumen de cambios físicos

**Archivos nuevos:**
- `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs`
- `Assets/Settings/Events/SO_OnCoinsChanged.asset` (manual en Unity)
- `Assets/Settings/Events/SO_OnSessionCoinsChanged.asset` (manual en Unity)

**Archivos modificados:**
- `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`
- `Assets/Code/Runtime/Data/GameConfigSO.cs` (sólo default y tooltip de `_gemValue`)
- `Assets/Code/Runtime/UI/UIController.cs`
- `Assets/Code/Runtime/Core/GameBootstrap.cs`
- `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs` (sólo doc XML)
- `Assets/Settings/SO_GameConfig.asset` (`_gemValue: 10 → 1`)
- `Assets/Scenes/SampleScene.unity` (wires nuevos)
- `zigzag_gdd.md`, `zigzag_architecture.md`, `devlog.md`

**Sin renames de tipos, campos serializados públicos, propiedades públicas ni PlayerPrefs keys existentes.**

---

## 12. Addendum 2026-05-24 — Swap HUD ↔ GameOver

Tras el primer playtest, intercambio semántico de las dos pantallas:

- **HUD** ahora muestra **coins de la run actual** (session), formato `+{N}`.
- **GameOver** ahora muestra **wallet total persistente**, formato `Coins: {N}`.

Razón: el HUD es contexto de progreso ("vas ganando esto"), el GameOver es contexto de balance ("tienes esto en total para gastar después en la tienda").

Cambios:
- `UIController._gameOverSessionCoinsText` → renombrado `_gameOverTotalCoinsText` con `[FormerlySerializedAs("_gameOverSessionCoinsText")]` para preservar el wire de escena.
- `HandleSessionCoinsChanged` ahora escribe en el HUD, `HandleCoinsChanged` en el GameOver.
- Format strings: HUD `+{N}` (sin la palabra "coins" para mantener el HUD limpio); GameOver `Coins: {N}`.

El nombre `_hudCoinsText` se mantiene — el campo describe **dónde** está el TMP, no qué número muestra. El nombre del field de GameOver sí cambia porque "session" en el nombre quedaba directamente engañoso.

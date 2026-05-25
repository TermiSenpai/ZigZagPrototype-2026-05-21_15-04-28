# Split: gem coins ↔ distance score — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate the single combined score into two independent magnitudes — `ScoreManager` becomes distance-only; a new `CoinsWallet` collects gems as persistent currency for a future shop.

**Architecture:** New `CoinsWallet` MonoBehaviour subscribes to `SO_OnGemCollected` and maintains both a per-run `SessionCoins` counter and a persistent `TotalCoins` wallet (PlayerPrefs key `"Coins"`, written on every pickup for crash-robustness). `ScoreManager` stops listening to gems and becomes a pure distance tracker; all its public names (`CurrentScore`, `BestScore`, `SO_OnScoreChanged`, PlayerPrefs key `"BestScore"`) stay intact — only their semantics change. Two new `IntGameEventSO` assets broadcast wallet state to the UI.

**Tech Stack:** Unity 2022.3.62f2 LTS, C# (Unity 2022.3 / .NET Standard 2.1), `UnityEngine.PlayerPrefs`, `IntGameEventSO` (existing typed event channel).

**Reference:** Design spec at `docs/superpowers/specs/2026-05-23-split-coins-and-distance-score-design.md`.

**Scope note:** Unity-side wiring (creating `.asset` files, GameObjects in the scene, dragging TMP refs, configuring `SO_GameConfig`) is **not** in these tasks — those require the Editor and are delivered to the user as a manual setup guide after the code lands.

**Testing note:** Per spec §9, no new automated tests. Existing `ScoreCalculator` EditMode tests must still pass after Task 2 — they cover the distance math, which is unchanged. Manual verification happens during the user's Unity setup pass.

---

## File Structure

**New files:**
- `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs` — single MonoBehaviour responsible for accumulating coins and persisting the wallet. Sole owner of PlayerPrefs key `"Coins"`. New sub-feature folder under the existing `ZigZag.Runtime.Gameplay` asmdef.

**Modified files (code):**
- `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs` — remove gem accumulation entirely; `CurrentScore` becomes distance only.
- `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs` — XML doc only (clarify `Value` is coins, not score points).
- `Assets/Code/Runtime/Data/GameConfigSO.cs` — `_gemValue` default `10 → 1`, tooltip rewording.
- `Assets/Code/Runtime/UI/UIController.cs` — add `_hudCoinsText`, `_gameOverSessionCoinsText`, two new event channel refs, two new handlers.
- `Assets/Code/Runtime/Core/GameBootstrap.cs` — add `_coinsWallet` ref + assert.

**Modified files (docs):**
- `zigzag_gdd.md` — §5.5, §7.2, §10.2, §10.3, §11, §12.
- `zigzag_architecture.md` — §6.2 catalog, new §7.17, new ADR-013.
- `devlog.md` — new iteration 4.1 entry.

---

## Task 1: Create `CoinsWallet`

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs`

- [ ] **Step 1: Create the sub-feature folder and the source file**

Write the file with the following exact content:

```csharp
using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Economy
{
    /// <summary>
    /// Accumulates coins earned by collecting gems and persists the all-time
    /// wallet to <see cref="PlayerPrefs"/>. Sole owner of the <c>"Coins"</c> key —
    /// no other system reads or writes it.
    /// </summary>
    /// <remarks>
    /// Tracks two values: <see cref="TotalCoins"/> (the persistent wallet, the
    /// user's currency balance across all runs) and <see cref="SessionCoins"/>
    /// (coins earned in the current run, reset on <c>SO_OnGameReset</c> so the
    /// GameOver panel can display "+N coins" for the just-ended run).
    ///
    /// Persistence cadence: <c>PlayerPrefs.SetInt + Save</c> on every pickup.
    /// A run-mid crash (alt-F4, editor stop) must not steal coins from the
    /// player — they are currency, not a volatile score.
    ///
    /// There is intentionally no <c>Spend(int)</c> API. The shop / item system
    /// that will consume coins is out of scope for this sprint; when it lands,
    /// it adds the spend path with a fund-sufficient guard and raises
    /// <see cref="_onCoinsChanged"/> in the same way as pickup.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CoinsWallet : MonoBehaviour
    {
        private const string CoinsPrefKey = "Coins";

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: each raise adds the payload to both the wallet and the current session counter.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Listened-to: clears SessionCoins back to zero. TotalCoins is preserved across runs by design.")]
        private GameEventSO _onGameReset;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised whenever TotalCoins changes (wallet update). Subscribed by the HUD.")]
        private IntGameEventSO _onCoinsChanged;

        [SerializeField, Tooltip("Raised whenever SessionCoins changes. Subscribed by the GameOver panel to show \"+N coins\".")]
        private IntGameEventSO _onSessionCoinsChanged;

        public int TotalCoins { get; private set; }
        public int SessionCoins { get; private set; }

        private void Awake()
        {
            Debug.Assert(_onGemCollected != null, $"{nameof(CoinsWallet)} requires {nameof(_onGemCollected)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(CoinsWallet)} requires {nameof(_onGameReset)}.", this);
            Debug.Assert(_onCoinsChanged != null, $"{nameof(CoinsWallet)} requires {nameof(_onCoinsChanged)}.", this);
            Debug.Assert(_onSessionCoinsChanged != null, $"{nameof(CoinsWallet)} requires {nameof(_onSessionCoinsChanged)}.", this);

            TotalCoins = PlayerPrefs.GetInt(CoinsPrefKey, 0);
        }

        private void OnEnable()
        {
            if (_onGemCollected != null) _onGemCollected.Register(HandleGemCollected);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGemCollected != null) _onGemCollected.Unregister(HandleGemCollected);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        private void Start()
        {
            // Broadcast loaded wallet so the menu/HUD can paint before any run.
            _onCoinsChanged.Raise(TotalCoins);
            _onSessionCoinsChanged.Raise(SessionCoins);
        }

        private void HandleGemCollected(int value)
        {
            if (value <= 0) return;

            TotalCoins += value;
            SessionCoins += value;

            PlayerPrefs.SetInt(CoinsPrefKey, TotalCoins);
            PlayerPrefs.Save();

            _onCoinsChanged.Raise(TotalCoins);
            _onSessionCoinsChanged.Raise(SessionCoins);
        }

        private void HandleGameReset()
        {
            if (SessionCoins == 0) return;
            SessionCoins = 0;
            _onSessionCoinsChanged.Raise(SessionCoins);
            // TotalCoins intentionally not touched — wallet persists across runs.
        }
    }
}
```

- [ ] **Step 2: Verify file is syntactically valid**

Open the file (or re-Read it) and confirm: opening brace counts match, no stray characters, namespace path matches folder. No tooling-side verification is meaningful here — Unity will catch any compile error on next domain reload.

---

## Task 2: Refactor `ScoreManager` — remove gem accumulation

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs`

- [ ] **Step 1: Update the XML doc on the class**

Replace the existing `<summary>` block (lines 7–11) with:

```csharp
    /// <summary>
    /// Tracks the current run's distance score, persists the all-time best to
    /// <see cref="PlayerPrefs"/>, and broadcasts both via SO event channels so
    /// the UI never holds a reference to this component.
    /// </summary>
```

And replace the existing `<remarks>` block (lines 12–23) with:

```csharp
    /// <remarks>
    /// Distance is computed every frame from the ball's transform but only raises
    /// <c>SO_OnScoreChanged</c> when the integer total moves. The origin for
    /// distance is the path start position taken from
    /// <see cref="GameConfigSO.PathStartPosition"/>, so the progress axis
    /// matches what the <c>PathGenerator</c> uses for its buffers.
    ///
    /// Persistence: <c>PlayerPrefs.GetInt("BestScore", 0)</c> in <c>Awake</c>;
    /// <c>SetInt + Save</c> in <see cref="SaveBestIfHigher"/> (called on GameOver).
    /// ADR-003 in zigzag_architecture.md picks PlayerPrefs over a file-based
    /// store because a single int per device is the whole persistence story
    /// for the prototype.
    ///
    /// Gem pickups no longer contribute to the score — they are banked as
    /// persistent currency by <c>CoinsWallet</c>. See spec
    /// <c>docs/superpowers/specs/2026-05-23-split-coins-and-distance-score-design.md</c>.
    /// </remarks>
```

- [ ] **Step 2: Remove the serialized gem-collected field**

Delete lines 36–38 in the original file (the `_onGemCollected` block under "Event Channels (Inbound)"):

```csharp
        [SerializeField, Tooltip("Listened-to: each raise adds the payload to current score.")]
        private IntGameEventSO _onGemCollected;

```

The remaining inbound channels (`_onGameStarted`, `_onGameOver`, `_onGameReset`) stay.

- [ ] **Step 3: Remove the `_gemScore` field**

Delete the line:

```csharp
        private int _gemScore;
```

Keep `_distanceScore` and `_isTracking`.

- [ ] **Step 4: Remove the gem assert in `Awake`**

Delete the line:

```csharp
            Debug.Assert(_onGemCollected != null, $"{nameof(ScoreManager)} requires {nameof(_onGemCollected)}.", this);
```

- [ ] **Step 5: Remove the gem registration in `OnEnable`**

Delete the line:

```csharp
            if (_onGemCollected != null) _onGemCollected.Register(HandleGemCollected);
```

- [ ] **Step 6: Remove the gem unregistration in `OnDisable`**

Delete the line:

```csharp
            if (_onGemCollected != null) _onGemCollected.Unregister(HandleGemCollected);
```

- [ ] **Step 7: Remove the `HandleGemCollected` method entirely**

Delete the block:

```csharp
        private void HandleGemCollected(int gemValue)
        {
            _gemScore += gemValue;
            RecomputeAndBroadcast();
        }
```

- [ ] **Step 8: Update `HandleGameReset` to drop the `_gemScore` assignment**

Replace:

```csharp
        private void HandleGameReset()
        {
            _isTracking = false;
            _gemScore = 0;
            _distanceScore = 0;
            RecomputeAndBroadcast();
        }
```

with:

```csharp
        private void HandleGameReset()
        {
            _isTracking = false;
            _distanceScore = 0;
            RecomputeAndBroadcast();
        }
```

- [ ] **Step 9: Update `RecomputeAndBroadcast` to use distance only**

Replace:

```csharp
        private void RecomputeAndBroadcast()
        {
            int total = _gemScore + _distanceScore;
            if (total == CurrentScore) return;
            CurrentScore = total;
            _onScoreChanged.Raise(CurrentScore);
        }
```

with:

```csharp
        private void RecomputeAndBroadcast()
        {
            if (_distanceScore == CurrentScore) return;
            CurrentScore = _distanceScore;
            _onScoreChanged.Raise(CurrentScore);
        }
```

- [ ] **Step 10: Verify no orphan reference to `_gemScore` or `_onGemCollected` remains**

Run a grep for both symbols in the file:

```
rg -n "(_gemScore|_onGemCollected|HandleGemCollected)" Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs
```

Expected output: empty (no matches).

---

## Task 3: Update `GameConfigSO` — `_gemValue` default and tooltip

**Files:**
- Modify: `Assets/Code/Runtime/Data/GameConfigSO.cs`

- [ ] **Step 1: Change the `_gemValue` declaration**

Replace:

```csharp
        [SerializeField, Tooltip("Points awarded for each gem collected.")]
        private int _gemValue = 10;
```

with:

```csharp
        [SerializeField, Tooltip("Coins awarded per gem collected. Powerups may temporarily override this multiplier at runtime — see GDD §5.5.")]
        private int _gemValue = 1;
```

Note: the default value change here does **not** retroactively update an existing `SO_GameConfig.asset` (Unity stores the inspector value in the asset, not the C# default). The user's manual setup guide instructs them to set the field to `1` in the asset itself.

---

## Task 4: Update `Gem` XML doc

**Files:**
- Modify: `Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs`

- [ ] **Step 1: Replace the class `<summary>` block (lines 6–10)**

Replace:

```csharp
    /// <summary>
    /// A pickup that rewards the player with <see cref="Value"/> points. The instance
    /// is pooled; it returns itself to the pool on collection rather than being
    /// destroyed.
    /// </summary>
```

with:

```csharp
    /// <summary>
    /// A pickup that rewards the player with <see cref="Value"/> coins (banked by
    /// <c>CoinsWallet</c> as persistent currency, not added to the run score). The
    /// instance is pooled; it returns itself to the pool on collection rather than
    /// being destroyed.
    /// </summary>
```

---

## Task 5: Extend `UIController` with coin displays

**Files:**
- Modify: `Assets/Code/Runtime/UI/UIController.cs`

- [ ] **Step 1: Add the two new TMP fields under the "Score Display" header**

Locate the line `[SerializeField, Tooltip("GameObject toggled active when the just-ended run beat the previous best. Leave null if not used.")]` and immediately AFTER its serialized field `private GameObject _newRecordBadge;`, insert a new header block:

Replace the existing block (the four score-display fields ending in `_newRecordBadge`):

```csharp
        [Header("Score Display")]
        [SerializeField, Tooltip("HUD text showing the current run's score during Playing.")]
        private TextMeshProUGUI _hudScoreText;

        [SerializeField, Tooltip("GameOver panel text showing the final score of the just-ended run.")]
        private TextMeshProUGUI _gameOverFinalScoreText;

        [SerializeField, Tooltip("GameOver and Menu text showing the persisted best score.")]
        private TextMeshProUGUI _bestScoreText;

        [SerializeField, Tooltip("GameObject toggled active when the just-ended run beat the previous best. Leave null if not used.")]
        private GameObject _newRecordBadge;
```

with:

```csharp
        [Header("Score Display")]
        [SerializeField, Tooltip("HUD text showing the current run's distance score during Playing.")]
        private TextMeshProUGUI _hudScoreText;

        [SerializeField, Tooltip("GameOver panel text showing the final distance score of the just-ended run.")]
        private TextMeshProUGUI _gameOverFinalScoreText;

        [SerializeField, Tooltip("GameOver and Menu text showing the persisted best distance score.")]
        private TextMeshProUGUI _bestScoreText;

        [SerializeField, Tooltip("GameObject toggled active when the just-ended run beat the previous best. Leave null if not used.")]
        private GameObject _newRecordBadge;

        [Header("Coins Display")]
        [SerializeField, Tooltip("HUD text showing the persistent coin wallet during Playing and Menu.")]
        private TextMeshProUGUI _hudCoinsText;

        [SerializeField, Tooltip("GameOver panel text showing coins earned in the just-ended run, formatted as \"+N coins\".")]
        private TextMeshProUGUI _gameOverSessionCoinsText;
```

- [ ] **Step 2: Add the two new event-channel fields**

Locate the existing block ending with `private IntGameEventSO _onBestScoreChanged;` under `[Header("Event Channels (Inbound)")]` and replace the whole inbound block:

```csharp
        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Fires when the run starts; switches Menu → HUD.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Fires on death; switches HUD → GameOver.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Fires on retry; switches GameOver → HUD.")]
        private GameEventSO _onGameReset;

        [SerializeField, Tooltip("Listened-to: refreshes the HUD score text.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the best score text.")]
        private IntGameEventSO _onBestScoreChanged;
```

with:

```csharp
        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Fires when the run starts; switches Menu → HUD.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Fires on death; switches HUD → GameOver.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Fires on retry; switches GameOver → HUD.")]
        private GameEventSO _onGameReset;

        [SerializeField, Tooltip("Listened-to: refreshes the HUD distance score text.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the best distance score text.")]
        private IntGameEventSO _onBestScoreChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the HUD coin wallet text.")]
        private IntGameEventSO _onCoinsChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the GameOver \"+N coins\" text.")]
        private IntGameEventSO _onSessionCoinsChanged;
```

- [ ] **Step 3: Add asserts for the new refs in `Awake`**

Locate the existing assert block in `Awake` and append two new asserts after `_onBestScoreChanged`:

```csharp
            Debug.Assert(_onBestScoreChanged != null, $"{nameof(UIController)} requires {nameof(_onBestScoreChanged)}.", this);
```

becomes:

```csharp
            Debug.Assert(_onBestScoreChanged != null, $"{nameof(UIController)} requires {nameof(_onBestScoreChanged)}.", this);
            Debug.Assert(_hudCoinsText != null, $"{nameof(UIController)} requires {nameof(_hudCoinsText)}.", this);
            Debug.Assert(_gameOverSessionCoinsText != null, $"{nameof(UIController)} requires {nameof(_gameOverSessionCoinsText)}.", this);
            Debug.Assert(_onCoinsChanged != null, $"{nameof(UIController)} requires {nameof(_onCoinsChanged)}.", this);
            Debug.Assert(_onSessionCoinsChanged != null, $"{nameof(UIController)} requires {nameof(_onSessionCoinsChanged)}.", this);
```

- [ ] **Step 4: Subscribe in `OnEnable`**

Locate the existing block:

```csharp
            if (_onBestScoreChanged != null) _onBestScoreChanged.Register(HandleBestScoreChanged);
        }
```

and replace it with:

```csharp
            if (_onBestScoreChanged != null) _onBestScoreChanged.Register(HandleBestScoreChanged);
            if (_onCoinsChanged != null) _onCoinsChanged.Register(HandleCoinsChanged);
            if (_onSessionCoinsChanged != null) _onSessionCoinsChanged.Register(HandleSessionCoinsChanged);
        }
```

- [ ] **Step 5: Unsubscribe in `OnDisable`**

Locate the existing block:

```csharp
            if (_onBestScoreChanged != null) _onBestScoreChanged.Unregister(HandleBestScoreChanged);
        }
```

and replace it with:

```csharp
            if (_onBestScoreChanged != null) _onBestScoreChanged.Unregister(HandleBestScoreChanged);
            if (_onCoinsChanged != null) _onCoinsChanged.Unregister(HandleCoinsChanged);
            if (_onSessionCoinsChanged != null) _onSessionCoinsChanged.Unregister(HandleSessionCoinsChanged);
        }
```

- [ ] **Step 6: Add the two handler methods**

Find the `HandleBestScoreChanged` method. Immediately AFTER it (still inside the class), add:

```csharp
        private void HandleCoinsChanged(int totalCoins)
        {
            if (_hudCoinsText != null) _hudCoinsText.text = $"Coins: {totalCoins}";
        }

        private void HandleSessionCoinsChanged(int sessionCoins)
        {
            if (_gameOverSessionCoinsText != null) _gameOverSessionCoinsText.text = $"+{sessionCoins} coins";
        }
```

---

## Task 6: Extend `GameBootstrap` with `CoinsWallet` ref

**Files:**
- Modify: `Assets/Code/Runtime/Core/GameBootstrap.cs`

- [ ] **Step 1: Add the `using` for the Economy namespace**

Locate the using block at the top:

```csharp
using UnityEngine;
using ZigZag.Runtime.Gameplay.Collectibles;
using ZigZag.Runtime.Gameplay.Scoring;
using ZigZag.Runtime.Gameplay.World;
```

and replace it with:

```csharp
using UnityEngine;
using ZigZag.Runtime.Gameplay.Collectibles;
using ZigZag.Runtime.Gameplay.Economy;
using ZigZag.Runtime.Gameplay.Scoring;
using ZigZag.Runtime.Gameplay.World;
```

- [ ] **Step 2: Add the serialized field**

Locate the existing block of serialized refs ending with `_gemSpawner`:

```csharp
        [SerializeField, Tooltip("Scene's gem spawner.")]
        private GemSpawner _gemSpawner;
```

and immediately after, append:

```csharp
        [SerializeField, Tooltip("Scene's coins wallet.")]
        private CoinsWallet _coinsWallet;
```

- [ ] **Step 3: Add the assert in `Awake`**

Locate the line:

```csharp
            Debug.Assert(_gemSpawner != null, $"{nameof(GameBootstrap)} requires a {nameof(GemSpawner)} reference.", this);
```

and immediately after it, append:

```csharp
            Debug.Assert(_coinsWallet != null, $"{nameof(GameBootstrap)} requires a {nameof(CoinsWallet)} reference.", this);
```

---

## Task 7: Update `zigzag_gdd.md`

**Files:**
- Modify: `zigzag_gdd.md`

- [ ] **Step 1: Update §5.5 "Gemas"**

Locate the §5.5 block (around line 100):

```markdown
### 5.5 Gemas

- Spawn: al generar un tramo, con probabilidad **30%**, una gema se coloca sobre un cubo aleatorio del tramo.
- Visual: `Cube` rotado 45° (queda como octaedro), material rosa con emission.
- Trigger collider. Al `OnTriggerEnter` con la bola: +10 puntos, partículas, vuelta al pool.
- No bloquean ni desvían a la bola.
```

Replace the third bullet only:

```markdown
- Trigger collider. Al `OnTriggerEnter` con la bola: **+1 moneda** a la wallet persistente (no suma al score), partículas, vuelta al pool. El valor por gema (`_gemValue` en `GameConfigSO`) es configurable; powerups futuros podrán multiplicarlo temporalmente.
```

- [ ] **Step 2: Replace §7.2 "Score y persistencia" entirely**

Locate the §7.2 block:

```markdown
### 7.2 Score y persistencia

- **Distancia:** `Mathf.FloorToInt(ball.position.z) * distanceMultiplier`.
- **Gemas:** contador independiente × `gemValue`.
- **Score total:** suma de los dos.
- **Best score:** `PlayerPrefs.GetInt("BestScore", 0)` al cargar; se actualiza al entrar en `GameOver` si supera el anterior.
- **No** hay perfiles, leaderboards online ni historial. Un único int persistido.
```

Replace with:

```markdown
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
```

- [ ] **Step 3: Update §10.2 HUD durante partida**

Locate:

```markdown
### 10.2 HUD durante partida

- Esquina superior izquierda: **score actual** grande.
- Esquina superior derecha: **best score** pequeño.
- Si hay powerup activo: pequeño indicador con tiempo restante.
```

Replace with:

```markdown
### 10.2 HUD durante partida

- Esquina superior izquierda: **score actual** grande (solo distancia).
- Esquina superior derecha: **best score** pequeño.
- Coins (wallet persistente) visible en una esquina secundaria (ej. inferior izquierda) con icono o etiqueta `Coins: N`.
- Si hay powerup activo: pequeño indicador con tiempo restante.
```

- [ ] **Step 4: Update §10.3 Panel Game Over**

Locate:

```markdown
### 10.3 Panel Game Over

- Panel semi-transparente sobre escena congelada.
- **"GAME OVER"**.
- **"Score: XX"**.
- **"Best: YY"**. Si hubo nuevo récord, "¡Nuevo récord!" en rosa.
- Botón **"RETRY"**.

Todo con TextMeshPro. Fades simples, sin animaciones complejas.
```

Replace with:

```markdown
### 10.3 Panel Game Over

- Panel semi-transparente sobre escena congelada.
- **"GAME OVER"**.
- **"Score: XX"** (distancia final).
- **"Best: YY"**. Si hubo nuevo récord, "¡Nuevo récord!" en rosa.
- **"+N coins"** — monedas ganadas en la run que acaba de terminar (la wallet total persiste sin necesidad de mostrarse aquí; se ve en el HUD durante la próxima run).
- Botón **"RETRY"**.

Todo con TextMeshPro. Fades simples, sin animaciones complejas.
```

- [ ] **Step 5: Update §11 row "Sin sistema de monedas / shop"**

Locate the row:

```markdown
| Sin sistema de monedas / shop | Fuera del scope. Un test no se gana inventando features |
```

Replace with:

```markdown
| Tienda y gasto de currency fuera de scope este sprint | Las gemas otorgan currency persistente (`CoinsWallet`, key `"Coins"`), lista para una tienda futura. El gasto, los items y la UI de shop entran en una iteración posterior. Demuestra arquitectura extensible sin invertir tiempo en features que el test no requiere |
```

- [ ] **Step 6: Update §12 "Fuera de alcance"**

Locate the existing bullet list and find:

```markdown
- Anuncios, IAP.
```

Immediately above it, find/keep `Múltiples skins.` and ensure the list reflects the updated scope. Specifically, leave the rest unchanged, but no item in §12 currently says "sistema de monedas" — verify with a search:

Run:

```
rg -n "monedas" zigzag_gdd.md
```

Expected after the previous edits: only the references inside §5.5, §7.2.2, §11 and §10 we just edited (no occurrence inside §12). If any other "monedas" appears in §12, replace that bullet with `- Tienda con items y gasto de currency (la infraestructura de wallet sí está; la UI y los items no).`

---

## Task 8: Update `zigzag_architecture.md`

**Files:**
- Modify: `zigzag_architecture.md`

- [ ] **Step 1: Append two rows to §6.2 catalog table (Globales)**

Locate the table in §6.2 whose last row is:

```markdown
| `SO_OnPowerupExpired`       | `GameEventSO`       | `PowerupManager`             | `UIController`                                    |
```

Immediately after that row, append:

```markdown
| `SO_OnCoinsChanged`         | `IntGameEventSO`    | `CoinsWallet`                | `UIController` (HUD wallet display)               |
| `SO_OnSessionCoinsChanged`  | `IntGameEventSO`    | `CoinsWallet`                | `UIController` (GameOver `+N coins`)              |
```

- [ ] **Step 2: Add §7.17 `CoinsWallet`**

Locate §7.17 `GameBootstrap` (the last subsection of §7). Immediately BEFORE it, insert:

```markdown
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

```

(Note: the existing `GameBootstrap` subsection then renumbers to §7.18 conceptually — section numbers in this doc are informal and we do not re-number the others to avoid churn.)

- [ ] **Step 3: Add ADR-013**

Locate the end of §8 (after the existing `### ADR-012 — `sealed` por defecto en clases concretas` block, just before `## 9. SOLID aplicado (concreción)`). Insert:

```markdown
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
- Score y wallet evolucionan de forma independiente; añadir powerups que multipliquen coins (ver future considerations del spec) no toca `ScoreManager`.
- Dos PlayerPrefs keys (`"BestScore"`, `"Coins"`) en lugar de una. Trivial.
- Cuando llegue la tienda, `CoinsWallet` añade `bool TrySpend(int)` con guard de fondos suficientes, sin tocar el resto del sistema.
```

---

## Task 9: Append iteration 4.1 entry to `devlog.md`

**Files:**
- Modify: `devlog.md`

- [ ] **Step 1: Append a new entry at the end of the file**

Locate the end of the file (after the iteration-4 entry's "Próxima iteración" paragraph). Append:

```markdown

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

6. **Docs**: GDD §5.5, §7.2 (separada en 7.2.1 score y 7.2.2 coins), §10.2, §10.3, §11, §12; arquitectura §6.2, nuevo §7.17 `CoinsWallet`, nuevo ADR-013 "Wallet separada del score, persistida por pickup".

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
```

---

## Task 10: Commit

**Files:**
- All modified/created files from tasks 1–9 + `Assets/Code/Runtime/Gameplay/Economy/.meta` (auto-generated by Unity on next domain reload; do **not** create manually).

- [ ] **Step 1: Stage code, design docs, and spec/plan**

```
git add Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs Assets/Code/Runtime/Gameplay/Scoring/ScoreManager.cs Assets/Code/Runtime/Data/GameConfigSO.cs Assets/Code/Runtime/Gameplay/Collectibles/Gem.cs Assets/Code/Runtime/UI/UIController.cs Assets/Code/Runtime/Core/GameBootstrap.cs zigzag_gdd.md zigzag_architecture.md devlog.md docs/superpowers/specs/2026-05-23-split-coins-and-distance-score-design.md docs/superpowers/plans/2026-05-23-split-coins-and-distance-score.md
```

Do NOT stage `Assets/Scenes/SampleScene.unity` or `Assets/Settings/SO_GameConfig.asset` — those are user-side Unity edits (HUD wiring, gem value tweak, new component refs). They get their own commit after the user runs the manual setup guide.

- [ ] **Step 2: Confirm what is staged**

```
git status
```

Expected: only the files listed in Step 1 are staged. No `.meta` files (Unity will generate `Economy.meta` and `CoinsWallet.cs.meta` on next Editor focus — those go in a follow-up commit alongside the scene wiring).

- [ ] **Step 3: Create the commit**

```
git commit -m "feat(economy): split gem coins into persistent wallet, score becomes distance-only

ScoreManager no longer listens to SO_OnGemCollected; CurrentScore now
reflects only distance. New CoinsWallet (Gameplay/Economy/) owns the
\"Coins\" PlayerPrefs key and persists on every pickup so a run-mid crash
cannot lose currency.

Names CurrentScore, BestScore, SO_OnScoreChanged and key \"BestScore\"
are unchanged by user request — only semantics shift. Two new event
channels (SO_OnCoinsChanged, SO_OnSessionCoinsChanged) feed the HUD
wallet display and the GameOver \"+N coins\" line.

Spec: docs/superpowers/specs/2026-05-23-split-coins-and-distance-score-design.md
ADR-013 added to zigzag_architecture.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 4: Verify the commit landed**

```
git log -1 --stat
```

Expected: single commit listing the 9 modified files + 3 new files (CoinsWallet.cs, the spec, the plan).

---

## Self-Review (executed inline before handoff)

**1. Spec coverage:** every spec section maps to a task —
- §4.1 `CoinsWallet` → Task 1
- §4.2 `ScoreManager` refactor → Task 2
- §4.3 `GameConfigSO` → Task 3
- §4.4 `UIController` → Task 5
- §4.5 `GameBootstrap` → Task 6
- §4.6 `Gem` doc → Task 4
- §5 new assets → out of plan scope (manual setup guide, as called out in plan header)
- §7 migration → no-op (decided no code), called out in Task 10 step note
- §8 docs → Tasks 7, 8, 9
- §9 tests → no-op, called out in plan header

**2. Placeholder scan:** no "TBD" / "TODO" / "implement later" / "appropriate error handling" / "similar to Task N" patterns found in the plan body.

**3. Type / name consistency:** field names match between tasks (`_onCoinsChanged`, `_onSessionCoinsChanged`, `_hudCoinsText`, `_gameOverSessionCoinsText`, `_coinsWallet`). PlayerPrefs key `"Coins"` is referenced only in Task 1 (sole owner). The `CoinsWallet` namespace `ZigZag.Runtime.Gameplay.Economy` is consistent across Task 1, Task 6 (using), and Task 8 (architecture doc).

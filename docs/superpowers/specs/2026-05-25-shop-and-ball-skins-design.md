# Tienda de skins de la pelota

**Fecha:** 2026-05-25
**Iteración asociada:** 5 (sucede a iteración 4.2 — camera forward-only follow)
**Autor:** Brainstorm sesión 2026-05-25.

---

## 1. Contexto

Estado actual: la wallet de monedas (`CoinsWallet`, `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs`) acumula coins persistentes en `PlayerPrefs["Coins"]` desde la iteración 4.1, pero **no expone `Spend(int)`**. El spec [2026-05-23 split-coins-and-distance-score](2026-05-23-split-coins-and-distance-score-design.md) §4.1 dejó esa API explícitamente diferida hasta que llegase la tienda — esta iteración la añade.

En paralelo, el GDD §5.6 y `zigzag_architecture.md` ADR-009 prevén un **powerup imán** (`IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool`) como demostración de arquitectura extensible. Esta iteración **lo descope**: la extensibilidad se demuestra ahora con el catálogo de skins (`BallSkinSO` + `BallSkinCatalogSO`) y la `TrySpend` API. El esfuerzo se redirige a una vertical más vistosa para el evaluador del test técnico (visibilidad inmediata > efecto temporal de gameplay).

---

## 2. Objetivo

Introducir una **tienda de skins cosméticos para la pelota** accesible desde el menú principal, que permita:

- **Comprar** una skin gastando coins de la wallet persistente.
- **Equipar** la skin comprada (la compra equipa automáticamente).
- **Aplicar** el material correspondiente al `MeshRenderer` del ball.
- **Persistir** entre runs las skins poseídas y la skin equipada.

La compra **es la única forma de gasto de coins** que existe. La wallet sigue siendo el único owner de la PlayerPrefs key `"Coins"`.

---

## 3. Restricciones del usuario

1. **Reemplaza al powerup imán** — `IPowerup`, `MagnetPowerup`, `PowerupManager`, `PowerupPool` y la subcarpeta `Gameplay/Powerups/` quedan fuera del scope del prototipo.
2. **Skin = solo visual.** Un `Material` distinto por skin. Sin trails, partículas ni efectos de gameplay (campo extra trivial si se piden luego).
3. **Acceso al shop = botón en el panel Menu.** Sin nuevo `GameState`; el shop es un panel overlay sobre Menu.
4. **Compra ⇒ equipa inmediatamente.** Sin preview/try-before-buy, sin diálogo de confirmación.
5. **Catálogo inicial = 5 skins.** Default (precio 0, siempre poseída, equipada por defecto) + 4 de pago.
6. **Precios iniciales:** 0, 25, 75, 200, 500. Escalado x2–x3 para sentir progresión sin pedir grindeo. Tunables vía el campo `Price` del asset de cada skin.
7. **Sin renames** de la API pública existente. `CoinsWallet.TotalCoins`, `SessionCoins`, eventos y PlayerPrefs key `"Coins"` se mantienen.

---

## 4. Arquitectura propuesta

### 4.1 Nuevo canal de evento: `StringGameEventSO`

**Ubicación:** `Assets/Code/Runtime/Events/StringGameEventSO.cs`
**Asmdef:** `ZigZag.Runtime.Events` (existente).
**Tipo:** `sealed class StringGameEventSO : GameEventSO<string>`.

Trivial, sigue el mismo patrón que `IntGameEventSO`. Una sola línea de cuerpo:

```csharp
[CreateAssetMenu(menuName = "ZigZag/Events/String Event", fileName = "SO_StringEvent")]
public sealed class StringGameEventSO : GameEventSO<string> { }
```

### 4.2 Nuevos ScriptableObjects: `BallSkinSO` y `BallSkinCatalogSO`

**Ubicación:** `Assets/Code/Runtime/Gameplay/Cosmetics/`
**Asmdef:** `ZigZag.Runtime.Gameplay` (existente; nueva sub-feature `Cosmetics`).

#### `BallSkinSO`

Un asset por skin. Define qué se compra/equipa.

```csharp
[CreateAssetMenu(menuName = "ZigZag/Cosmetics/Ball Skin", fileName = "SO_Skin_")]
public sealed class BallSkinSO : ScriptableObject
{
    [SerializeField, Tooltip("Stable identifier persisted in PlayerPrefs. Never rename after release.")]
    private string _id;

    [SerializeField, Tooltip("Player-facing name shown in the shop row.")]
    private string _displayName;

    [SerializeField, Min(0), Tooltip("Cost in coins. 0 = always free (default skin).")]
    private int _price;

    [SerializeField, Tooltip("Material applied to the ball's MeshRenderer when this skin is equipped.")]
    private Material _material;

    public string Id => _id;
    public string DisplayName => _displayName;
    public int Price => _price;
    public Material Material => _material;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(_id))
            Debug.LogError($"{name}: BallSkinSO requires a non-empty Id.", this);
        if (_material == null)
            Debug.LogError($"{name}: BallSkinSO requires a Material reference.", this);
    }
#endif
}
```

**Regla clave:** `Id` es la fuente de verdad persistida en `PlayerPrefs`. Nunca renombrar tras release; el `DisplayName` es lo que sí puede cambiar.

#### `BallSkinCatalogSO`

Un único asset `SO_BallSkinCatalog`. Owner de la lista ordenada de skins disponibles.

```csharp
[CreateAssetMenu(menuName = "ZigZag/Cosmetics/Ball Skin Catalog", fileName = "SO_BallSkinCatalog")]
public sealed class BallSkinCatalogSO : ScriptableObject
{
    [SerializeField, Tooltip("Catalog order = shop display order. First entry is the default skin (always owned, default equipped).")]
    private BallSkinSO[] _skins;

    public IReadOnlyList<BallSkinSO> Skins => _skins;
    public BallSkinSO Default => _skins != null && _skins.Length > 0 ? _skins[0] : null;

    public BallSkinSO GetById(string id)
    {
        if (string.IsNullOrEmpty(id) || _skins == null) return null;
        for (int i = 0; i < _skins.Length; i++)
            if (_skins[i] != null && _skins[i].Id == id) return _skins[i];
        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_skins == null || _skins.Length == 0) { Debug.LogError($"{name}: catalog must contain at least one skin (the default).", this); return; }
        if (_skins[0] != null && _skins[0].Price != 0)
            Debug.LogError($"{name}: first skin must have Price = 0 (it's the default).", this);
        // Duplicate-id detection
        var seen = new HashSet<string>();
        for (int i = 0; i < _skins.Length; i++)
            if (_skins[i] != null && !seen.Add(_skins[i].Id))
                Debug.LogError($"{name}: duplicate skin Id '{_skins[i].Id}'.", this);
    }
#endif
}
```

`GetById` usa `for` (no LINQ) — catálogo es lista pequeña pero el patrón está en CLAUDE.md §8.

### 4.3 Nuevo componente: `SkinInventory`

**Ubicación:** `Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs`
**Tipo:** `[DisallowMultipleComponent] sealed class SkinInventory : MonoBehaviour`.

**Responsabilidad:** owner único de las dos PlayerPrefs keys nuevas (`"OwnedSkins"` y `"EquippedSkin"`). Tramita compras y cambios de equip. Mismo patrón que `CoinsWallet` (un sistema = una key, persistencia inmediata).

**Constantes:**
```csharp
private const string OwnedSkinsPrefKey = "OwnedSkins";    // CSV de ids
private const string EquippedSkinPrefKey = "EquippedSkin"; // id único
private const char OwnedSkinsSeparator = ',';
```

**Dependencias (Inspector):**
- `BallSkinCatalogSO _catalog` — fuente de verdad de qué ids existen.
- `CoinsWallet _coinsWallet` — referencia directa al componente para llamar `TrySpend` (no via SO event; la transacción es síncrona y atómica).
- `StringGameEventSO _onSkinPurchaseRequested` (inbound).
- `StringGameEventSO _onSkinEquipRequested` (inbound).
- `StringGameEventSO _onSkinEquipped` (outbound).
- `GameEventSO _onInventoryChanged` (outbound; raise sin payload tras cualquier mutación de owned o equipped).

**API pública (read-only):**
```csharp
public IReadOnlyCollection<string> OwnedSkinIds { get; }   // delega a _owned como ReadOnly wrapper
public string EquippedSkinId { get; private set; }
public bool IsOwned(string skinId);
```

**Estado interno:**
```csharp
private HashSet<string> _owned;  // expuesto sólo via IsOwned + OwnedSkinIds (read-only)
```

**Ciclo de vida:**

- **`Awake`** — `Debug.Assert` sobre cada ref serializada. Luego:
  1. `_owned = ParseOwnedCsv(PlayerPrefs.GetString(OwnedSkinsPrefKey, ""))`.
     - `ParseOwnedCsv` ignora ids vacíos y ids no presentes en el catálogo (defensivo: una skin removida en un update no rompe el save).
  2. Garantiza que la skin default está owned: si `_catalog.Default != null && !_owned.Contains(_catalog.Default.Id)` → `_owned.Add(_catalog.Default.Id)`.
  3. Carga `EquippedSkinId = PlayerPrefs.GetString(EquippedSkinPrefKey, "")`. Si vacío o no es id válido del catálogo o no está owned → `EquippedSkinId = _catalog.Default.Id`.
  4. Persistencia inmediata si Awake tuvo que sanear (default-add, equipped-fallback) — así el siguiente boot carga limpio.

- **`OnEnable` / `OnDisable`** — Register/Unregister de los dos canales inbound.

- **`Start`** — `_onSkinEquipped.Raise(EquippedSkinId)` y `_onInventoryChanged.Raise()`. Garantiza que `BallSkinApplier` y `ShopPanel` pintan estado correcto antes de cualquier interacción.

**Handlers:**

```csharp
private void HandlePurchaseRequested(string skinId)
{
    if (string.IsNullOrEmpty(skinId)) return;
    if (_owned.Contains(skinId)) return;  // idempotente
    BallSkinSO skin = _catalog.GetById(skinId);
    if (skin == null) { Debug.LogError($"Purchase request for unknown skin id '{skinId}'.", this); return; }
    if (!_coinsWallet.TrySpend(skin.Price)) return;  // fondos insuficientes — silencio, UI deshabilita el botón

    _owned.Add(skinId);
    EquippedSkinId = skinId;
    PersistAll();
    _onInventoryChanged.Raise();
    _onSkinEquipped.Raise(skinId);
}

private void HandleEquipRequested(string skinId)
{
    if (string.IsNullOrEmpty(skinId)) return;
    if (!_owned.Contains(skinId)) return;  // no se puede equipar lo que no se posee
    if (EquippedSkinId == skinId) return;  // idempotente
    EquippedSkinId = skinId;
    PersistEquipped();
    _onInventoryChanged.Raise();
    _onSkinEquipped.Raise(skinId);
}

private void PersistAll()
{
    PlayerPrefs.SetString(OwnedSkinsPrefKey, string.Join(OwnedSkinsSeparator.ToString(), _owned));
    PlayerPrefs.SetString(EquippedSkinPrefKey, EquippedSkinId);
    PlayerPrefs.Save();
}

private void PersistEquipped()
{
    PlayerPrefs.SetString(EquippedSkinPrefKey, EquippedSkinId);
    PlayerPrefs.Save();
}
```

**`ParseOwnedCsv` (internal, testeable):**

```csharp
internal static HashSet<string> ParseOwnedCsv(string csv, BallSkinCatalogSO catalog)
{
    var result = new HashSet<string>();
    if (string.IsNullOrEmpty(csv) || catalog == null) return result;
    string[] parts = csv.Split(OwnedSkinsSeparator);
    for (int i = 0; i < parts.Length; i++)
    {
        string id = parts[i];
        if (string.IsNullOrWhiteSpace(id)) continue;
        if (catalog.GetById(id) == null) continue;  // skin removida del catálogo → drop
        result.Add(id);
    }
    return result;
}
```

Marcado `internal` y testeado desde un assembly que usa `[InternalsVisibleTo("ZigZag.Tests.EditMode")]` en el runtime asmdef.

### 4.4 Nuevo componente: `BallSkinApplier`

**Ubicación:** `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs`
**Tipo:** `[DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))] sealed class BallSkinApplier : MonoBehaviour`.

**Responsabilidad:** aplicar al `MeshRenderer` el `Material` de la skin equipada.

**Dependencias (Inspector):**
- `BallSkinCatalogSO _catalog` — para resolver id → material.
- `StringGameEventSO _onSkinEquipped` (inbound).

**Lógica:**
```csharp
private MeshRenderer _meshRenderer;

private void Awake() { _meshRenderer = GetComponent<MeshRenderer>(); /* asserts */ }

private void OnEnable() { _onSkinEquipped.Register(HandleSkinEquipped); }
private void OnDisable() { _onSkinEquipped.Unregister(HandleSkinEquipped); }

private void HandleSkinEquipped(string skinId)
{
    BallSkinSO skin = _catalog.GetById(skinId);
    if (skin == null || skin.Material == null) return;
    _meshRenderer.sharedMaterial = skin.Material;
}
```

**Por qué `sharedMaterial` y no `material`:** acceder a `.material` instancia el material por GameObject (heap alloc + ruptura de batching). Como todas las pelotas (en este prototipo sólo una) deben mostrar la misma skin a la vez, `sharedMaterial` es correcto y barato.

**Orden de booteo:** `SkinInventory.Start` raise `_onSkinEquipped` después de que `BallSkinApplier.OnEnable` lo haya registrado. Ambos suceden en el primer frame; Unity garantiza que todos los `OnEnable` corren antes de cualquier `Start`. Sin race.

### 4.5 Modificación: `CoinsWallet` — añadir `TrySpend`

Agregar el método anticipado por la doc XML existente del archivo (líneas 21-24 de `CoinsWallet.cs`):

```csharp
/// <summary>
/// Attempts to deduct <paramref name="amount"/> from the persistent wallet.
/// Returns <c>true</c> on success (funds were sufficient and the deduction was
/// persisted), <c>false</c> on failure (insufficient funds or non-positive amount;
/// no state change occurred).
/// </summary>
public bool TrySpend(int amount)
{
    if (amount <= 0) return false;
    if (TotalCoins < amount) return false;

    TotalCoins -= amount;
    PlayerPrefs.SetInt(CoinsPrefKey, TotalCoins);
    PlayerPrefs.Save();
    _onCoinsChanged.Raise(TotalCoins);
    return true;
}
```

**Por qué `bool` y no `void` + excepción:** el caller (ShopPanel) necesita saber el resultado para refrescar UI; un `try/catch` por flujo normal sería sobrediseño. Falla silenciosa porque la UI ya debe deshabilitar el botón si no alcanza.

**`SessionCoins` no se toca en `TrySpend`** — gastar no es un evento de run, no se descuenta de la sesión visible. (`SessionCoins` representa "coins ganadas en este run", no "coins netas").

Actualizar el comentario `<remarks>` para borrar la frase "There is intentionally no Spend(int) API" (líneas 21-24 actuales).

### 4.6 Modificación: `InputHandler` — bloqueo de tap durante shop

El problema: en estado `Menu`, cualquier tap arranca la partida (`GameStateMachine.HandleTap` switch case `Menu` → `StartGame`). Si el ShopPanel está abierto sobre Menu, un tap dentro del panel hoy iniciaría la partida tras cerrar el shop (o peor, mientras está abierto).

**Solución:** `InputHandler` ignora taps cuando un flag interno está activo. El flag se controla por dos canales SO inbound.

```csharp
[SerializeField, Tooltip("Listened-to: suspends OnTapped until shop closes.")]
private GameEventSO _onShopOpened;

[SerializeField, Tooltip("Listened-to: re-enables OnTapped.")]
private GameEventSO _onShopClosed;

private bool _isBlocked;

private void OnEnable()
{
    if (_onShopOpened != null) _onShopOpened.Register(HandleShopOpened);
    if (_onShopClosed != null) _onShopClosed.Register(HandleShopClosed);
}
private void OnDisable() { /* unregister symmetric */ }

private void HandleShopOpened() => _isBlocked = true;
private void HandleShopClosed() => _isBlocked = false;

private void Update()
{
    if (_isBlocked) return;
    if (UnityInput.GetMouseButtonDown(0) || UnityInput.GetKeyDown(KeyCode.Space))
        OnTapped?.Invoke();
}
```

**Alternativa rechazada:** usar `EventSystem.current.IsPointerOverGameObject()`. Funciona para click de ratón pero no captura `Space` y depende del raycast UI; el flag es más explícito y menos frágil.

### 4.7 Nuevo componente UI: `ShopPanel`

**Ubicación:** `Assets/Code/Runtime/UI/Shop/ShopPanel.cs`
**Asmdef:** `ZigZag.Runtime.UI` (existente; nueva sub-feature `Shop`).
**Tipo:** `[DisallowMultipleComponent] sealed class ShopPanel : MonoBehaviour`.

**Responsabilidad:** controlar visibilidad del panel; instanciar una `ShopRowView` por cada skin del catálogo; refrescar filas tras cambios de inventario o de wallet.

**Dependencias (Inspector):**
- `GameObject _panelRoot` — raíz del overlay, se activa/desactiva.
- `Transform _rowsContainer` — `VerticalLayoutGroup` padre de las filas.
- `ShopRowView _rowPrefab` — `P_ShopRow`.
- `BallSkinCatalogSO _catalog`.
- `SkinInventory _inventory` — para query `IsOwned` y `EquippedSkinId` al refrescar.
- `CoinsWallet _coinsWallet` — para query `TotalCoins` al refrescar (sabe si puede pagar).
- `TextMeshProUGUI _walletText` — header del panel ("Coins: 1234").
- `GameEventSO _onInventoryChanged` (inbound — refresca filas).
- `IntGameEventSO _onCoinsChanged` (inbound — refresca wallet text + filas).
- `GameEventSO _onShopOpened` (outbound).
- `GameEventSO _onShopClosed` (outbound).

**API pública (llamada por botones UI):**

```csharp
public void OpenShop()
{
    _panelRoot.SetActive(true);
    RefreshAll();
    _onShopOpened.Raise();
}

public void CloseShop()
{
    _panelRoot.SetActive(false);
    _onShopClosed.Raise();
}
```

Botones "SHOP" en menu y "X" en el panel wirean sus `onClick` a estos métodos via UnityEvent (mismo argumento que el botón Retry actual — coste pagado por click, no por frame, evita crear un asmdef cycle).

**Ciclo de vida:**

- **`Awake`** — asserts. `_panelRoot.SetActive(false)` (arranca cerrado).
- **`OnEnable`** — register de los dos canales inbound. **Esto sucede una vez al inicio**, no en cada apertura, porque el script está en el panel root pero las suscripciones existen siempre. Decisión: el script vive en un GameObject **siempre activo** (no en `_panelRoot`); `_panelRoot` es un hijo que se activa/desactiva. Permite a `_walletText` y filas refrescarse aunque el panel esté oculto (barato; sólo escribe TMP texts).
- **`Start`** — `BuildRows()` instancia una `ShopRowView` por skin del catálogo. Se hace en `Start`, no en `OpenShop`, para no pagar `Instantiate` cada apertura.

**`BuildRows`:**
```csharp
private List<ShopRowView> _rows;  // cache para refresco rápido

private void BuildRows()
{
    _rows = new List<ShopRowView>(_catalog.Skins.Count);
    for (int i = 0; i < _catalog.Skins.Count; i++)
    {
        BallSkinSO skin = _catalog.Skins[i];
        if (skin == null) continue;
        ShopRowView row = Instantiate(_rowPrefab, _rowsContainer);
        row.Bind(skin);
        _rows.Add(row);
    }
    RefreshAll();
}
```

**`RefreshAll`:**
```csharp
private void RefreshAll()
{
    _walletText.text = $"Coins: {_coinsWallet.TotalCoins}";
    for (int i = 0; i < _rows.Count; i++)
    {
        BallSkinSO skin = _rows[i].Skin;
        bool owned = _inventory.IsOwned(skin.Id);
        bool equipped = _inventory.EquippedSkinId == skin.Id;
        bool canAfford = _coinsWallet.TotalCoins >= skin.Price;
        _rows[i].Refresh(owned, equipped, canAfford);
    }
}
```

Handlers `HandleInventoryChanged()` y `HandleCoinsChanged(int _)` ambos llaman a `RefreshAll`. El payload de coins no se usa porque `RefreshAll` re-lee `TotalCoins` directamente — mantiene una sola ruta de cómputo.

### 4.8 Nuevo componente UI: `ShopRowView`

**Ubicación:** `Assets/Code/Runtime/UI/Shop/ShopRowView.cs`
**Prefab:** `Assets/Prefabs/UI/P_ShopRow.prefab`.
**Tipo:** `[DisallowMultipleComponent] sealed class ShopRowView : MonoBehaviour`.

**Layout del prefab (UI children del row):**
- `Image _swatch` — tinted con `skin.Material.color` como mini-preview.
- `TextMeshProUGUI _nameText`.
- `TextMeshProUGUI _priceText`.
- `Button _actionButton` con hijo TMP `_actionButtonLabel`.

**Dependencias (Inspector — sólo refs a hijos):** los 4 anteriores.

**Dependencias (Inspector — canales):**
- `StringGameEventSO _onSkinPurchaseRequested` (outbound).
- `StringGameEventSO _onSkinEquipRequested` (outbound).

**API:**

```csharp
public BallSkinSO Skin { get; private set; }

private enum RowAction { None, Buy, Equip }
private RowAction _action = RowAction.None;

public void Bind(BallSkinSO skin)
{
    Skin = skin;
    _nameText.text = skin.DisplayName;
    _swatch.color = skin.Material != null ? skin.Material.color : Color.white;
    _actionButton.onClick.AddListener(OnActionClicked);
}

public void Refresh(bool owned, bool equipped, bool canAfford)
{
    _priceText.text = Skin.Price == 0 ? "FREE" : Skin.Price.ToString();
    if (equipped)        { _action = RowAction.None;  _actionButtonLabel.text = "EQUIPPED";          _actionButton.interactable = false; }
    else if (owned)      { _action = RowAction.Equip; _actionButtonLabel.text = "EQUIP";             _actionButton.interactable = true; }
    else                 { _action = RowAction.Buy;   _actionButtonLabel.text = $"BUY {Skin.Price}"; _actionButton.interactable = canAfford; }
}

private void OnActionClicked()
{
    switch (_action)
    {
        case RowAction.Buy:   _onSkinPurchaseRequested.Raise(Skin.Id); break;
        case RowAction.Equip: _onSkinEquipRequested.Raise(Skin.Id); break;
    }
}
```

**Por qué un enum cacheado en `Refresh` y no re-query en `OnActionClicked`:** el row no debe acoplarse a `SkinInventory` (eso lo haría dependiente de `Gameplay/`); pasar `owned`/`equipped` en cada `Refresh` mantiene el row puro de presentación. Una alternativa considerada (dispatch por `_actionButtonLabel.text == "EQUIP"`) se descarta — acoplarse a strings de UI es frágil.

**Listener cleanup:** `Bind` añade `AddListener` una sola vez; las filas viven todo el scene lifetime (no se destruyen), así que no se acumulan listeners. Si en el futuro `BuildRows` se llamara más de una vez, añadir `_actionButton.onClick.RemoveAllListeners()` al inicio de `Bind`.

### 4.9 Modificación: `UIController` — botón SHOP en Menu

Añadir:
```csharp
[Header("Shop")]
[SerializeField, Tooltip("Shop panel. UIController only triggers OpenShop on the SHOP button click.")]
private ShopPanel _shopPanel;

public void OnShopButtonClicked()
{
    if (_shopPanel != null) _shopPanel.OpenShop();
}
```

El botón SHOP en el panel Menu wirea su `onClick` a `OnShopButtonClicked` (mismo patrón que Retry). El botón X dentro del ShopPanel wirea a `ShopPanel.CloseShop` directamente — no pasa por UIController.

### 4.10 Modificación: `GameBootstrap` — validar 3 nuevas refs

```csharp
[SerializeField, Tooltip("Scene's skin inventory.")]
private SkinInventory _skinInventory;

[SerializeField, Tooltip("Ball's BallSkinApplier.")]
private BallSkinApplier _ballSkinApplier;

[SerializeField, Tooltip("Scene's shop panel.")]
private ShopPanel _shopPanel;

// + 3 Debug.Assert en Awake
```

### 4.11 Descope: `IPowerup` y compañía

Borrar de la documentación las referencias a `IPowerup`/`MagnetPowerup`/`PowerupManager`/`PowerupPool`. **No** se borra código porque nunca se llegó a implementar (sólo está en docs). Ver §8 (documentación).

### 4.12 Impacto en assembly definitions

`ZigZag.Runtime.UI.asmdef` actualmente referencia sólo `ZigZag.Runtime.Events` y `Unity.TextMeshPro`. `ShopPanel` necesita refs directas a `SkinInventory` (en `Gameplay/Cosmetics/`) y `CoinsWallet` (en `Gameplay/Economy/`), ambas dentro del asmdef `ZigZag.Runtime.Gameplay`. **Se añade** `"ZigZag.Runtime.Gameplay"` a `references[]` del UI asmdef.

**Por qué refs directas y no event-only:** el patrón actual de `UIController` (event-only) funciona porque sus datos son escalares (`int score`, `int coins`). El shop necesita consultar un **conjunto** (`IsOwned`) y un escalar mutable (`EquippedSkinId`) para refrescar filas. Reconstruir esos via eventos requeriría un mirror local en `ShopPanel` actualizado por canales tipo `SO_OnSkinOwned(string)` con broadcast inicial de cada id en `SkinInventory.Start` — más código y dos fuentes de verdad sincronizadas (`SkinInventory._owned` y `ShopPanel._localMirror`). La dependencia UI → Gameplay es **la dirección explícitamente permitida** por CLAUDE.md §5; añadirla aquí es alineado con la arquitectura, no contra ella.

`ZigZag.Runtime.Gameplay.asmdef` no cambia: la nueva sub-feature `Cosmetics/` y el `BallSkinApplier` viven dentro de ese mismo asmdef. `ZigZag.Runtime.Input.asmdef` tampoco cambia (sólo necesita `Events`, que ya tiene).

---

## 5. Catálogo inicial de skins

5 skins en `SO_BallSkinCatalog._skins[]`, en este orden:

| Index | Id        | DisplayName | Price | Material color (aprox) |
|-------|-----------|-------------|-------|------------------------|
| 0     | `default` | Default     | 0     | Blanco (material actual del ball)
| 1     | `red`     | Crimson     | 25    | `#E53935`
| 2     | `green`   | Emerald     | 75    | `#43A047`
| 3     | `blue`    | Sapphire    | 200   | `#1E88E5`
| 4     | `gold`    | Gold        | 500   | `#FBC02D` con emission

Materiales correspondientes en `Assets/Art/M_BallSkin_<Id>.mat`. El default reutiliza el material existente del prefab `P_Ball` (renombrado a `M_BallSkin_Default.mat` si hace falta, manteniendo el `.meta`).

---

## 6. Flujo de eventos resultante

**Boot:**
```
SkinInventory.Awake
  ├─> ParseOwnedCsv("OwnedSkins") + sanea (asegura default owned, equipped fallback)
  └─> Persiste si saneó
SkinInventory.Start
  ├─> _onSkinEquipped.Raise(equippedId)
  │    └─> BallSkinApplier aplica material antes del primer frame visible
  └─> _onInventoryChanged.Raise()
       └─> ShopPanel.RefreshAll (panel oculto pero textos pintados)
CoinsWallet.Start (existente)
  └─> _onCoinsChanged.Raise(TotalCoins)
       └─> ShopPanel.RefreshAll
```

**Abrir tienda:**
```
Click SHOP en Menu
  └─> UIController.OnShopButtonClicked
       └─> ShopPanel.OpenShop
            ├─> _panelRoot.SetActive(true)
            ├─> RefreshAll (filas + wallet text)
            └─> _onShopOpened.Raise
                 └─> InputHandler._isBlocked = true (tap no arranca partida)
```

**Comprar skin:**
```
Click BUY en fila "blue" (precio 200, wallet 350)
  └─> ShopRowView raises _onSkinPurchaseRequested("blue")
       └─> SkinInventory.HandlePurchaseRequested("blue")
            ├─> _coinsWallet.TrySpend(200)
            │    ├─> TotalCoins: 350 → 150
            │    ├─> PlayerPrefs.SetInt("Coins", 150) + Save
            │    └─> _onCoinsChanged.Raise(150)
            │         ├─> UIController actualiza HUD/GameOver coins text (no-op si menu)
            │         └─> ShopPanel.HandleCoinsChanged → RefreshAll (todas las filas + wallet text)
            ├─> _owned.Add("blue"); EquippedSkinId = "blue"
            ├─> PersistAll: SetString("OwnedSkins", "default,red,blue") + SetString("EquippedSkin","blue") + Save
            ├─> _onInventoryChanged.Raise → ShopPanel.RefreshAll (filas con nuevo owned/equipped)
            └─> _onSkinEquipped.Raise("blue")
                 ├─> BallSkinApplier cambia material del MeshRenderer del ball
                 └─> (ShopPanel ya refrescó por _onInventoryChanged; este raise no añade trabajo extra)
```

**Equipar skin ya poseída:**
```
Click EQUIP en fila "red"
  └─> ShopRowView raises _onSkinEquipRequested("red")
       └─> SkinInventory.HandleEquipRequested("red")
            ├─> EquippedSkinId = "red"
            ├─> PersistEquipped: SetString("EquippedSkin","red") + Save
            ├─> _onInventoryChanged.Raise → RefreshAll
            └─> _onSkinEquipped.Raise("red") → BallSkinApplier
```

**Cerrar tienda:**
```
Click X
  └─> ShopPanel.CloseShop
       ├─> _panelRoot.SetActive(false)
       └─> _onShopClosed.Raise
            └─> InputHandler._isBlocked = false
```

**Game flow no cambia.** Iniciar partida desde Menu funciona igual; durante Playing el shop no es accesible; al GameOver se vuelve al Menu donde el shop está disponible.

---

## 7. Persistencia

Tres PlayerPrefs keys totales (1 existente + 2 nuevas):

| Key             | Owner          | Tipo   | Formato                              |
|-----------------|----------------|--------|--------------------------------------|
| `Coins`         | `CoinsWallet`  | int    | Total wallet                         |
| `BestScore`     | `ScoreManager` | int    | Mejor distance score                 |
| `OwnedSkins`    | `SkinInventory`| string | CSV de ids, ej. `"default,red,blue"` |
| `EquippedSkin`  | `SkinInventory`| string | Id único, ej. `"blue"`               |

**Migración:** ninguna. Players preexistentes verán `OwnedSkins=""` → `Awake` añade `default` automáticamente; `EquippedSkin=""` → fallback al default. No se requiere código de migración (mismo criterio que iteración 4.1).

**Anti-cheat:** trivial editar `regedit` para añadir skins/coins. Asumimos jugador honesto (mismo argumento del spec 4.1).

---

## 8. Documentación a actualizar

- **`zigzag_gdd.md`**:
  - **§5.6 Powerup imán** → reescribir como **§5.6 Tienda de skins (sustituye al powerup imán originalmente planeado)**. Describir compra, equip, persistencia, 5 skins.
  - **§7 Sistemas / orden de implementación** → reemplazar día 6 ("Powerup imán + interfaz IPowerup") por "Tienda de skins + catálogo + SkinInventory".
  - **§10.2 HUD** y **§10.3 GameOver** sin cambios (la tienda sólo vive en Menu).
  - **§10.4 (nueva)** Shop panel layout: header con coins, lista vertical de filas.
  - **§11 Decisiones intencionales** → mover "Múltiples skins" de §12 a §11 marcado como "✅ incluido".
  - **§11** la fila "Tienda y gasto de currency fuera de scope" → reformular: "Tienda incluida (5 skins, compra+equip). Sin items consumibles, sin economía de packs, sin daily deals."
  - **§12 Out of scope** → quitar "Múltiples skins" y "Tienda".
  - **§14 Riesgos / mitigaciones** → quitar fila "Powerup activo al hacer GameOver no se limpia" (descope) y añadir "Skin desconocida tras update remoto → drop silencioso en parse (defensivo)".

- **`zigzag_architecture.md`**:
  - **§6.2 Catálogo de canales SO** → añadir filas:
    - `SO_OnSkinPurchaseRequested` (`StringGameEventSO`, raised by `ShopRowView`, listened by `SkinInventory`).
    - `SO_OnSkinEquipRequested` (`StringGameEventSO`, mismo).
    - `SO_OnSkinEquipped` (`StringGameEventSO`, raised by `SkinInventory`, listened by `BallSkinApplier`, `ShopPanel`).
    - `SO_OnInventoryChanged` (`GameEventSO`, raised by `SkinInventory`, listened by `ShopPanel`).
    - `SO_OnShopOpened` / `SO_OnShopClosed` (`GameEventSO`, raised by `ShopPanel`, listened by `InputHandler`).
  - **§7** → nuevas subsecciones:
    - `7.18 BallSkinSO`
    - `7.19 BallSkinCatalogSO`
    - `7.20 SkinInventory`
    - `7.21 BallSkinApplier`
    - `7.22 ShopPanel`
    - `7.23 ShopRowView`
  - **§7.11 IPowerup, §7.12 MagnetPowerup, §7.13 PowerupManager** → marcar como **descope** con nota cruzando a este spec; conservar el texto histórico (es contexto para entender la evolución).
  - **§7.16 Pools** → quitar referencia a `PowerupPool`.
  - **§ADR-009** "Interfaz IPowerup aunque solo haya un powerup" → añadir addendum: "**2026-05-25**: descope completo del powerup. La extensibilidad pasa a demostrarse con `BallSkinCatalogSO` + `SkinInventory.TrySpend` (ver ADR-015)."
  - **§ADR-015 (nueva)** "Tienda de skins reemplaza al powerup imán como demostración de arquitectura extensible". Justificación: visibilidad inmediata para evaluador del test técnico, reusa wallet existente, expone el patrón "catálogo de SO + inventory persistido" que es más generalizable que `IPowerup`.
  - **§ADR-016 (nueva)** "Shop como overlay sobre Menu, sin nuevo GameState. InputHandler suspende tap via canal SO." Justificación: shop no convive con Playing/GameOver, no merece estado propio; bloqueo de input es una sola línea (`if (_isBlocked) return`) más simple que routear via FSM.
  - **§Riesgos** → quitar "Powerup activo al hacer GameOver no se limpia". Añadir "PlayerPrefs `OwnedSkins` contiene id no presente en catálogo → drop silencioso en `ParseOwnedCsv`".

- **`devlog.md`**: nueva entrada `## 2026-05-25 — Iteración 5: tienda de skins`. Contenido a llenar al cierre.

---

## 9. Tests

**EditMode**, todos pure C#:

1. **`CoinsWalletTests.TrySpend_DeductsAndReturnsTrue_WhenSufficient`** — wallet con 100 (vía field reflection o test helper que invoque el handler de `_onGemCollected` con payload 100), `TrySpend(40)` → return `true`, `TotalCoins == 60`. Verifica que `_onCoinsChanged` se raised con payload 60.
2. **`CoinsWalletTests.TrySpend_LeavesBalanceAndReturnsFalse_WhenInsufficient`** — wallet con 30, `TrySpend(50)` → return `false`, `TotalCoins == 30`. `_onCoinsChanged` NO se raised.
3. **`CoinsWalletTests.TrySpend_ReturnsFalse_OnNonPositiveAmount`** — `TrySpend(0)` y `TrySpend(-5)` retornan `false`, balance intacto.
4. **`SkinInventoryTests.ParseOwnedCsv_ReturnsIdsPresentInCatalog`** — catalog mock con ids `{default,red,blue}`, CSV `"default,red,blue"` → set de 3.
5. **`SkinInventoryTests.ParseOwnedCsv_DropsUnknownIds`** — mismo catalog, CSV `"default,ghost,red"` → set de 2 (`default`, `red`).
6. **`SkinInventoryTests.ParseOwnedCsv_IgnoresEmptyAndWhitespace`** — CSV `",default,, ,red,"` → set de 2.
7. **`SkinInventoryTests.ParseOwnedCsv_EmptyCsv_ReturnsEmpty`** — CSV `""` → set vacío.

**Notas de harness (detalle del plan):**
- `CoinsWallet.TrySpend` requiere instanciar el componente con sus SOs serializados wireados. Patrón estándar: GameObject inactivo, `AddComponent`, set de campos privados vía reflection (o `SerializedObject` editor-side), `SetActive(true)` para disparar `Awake` con refs ya seteadas. `PlayerPrefs.DeleteKey("Coins")` en `[SetUp]`/`[TearDown]`.
- `SkinInventory.ParseOwnedCsv` se expone como `internal static` para que un test sin instancia lo pueda invocar — requiere `[InternalsVisibleTo("ZigZag.Tests.EditMode")]` en `ZigZag.Runtime.Gameplay.asmdef` (via `AssemblyInfo.cs` en `Cosmetics/`).

**No** se testean: `BallSkinApplier` (un solo Material assign), `ShopPanel` (wiring + RefreshAll trivial), `ShopRowView` (UI binding), `InputHandler` block flag (un bool flip). Mismo criterio del proyecto: ROI bajo, mocks de UnityEngine no son productivos.

---

## 10. Future considerations (no implementar ahora)

- **Trails / partículas por skin.** Añadir campo `[SerializeField] private TrailRenderer _trailPrefab;` a `BallSkinSO` y un componente nuevo `BallSkinTrailApplier` (o ampliar `BallSkinApplier` si es trivial). El renderer y el catálogo ya no cambian.
- **Items consumibles** (revives, magnet temporal, etc.). El powerup imán podría volver como item de tienda en vez de pickup en mundo. Patrón: `IShopItem { string Id; int Price; void Purchase(); }` + `ShopRowView` neutralizada hacia `IShopItem` en vez de `BallSkinSO`. Requiere refactor de filas pero no de la wallet.
- **Multi-currency** (coins + gems premium). `CoinsWallet` se renombra a `Wallet<TCurrency>` o se duplica. Lejos en el horizonte para un prototipo.
- **Daily deal / rotación de tienda.** ScriptableObject de configuración con timestamp + `DateTime.UtcNow` (rompería determinismo en gameplay code pero no aquí — la tienda es out-of-run).
- **Sync cloud de inventario.** Fuera de scope a perpetuidad para test técnico.
- **Localización del catálogo.** `DisplayName` por `LocalizedString` cuando se introduzca i18n.

---

## 11. Resumen de cambios físicos

**Archivos nuevos (código):**
- `Assets/Code/Runtime/Events/StringGameEventSO.cs`
- `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs`
- `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs`
- `Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs`
- `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs`
- `Assets/Code/Runtime/UI/Shop/ShopPanel.cs`
- `Assets/Code/Runtime/UI/Shop/ShopRowView.cs`
- `Assets/Code/Tests/EditMode/CoinsWalletTests.cs`
- `Assets/Code/Tests/EditMode/SkinInventoryTests.cs`
- `Assets/Code/Runtime/Gameplay/Cosmetics/AssemblyInfo.cs` — `[InternalsVisibleTo("ZigZag.Tests.EditMode")]` para testear `ParseOwnedCsv`

**Archivos nuevos (assets — manual en Unity):**
- `Assets/Settings/SO_BallSkinCatalog.asset`
- `Assets/Settings/Skins/SO_Skin_Default.asset`
- `Assets/Settings/Skins/SO_Skin_Red.asset`
- `Assets/Settings/Skins/SO_Skin_Green.asset`
- `Assets/Settings/Skins/SO_Skin_Blue.asset`
- `Assets/Settings/Skins/SO_Skin_Gold.asset`
- `Assets/Art/M_BallSkin_Default.mat` (puede ser rename del material actual del ball)
- `Assets/Art/M_BallSkin_Red.mat`, `M_BallSkin_Green.mat`, `M_BallSkin_Blue.mat`, `M_BallSkin_Gold.mat`
- `Assets/Settings/Events/SO_OnSkinPurchaseRequested.asset` (`StringGameEventSO`)
- `Assets/Settings/Events/SO_OnSkinEquipRequested.asset`
- `Assets/Settings/Events/SO_OnSkinEquipped.asset`
- `Assets/Settings/Events/SO_OnInventoryChanged.asset` (`GameEventSO`)
- `Assets/Settings/Events/SO_OnShopOpened.asset`
- `Assets/Settings/Events/SO_OnShopClosed.asset`
- `Assets/Prefabs/UI/P_ShopRow.prefab` (TMP, Image swatch, Button)

**Archivos modificados (código):**
- `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs` (+ `TrySpend`, actualizar XML)
- `Assets/Code/Runtime/Input/InputHandler.cs` (+ block flag y suscripciones)
- `Assets/Code/Runtime/Core/GameBootstrap.cs` (+ 3 asserts)
- `Assets/Code/Runtime/UI/UIController.cs` (+ `_shopPanel` ref + `OnShopButtonClicked`)
- `Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef` (añadir `"ZigZag.Runtime.Gameplay"` a `references[]`)

**Archivos modificados (assets/scene):**
- `Assets/Scenes/SampleScene.unity` (GameObject `SkinInventory`, panel `ShopPanel` bajo Canvas, botón SHOP en Menu, `BallSkinApplier` en P_Ball, todos los wires de canales)
- `Assets/Prefabs/P_Ball.prefab` (componente `BallSkinApplier`)
- `zigzag_gdd.md`, `zigzag_architecture.md`, `devlog.md`

**Sin renames** de tipos, campos serializados públicos, propiedades públicas, ni PlayerPrefs keys existentes.

**Sin cambios** a `ScoreManager`, `BallController`, `GameStateMachine`, `PathGenerator`, `GemPool`, `GemSpawner`, `Gem`, `Segment`, `PlatformPool`, `CameraFollow`, `ScoreCalculator`.

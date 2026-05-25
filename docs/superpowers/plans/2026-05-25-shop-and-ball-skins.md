# Shop and Ball Skins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Menu-accessible shop where the player spends `CoinsWallet` coins to buy and equip cosmetic ball skins (material swap only), replacing the descoped powerup work.

**Architecture:** Catalog-of-ScriptableObjects (`BallSkinSO[]` inside `BallSkinCatalogSO`) + a single-instance `SkinInventory` MonoBehaviour that owns persistence (PlayerPrefs keys `OwnedSkins` CSV + `EquippedSkin` id). Communication is event-channel based via existing `GameEventSO` patterns, plus a new `StringGameEventSO`. UI lives as a Shop overlay on the Menu panel; `InputHandler` suspends tap-to-start while the shop is open via two new SO channels. The ball receives the equipped material through a `BallSkinApplier` component on `P_Ball`.

**Tech Stack:** Unity 2022.3.62f2 LTS, C# .NET Standard 2.1, Built-in Render Pipeline, TextMeshPro, NUnit (EditMode tests). No new packages.

**Spec reference:** [`docs/superpowers/specs/2026-05-25-shop-and-ball-skins-design.md`](../specs/2026-05-25-shop-and-ball-skins-design.md)

**Naming conventions to follow (CLAUDE.md §4):**
- Files end with newline, UTF-8 (no BOM)
- Namespaces `ZigZag.Runtime.<Layer>.<Feature>`
- All identifiers in English
- Commit messages: conventional-commits prefix, no body, no co-author footer (per user memory)

---

## File Structure

**New code files (10):**

| Path | Responsibility |
|------|----------------|
| `Assets/Code/Runtime/Events/StringGameEventSO.cs` | String-payload event channel |
| `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs` | Per-skin data (id, name, price, material) |
| `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs` | Ordered list of skins + lookup by id |
| `Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs` | PlayerPrefs owner; handles purchase/equip flow |
| `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs` | Applies material to ball's MeshRenderer on equip |
| `Assets/Code/Runtime/Gameplay/Cosmetics/AssemblyInfo.cs` | `[InternalsVisibleTo("ZigZag.Tests.EditMode")]` |
| `Assets/Code/Runtime/UI/Shop/ShopPanel.cs` | Panel controller + rows builder + refresh |
| `Assets/Code/Runtime/UI/Shop/ShopRowView.cs` | One shop row: name, swatch, price, action button |
| `Assets/Code/Tests/EditMode/Economy/CoinsWalletTests.cs` | TrySpend behavior tests |
| `Assets/Code/Tests/EditMode/Cosmetics/SkinInventoryTests.cs` | ParseOwnedCsv tests |

**Modified code files (5):**

| Path | What changes |
|------|--------------|
| `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs` | Add `bool TrySpend(int)`, update XML remarks |
| `Assets/Code/Runtime/Input/InputHandler.cs` | Add `_onShopOpened`/`_onShopClosed` SO refs + `_isBlocked` flag |
| `Assets/Code/Runtime/Core/GameBootstrap.cs` | +3 `[SerializeField]` refs + asserts |
| `Assets/Code/Runtime/UI/UIController.cs` | +`_shopPanel` ref + `OnShopButtonClicked` |
| `Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef` | Add `"ZigZag.Runtime.Gameplay"` to `references[]` |

**New Unity assets (manual via Editor, instructions inline):**

- 6 SO event channel assets (`SO_OnSkinPurchaseRequested`, `_OnSkinEquipRequested`, `_OnSkinEquipped`, `_OnInventoryChanged`, `_OnShopOpened`, `_OnShopClosed`)
- 5 skin SO assets (`SO_Skin_Default`, `_Red`, `_Green`, `_Blue`, `_Gold`)
- 1 catalog asset (`SO_BallSkinCatalog`)
- 4 new materials (`M_BallSkin_Red`, `_Green`, `_Blue`, `_Gold`) + 1 rename (`M_Platform` is unrelated — the ball's current material gets renamed to `M_BallSkin_Default` if not already)
- 1 prefab (`P_ShopRow`)
- Scene wiring in `SampleScene.unity`: new GameObjects `SkinInventory`, ShopPanel UI hierarchy, SHOP button in Menu, `BallSkinApplier` on `P_Ball`

**Docs (last):**
- `zigzag_gdd.md`, `zigzag_architecture.md`, `devlog.md`

---

## Task 1: Add `StringGameEventSO`

**Files:**
- Create: `Assets/Code/Runtime/Events/StringGameEventSO.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

namespace ZigZag.Runtime.Events
{
    /// <summary>
    /// String-payload event channel. Used for cross-system events whose payload is a
    /// stable identifier (skin id, achievement key, ...). Subscribers must
    /// <see cref="GameEventSO{T}.Register"/> in <c>OnEnable</c> and
    /// <see cref="GameEventSO{T}.Unregister"/> in <c>OnDisable</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "ZigZag/Events/String Event", fileName = "SO_StringEvent")]
    public sealed class StringGameEventSO : GameEventSO<string> { }
}
```

- [ ] **Step 2: Verify Unity compiles**

Open Unity. Wait for compile (bottom-right spinner). Console must show **zero errors**.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/Events/StringGameEventSO.cs Assets/Code/Runtime/Events/StringGameEventSO.cs.meta
git commit -m "feat(events): add StringGameEventSO channel"
```

---

## Task 2: Add `BallSkinSO`

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs`

- [ ] **Step 1: Create the Cosmetics directory and the file**

Create directory `Assets/Code/Runtime/Gameplay/Cosmetics/` (Unity will generate the `.meta` for the folder when it scans).

File `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinSO.cs`:

```csharp
using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Cosmetic ball skin definition. Owns the stable identifier persisted in
    /// PlayerPrefs (<see cref="Id"/>), the player-facing name, the shop price and
    /// the <see cref="Material"/> applied to the ball's <c>MeshRenderer</c> when
    /// this skin is equipped.
    /// </summary>
    /// <remarks>
    /// <see cref="Id"/> is the persistence contract — never rename after release.
    /// <see cref="DisplayName"/> may change freely.
    /// </remarks>
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
}
```

- [ ] **Step 2: Verify Unity compiles**

Console must be clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Cosmetics/
git commit -m "feat(cosmetics): add BallSkinSO data container"
```

---

## Task 3: Add `BallSkinCatalogSO`

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Ordered list of all <see cref="BallSkinSO"/> known to the game. The first
    /// entry is the default skin: it must have <c>Price = 0</c>, is always owned
    /// and is the fallback equipped skin when PlayerPrefs are empty or contain an
    /// unknown id.
    /// </summary>
    [CreateAssetMenu(menuName = "ZigZag/Cosmetics/Ball Skin Catalog", fileName = "SO_BallSkinCatalog")]
    public sealed class BallSkinCatalogSO : ScriptableObject
    {
        [SerializeField, Tooltip("Catalog order = shop display order. First entry is the default skin (always owned, default equipped).")]
        private BallSkinSO[] _skins;

        public IReadOnlyList<BallSkinSO> Skins => _skins;

        public BallSkinSO Default => (_skins != null && _skins.Length > 0) ? _skins[0] : null;

        public BallSkinSO GetById(string id)
        {
            if (string.IsNullOrEmpty(id) || _skins == null) return null;
            for (int i = 0; i < _skins.Length; i++)
            {
                if (_skins[i] != null && _skins[i].Id == id) return _skins[i];
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_skins == null || _skins.Length == 0)
            {
                Debug.LogError($"{name}: catalog must contain at least one skin (the default).", this);
                return;
            }
            if (_skins[0] != null && _skins[0].Price != 0)
            {
                Debug.LogError($"{name}: first skin must have Price = 0 (it's the default).", this);
            }
            var seen = new HashSet<string>();
            for (int i = 0; i < _skins.Length; i++)
            {
                if (_skins[i] != null && !seen.Add(_skins[i].Id))
                {
                    Debug.LogError($"{name}: duplicate skin Id '{_skins[i].Id}'.", this);
                }
            }
        }
#endif
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinCatalogSO.cs.meta
git commit -m "feat(cosmetics): add BallSkinCatalogSO with id lookup"
```

---

## Task 4: Add `CoinsWallet.TrySpend` (TDD)

**Files:**
- Create: `Assets/Code/Tests/EditMode/Economy/CoinsWalletTests.cs`
- Modify: `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs`

- [ ] **Step 1: Write the failing tests**

Create directory `Assets/Code/Tests/EditMode/Economy/` and file `CoinsWalletTests.cs`:

```csharp
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Economy;

namespace ZigZag.Tests.EditMode.Economy
{
    [TestFixture]
    public sealed class CoinsWalletTests
    {
        private const string CoinsPrefKey = "Coins";

        private GameObject _go;
        private CoinsWallet _wallet;
        private IntGameEventSO _onCoinsChanged;
        private int _lastCoinsChangedPayload;
        private int _coinsChangedRaiseCount;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(CoinsPrefKey);
            PlayerPrefs.SetInt(CoinsPrefKey, 100);

            _go = new GameObject("CoinsWalletUnderTest");
            _go.SetActive(false);
            _wallet = _go.AddComponent<CoinsWallet>();

            _onCoinsChanged = ScriptableObject.CreateInstance<IntGameEventSO>();
            SetField(_wallet, "_onGemCollected", ScriptableObject.CreateInstance<IntGameEventSO>());
            SetField(_wallet, "_onGameReset", ScriptableObject.CreateInstance<GameEventSO>());
            SetField(_wallet, "_onCoinsChanged", _onCoinsChanged);
            SetField(_wallet, "_onSessionCoinsChanged", ScriptableObject.CreateInstance<IntGameEventSO>());

            _lastCoinsChangedPayload = -1;
            _coinsChangedRaiseCount = 0;
            _onCoinsChanged.Register(OnCoinsChangedHandler);

            _go.SetActive(true); // Awake runs now: TotalCoins = 100
        }

        [TearDown]
        public void TearDown()
        {
            _onCoinsChanged.Unregister(OnCoinsChangedHandler);
            Object.DestroyImmediate(_go);
            PlayerPrefs.DeleteKey(CoinsPrefKey);
        }

        private void OnCoinsChangedHandler(int v) { _lastCoinsChangedPayload = v; _coinsChangedRaiseCount++; }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            f.SetValue(target, value);
        }

        [Test]
        public void TrySpend_DeductsAndReturnsTrue_WhenSufficient()
        {
            // Wallet starts at 100 (loaded by Awake). Discard the Start() broadcast.
            _coinsChangedRaiseCount = 0;

            bool ok = _wallet.TrySpend(40);

            Assert.IsTrue(ok);
            Assert.AreEqual(60, _wallet.TotalCoins);
            Assert.AreEqual(60, PlayerPrefs.GetInt(CoinsPrefKey, -1));
            Assert.AreEqual(1, _coinsChangedRaiseCount);
            Assert.AreEqual(60, _lastCoinsChangedPayload);
        }

        [Test]
        public void TrySpend_LeavesBalanceAndReturnsFalse_WhenInsufficient()
        {
            _coinsChangedRaiseCount = 0;

            bool ok = _wallet.TrySpend(150);

            Assert.IsFalse(ok);
            Assert.AreEqual(100, _wallet.TotalCoins);
            Assert.AreEqual(100, PlayerPrefs.GetInt(CoinsPrefKey, -1));
            Assert.AreEqual(0, _coinsChangedRaiseCount);
        }

        [Test]
        public void TrySpend_ReturnsFalse_OnZeroOrNegativeAmount()
        {
            _coinsChangedRaiseCount = 0;

            Assert.IsFalse(_wallet.TrySpend(0));
            Assert.IsFalse(_wallet.TrySpend(-5));
            Assert.AreEqual(100, _wallet.TotalCoins);
            Assert.AreEqual(0, _coinsChangedRaiseCount);
        }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

In Unity: `Window → General → Test Runner → EditMode → Run All`.
Expected: **3 failures** in `CoinsWalletTests` with compiler error "CoinsWallet does not contain a definition for 'TrySpend'" (the project will fail to compile until step 3).

- [ ] **Step 3: Add `TrySpend` to `CoinsWallet`**

Modify `Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs`. Replace the `<remarks>` block (lines 11-25 currently) and append the new method.

Replace the `<remarks>` (lines 11-25) with:

```csharp
    /// <remarks>
    /// Tracks two values: <see cref="TotalCoins"/> (the persistent wallet, the
    /// user's currency balance across all runs) and <see cref="SessionCoins"/>
    /// (coins earned in the current run, reset on <c>SO_OnGameReset</c> so the
    /// GameOver panel can display "+N coins" for the just-ended run).
    ///
    /// Persistence cadence: <c>PlayerPrefs.SetInt + Save</c> on every pickup and
    /// on every successful <see cref="TrySpend"/>. A run-mid crash (alt-F4,
    /// editor stop) must not steal coins from the player — they are currency,
    /// not a volatile score.
    /// </remarks>
```

Then add the `TrySpend` method below `HandleGameReset` (immediately before the closing brace of the class):

```csharp
        /// <summary>
        /// Attempts to deduct <paramref name="amount"/> from the persistent wallet.
        /// Returns <c>true</c> on success (funds were sufficient, deduction persisted
        /// and <see cref="_onCoinsChanged"/> raised). Returns <c>false</c> on
        /// non-positive amount or insufficient funds; no state change occurred.
        /// <see cref="SessionCoins"/> is intentionally untouched — spending is not a
        /// run-time event.
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

- [ ] **Step 4: Run tests and verify they pass**

Test Runner → Run All. Expected: all 3 `CoinsWalletTests` **green**.

- [ ] **Step 5: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Economy/CoinsWallet.cs Assets/Code/Tests/EditMode/Economy/
git commit -m "feat(economy): add CoinsWallet.TrySpend with tests"
```

---

## Task 5: Add `SkinInventory` with `ParseOwnedCsv` tests (TDD)

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Cosmetics/AssemblyInfo.cs`
- Create: `Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs`
- Create: `Assets/Code/Tests/EditMode/Cosmetics/SkinInventoryTests.cs`

- [ ] **Step 1: Add `AssemblyInfo.cs` for `InternalsVisibleTo`**

Create `Assets/Code/Runtime/Gameplay/Cosmetics/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZigZag.Tests.EditMode")]
```

- [ ] **Step 2: Write failing tests**

Create directory `Assets/Code/Tests/EditMode/Cosmetics/` and file `SkinInventoryTests.cs`:

```csharp
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.Cosmetics;

namespace ZigZag.Tests.EditMode.Cosmetics
{
    [TestFixture]
    public sealed class SkinInventoryTests
    {
        private BallSkinCatalogSO _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BuildCatalog("default", "red", "blue");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
        }

        [Test]
        public void ParseOwnedCsv_ReturnsAllKnownIds()
        {
            HashSet<string> result = SkinInventory.ParseOwnedCsv("default,red,blue", _catalog);
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.Contains("default"));
            Assert.IsTrue(result.Contains("red"));
            Assert.IsTrue(result.Contains("blue"));
        }

        [Test]
        public void ParseOwnedCsv_DropsUnknownIds()
        {
            HashSet<string> result = SkinInventory.ParseOwnedCsv("default,ghost,red", _catalog);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("default"));
            Assert.IsTrue(result.Contains("red"));
            Assert.IsFalse(result.Contains("ghost"));
        }

        [Test]
        public void ParseOwnedCsv_IgnoresEmptyAndWhitespace()
        {
            HashSet<string> result = SkinInventory.ParseOwnedCsv(",default,, ,red,", _catalog);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("default"));
            Assert.IsTrue(result.Contains("red"));
        }

        [Test]
        public void ParseOwnedCsv_EmptyOrNullCsv_ReturnsEmptySet()
        {
            Assert.AreEqual(0, SkinInventory.ParseOwnedCsv("", _catalog).Count);
            Assert.AreEqual(0, SkinInventory.ParseOwnedCsv(null, _catalog).Count);
        }

        private static BallSkinCatalogSO BuildCatalog(params string[] ids)
        {
            var skins = new BallSkinSO[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var skin = ScriptableObject.CreateInstance<BallSkinSO>();
                SetField(skin, "_id", ids[i]);
                SetField(skin, "_displayName", ids[i]);
                SetField(skin, "_price", i == 0 ? 0 : i * 25);
                // _material left null — not read by ParseOwnedCsv.
                skins[i] = skin;
            }
            var catalog = ScriptableObject.CreateInstance<BallSkinCatalogSO>();
            SetField(catalog, "_skins", skins);
            return catalog;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            f.SetValue(target, value);
        }
    }
}
```

- [ ] **Step 3: Run tests and verify they fail**

Expected: compiler error "SkinInventory does not exist" until step 4.

- [ ] **Step 4: Implement `SkinInventory`**

Create `Assets/Code/Runtime/Gameplay/Cosmetics/SkinInventory.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Economy;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Sole owner of the PlayerPrefs keys <c>"OwnedSkins"</c> (CSV of skin ids) and
    /// <c>"EquippedSkin"</c> (single id). Brokers purchase and equip requests
    /// raised by the shop UI: validates funds against <see cref="CoinsWallet"/>,
    /// mutates the inventory, persists immediately, and broadcasts the result via
    /// <see cref="_onSkinEquipped"/> and <see cref="_onInventoryChanged"/>.
    /// </summary>
    /// <remarks>
    /// PlayerPrefs are saved on every mutation. Same argument as <see cref="CoinsWallet"/>:
    /// a brutal stop must not lose inventory that the player paid for.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SkinInventory : MonoBehaviour
    {
        private const string OwnedSkinsPrefKey = "OwnedSkins";
        private const string EquippedSkinPrefKey = "EquippedSkin";
        private const char OwnedSkinsSeparator = ',';

        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of truth for which skin ids exist.")]
        private BallSkinCatalogSO _catalog;

        [SerializeField, Tooltip("Wallet whose TrySpend is invoked on purchase.")]
        private CoinsWallet _coinsWallet;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: payload is the skin id the shop wants to buy.")]
        private StringGameEventSO _onSkinPurchaseRequested;

        [SerializeField, Tooltip("Listened-to: payload is the skin id the shop wants to equip (must already be owned).")]
        private StringGameEventSO _onSkinEquipRequested;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised on every successful equip (including the boot broadcast). Payload is the equipped skin id.")]
        private StringGameEventSO _onSkinEquipped;

        [SerializeField, Tooltip("Raised after any inventory mutation (owned added or equipped changed). Lets the shop refresh.")]
        private GameEventSO _onInventoryChanged;

        private HashSet<string> _owned;

        public IReadOnlyCollection<string> OwnedSkinIds => _owned;
        public string EquippedSkinId { get; private set; }

        public bool IsOwned(string skinId) => !string.IsNullOrEmpty(skinId) && _owned != null && _owned.Contains(skinId);

        private void Awake()
        {
            Debug.Assert(_catalog != null, $"{nameof(SkinInventory)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_coinsWallet != null, $"{nameof(SkinInventory)} requires a {nameof(CoinsWallet)} reference.", this);
            Debug.Assert(_onSkinPurchaseRequested != null, $"{nameof(SkinInventory)} requires {nameof(_onSkinPurchaseRequested)}.", this);
            Debug.Assert(_onSkinEquipRequested != null, $"{nameof(SkinInventory)} requires {nameof(_onSkinEquipRequested)}.", this);
            Debug.Assert(_onSkinEquipped != null, $"{nameof(SkinInventory)} requires {nameof(_onSkinEquipped)}.", this);
            Debug.Assert(_onInventoryChanged != null, $"{nameof(SkinInventory)} requires {nameof(_onInventoryChanged)}.", this);

            bool needsResave = false;

            _owned = ParseOwnedCsv(PlayerPrefs.GetString(OwnedSkinsPrefKey, string.Empty), _catalog);

            BallSkinSO defaultSkin = _catalog != null ? _catalog.Default : null;
            if (defaultSkin != null && _owned.Add(defaultSkin.Id))
            {
                needsResave = true; // default wasn't there yet
            }

            string storedEquipped = PlayerPrefs.GetString(EquippedSkinPrefKey, string.Empty);
            if (string.IsNullOrEmpty(storedEquipped)
                || _catalog == null || _catalog.GetById(storedEquipped) == null
                || !_owned.Contains(storedEquipped))
            {
                EquippedSkinId = defaultSkin != null ? defaultSkin.Id : string.Empty;
                needsResave = true;
            }
            else
            {
                EquippedSkinId = storedEquipped;
            }

            if (needsResave) PersistAll();
        }

        private void OnEnable()
        {
            if (_onSkinPurchaseRequested != null) _onSkinPurchaseRequested.Register(HandlePurchaseRequested);
            if (_onSkinEquipRequested != null) _onSkinEquipRequested.Register(HandleEquipRequested);
        }

        private void OnDisable()
        {
            if (_onSkinPurchaseRequested != null) _onSkinPurchaseRequested.Unregister(HandlePurchaseRequested);
            if (_onSkinEquipRequested != null) _onSkinEquipRequested.Unregister(HandleEquipRequested);
        }

        private void Start()
        {
            // Broadcast equipped skin so BallSkinApplier and ShopPanel paint correct state on first frame.
            _onSkinEquipped.Raise(EquippedSkinId);
            _onInventoryChanged.Raise();
        }

        private void HandlePurchaseRequested(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            if (_owned.Contains(skinId)) return;
            BallSkinSO skin = _catalog != null ? _catalog.GetById(skinId) : null;
            if (skin == null)
            {
                Debug.LogError($"Purchase request for unknown skin id '{skinId}'.", this);
                return;
            }
            if (!_coinsWallet.TrySpend(skin.Price)) return; // insufficient funds — silent; UI disables the button

            _owned.Add(skinId);
            EquippedSkinId = skinId;
            PersistAll();
            _onInventoryChanged.Raise();
            _onSkinEquipped.Raise(skinId);
        }

        private void HandleEquipRequested(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            if (!_owned.Contains(skinId)) return;
            if (EquippedSkinId == skinId) return;
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

        internal static HashSet<string> ParseOwnedCsv(string csv, BallSkinCatalogSO catalog)
        {
            var result = new HashSet<string>();
            if (string.IsNullOrEmpty(csv) || catalog == null) return result;
            string[] parts = csv.Split(OwnedSkinsSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (catalog.GetById(id) == null) continue;
                result.Add(id);
            }
            return result;
        }
    }
}
```

- [ ] **Step 5: Run tests and verify they pass**

Test Runner → Run All. Expected: all 4 `SkinInventoryTests` **green**, all `CoinsWalletTests` still green, all pre-existing tests still green.

- [ ] **Step 6: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Cosmetics/ Assets/Code/Tests/EditMode/Cosmetics/
git commit -m "feat(cosmetics): add SkinInventory with PlayerPrefs persistence"
```

---

## Task 6: Add `BallSkinApplier`

**Files:**
- Create: `Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Lives on the ball. Listens for <see cref="_onSkinEquipped"/> and swaps the
    /// <c>MeshRenderer.sharedMaterial</c> to the equipped skin's material.
    /// </summary>
    /// <remarks>
    /// Uses <c>sharedMaterial</c> deliberately: accessing <c>.material</c> would
    /// instance the material at runtime (heap alloc + broken batching). All balls
    /// (this prototype has one) show the same skin at a time, so the shared slot
    /// is correct and cheap.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BallSkinApplier : MonoBehaviour
    {
        [SerializeField, Tooltip("Catalog used to resolve a skin id into its material.")]
        private BallSkinCatalogSO _catalog;

        [SerializeField, Tooltip("Listened-to: payload is the newly equipped skin id.")]
        private StringGameEventSO _onSkinEquipped;

        private MeshRenderer _meshRenderer;

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            Debug.Assert(_catalog != null, $"{nameof(BallSkinApplier)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_onSkinEquipped != null, $"{nameof(BallSkinApplier)} requires {nameof(_onSkinEquipped)}.", this);
        }

        private void OnEnable()
        {
            if (_onSkinEquipped != null) _onSkinEquipped.Register(HandleSkinEquipped);
        }

        private void OnDisable()
        {
            if (_onSkinEquipped != null) _onSkinEquipped.Unregister(HandleSkinEquipped);
        }

        private void HandleSkinEquipped(string skinId)
        {
            BallSkinSO skin = _catalog != null ? _catalog.GetById(skinId) : null;
            if (skin == null || skin.Material == null) return;
            _meshRenderer.sharedMaterial = skin.Material;
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs Assets/Code/Runtime/Gameplay/Cosmetics/BallSkinApplier.cs.meta
git commit -m "feat(cosmetics): add BallSkinApplier to swap ball material on equip"
```

---

## Task 7: Add tap-block to `InputHandler`

**Files:**
- Modify: `Assets/Code/Runtime/Input/InputHandler.cs`

- [ ] **Step 1: Replace the file contents**

The current file has no SerializeFields and is straightforward. Replace with:

```csharp
using System;
using UnityEngine;
using ZigZag.Runtime.Events;
using UnityInput = UnityEngine.Input;

namespace ZigZag.Runtime.Input
{
    /// <summary>
    /// Single-action input abstraction. Fires <see cref="OnTapped"/> on any of:
    /// left mouse click, first touch on a mobile device (Unity maps touch 0 to mouse
    /// button 0 automatically), or <see cref="KeyCode.Space"/> as an editor shortcut.
    /// </summary>
    /// <remarks>
    /// ADR-006 in <c>zigzag_architecture.md</c> selects the classic
    /// <c>UnityEngine.Input</c> over the new Input System for this prototype. Wrapping
    /// the call inside this handler keeps a future migration to a one-file change.
    ///
    /// The shop overlay suspends tap routing via <see cref="_onShopOpened"/>/
    /// <see cref="_onShopClosed"/> so a tap inside the shop UI does not start a run.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class InputHandler : MonoBehaviour
    {
        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: suspends OnTapped until the shop closes.")]
        private GameEventSO _onShopOpened;

        [SerializeField, Tooltip("Listened-to: re-enables OnTapped.")]
        private GameEventSO _onShopClosed;

        public event Action OnTapped;

        private bool _isBlocked;

        private void OnEnable()
        {
            if (_onShopOpened != null) _onShopOpened.Register(HandleShopOpened);
            if (_onShopClosed != null) _onShopClosed.Register(HandleShopClosed);
        }

        private void OnDisable()
        {
            if (_onShopOpened != null) _onShopOpened.Unregister(HandleShopOpened);
            if (_onShopClosed != null) _onShopClosed.Unregister(HandleShopClosed);
        }

        private void HandleShopOpened() => _isBlocked = true;
        private void HandleShopClosed() => _isBlocked = false;

        private void Update()
        {
            if (_isBlocked) return;
            if (UnityInput.GetMouseButtonDown(0) || UnityInput.GetKeyDown(KeyCode.Space))
            {
                OnTapped?.Invoke();
            }
        }
    }
}
```

- [ ] **Step 2: Update the asmdef to reference Events**

Check `Assets/Code/Runtime/Input/ZigZag.Runtime.Input.asmdef`. If `"ZigZag.Runtime.Events"` is **not** already in `references[]`, add it. Example after edit:

```json
{
    "name": "ZigZag.Runtime.Input",
    "rootNamespace": "ZigZag.Runtime.Input",
    "references": [
        "ZigZag.Runtime.Events"
    ],
    "includePlatforms": [],
    ...
}
```

- [ ] **Step 3: Verify Unity compiles**

Console clean. Note: at this point the InputHandler component in the scene now has two new serialized refs that will be `null` — that is fine until Task 14 wires them. The script's null checks tolerate it.

- [ ] **Step 4: Commit**

```bash
git add Assets/Code/Runtime/Input/
git commit -m "feat(input): add shop-aware tap suspension to InputHandler"
```

---

## Task 8: Update `ZigZag.Runtime.UI.asmdef` to reference Gameplay

**Files:**
- Modify: `Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef`

- [ ] **Step 1: Edit the asmdef**

Add `"ZigZag.Runtime.Gameplay"` to the `references` array. Result:

```json
{
    "name": "ZigZag.Runtime.UI",
    "rootNamespace": "ZigZag.Runtime.UI",
    "references": [
        "ZigZag.Runtime.Events",
        "ZigZag.Runtime.Gameplay",
        "Unity.TextMeshPro"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Verify Unity compiles**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/UI/ZigZag.Runtime.UI.asmdef
git commit -m "chore(asmdef): UI references Gameplay for upcoming Shop"
```

---

## Task 9: Add `ShopRowView`

**Files:**
- Create: `Assets/Code/Runtime/UI/Shop/ShopRowView.cs`

- [ ] **Step 1: Create the file**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Cosmetics;

namespace ZigZag.Runtime.UI.Shop
{
    /// <summary>
    /// One shop row: name + swatch + price + a single action button whose label and
    /// behavior reflect the row's state (Buy / Equip / Equipped). Pure presentation
    /// — raises intent via SO event channels; the actual purchase/equip transition
    /// lives in <see cref="SkinInventory"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopRowView : MonoBehaviour
    {
        [Header("Children (Prefab)")]
        [SerializeField, Tooltip("Swatch image tinted with the skin's material color.")]
        private Image _swatch;

        [SerializeField, Tooltip("Skin display name TMP text.")]
        private TextMeshProUGUI _nameText;

        [SerializeField, Tooltip("Price TMP text ('FREE' when price == 0).")]
        private TextMeshProUGUI _priceText;

        [SerializeField, Tooltip("Single action button (Buy/Equip/Equipped). Disabled when there is no valid action.")]
        private Button _actionButton;

        [SerializeField, Tooltip("Label inside the action button.")]
        private TextMeshProUGUI _actionButtonLabel;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised on click when the row is in Buy state. Payload is the skin id.")]
        private StringGameEventSO _onSkinPurchaseRequested;

        [SerializeField, Tooltip("Raised on click when the row is in Equip state. Payload is the skin id.")]
        private StringGameEventSO _onSkinEquipRequested;

        public BallSkinSO Skin { get; private set; }

        private enum RowAction { None, Buy, Equip }
        private RowAction _action = RowAction.None;

        public void Bind(BallSkinSO skin)
        {
            Skin = skin;
            _nameText.text = skin.DisplayName;
            _swatch.color = skin.Material != null ? skin.Material.color : Color.white;
            _actionButton.onClick.RemoveAllListeners();
            _actionButton.onClick.AddListener(OnActionClicked);
        }

        public void Refresh(bool owned, bool equipped, bool canAfford)
        {
            _priceText.text = Skin.Price == 0 ? "FREE" : Skin.Price.ToString();

            if (equipped)
            {
                _action = RowAction.None;
                _actionButtonLabel.text = "EQUIPPED";
                _actionButton.interactable = false;
            }
            else if (owned)
            {
                _action = RowAction.Equip;
                _actionButtonLabel.text = "EQUIP";
                _actionButton.interactable = true;
            }
            else
            {
                _action = RowAction.Buy;
                _actionButtonLabel.text = $"BUY {Skin.Price}";
                _actionButton.interactable = canAfford;
            }
        }

        private void OnActionClicked()
        {
            switch (_action)
            {
                case RowAction.Buy:
                    if (_onSkinPurchaseRequested != null) _onSkinPurchaseRequested.Raise(Skin.Id);
                    break;
                case RowAction.Equip:
                    if (_onSkinEquipRequested != null) _onSkinEquipRequested.Raise(Skin.Id);
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/UI/Shop/
git commit -m "feat(ui): add ShopRowView for skin shop entries"
```

---

## Task 10: Add `ShopPanel`

**Files:**
- Create: `Assets/Code/Runtime/UI/Shop/ShopPanel.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Cosmetics;
using ZigZag.Runtime.Gameplay.Economy;

namespace ZigZag.Runtime.UI.Shop
{
    /// <summary>
    /// Owns the shop overlay: builds one <see cref="ShopRowView"/> per catalog
    /// entry on <c>Start</c>, refreshes them on every inventory or wallet change,
    /// and toggles <see cref="_panelRoot"/> on/off via <see cref="OpenShop"/>/
    /// <see cref="CloseShop"/> (wired to UI buttons through the inspector).
    /// </summary>
    /// <remarks>
    /// This script lives on a GameObject that is ALWAYS active. The panel
    /// (<see cref="_panelRoot"/>) is a child of it that gets shown/hidden — that
    /// way subscriptions and row construction happen once at scene load, and
    /// only <see cref="_panelRoot"/>'s active state changes per open/close.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ShopPanel : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Catalog of available skins. Display order = array order.")]
        private BallSkinCatalogSO _catalog;

        [SerializeField, Tooltip("Inventory queried for owned/equipped state on refresh.")]
        private SkinInventory _inventory;

        [SerializeField, Tooltip("Wallet queried for current balance and affordability on refresh.")]
        private CoinsWallet _coinsWallet;

        [Header("UI")]
        [SerializeField, Tooltip("Root of the shop overlay. Toggled active in OpenShop/CloseShop.")]
        private GameObject _panelRoot;

        [SerializeField, Tooltip("Parent transform (usually a VerticalLayoutGroup) where rows are instantiated.")]
        private Transform _rowsContainer;

        [SerializeField, Tooltip("Prefab for each shop row.")]
        private ShopRowView _rowPrefab;

        [SerializeField, Tooltip("Header TMP showing the current wallet balance.")]
        private TextMeshProUGUI _walletText;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: any inventory mutation. Triggers row refresh.")]
        private GameEventSO _onInventoryChanged;

        [SerializeField, Tooltip("Listened-to: wallet changes. Triggers row refresh and updates the header.")]
        private IntGameEventSO _onCoinsChanged;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised on OpenShop. InputHandler listens to suspend tap.")]
        private GameEventSO _onShopOpened;

        [SerializeField, Tooltip("Raised on CloseShop. InputHandler listens to resume tap.")]
        private GameEventSO _onShopClosed;

        private List<ShopRowView> _rows;

        public void OpenShop()
        {
            if (_panelRoot != null) _panelRoot.SetActive(true);
            RefreshAll();
            if (_onShopOpened != null) _onShopOpened.Raise();
        }

        public void CloseShop()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
            if (_onShopClosed != null) _onShopClosed.Raise();
        }

        private void Awake()
        {
            Debug.Assert(_catalog != null, $"{nameof(ShopPanel)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_inventory != null, $"{nameof(ShopPanel)} requires a {nameof(SkinInventory)} reference.", this);
            Debug.Assert(_coinsWallet != null, $"{nameof(ShopPanel)} requires a {nameof(CoinsWallet)} reference.", this);
            Debug.Assert(_panelRoot != null, $"{nameof(ShopPanel)} requires {nameof(_panelRoot)}.", this);
            Debug.Assert(_rowsContainer != null, $"{nameof(ShopPanel)} requires {nameof(_rowsContainer)}.", this);
            Debug.Assert(_rowPrefab != null, $"{nameof(ShopPanel)} requires {nameof(_rowPrefab)}.", this);
            Debug.Assert(_walletText != null, $"{nameof(ShopPanel)} requires {nameof(_walletText)}.", this);
            Debug.Assert(_onInventoryChanged != null, $"{nameof(ShopPanel)} requires {nameof(_onInventoryChanged)}.", this);
            Debug.Assert(_onCoinsChanged != null, $"{nameof(ShopPanel)} requires {nameof(_onCoinsChanged)}.", this);
            Debug.Assert(_onShopOpened != null, $"{nameof(ShopPanel)} requires {nameof(_onShopOpened)}.", this);
            Debug.Assert(_onShopClosed != null, $"{nameof(ShopPanel)} requires {nameof(_onShopClosed)}.", this);

            if (_panelRoot != null) _panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (_onInventoryChanged != null) _onInventoryChanged.Register(HandleInventoryChanged);
            if (_onCoinsChanged != null) _onCoinsChanged.Register(HandleCoinsChanged);
        }

        private void OnDisable()
        {
            if (_onInventoryChanged != null) _onInventoryChanged.Unregister(HandleInventoryChanged);
            if (_onCoinsChanged != null) _onCoinsChanged.Unregister(HandleCoinsChanged);
        }

        private void Start()
        {
            BuildRows();
        }

        private void BuildRows()
        {
            IReadOnlyList<BallSkinSO> skins = _catalog.Skins;
            _rows = new List<ShopRowView>(skins.Count);
            for (int i = 0; i < skins.Count; i++)
            {
                BallSkinSO skin = skins[i];
                if (skin == null) continue;
                ShopRowView row = Instantiate(_rowPrefab, _rowsContainer);
                row.Bind(skin);
                _rows.Add(row);
            }
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_rows == null) return; // BuildRows hasn't run yet
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

        private void HandleInventoryChanged() => RefreshAll();
        private void HandleCoinsChanged(int _) => RefreshAll();
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/UI/Shop/ShopPanel.cs Assets/Code/Runtime/UI/Shop/ShopPanel.cs.meta
git commit -m "feat(ui): add ShopPanel overlay with rows builder and refresh"
```

---

## Task 11: Wire `ShopPanel` into `UIController`

**Files:**
- Modify: `Assets/Code/Runtime/UI/UIController.cs`

- [ ] **Step 1: Add `_shopPanel` ref and the click handler**

After the `[Header("Event Channels (Outbound)")]` block (around line 76-78), add:

```csharp
        [Header("Shop")]
        [SerializeField, Tooltip("Shop panel. UIController only triggers OpenShop on the SHOP button click.")]
        private ShopPanel _shopPanel;
```

At the end of the class (after `SetPanels`), add:

```csharp
        /// <summary>
        /// Invoked by the SHOP button's <c>onClick</c> in the Menu panel.
        /// Forwards the request to <see cref="ShopPanel.OpenShop"/>.
        /// </summary>
        public void OnShopButtonClicked()
        {
            if (_shopPanel != null) _shopPanel.OpenShop();
        }
```

Add a `using` at the top:

```csharp
using ZigZag.Runtime.UI.Shop;
```

- [ ] **Step 2: Verify Unity compiles**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Code/Runtime/UI/UIController.cs
git commit -m "feat(ui): add UIController.OnShopButtonClicked to open Shop overlay"
```

---

## Task 12: Add `SkinInventory`, `BallSkinApplier`, `ShopPanel` asserts to `GameBootstrap`

**Files:**
- Modify: `Assets/Code/Runtime/Core/GameBootstrap.cs`

- [ ] **Step 1: Add three serialized refs and three asserts**

At the top, add usings:

```csharp
using ZigZag.Runtime.Gameplay.Cosmetics;
using ZigZag.Runtime.UI.Shop;
```

Wait — `Core` cannot reference `UI` (CLAUDE.md §5: dependency direction is `UI → Gameplay → Core`, never upward). So `GameBootstrap` cannot assert on `ShopPanel`. The Shop panel is validated by `UIController`'s `Awake` if we add an assert there, OR `ShopPanel` self-asserts its dependencies (already does). **Drop the `_shopPanel` assertion from `GameBootstrap`.**

Net change to `GameBootstrap`: 2 new refs (`SkinInventory`, `BallSkinApplier`), both in `Gameplay/Cosmetics/`, both already reachable by the Core → Gameplay asmdef link.

Add the using:

```csharp
using ZigZag.Runtime.Gameplay.Cosmetics;
```

Add two new `[SerializeField]` after the `_coinsWallet` field (around line 44):

```csharp
        [SerializeField, Tooltip("Scene's skin inventory.")]
        private SkinInventory _skinInventory;

        [SerializeField, Tooltip("Ball's BallSkinApplier component.")]
        private BallSkinApplier _ballSkinApplier;
```

Add two asserts at the end of `Awake`:

```csharp
            Debug.Assert(_skinInventory != null, $"{nameof(GameBootstrap)} requires a {nameof(SkinInventory)} reference.", this);
            Debug.Assert(_ballSkinApplier != null, $"{nameof(GameBootstrap)} requires a {nameof(BallSkinApplier)} reference.", this);
```

- [ ] **Step 2: Check the Core asmdef references Gameplay**

Open `Assets/Code/Runtime/Core/ZigZag.Runtime.Core.asmdef`. The `references` array must contain `"ZigZag.Runtime.Gameplay"`. If it's already there (likely, since Core already uses `CoinsWallet`), leave it. If not, add it.

- [ ] **Step 3: Verify Unity compiles**

Console clean. The two new fields show in the GameBootstrap inspector with empty refs — wires come in Task 14.

- [ ] **Step 4: Commit**

```bash
git add Assets/Code/Runtime/Core/GameBootstrap.cs
git commit -m "feat(core): GameBootstrap asserts SkinInventory and BallSkinApplier"
```

---

## Task 13: Create event SO assets + skin assets + materials + catalog (Unity Editor work)

This task is done entirely inside the Unity Editor. No code changes.

**Files (created via Editor menu):**
- 6 event channel assets
- 5 skin assets
- 1 catalog asset
- 4 new materials (the existing ball material is renamed to become the default)

- [ ] **Step 1: Create the 6 event channel SO assets**

In `Assets/Settings/Events/`, right-click → Create → ZigZag → Events:

| Asset to create                            | Menu item                |
|--------------------------------------------|--------------------------|
| `SO_OnSkinPurchaseRequested.asset`         | `String Event`           |
| `SO_OnSkinEquipRequested.asset`            | `String Event`           |
| `SO_OnSkinEquipped.asset`                  | `String Event`           |
| `SO_OnInventoryChanged.asset`              | `Game Event`             |
| `SO_OnShopOpened.asset`                    | `Game Event`             |
| `SO_OnShopClosed.asset`                    | `Game Event`             |

- [ ] **Step 2: Create or identify the 5 ball materials**

In `Assets/Art/`:

1. Locate the current material applied to `P_Ball`'s `MeshRenderer`. Rename it to `M_BallSkin_Default.mat` (Unity will keep the `.meta` GUID, so the prefab reference is preserved).
2. Create 4 new materials (`Assets → Create → Material`): `M_BallSkin_Red.mat`, `M_BallSkin_Green.mat`, `M_BallSkin_Blue.mat`, `M_BallSkin_Gold.mat`.
3. Set each material's shader to **Standard** (Built-in pipeline). Set `Albedo` color:
   - Red: `#E53935`
   - Green: `#43A047`
   - Blue: `#1E88E5`
   - Gold: `#FBC02D`, additionally enable `Emission` with color `#FFD54F` and intensity around 0.5.

- [ ] **Step 3: Create the 5 skin SO assets**

In `Assets/Settings/Skins/` (create the folder), right-click → Create → ZigZag → Cosmetics → `Ball Skin`. For each, fill the inspector:

| Asset filename              | Id        | Display Name | Price | Material              |
|-----------------------------|-----------|--------------|-------|-----------------------|
| `SO_Skin_Default.asset`     | `default` | `Default`    | 0     | `M_BallSkin_Default`  |
| `SO_Skin_Red.asset`         | `red`     | `Crimson`    | 25    | `M_BallSkin_Red`      |
| `SO_Skin_Green.asset`       | `green`   | `Emerald`    | 75    | `M_BallSkin_Green`    |
| `SO_Skin_Blue.asset`        | `blue`    | `Sapphire`   | 200   | `M_BallSkin_Blue`     |
| `SO_Skin_Gold.asset`        | `gold`    | `Gold`       | 500   | `M_BallSkin_Gold`     |

After filling, the Console should show no `OnValidate` errors. If an asset complains about empty Id or missing material, fix it.

- [ ] **Step 4: Create the catalog**

In `Assets/Settings/`, right-click → Create → ZigZag → Cosmetics → `Ball Skin Catalog`. Name the asset `SO_BallSkinCatalog`. In the inspector, set `_skins` size to 5 and drag the 5 skin assets in the order: Default, Red, Green, Blue, Gold (Default MUST be index 0).

Console must show no `OnValidate` errors (no duplicate ids, first skin has price 0).

- [ ] **Step 5: Commit the new assets**

```bash
git status # review every untracked file in Assets/ before staging
git add Assets/Art/ Assets/Settings/
git status # confirm only the expected files (skin SOs, catalog, event SOs, materials) are staged; nothing accidental
git commit -m "feat(cosmetics): add skin catalog, 5 skin SOs, materials and event channels"
```

If `git status` shows unrelated files staged (other materials, scene changes), unstage them with `git restore --staged <path>` and recommit.

If `git status` shows any expected file as untracked or modified that did not make it into the commit, `git add` it and amend with a follow-up commit.

---

## Task 14: Build `P_ShopRow` prefab and wire `BallSkinApplier` on `P_Ball`

**Files:**
- Create: `Assets/Prefabs/UI/P_ShopRow.prefab`
- Modify: `Assets/Prefabs/P_Ball.prefab` (or whatever the ball prefab is named — check `Assets/Prefabs/` for the existing ball prefab)

- [ ] **Step 1: Create `P_ShopRow` prefab**

In Unity, build the row hierarchy inside a temporary scene panel:

```
P_ShopRow (RectTransform, LayoutElement min-height ~80, ShopRowView component)
├── Swatch (Image, ~64x64 px on the left)
├── NameText (TextMeshProUGUI, center-left)
├── PriceText (TextMeshProUGUI, center-right)
└── ActionButton (Button)
    └── Label (TextMeshProUGUI as child)
```

On the root `ShopRowView`, wire the inspector fields:
- `_swatch` → `Swatch` Image
- `_nameText` → `NameText`
- `_priceText` → `PriceText`
- `_actionButton` → `ActionButton`
- `_actionButtonLabel` → `Label`
- `_onSkinPurchaseRequested` → `SO_OnSkinPurchaseRequested`
- `_onSkinEquipRequested` → `SO_OnSkinEquipRequested`

Drag the root into `Assets/Prefabs/UI/` to create `P_ShopRow.prefab`. (Create `Assets/Prefabs/UI/` folder if it does not exist.) Delete the temporary instance from the scene.

- [ ] **Step 2: Add `BallSkinApplier` to the ball prefab**

Open the ball prefab (`Assets/Prefabs/P_Ball.prefab` or similar — confirm path via the existing scene reference). Add component `BallSkinApplier`. Wire:
- `_catalog` → `SO_BallSkinCatalog`
- `_onSkinEquipped` → `SO_OnSkinEquipped`

Save the prefab.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/
git commit -m "feat(prefabs): add P_ShopRow and wire BallSkinApplier on P_Ball"
```

---

## Task 15: Scene wiring — `SkinInventory`, `ShopPanel`, SHOP button, all SO refs

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

This is the longest manual task. Work through every wire methodically — a missing one fails `Debug.Assert` at play.

- [ ] **Step 1: Add `SkinInventory` GameObject**

In `SampleScene`, create an empty GameObject named `SkinInventory` (sibling of `CoinsWallet`, `ScoreManager`, etc.). Add component `SkinInventory`. Wire:
- `_catalog` → `SO_BallSkinCatalog`
- `_coinsWallet` → `CoinsWallet` GameObject in the scene
- `_onSkinPurchaseRequested` → `SO_OnSkinPurchaseRequested`
- `_onSkinEquipRequested` → `SO_OnSkinEquipRequested`
- `_onSkinEquipped` → `SO_OnSkinEquipped`
- `_onInventoryChanged` → `SO_OnInventoryChanged`

- [ ] **Step 2: Build the shop UI hierarchy under Canvas**

Inside the existing UI Canvas, create:

```
ShopPanel (empty GO, always active, ShopPanel component)
└── PanelRoot (RectTransform stretched to full screen, dim background Image, INACTIVE by default)
    ├── Header (TextMeshProUGUI for the wallet "Coins: 0")
    ├── ScrollRect
    │   └── Viewport
    │       └── Content (RectTransform with VerticalLayoutGroup + ContentSizeFitter vertical=PreferredSize)
    └── CloseButton (Button with X label)
```

On the `ShopPanel` component, wire:
- `_catalog` → `SO_BallSkinCatalog`
- `_inventory` → `SkinInventory` GameObject
- `_coinsWallet` → `CoinsWallet` GameObject
- `_panelRoot` → `PanelRoot` GameObject
- `_rowsContainer` → `Content` Transform
- `_rowPrefab` → `P_ShopRow` prefab
- `_walletText` → `Header` TMP
- `_onInventoryChanged` → `SO_OnInventoryChanged`
- `_onCoinsChanged` → `SO_OnCoinsChanged` (existing asset from iteration 4.1)
- `_onShopOpened` → `SO_OnShopOpened`
- `_onShopClosed` → `SO_OnShopClosed`

On `CloseButton`, set `onClick` → `ShopPanel.CloseShop()`.

- [ ] **Step 3: Add a SHOP button in the Menu panel**

Inside the existing Menu panel under the Canvas, add a Button labeled "SHOP". Set its `onClick` → `UIController.OnShopButtonClicked()` (drag the UIController GameObject into the slot, pick the method from the dropdown).

- [ ] **Step 4: Wire `ShopPanel` into `UIController`**

On the `UIController` GameObject in the scene, populate the new `_shopPanel` field with the `ShopPanel` GameObject created in step 2.

- [ ] **Step 5: Wire shop events into `InputHandler`**

On the `InputHandler` GameObject (the one currently driving tap input), wire:
- `_onShopOpened` → `SO_OnShopOpened`
- `_onShopClosed` → `SO_OnShopClosed`

- [ ] **Step 6: Wire `SkinInventory` and `BallSkinApplier` into `GameBootstrap`**

On `GameBootstrap`, populate:
- `_skinInventory` → `SkinInventory` GameObject (from step 1)
- `_ballSkinApplier` → the `BallSkinApplier` on the ball instance in the scene

- [ ] **Step 7: Save the scene and verify Console is clean on play**

`Ctrl+S` to save. Hit Play. Console must show **zero asserts and zero errors** on the first frame. The ball must show the Default material. The Menu panel must show the new SHOP button.

Stop Play. Do NOT skip this check — if any of the ~25 wires above is missing, an assert will fire and tell you which one.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(scene): wire SkinInventory, ShopPanel and SHOP button into SampleScene"
```

---

## Task 16: Manual smoke test in the Editor

This task does no edits — it is the runtime verification that everything works end-to-end. Take screenshots if useful; document any defect immediately as a fixup task.

- [ ] **Step 1: Clear PlayerPrefs to test the cold-boot path**

Unity 2022.3 has **no built-in menu** to clear PlayerPrefs. Use one of:
- **Windows registry (this project's target):** delete the key `HKEY_CURRENT_USER\Software\DefaultCompany\ZigZagPrototype` (or `<Company>\<ProductName>` as set in `Edit → Project Settings → Player`). Refresh `regedit` after.
- **Or skip this step:** existing PlayerPrefs from previous testing will load. If `Coins > 0` or skins already owned, mentally adjust the expected starting state in the following steps.

Decision: skip is acceptable for this smoke test as long as you note your starting wallet/owned set in step 2.

- [ ] **Step 2: Verify boot state**

Hit Play. Expected:
- Ball appears with Default white material.
- Menu panel visible with SHOP button.
- Console clean.

- [ ] **Step 3: Open the shop with empty wallet**

Click SHOP. Expected:
- Shop overlay shows over the Menu.
- Header reads `Coins: 0` (unless you have prior PlayerPrefs coins).
- Default row says "EQUIPPED" (disabled button).
- Red/Green/Blue/Gold rows say "BUY 25" / "BUY 75" / etc., all **disabled** (greyed out).
- Clicking anywhere outside the panel does NOT start the game.

- [ ] **Step 4: Close shop, run, collect coins**

Click X. Shop closes. Tap to start. Collect gems until you have enough for the Red skin (25 coins).

- [ ] **Step 5: Die, open shop, buy Red**

After GameOver, click Retry to return to Menu. Open shop. Red row's button should now be enabled and say "BUY 25". Click it. Expected:
- Wallet header decreases by 25.
- Red row immediately shows "EQUIPPED" (disabled).
- Previous "EQUIPPED" badge moves off Default (Default row now says "EQUIP" enabled).
- Close shop. Tap to start a run. Ball is **red**.

- [ ] **Step 6: Re-equip Default without paying**

Stop, restart play to clear (or end the run normally). In Menu, open shop. Click Default's "EQUIP". Expected: instant skin change to white, wallet unchanged, Red row now shows "EQUIP" enabled.

- [ ] **Step 7: Verify persistence across scene reload**

Equip Red. Stop play. Hit Play again. Expected: ball loads with Red material (PlayerPrefs persisted).

- [ ] **Step 8: Defect log**

If any of the above did not match, file a fix as a follow-up task right now. Common offenders:
- `_rowPrefab` not wired → no rows show.
- `_panelRoot` referenced as the script's own GameObject (not a child) → opening shop also hides the script and breaks subscriptions.
- `ShopRowView` button wired to the wrong event SO → click does nothing or wrong action.
- `BallSkinApplier` not on the ball → material never changes.

- [ ] **Step 9: No code changes? No commit. Otherwise commit fixes**

If you only ran the smoke test and everything worked, skip the commit. If you fixed something, commit it with a focused message:

```bash
git add <fixed files>
git commit -m "fix(<area>): <one-line description>"
```

---

## Task 17: Update GDD, architecture and devlog

**Files:**
- Modify: `zigzag_gdd.md`
- Modify: `zigzag_architecture.md`
- Modify: `devlog.md`

- [ ] **Step 1: Update `zigzag_gdd.md`**

The exact lines change as the doc evolves, so use grep to locate each edit point.

Run (informational, do not commit output):
```bash
grep -n -i -E "powerup|tienda|shop|skin" zigzag_gdd.md
```

Apply the changes described in [spec §8](../specs/2026-05-25-shop-and-ball-skins-design.md):
- §5.6 → rewrite as "Tienda de skins (sustituye al powerup imán originalmente planeado)" describing buy/equip/persistence and the 5 skins.
- §7 (sistemas / orden de implementación) → replace day 6 ("Powerup imán + interfaz IPowerup") with "Tienda de skins + catálogo + SkinInventory".
- §10.2/§10.3 unchanged.
- §10 (or appropriate section): add §10.4 "Shop panel" describing layout (header with coins, vertical scrollable list of rows, single action button per row).
- §11 (decisiones intencionales): change "Tienda y gasto de currency fuera de scope" to "Tienda incluida (5 skins, compra+equip). Sin items consumibles, sin economía de packs, sin daily deals." Move "Múltiples skins" from §12 to §11 marked as `✅ incluido`.
- §12 (out of scope): remove "Múltiples skins" and "Tienda".
- §14 (riesgos / mitigaciones): remove the row about powerup-on-GameOver. Add: "Skin desconocida tras update remoto → drop silencioso en `ParseOwnedCsv`".

- [ ] **Step 2: Update `zigzag_architecture.md`**

Run:
```bash
grep -n -i -E "powerup|skin|shop|ADR-" zigzag_architecture.md
```

Apply:
- §6.2 (catálogo de canales SO): append 6 rows for the new channels. Format must match existing rows.
- §7 (sistemas runtime): add subsections `7.18 BallSkinSO`, `7.19 BallSkinCatalogSO`, `7.20 SkinInventory`, `7.21 BallSkinApplier`, `7.22 ShopPanel`, `7.23 ShopRowView`. Each ~10-15 lines: responsibility, dependencies, public API surface. Reuse signatures from the new source files.
- §7.11/7.12/7.13 (`IPowerup`/`MagnetPowerup`/`PowerupManager`): mark as **descope** with a one-line note pointing to this spec. Keep the historical text — it's context.
- §7.16 (Pools): remove the mention of `PowerupPool`.
- ADR-009 → add addendum dated 2026-05-25: descope of the powerup. Pointer to ADR-015.
- Add ADR-015 "Tienda de skins reemplaza al powerup imán como demostración de arquitectura extensible". Justification: catalog-of-SO + persistent inventory is a more generalizable pattern; visibility for the technical-test evaluator.
- Add ADR-016 "Shop como overlay sobre Menu, sin nuevo GameState. InputHandler suspende tap via canal SO." Justification: shop does not coexist with Playing/GameOver, no new state warranted; the block flag is one line.
- Riesgos section: remove the powerup-on-GameOver row; add "PlayerPrefs `OwnedSkins` contiene id no presente en catálogo → drop silencioso en `ParseOwnedCsv`".

- [ ] **Step 3: Add devlog entry**

Append to `devlog.md` (use the format of the previous entries — they appear in reverse-chronological order in the file; check the most recent entry's heading style):

```markdown
## 2026-05-25 — Iteración 5: tienda de skins

**Objetivo.** Añadir tienda accesible desde Menu para comprar y equipar skins cosméticos del ball, gastando coins persistentes de `CoinsWallet`. Reemplaza el powerup imán (descope).

**Cambios principales.**
1. **`CoinsWallet.TrySpend(int)`** — la API diferida del spec 4.1. Persiste en cada gasto, raise `_onCoinsChanged`. Tests EditMode (3) cubren los tres caminos (ok, fondos insuficientes, amount no positivo).
2. **Nueva sub-feature `Gameplay/Cosmetics/`** con:
   - `BallSkinSO` (SO por skin: id estable, displayName, price, material).
   - `BallSkinCatalogSO` (lista ordenada de skins + `GetById` + `Default`).
   - `SkinInventory` (MonoBehaviour único; owner de PlayerPrefs `"OwnedSkins"` CSV y `"EquippedSkin"`). Maneja purchase + equip via canales SO. `ParseOwnedCsv` `internal static`, testeado (4 tests).
   - `BallSkinApplier` (sobre `P_Ball`; aplica `sharedMaterial` al `MeshRenderer`).
3. **Nueva sub-feature `UI/Shop/`** con:
   - `ShopPanel` (overlay sobre Menu, build de rows en `Start`, refresh en cualquier cambio de wallet o inventario).
   - `ShopRowView` (presentación pura; estado cacheado en enum `RowAction`).
4. **`StringGameEventSO`** — canal con payload `string` para ids de skin.
5. **`InputHandler`** suspende `OnTapped` mientras el shop esté abierto, vía `_onShopOpened`/`_onShopClosed` SO channels.
6. **Asmdef `ZigZag.Runtime.UI`** añade `ZigZag.Runtime.Gameplay` (necesario para refs directas a `SkinInventory` y `CoinsWallet` desde `ShopPanel`).
7. **Descope** de `IPowerup`/`MagnetPowerup`/`PowerupManager`/`PowerupPool` (sólo estaban en docs, sin código a borrar). GDD §5.6 y ADR-009 actualizados con addendum.

**Decisiones de diseño relevantes.**
- **Refs directas UI → Gameplay (no event-only) en `ShopPanel`.** Justificación en spec §4.12: el shop necesita un conjunto (`IsOwned`) y un escalar mutable (`EquippedSkinId`); reconstruirlos vía eventos pediría un mirror local en UI con dos fuentes de verdad sincronizadas. La dirección UI → Gameplay es la explícitamente permitida (CLAUDE.md §5).
- **Tap-block via SO channels (no via UI raycast).** Captura también `Space` y no depende del raycast.
- **`sharedMaterial` no `.material`** en `BallSkinApplier` para evitar instanciar el material por GameObject.
- **PlayerPrefs persistido en cada mutación.** Mismo argumento que `CoinsWallet`: el jugador paga, no le robas.

**Tests añadidos.**
- `CoinsWalletTests` (3 tests: deduct OK, fondos insuficientes, amount no positivo).
- `SkinInventoryTests` (4 tests: parse de ids conocidos, drop de desconocidos, whitespace, CSV vacío).

**Próxima iteración (sugerida).**
- Trail/partículas por skin (campo `TrailRenderer` en `BallSkinSO`).
- Items consumibles vía `IShopItem` (el powerup imán podría volver como item de tienda en vez de pickup en mundo).
```

- [ ] **Step 4: Commit docs**

```bash
git add zigzag_gdd.md zigzag_architecture.md devlog.md
git commit -m "docs: log iteration 5 — shop of ball skins (descope powerup)"
```

---

## Self-Review (engineer reads this after Task 17)

After completing all 17 tasks, run through this checklist before declaring done:

- [ ] All EditMode tests green (`ScoreCalculatorTests`, `CameraFollowMathTests`, `CoinsWalletTests`, `SkinInventoryTests`).
- [ ] Unity Console clean on Play.
- [ ] Smoke test (Task 16) all steps pass on a fresh PlayerPrefs.
- [ ] No `// FIXME`, `// XXX` or untagged TODOs introduced. New TODOs use `// TODO: <text> (<context>)` per CLAUDE.md §2.
- [ ] No `public` mutable fields, no `public` methods that mutate without justification, no `FindObjectOfType` / `GameObject.Find` in new code.
- [ ] No allocations in `Update` of any new component (`ShopPanel.RefreshAll` is called only on events, not in Update).
- [ ] All new MonoBehaviours register in `OnEnable` and unregister symmetrically in `OnDisable`.
- [ ] `git log --oneline -25` shows tidy, conventional-commits messages with no co-author footers.

If anything fails: fix it, commit the fix, re-run.

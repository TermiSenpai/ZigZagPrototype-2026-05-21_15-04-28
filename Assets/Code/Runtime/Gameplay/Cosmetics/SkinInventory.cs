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

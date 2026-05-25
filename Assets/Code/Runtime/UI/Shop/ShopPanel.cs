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

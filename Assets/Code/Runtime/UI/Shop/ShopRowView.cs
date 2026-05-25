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

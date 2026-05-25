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

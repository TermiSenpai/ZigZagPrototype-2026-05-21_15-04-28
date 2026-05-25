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

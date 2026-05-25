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

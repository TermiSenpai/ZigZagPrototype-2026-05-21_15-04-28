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

            // EditMode quirk: SetActive(true) does NOT invoke Awake/OnEnable
            // because there is no PlayerLoop driving MonoBehaviour lifecycle.
            // Invoke them by reflection so TotalCoins is loaded from PlayerPrefs
            // and the wallet subscribes to its event channels.
            _go.SetActive(true);
            InvokeLifecycle(_wallet, "Awake");
            InvokeLifecycle(_wallet, "OnEnable");
        }

        private static void InvokeLifecycle(MonoBehaviour target, string methodName)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m, $"Method '{methodName}' not found on {target.GetType().Name}.");
            m.Invoke(target, null);
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

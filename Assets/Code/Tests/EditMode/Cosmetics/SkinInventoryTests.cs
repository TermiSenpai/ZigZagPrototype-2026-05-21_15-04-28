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

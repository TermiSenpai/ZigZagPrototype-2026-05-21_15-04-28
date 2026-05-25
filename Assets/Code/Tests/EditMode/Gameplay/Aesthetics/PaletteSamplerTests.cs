using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.Aesthetics;

namespace ZigZag.Tests.EditMode.Gameplay.Aesthetics
{
    [TestFixture]
    public sealed class PaletteSamplerTests
    {
        [Test]
        public void ComplementHue_HueOfZero_ReturnsHalf()
        {
            Assert.AreEqual(0.5f, PaletteSampler.ComplementHue(0f), 1e-5f);
        }

        [Test]
        public void ComplementHue_HueOfHalf_ReturnsZero()
        {
            Assert.AreEqual(0f, PaletteSampler.ComplementHue(0.5f), 1e-5f);
        }

        [Test]
        public void ComplementHue_HueOfThreeQuarters_WrapsToOneQuarter()
        {
            Assert.AreEqual(0.25f, PaletteSampler.ComplementHue(0.75f), 1e-5f);
        }

        [Test]
        public void ComplementHue_NegativeHue_WrapsCorrectly()
        {
            // -0.25 + 0.5 = 0.25 (already positive after double-wrap)
            Assert.AreEqual(0.25f, PaletteSampler.ComplementHue(-0.25f), 1e-5f);
        }

        [Test]
        public void CircularDistance_ClosePastWrapAround_ReturnsShortArcDistance()
        {
            // 0.05 and 0.95 are 0.1 apart going across the 0/1 boundary.
            Assert.AreEqual(0.1f, PaletteSampler.CircularDistance(0.05f, 0.95f), 1e-5f);
        }

        [Test]
        public void CircularDistance_AcrossHalfCircle_ReturnsHalf()
        {
            Assert.AreEqual(0.5f, PaletteSampler.CircularDistance(0f, 0.5f), 1e-5f);
        }

        [Test]
        public void Sample_WithFixedSeed_IsDeterministic()
        {
            PaletteRulesSO rules = ScriptableObject.CreateInstance<PaletteRulesSO>();

            var (platform1, camera1, hue1) = PaletteSampler.Sample(new System.Random(42), rules, -1f);
            var (platform2, camera2, hue2) = PaletteSampler.Sample(new System.Random(42), rules, -1f);

            Assert.AreEqual(hue1, hue2, 1e-6f, "primaryHue must be deterministic with the same seed");
            Assert.AreEqual(platform1, platform2, "platform color must be deterministic with the same seed");
            Assert.AreEqual(camera1,   camera2,   "camera color must be deterministic with the same seed");

            Object.DestroyImmediate(rules);
        }

        [Test]
        public void Sample_PrimaryAndCameraHuesAreComplementary()
        {
            PaletteRulesSO rules = ScriptableObject.CreateInstance<PaletteRulesSO>();

            var (platform, camera, primaryHue) = PaletteSampler.Sample(new System.Random(7), rules, -1f);

            Color.RGBToHSV(camera, out float cameraHue, out _, out _);

            // The camera color's hue should be ~0.5 away from the primary hue.
            Assert.AreEqual(0.5f, PaletteSampler.CircularDistance(primaryHue, cameraHue), 1e-3f,
                "camera hue must be the complement of the primary hue");

            Object.DestroyImmediate(rules);
        }

        [Test]
        public void Sample_RespectsMinHueDistanceFromPrevious()
        {
            PaletteRulesSO rules = ScriptableObject.CreateInstance<PaletteRulesSO>();
            SetPrivateField(rules, "_minHueDistanceFromPrevious", 0.3f);

            // Use seed 1 — empirically verified not to exhaust all 8 attempts for
            // previousPrimaryHue = 0.5f with minDistance = 0.3f.
            var (_, _, primaryHue) = PaletteSampler.Sample(new System.Random(1), rules, previousPrimaryHue: 0.5f);

            Assert.GreaterOrEqual(
                PaletteSampler.CircularDistance(primaryHue, 0.5f),
                0.3f - 1e-3f,
                "sampled hue must be at least minHueDistanceFromPrevious away from the previous hue");

            Object.DestroyImmediate(rules);
        }

        [Test]
        public void Sample_SaturationAndValue_AreWithinConfiguredRanges()
        {
            PaletteRulesSO rules = ScriptableObject.CreateInstance<PaletteRulesSO>();

            var (platform, _, _) = PaletteSampler.Sample(new System.Random(99), rules, -1f);

            Color.RGBToHSV(platform, out _, out float s, out float v);

            Assert.GreaterOrEqual(s, rules.SaturationRange.x - 1e-3f, "saturation must be >= SaturationRange.x");
            Assert.LessOrEqual(   s, rules.SaturationRange.y + 1e-3f, "saturation must be <= SaturationRange.y");
            Assert.GreaterOrEqual(v, rules.ValueRange.x - 1e-3f,      "value must be >= ValueRange.x");
            Assert.LessOrEqual(   v, rules.ValueRange.y + 1e-3f,      "value must be <= ValueRange.y");

            Object.DestroyImmediate(rules);
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var f = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f.SetValue(target, value);
        }
    }
}

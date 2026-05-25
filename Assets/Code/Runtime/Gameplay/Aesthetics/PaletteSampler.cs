using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Aesthetics
{
    /// <summary>
    /// Pure-static helper that samples a complementary color pair from a
    /// <see cref="PaletteRulesSO"/>. Callers must pass a seeded
    /// <see cref="System.Random"/> instance to ensure deterministic output.
    /// Has no Unity lifecycle or state of its own; safe to call from any
    /// thread or edit-mode test.
    /// </summary>
    internal static class PaletteSampler
    {
        private const int MaxHueSamples = 8;

        /// <summary>
        /// Samples a new complementary color pair that respects the hue-distance
        /// constraint in <paramref name="rules"/>.
        /// </summary>
        /// <param name="rng">Seeded random instance owned by the caller.</param>
        /// <param name="rules">Palette configuration asset.</param>
        /// <param name="previousPrimaryHue">
        /// Primary hue used in the previous palette swap, in [0, 1). Pass any value
        /// outside [0, 1) (e.g. <c>-1f</c>) on the very first call to skip the
        /// distance check.
        /// </param>
        /// <returns>
        /// A tuple of <c>platform</c> color, <c>camera</c> color (complement hue,
        /// same saturation and value), and the sampled <c>primaryHue</c> for the
        /// caller to store and pass back next time.
        /// </returns>
        public static (Color platform, Color camera, float primaryHue) Sample(
            System.Random rng,
            PaletteRulesSO rules,
            float previousPrimaryHue)
        {
            float hue = 0f;
            for (int attempt = 0; attempt < MaxHueSamples; attempt++)
            {
                hue = (float)rng.NextDouble();
                bool checkDistance = previousPrimaryHue >= 0f && previousPrimaryHue < 1f;
                if (!checkDistance) break;
                if (CircularDistance(hue, previousPrimaryHue) >= rules.MinHueDistanceFromPrevious) break;
                // else loop continues; on the final iteration we exit the loop with the
                // last sampled hue, which is the documented fallback behavior.
            }

            float sat = SampleRange(rng, rules.SaturationRange.x, rules.SaturationRange.y);
            float val = SampleRange(rng, rules.ValueRange.x, rules.ValueRange.y);

            Color platform = Color.HSVToRGB(hue, sat, val);
            Color camera   = Color.HSVToRGB(ComplementHue(hue), sat, val);

            return (platform, camera, hue);
        }

        /// <summary>
        /// Returns the hue directly opposite <paramref name="hue"/> on the color wheel
        /// (offset by 0.5, wrapped into [0, 1)).
        /// </summary>
        public static float ComplementHue(float hue)
        {
            return ((hue + 0.5f) % 1f + 1f) % 1f;
        }

        /// <summary>
        /// Returns the minimum angular distance between two hues on the unit circle
        /// (result is always in [0, 0.5]).
        /// </summary>
        public static float CircularDistance(float a, float b)
        {
            float diff = Mathf.Abs(a - b);
            return Mathf.Min(diff, 1f - diff);
        }

        private static float SampleRange(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}

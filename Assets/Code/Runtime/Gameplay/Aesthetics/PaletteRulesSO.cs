using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Aesthetics
{
    /// <summary>
    /// Configuration asset for the palette-cycling feature. Defines when palette swaps
    /// occur (score threshold), how long each color transition lasts, the HSV ranges
    /// used when sampling new colors, and the boot/reset colors for the camera and
    /// platform material. All fields are serialized and exposed through get-only
    /// properties; tunable values that are not mutated at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "ZigZag/Aesthetics/Palette Rules", fileName = "SO_PaletteRules")]
    public sealed class PaletteRulesSO : ScriptableObject
    {
        [Header("Timing")]
        [SerializeField, Tooltip("Score points between palette swaps. Must be >= 1.")]
        private int _scoreThresholdStep = 50;

        [SerializeField, Tooltip("Duration in seconds of the lerp between two palette states. Clamped to [0.5, 3.0].")]
        private float _transitionSeconds = 1.5f;

        [Header("HSV Sampling")]
        [SerializeField, Tooltip("Saturation range (x = min, y = max) used when sampling a new primary hue. Both values in [0, 1]; x <= y.")]
        private Vector2 _saturationRange = new Vector2(0.55f, 0.85f);

        [SerializeField, Tooltip("Value (brightness) range (x = min, y = max) used when sampling a new primary hue. Both values in [0, 1]; x <= y.")]
        private Vector2 _valueRange = new Vector2(0.70f, 0.95f);

        [SerializeField, Tooltip("Minimum circular hue distance from the previously sampled primary hue. Prevents nearly identical consecutive palettes. Clamped to [0, 0.5].")]
        private float _minHueDistanceFromPrevious = 0.15f;

        [Header("Initial Colors")]
        [SerializeField, Tooltip("Color applied to the platform material at boot and after every game reset. Matches the current _Color of Assets/Art/M_Platform.mat.")]
        private Color _initialPlatformColor = new Color(0.14117646f, 0.52665764f, 0.8980392f, 1f);

        [SerializeField, Tooltip("Solid background clear color used by the camera at boot and after every game reset. Matches the current m_BackGroundColor in SampleScene.unity.")]
        private Color _initialCameraColor = new Color(0.76495194f, 0.797839f, 0.8490566f, 0f);

        [Header("Shader")]
        [SerializeField, Tooltip("Name of the shader color property to set on the platform material. Default matches the Built-in Standard shader (_Color); change to _BaseColor for URP without touching code.")]
        private string _colorShaderProperty = "_Color";

        /// <summary>Points between palette swaps.</summary>
        public int ScoreThresholdStep => _scoreThresholdStep;

        /// <summary>Duration in seconds of the lerp between palette states.</summary>
        public float TransitionSeconds => _transitionSeconds;

        /// <summary>HSV saturation range (x = min, y = max).</summary>
        public Vector2 SaturationRange => _saturationRange;

        /// <summary>HSV value (brightness) range (x = min, y = max).</summary>
        public Vector2 ValueRange => _valueRange;

        /// <summary>Minimum circular hue distance from the previous primary hue.</summary>
        public float MinHueDistanceFromPrevious => _minHueDistanceFromPrevious;

        /// <summary>Platform material color at boot / after reset.</summary>
        public Color InitialPlatformColor => _initialPlatformColor;

        /// <summary>Camera clear color at boot / after reset.</summary>
        public Color InitialCameraColor => _initialCameraColor;

        /// <summary>Shader property name used to tint the platform material.</summary>
        public string ColorShaderProperty => _colorShaderProperty;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_scoreThresholdStep < 1)
                _scoreThresholdStep = 1;

            if (_transitionSeconds < 0.5f)
                _transitionSeconds = 0.5f;
            else if (_transitionSeconds > 3.0f)
                _transitionSeconds = 3.0f;

            _saturationRange.x = Mathf.Clamp01(_saturationRange.x);
            _saturationRange.y = Mathf.Clamp(_saturationRange.y, _saturationRange.x, 1f);

            _valueRange.x = Mathf.Clamp01(_valueRange.x);
            _valueRange.y = Mathf.Clamp(_valueRange.y, _valueRange.x, 1f);

            if (_minHueDistanceFromPrevious < 0f)
                _minHueDistanceFromPrevious = 0f;
            else if (_minHueDistanceFromPrevious > 0.5f)
                _minHueDistanceFromPrevious = 0.5f;

            if (string.IsNullOrWhiteSpace(_colorShaderProperty))
                _colorShaderProperty = "_Color";
        }
#endif
    }
}

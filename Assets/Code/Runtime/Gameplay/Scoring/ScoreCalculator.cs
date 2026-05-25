using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Scoring
{
    /// <summary>
    /// Pure helpers for score arithmetic. No Unity lifecycle, no state — every method
    /// is deterministic given its inputs, which keeps the rules easy to unit-test in
    /// EditMode without touching MonoBehaviours or PlayerPrefs.
    /// </summary>
    public static class ScoreCalculator
    {
        /// <summary>
        /// Projects the ball's displacement from <paramref name="origin"/> onto
        /// <paramref name="forwardAxis"/> and floors the result into integer points,
        /// scaled by <paramref name="multiplier"/>. Negative progress is clamped to
        /// zero so a misconfigured spawn origin can never produce a negative score.
        /// </summary>
        /// <param name="ballPosition">Current world position of the ball.</param>
        /// <param name="origin">World position the ball started from (path spawn point).</param>
        /// <param name="forwardAxis">Unit vector representing forward progress. The path
        /// uses <c>(-1, 0, 1).normalized</c> — the diagonal between the two ball directions.</param>
        /// <param name="multiplier">Points per integer unit of progress.</param>
        public static int ComputeDistanceScore(Vector3 ballPosition, Vector3 origin, Vector3 forwardAxis, int multiplier)
        {
            float progress = Vector3.Dot(ballPosition - origin, forwardAxis);
            if (progress < 0f) progress = 0f;
            return Mathf.FloorToInt(progress) * multiplier;
        }
    }
}

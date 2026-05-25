using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.Scoring;

namespace ZigZag.Tests.EditMode.Scoring
{
    [TestFixture]
    public sealed class ScoreCalculatorTests
    {
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
        private static readonly Vector3 Origin = new Vector3(-2f, 0f, 3f);

        [Test]
        public void ComputeDistanceScore_AtOrigin_ReturnsZero()
        {
            int score = ScoreCalculator.ComputeDistanceScore(Origin, Origin, GlobalForward, multiplier: 1);
            Assert.AreEqual(0, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedAlongPositiveZ_ReturnsPositiveProgress()
        {
            Vector3 ballPosition = new Vector3(-2f, 0f, 10f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            // Δ = (0, 0, 7); dot with (-1,0,1)/√2 = 7/√2 ≈ 4.949; floor = 4.
            Assert.AreEqual(4, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedAlongNegativeX_ReturnsPositiveProgress()
        {
            Vector3 ballPosition = new Vector3(-9f, 0f, 3f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            // Δ = (-7, 0, 0); dot with (-1,0,1)/√2 = 7/√2 ≈ 4.949; floor = 4.
            Assert.AreEqual(4, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedDiagonally_AccumulatesBothAxes()
        {
            Vector3 ballPosition = new Vector3(-9f, 0f, 10f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            // Δ = (-7, 0, 7); dot = 14/√2 ≈ 9.899; floor = 9.
            Assert.AreEqual(9, score);
        }

        [Test]
        public void ComputeDistanceScore_BallMovedBackwards_ReturnsZero()
        {
            // Ball cannot actually move backwards in gameplay, but the API must clamp.
            Vector3 ballPosition = new Vector3(5f, 0f, -3f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 1);
            Assert.AreEqual(0, score);
        }

        [Test]
        public void ComputeDistanceScore_MultiplierScalesResult()
        {
            Vector3 ballPosition = new Vector3(-2f, 0f, 10f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 3);
            // Floor first, then multiply: 4 * 3 = 12.
            Assert.AreEqual(12, score);
        }

        [Test]
        public void ComputeDistanceScore_ZeroMultiplier_ReturnsZero()
        {
            Vector3 ballPosition = new Vector3(-2f, 0f, 100f);
            int score = ScoreCalculator.ComputeDistanceScore(ballPosition, Origin, GlobalForward, multiplier: 0);
            Assert.AreEqual(0, score);
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheGuild.Core.Data;

namespace Tests.EditMode.Core.Data
{
    public sealed class RandomPoolTests
    {
        [Test]
        public void PickWithoutReplacement_Uniform_NoDuplicates()
        {
            var source = new List<string> { "a", "b", "c", "d" };

            List<string> picked = RandomPool.PickWithoutReplacement(
                source,
                pickCount: 3,
                pickMode: "uniform",
                weights: null,
                random: new Random(10),
                out bool fallback);

            Assert.IsFalse(fallback);
            Assert.AreEqual(3, picked.Count);
            Assert.AreNotEqual(picked[0], picked[1]);
            Assert.AreNotEqual(picked[1], picked[2]);
            Assert.AreNotEqual(picked[0], picked[2]);
        }

        [Test]
        public void PickWithoutReplacement_WeightedAllZero_FallbackToUniform()
        {
            var source = new List<int> { 1, 2, 3 };

            List<int> picked = RandomPool.PickWithoutReplacement(
                source,
                pickCount: 2,
                pickMode: "weighted",
                weights: new List<float> { 0f, 0f, 0f },
                random: new Random(7),
                out bool fallback);

            Assert.IsTrue(fallback);
            Assert.AreEqual(2, picked.Count);
        }

        [Test]
        public void PickWithoutReplacement_PickCountExceedsPool_ReturnAll()
        {
            var source = new List<int> { 10, 20, 30 };

            List<int> picked = RandomPool.PickWithoutReplacement(
                source,
                pickCount: 10,
                pickMode: "uniform",
                weights: null,
                random: new Random(99),
                out bool fallback);

            Assert.IsFalse(fallback);
            Assert.AreEqual(3, picked.Count);
        }
    }
}

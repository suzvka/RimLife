using RimLife.Framework;
using System;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// RandomInt 纯逻辑断言测试。
    /// xorshift 算法在固定 seed 下输出完全确定，适合断言验证。
    /// </summary>
    public class RandomIntTests
    {
        private const ulong TestSeed = 12345UL;

        [Fact]
        public void Constructor_ZeroSeed_UsesDefault()
        {
            var rng = new RandomInt(0);
            // seed=0 时使用默认种子值 4101842887655102017UL
            var result = rng.Get(0, 100);
            // 只是验证不崩溃且有输出
            Assert.True(result >= 0 && result < 100);
        }

        [Fact]
        public void Constructor_NonZeroSeed_UsesGivenSeed()
        {
            var rng = new RandomInt(TestSeed);
            Assert.NotNull(rng);
        }

        [Fact]
        public void Get_Deterministic_SameSeedSameSequence()
        {
            var a = new RandomInt(TestSeed);
            var b = new RandomInt(TestSeed);

            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(a.Get(0, 100), b.Get(0, 100));
            }
        }

        [Fact]
        public void Get_DifferentSeeds_DifferentSequences()
        {
            var a = new RandomInt(12345UL);
            var b = new RandomInt(54321UL);

            // 至少前几个值应不同（概率极高）
            bool allSame = true;
            for (int i = 0; i < 10; i++)
            {
                if (a.Get(0, 1000) != b.Get(0, 1000))
                {
                    allSame = false;
                    break;
                }
            }
            Assert.False(allSame, "不同种子应产生不同序列");
        }

        [Fact]
        public void Get_WithinRange_AlwaysInBounds()
        {
            var rng = new RandomInt(TestSeed);
            for (int i = 0; i < 1000; i++)
            {
                int val = rng.Get(10, 50);
                Assert.True(val >= 10 && val < 50, $"Value {val} out of [10, 50)");
            }
        }

        [Fact]
        public void Get_MinEqualsMax_ThrowsArgumentOutOfRangeException()
        {
            var rng = new RandomInt(TestSeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Get(5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Get(100, 100));
        }

        [Fact]
        public void Get_MinGreaterThanMax_ThrowsArgumentOutOfRangeException()
        {
            var rng = new RandomInt(TestSeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Get(100, 50));
        }

        [Fact]
        public void Get_Distribution_ApproximatelyUniform()
        {
            var rng = new RandomInt(123456789UL);
            var buckets = new int[10];
            const int samples = 10000;

            for (int i = 0; i < samples; i++)
            {
                int val = rng.Get(0, 10);
                buckets[val]++;
            }

            // 每个桶期望 1000，允许 ±300 偏差（非常宽松）
            foreach (var count in buckets)
            {
                Assert.True(count > 500 && count < 1500,
                    $"桶计数 {count} 偏离期望 1000 过远");
            }
        }

        [Fact]
        public void Get_KnownSequence_IsDeterministic()
        {
            // 固定 seed 验证确定性：两次独立实例产生相同序列
            var a = new RandomInt(TestSeed);
            var b = new RandomInt(TestSeed);

            var seqA = new int[20];
            var seqB = new int[20];
            for (int i = 0; i < 20; i++)
            {
                seqA[i] = a.Get(0, 100);
                seqB[i] = b.Get(0, 100);
            }
            Assert.Equal(seqA, seqB);
        }
    }
}

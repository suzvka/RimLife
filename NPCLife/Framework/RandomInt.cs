using System;

namespace NPCLife.Framework
{
    /// <summary>
    /// 轻量 xorshift 伪随机数生成器。零外部依赖。
    /// </summary>
    public class RandomInt
    {
        private ulong _state;

        public RandomInt(ulong seed = 4101842887655102017UL)
        {
            _state = seed == 0 ? 4101842887655102017UL : seed;
        }

        /// <summary>
        /// 返回 [min, max) 范围内的随机整数。
        /// </summary>
        public int Get(int min, int max)
        {
            if (min >= max)
                throw new ArgumentOutOfRangeException(nameof(min), $"min({min}) must be less than max({max})");

            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;

            uint range = (uint)(max - min);
            ulong randomValue = _state * 0x2545F4914F6CDD1DUL;
            return (int)((randomValue >> 32) % range) + min;
        }
    }
}

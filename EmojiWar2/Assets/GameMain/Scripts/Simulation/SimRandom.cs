//------------------------------------------------------------
// EmojiWar GameMain - 确定性随机数生成器
// xorshift32 实现：同一种子 + 相同调用序列 → 所有端结果一致。
// 帧同步（Lockstep）核心依赖：绝不用 UnityEngine.Random。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>
    /// 确定性随机（xorshift32）：用于帧同步模拟的随机数。
    /// </summary>
    public struct SimRandom
    {
        private uint m_State;

        public SimRandom(uint seed)
        {
            m_State = seed != 0u ? seed : 0x853C49E2u;
        }

        /// <summary>下一个 32 位无符号随机数。</summary>
        public uint NextUInt()
        {
            uint x = m_State;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            m_State = x;
            return x;
        }

        /// <summary>[0,1) 浮点随机。</summary>
        public float NextFloat()
        {
            return (NextUInt() & 0xFFFFFFu) / 16777216f;
        }

        /// <summary>[min,max) 整数随机。</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
            {
                return min;
            }
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }

        /// <summary>[min,max] 浮点随机。</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>单位圆内随机点（用于敌人生成位置）。</summary>
        public Vector2 InsideUnitCircle()
        {
            float angle = NextFloat() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(NextFloat());   // 均匀分布
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        /// <summary>均匀随机方向（用于子弹散射）。</summary>
        public Vector2 RandomDirection()
        {
            float angle = NextFloat() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
    }
}

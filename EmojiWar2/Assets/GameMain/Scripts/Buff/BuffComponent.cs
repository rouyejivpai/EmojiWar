//------------------------------------------------------------
// EmojiWar GameMain - Buff 组件
// 挂在实体上，统一管理所有 Buff：添加/叠加/移除/查询修正。
// 实体通过 IBuffTarget 查询修正后的属性。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Buff
{
    /// <summary>
    /// Buff 组件：管理实体身上的所有状态效果。
    /// </summary>
    public class BuffComponent : MonoBehaviour, IBuffTarget
    {
        private readonly List<BuffInstance> m_Buffs = new List<BuffInstance>();

        // ---- IBuffTarget ----

        public float MoveSpeedMultiplier
        {
            get
            {
                float multiplier = 1f;
                foreach (var buff in m_Buffs)
                {
                    if (buff.Type == BuffType.Slow)
                    {
                        multiplier = Mathf.Min(multiplier, 1f - buff.Strength);
                    }
                }
                return multiplier;
            }
        }

        public bool IsStunned
        {
            get
            {
                foreach (var buff in m_Buffs)
                {
                    if (buff.Type == BuffType.Stun)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool IsInvulnerable
        {
            get
            {
                foreach (var buff in m_Buffs)
                {
                    if (buff.Type == BuffType.Invulnerable)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>当前 Buff 数量。</summary>
        public int BuffCount
        {
            get { return m_Buffs.Count; }
        }

        /// <summary>全部 Buff（只读）。</summary>
        public IReadOnlyList<BuffInstance> Buffs
        {
            get { return m_Buffs; }
        }

        private void Update()
        {
            TickBuffs(Time.deltaTime);
        }

        /// <summary>
        /// 添加 Buff（同类型叠加）。
        /// </summary>
        public BuffInstance AddBuff(BuffType type, float duration, float strength, int stacks = 1, int maxStacks = 1)
        {
            // 查找同类型
            BuffInstance existing = null;
            foreach (var buff in m_Buffs)
            {
                if (buff.Type == type)
                {
                    existing = buff;
                    break;
                }
            }

            if (existing != null)
            {
                // 叠加层数
                existing.Stacks = Mathf.Min(existing.Stacks + stacks, existing.MaxStacks);
                if (existing.RefreshDurationOnStack)
                {
                    existing.Duration = Mathf.Max(existing.Duration, duration);
                }
                existing.Strength = Mathf.Max(existing.Strength, strength);
                return existing;
            }

            var newBuff = new BuffInstance(type, duration, strength, stacks, maxStacks);
            m_Buffs.Add(newBuff);
            return newBuff;
        }

        /// <summary>
        /// 移除指定类型的全部 Buff。
        /// </summary>
        public void RemoveBuff(BuffType type)
        {
            m_Buffs.RemoveAll(b => b.Type == type);
        }

        /// <summary>
        /// 清空所有 Buff。
        /// </summary>
        public void ClearAllBuffs()
        {
            m_Buffs.Clear();
        }

        /// <summary>
        /// 是否拥有指定类型的 Buff。
        /// </summary>
        public bool HasBuff(BuffType type)
        {
            foreach (var buff in m_Buffs)
            {
                if (buff.Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取指定类型 Buff 的总强度（累加）。
        /// </summary>
        public float GetBuffStrength(BuffType type)
        {
            float total = 0f;
            foreach (var buff in m_Buffs)
            {
                if (buff.Type == type)
                {
                    total += buff.Strength;
                }
            }
            return total;
        }

        /// <summary>
        /// 推进所有 Buff 计时，移除到期 Buff。
        /// </summary>
        private void TickBuffs(float deltaTime)
        {
            for (int i = m_Buffs.Count - 1; i >= 0; i--)
            {
                var buff = m_Buffs[i];
                if (buff.ShouldRemove())
                {
                    m_Buffs.RemoveAt(i);
                    continue;
                }

                if (!buff.IsPermanent)
                {
                    buff.Duration -= deltaTime;
                }
            }
        }
    }
}

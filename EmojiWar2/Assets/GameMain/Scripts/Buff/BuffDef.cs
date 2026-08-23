//------------------------------------------------------------
// EmojiWar GameMain - Buff 定义
// 统一状态效果模型：减速/眩晕等。
// 纯数据 + 回调，由 BuffComponent 管理生命周期。
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EmojiWar.GameMain.Buff
{
    /// <summary>
    /// Buff 类型。
    /// </summary>
    public enum BuffType
    {
        None = 0,
        Slow = 1,       // 减速
        Stun = 2,       // 眩晕
        FireRateUp = 3, // 攻速提升（武器）
        Invulnerable = 4, // 无敌
    }

    /// <summary>
    /// Buff 实例（纯数据）。
    /// </summary>
    public sealed class BuffInstance
    {
        public BuffType Type;
        public float Duration;          // 剩余时长（-1 永久）
        public float Strength;          // 强度（减速系数等 0~1）
        public int Stacks;
        public int MaxStacks;
        public bool IsPermanent;

        /// <summary>叠加时是否刷新时长。</summary>
        public bool RefreshDurationOnStack = true;

        public BuffInstance(BuffType type, float duration, float strength, int stacks = 1, int maxStacks = 1)
        {
            Type = type;
            Duration = duration;
            Strength = strength;
            Stacks = stacks;
            MaxStacks = maxStacks;
            IsPermanent = duration < 0f;
        }

        /// <summary>是否到期应移除。</summary>
        public bool ShouldRemove()
        {
            return !IsPermanent && Duration <= 0f;
        }
    }

    /// <summary>
    /// Buff 结果接口（供 BuffComponent 查询/修改）。
    /// </summary>
    public interface IBuffTarget
    {
        /// <summary>移动速度倍率（Buff 修正后的实际值）。</summary>
        float MoveSpeedMultiplier { get; }

        /// <summary>是否被眩晕。</summary>
        bool IsStunned { get; }

        /// <summary>是否无敌。</summary>
        bool IsInvulnerable { get; }
    }
}

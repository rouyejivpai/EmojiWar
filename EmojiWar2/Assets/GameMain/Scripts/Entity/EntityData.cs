//------------------------------------------------------------
// EmojiWar GameMain - 实体数据（纯数据，可网络同步）
// 与 MonoBehaviour 解耦：后续网络化时此数据类可直接序列化同步。
//------------------------------------------------------------

using System;

namespace EmojiWar.GameMain.Entity
{
    /// <summary>
    /// 实体阵营。
    /// </summary>
    public enum EntityTeam
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2,
    }

    /// <summary>
    /// 实体状态。
    /// </summary>
    public enum EntityState
    {
        Idle = 0,
        Moving = 1,
        Attacking = 2,
        Stunned = 3,
        Dead = 4,
    }

    /// <summary>
    /// 实体战斗数据（纯数据，跨网络/表现层复用）。
    /// </summary>
    [Serializable]
    public sealed class EntityData
    {
        /// <summary>实体唯一 ID（网络同步用）。</summary>
        public int EntityId = 0;

        /// <summary>阵营。</summary>
        public EntityTeam Team = EntityTeam.Neutral;

        /// <summary>最大生命值。</summary>
        public float MaxHealth = 100f;

        /// <summary>当前生命值。</summary>
        public float CurrentHealth = 100f;

        /// <summary>移动速度。</summary>
        public float MoveSpeed = 5f;

        /// <summary>是否存活。</summary>
        public bool IsAlive = true;

        /// <summary>当前状态。</summary>
        public EntityState State = EntityState.Idle;

        public EntityData() { }

        public EntityData(int entityId, EntityTeam team, float maxHealth, float moveSpeed)
        {
            EntityId = entityId;
            Team = team;
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
            MoveSpeed = moveSpeed;
            IsAlive = true;
            State = EntityState.Idle;
        }

        /// <summary>生命值百分比。</summary>
        public float HealthPercent
        {
            get
            {
                return MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
            }
        }

        /// <summary>重置为满血存活。</summary>
        public void Reset()
        {
            CurrentHealth = MaxHealth;
            IsAlive = true;
            State = EntityState.Idle;
        }
    }
}

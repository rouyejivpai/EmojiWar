//------------------------------------------------------------
// EmojiWar GameMain - 施法事件总线（CastEventBus，D10 / S4）
//
// 依据：doc/法术编程玩法设计文档.md §4（被动触发系统）
//         §4.1 主动物品=顺序执行 / 被动物品=事件驱动
//         §4.3 触发事件类型（Q5 第一版集合 = 施法开始/结束/序列清空 + 命中 + 击杀）
//         §4.4 触发目标与顺序；§4.5 资源模式（后扣 + 限次）；§4.6 序列指针规则（临时指针 + 嵌套 ≤3）
//       doc/法术编程系统-执行文档.md §1.2 D10/D11、流程 F/G、§4（被动剩余次数/冷却入哈希）
//
// 定位：**模拟层（命中/击杀）与施法解释器（被动触发）之间的解耦缝**。事件源有两个：
//   · `CastResolver` —— 施法类事件（施法开始/结束/序列清空）。它们在**同帧内就地**触发被动，
//     **不经总线**（总线只为"施法之外产生的事件"服务，避免无谓的绕行）。
//   · `LockstepSimulation` —— 命中/击杀。它们由子弹与敌人的交互产生，发生在施法流程之外，
//     必须经总线转交给对应那一手的解释器。
//
// 确定性（AGENTS §三）：
//   · **不是 C# 委托事件，而是有序队列（FIFO）**。委托的多播顺序与订阅方数量不可控，
//     两端一旦不一致就会分叉 —— 模拟层禁止使用。
//   · 每帧由 `LockstepSimulation` 按**固定的玩家顺序**，把属于各 SessionId 的事件交给该玩家的两手处理；
//     同一 SessionId 内部按入队顺序消费。
//
// ⚠️ **必须是实例，不能做成静态类**：回环/联机对拍会在**同一进程**里跑两个模拟，
//   静态队列会让两个模拟互相偷事件（非确定性）。每个 `LockstepSimulation` 持有自己的总线。
//------------------------------------------------------------

using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>外部（非施法流程内产生的）事件种类。</summary>
    public enum CastExternalEventKind
    {
        None = 0,
        Hit = 1,    // 命中敌人（对应 SpellPassiveEvent.Hit）
        Kill = 2,   // 击杀敌人（对应 SpellPassiveEvent.Kill）
    }

    /// <summary>一条外部事件（纯值类型；载荷足够被动做目标/条件判定）。</summary>
    public struct CastExternalEvent
    {
        public CastExternalEventKind Kind;

        /// <summary>造成该事件的玩家（`SimBullet.OwnerSession`）；用于把事件归属到某一手。</summary>
        public int OwnerSession;

        /// <summary>命中：子弹实体 Id（设计 §流程 G：携带子弹 Id）。</summary>
        public int BulletId;

        /// <summary>命中：子弹标签位掩码（`SpellTag`），供标签过滤型被动使用。</summary>
        public int Tags;

        /// <summary>命中/击杀：目标敌人实体 Id。</summary>
        public int TargetEntityId;

        /// <summary>击杀：击杀者玩家实体 Id（设计 §流程 G：携带击杀者）。</summary>
        public int KillerEntityId;

        /// <summary>命中：本次伤害（取整；供阈值型被动使用）。</summary>
        public int Damage;
    }

    /// <summary>
    /// 施法外部事件总线（每模拟一个实例；FIFO；无委托 → 确定性）。
    /// </summary>
    public sealed class CastEventBus
    {
        /// <summary>初始容量（结构常量：FIFO 数组起点，会按需扩容，不是"上限"）。</summary>
        public const int DefaultCapacity = 64;

        private CastExternalEvent[] m_Items;
        private int m_Count;

        public CastEventBus() : this(DefaultCapacity) { }

        public CastEventBus(int capacity)
        {
            m_Items = new CastExternalEvent[capacity > 0 ? capacity : DefaultCapacity];
            m_Count = 0;
        }

        /// <summary>当前待消费事件数。</summary>
        public int Count { get { return m_Count; } }

        /// <summary>累计发布数（探针/诊断用；**不进状态哈希**）。</summary>
        public int TotalPublished { get; private set; }

        /// <summary>累计消费数（探针/诊断用；**不进状态哈希**）。</summary>
        public int TotalConsumed { get; private set; }

        /// <summary>累计因容量不足被丢弃的事件数（正常应为 0）。</summary>
        public int DroppedCount { get; private set; }

        /// <summary>发布"命中"。`tags` 为 `SpellTag` 位掩码。</summary>
        public void PublishHit(int ownerSession, int bulletId, SpellTag tags, int targetEntityId, int damage)
        {
            var e = new CastExternalEvent();
            e.Kind = CastExternalEventKind.Hit;
            e.OwnerSession = ownerSession;
            e.BulletId = bulletId;
            e.Tags = (int)tags;
            e.TargetEntityId = targetEntityId;
            e.Damage = damage;
            Enqueue(e);
        }

        /// <summary>发布"击杀"。</summary>
        public void PublishKill(int ownerSession, int killerEntityId, int targetEntityId)
        {
            var e = new CastExternalEvent();
            e.Kind = CastExternalEventKind.Kill;
            e.OwnerSession = ownerSession;
            e.KillerEntityId = killerEntityId;
            e.TargetEntityId = targetEntityId;
            Enqueue(e);
        }

        /// <summary>取出并**移除**该 SessionId 最早的一条事件（无则返回 false）。</summary>
        public bool TryDequeueForSession(int ownerSession, out CastExternalEvent e)
        {
            for (int i = 0; i < m_Count; i++)
            {
                if (m_Items[i].OwnerSession != ownerSession) { continue; }
                e = m_Items[i];
                for (int k = i; k < m_Count - 1; k++) { m_Items[k] = m_Items[k + 1]; }
                m_Count--;
                m_Items[m_Count] = default(CastExternalEvent);
                TotalConsumed++;
                return true;
            }
            e = default(CastExternalEvent);
            return false;
        }

        /// <summary>丢弃指定 SessionId 的全部待消费事件（玩家离场/重开局用）；返回丢弃条数。</summary>
        public int DropForSession(int ownerSession)
        {
            int dropped = 0;
            for (int i = m_Count - 1; i >= 0; i--)
            {
                if (m_Items[i].OwnerSession != ownerSession) { continue; }
                for (int k = i; k < m_Count - 1; k++) { m_Items[k] = m_Items[k + 1]; }
                m_Count--;
                dropped++;
            }
            return dropped;
        }

        /// <summary>清空（新一局/重置用）。注意：**不要**清 TotalPublished/TotalConsumed/DroppedCount（诊断累计量）。</summary>
        public void Clear() { m_Count = 0; }

        /// <summary>把外部事件种类映射到被动监听的事件（Q5 的 5 个事件之一）。</summary>
        public static SpellPassiveEvent ToPassiveEvent(CastExternalEventKind kind)
        {
            switch (kind)
            {
                case CastExternalEventKind.Hit: return SpellPassiveEvent.Hit;
                case CastExternalEventKind.Kill: return SpellPassiveEvent.Kill;
                default: return SpellPassiveEvent.None;
            }
        }

        private void Enqueue(in CastExternalEvent e)
        {
            if (m_Count >= m_Items.Length)
            {
                int size = m_Items.Length * 2;
                var next = new CastExternalEvent[size];
                System.Array.Copy(m_Items, next, m_Items.Length);
                m_Items = next;
            }
            m_Items[m_Count++] = e;
            TotalPublished++;
        }
    }
}

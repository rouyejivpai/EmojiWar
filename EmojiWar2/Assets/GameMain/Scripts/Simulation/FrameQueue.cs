//------------------------------------------------------------
// EmojiWar GameMain - 逻辑帧队列（W-13）
//
// 为什么需要它：
//   客户端以前**收到输入帧就立刻 Tick**。但一次 TCP `Read` 会把连着的好几帧一起派发
//   （`NetConnection` 单次读 4096 字节，而一帧只有一百多字节）→ 同一个渲染帧里会跑 2~4 个逻辑帧，
//   下一帧又跑 0 个。表现上就是报告 E 系列点名的"**卡一下猛冲**"：画面一顿一冲，
//   插值系数也会因为帧号跳变而抖动。
//
// 本类只做一件事：**把"到达"与"消费"解耦** —— 到达的帧先入队（按帧号去重、限长），
// 由消费调度器按本地时间轴一帧一帧取。它刻意是泛型且不引用任何网络类型
// （`EmojiWar.GameMain.Network` 的 `S2CInputFrame` 由调用方传帧号进来），
// 这样将来逻辑层拆程序集（W-11）时它可以直接跟着走。
//
// 注意：队列**不做重排**。传输层是 TCP（可靠有序），乱序/丢失在这里只可能是协议被破坏；
// 真正需要重排的是将来换 UDP（Steam Datagram）的时候 —— 那时在这里加"缺口等待 + 超时跳过"，
// 消费侧接口不用改。
//------------------------------------------------------------

using System.Collections.Generic;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>按帧号去重的逻辑帧队列（W-13）。</summary>
    public sealed class FrameQueue<T>
    {
        private readonly Queue<T> m_Queue = new Queue<T>();
        private readonly int m_Capacity;

        public FrameQueue(int capacity = 32)
        {
            m_Capacity = capacity > 0 ? capacity : 32;
        }

        /// <summary>当前排队待消费的帧数（= 本地时间轴缓冲深度）。</summary>
        public int Count { get { return m_Queue.Count; } }

        /// <summary>已入队的最大帧号（-1 = 尚无）。去重/缺口判定都以它为基准。</summary>
        public int LastEnqueuedFrame { get; private set; } = -1;

        /// <summary>因重复/过期被丢弃的帧数（诊断用）。</summary>
        public int DuplicateCount { get; private set; }

        /// <summary>因超出容量被丢弃的**最旧**帧数（诊断用；持续增长说明消费跟不上）。</summary>
        public int OverflowDroppedCount { get; private set; }

        public void Clear()
        {
            m_Queue.Clear();
            LastEnqueuedFrame = -1;
        }

        /// <summary>
        /// 入队。返回 false 表示该帧是**重复或过期**（帧号 ≤ 已入队的最大帧号）→ 未入队。
        /// 超出容量时丢弃**最旧**的帧（丢掉最旧而不是拒绝最新：最新帧承载的是最新意图，
        /// 丢它会让控制"越排越旧"）。
        /// </summary>
        public bool Enqueue(int frameIndex, T item)
        {
            if (frameIndex <= LastEnqueuedFrame)
            {
                DuplicateCount++;
                return false;
            }
            LastEnqueuedFrame = frameIndex;
            m_Queue.Enqueue(item);
            while (m_Queue.Count > m_Capacity)
            {
                m_Queue.Dequeue();
                OverflowDroppedCount++;
            }
            return true;
        }

        public bool TryDequeue(out T item)
        {
            if (m_Queue.Count == 0)
            {
                item = default(T);
                return false;
            }
            item = m_Queue.Dequeue();
            return true;
        }
    }
}

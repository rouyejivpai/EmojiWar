//------------------------------------------------------------
// EmojiWar GameMain - 法术临时 Buff 实例（第三层，D13 / S3）
//
// 依据：doc/法术编程玩法设计文档.md §3（临时 Buff 系统）
//         §3.2 属性 / §3.3 计时方式 / §3.4 叠加规则 / §3.6 Buff 与核心属性
//       doc/法术编程系统-执行文档.md §1.3（D13/D14）、流程 E、§8（配置驱动 P11）
//
// 定位（设计 §3.1）：**附着在物品卡实例上的限时状态**，不是玩家状态、不是全局状态。
//   修饰器物品是"指令"，临时 Buff 是"状态"。
//   宿主 = ItemInstance（物品卡实例，Q4）：随卡移动、跨施法保留、进状态哈希。
//
// 确定性（AGENTS §三）：纯值类型，只用 int/float/枚举；不依赖 Unity 时间/随机/表现数据。
//
// ⚠️ **值语义纪律**：本文件所有容器都遵循 ItemTypes.cs 立下的规矩——
//   "带数组字段的类型必须整体替换，禁止原地改数组；旧版正是拷贝构造共享 buffs 引用
//     导致委托逐发累积事故"。因此 BuffSet 的每次修改都返回**新的** BuffSet（内部 Clone），
//   禁止在多处持有同一个 BuffSet 引用后分别改。
//
// ⚠️ **命名冲突**：EmojiWar.GameMain.Buff.BuffInstance（Scripts/Buff/BuffDef.cs）是
//   **旧武器/状态系统**的同名类型（Slow/Stun/FireRateUp/Invulnerable），与本类型无关。
//   本类型在 EmojiWar.GameMain.Items 命名空间；两者禁止互相引用（执行文档 §1.3 D13）。
//------------------------------------------------------------

using System;
using System.Text;

namespace EmojiWar.GameMain.Items
{
    /// <summary>
    /// Buff 运行期上限/兜底。**全部来自 SpellSystemConfigSO**（P11：禁止硬编码平衡数值），
    /// 由 <see cref="FromConfig"/> 读取；自检用 <see cref="Factory"/> 拿出厂默认，避免依赖 GameEntry。
    /// </summary>
    public struct BuffLimits
    {
        /// <summary>任何 Buff 的层数不得超过（SpellSystemConfigSO.GlobalMaxBuffStacks）。</summary>
        public int GlobalMaxStacks;

        /// <summary>Buff 未填 MaxStacks 时的兜底（SpellSystemConfigSO.DefaultBuffMaxStacks）。</summary>
        public int DefaultMaxStacks;

        /// <summary>引爆每层伤害（SpellSystemConfigSO.BuffDetonateDamagePerStack）。</summary>
        public int DetonateDamagePerStack;

        /// <summary>出厂默认（SpellSystemConfigSO.Reset() 同值；用于纯 C# 自检）。</summary>
        public static BuffLimits Factory
        {
            get
            {
                BuffLimits l;
                l.GlobalMaxStacks = 99;
                l.DefaultMaxStacks = 1;
                l.DetonateDamagePerStack = 5;
                return l;
            }
        }

        /// <summary>从配置读（缺失则用出厂默认；运行期唯一正确入口）。</summary>
        public static BuffLimits FromConfig()
        {
            return FromSO(Simulation.SimInjectedConfig.Spell);
        }

        /// <summary>
        /// 从**显式传入**的 SO 读（模拟层用：`CastResolver` 拿到的是注入的 config，
        /// 且两条执行路径必须用**同一份** limits，否则同源码两套数值）。
        /// </summary>
        public static BuffLimits FromSO(Simulation.ISimSpellConfig so)
        {
            var f = Factory;
            if (so == null) { return f; }
            BuffLimits l;
            l.GlobalMaxStacks = so.GlobalMaxBuffStacks > 0 ? so.GlobalMaxBuffStacks : f.GlobalMaxStacks;
            l.DefaultMaxStacks = so.DefaultBuffMaxStacks > 0 ? so.DefaultBuffMaxStacks : f.DefaultMaxStacks;
            l.DetonateDamagePerStack = so.BuffDetonateDamagePerStack > 0 ? so.BuffDetonateDamagePerStack : f.DetonateDamagePerStack;
            return l;
        }
    }

    /// <summary>
    /// 一条临时 Buff 的运行实例（设计 §3.2）。
    /// **值语义** —— 一律整体替换，禁止共享引用。
    /// </summary>
    [Serializable]
    public struct BuffInstance
    {
        // ---- 定义（来自 CastBuffDef，编译期确定） ----
        /// <summary>BuffKey 的 FNV-1a 哈希（进状态哈希；不携带字符串）。</summary>
        public int KeyHash;
        public Data.BuffStat Stat;
        public float ValuePerStack;
        public int MaxStacks;
        public Data.BuffTiming Timing;
        public Data.BuffStackRule StackRule;
        public bool IsNegative;

        // ---- 运行状态（进状态哈希） ----
        /// <summary>当前层数。</summary>
        public int Stacks;
        /// <summary>剩余量：次数型/施法型 = 剩余次数；时间型 = 剩余帧数（20Hz 量化）。</summary>
        public int Remaining;
        /// <summary>初始时长（RefreshTime 叠加时用来重置 Remaining）。</summary>
        public int Duration;
        /// <summary>固化：下一次该递减时不递减（设计 §3.7 "固化"）。用完自动清除。</summary>
        public bool Solidified;

        public static readonly BuffInstance Empty = new BuffInstance();

        public bool IsEmpty { get { return KeyHash == 0 || Stacks <= 0; } }

        /// <summary>是否已到期（层数耗尽或剩余量耗尽）。</summary>
        public bool IsExpired { get { return Stacks <= 0 || Remaining <= 0; } }

        /// <summary>
        /// 当前生效数值。
        /// 加法型/刷新/独立 = 每层数值 × 层数；取最高 = 单层数值（层数不叠加）；
        /// 乘法型 = 每层数值连乘 Stacks 次（**迭代相乘**，不用 Math.Pow，避免跨平台浮点差异）。
        /// </summary>
        public float EffectiveValue
        {
            get
            {
                switch (StackRule)
                {
                    case Data.BuffStackRule.Highest:
                        return ValuePerStack;
                    case Data.BuffStackRule.Multiplicative:
                        {
                            float v = 1f;
                            int n = Stacks > 0 ? Stacks : 0;
                            for (int i = 0; i < n; i++) { v *= ValuePerStack; }
                            return v;
                        }
                    default:
                        return ValuePerStack * (Stacks > 0 ? Stacks : 0);
                }
            }
        }

        /// <summary>
        /// 从编译期定义构造一条新实例（设计 §3.2：定义 → 运行状态）。
        /// </summary>
        public static BuffInstance Create(in CastBuffDef def, in BuffLimits limits)
        {
            BuffInstance b = Empty;
            b.KeyHash = def.KeyHash;
            b.Stat = def.Stat;
            b.ValuePerStack = def.ValuePerStack;
            b.StackRule = def.StackRule;
            b.IsNegative = def.IsNegative;
            b.Timing = def.Timing;
            b.Duration = def.Duration;

            int max = def.MaxStacks > 0 ? def.MaxStacks : limits.DefaultMaxStacks;
            if (limits.GlobalMaxStacks > 0 && max > limits.GlobalMaxStacks) { max = limits.GlobalMaxStacks; }
            b.MaxStacks = max;

            b.Stacks = 1;
            b.Remaining = def.Duration;
            b.Solidified = false;
            return b;
        }

        /// <summary>整体替换用：只改运行状态，保留定义（值语义）。</summary>
        public BuffInstance WithStacks(int stacks)
        {
            BuffInstance b = this;
            b.Stacks = stacks < 0 ? 0 : (stacks > MaxStacks ? MaxStacks : stacks);
            if (b.Stacks <= 0) { b.Stacks = 0; b.Remaining = 0; }
            return b;
        }

        public BuffInstance WithRemaining(int remaining)
        {
            BuffInstance b = this;
            b.Remaining = remaining < 0 ? 0 : remaining;
            return b;
        }

        /// <summary>时间型：推进 frames 帧（固化时跳过本次并清除固化标记）。</summary>
        public BuffInstance TickFrames(int frames)
        {
            if (Timing != Data.BuffTiming.BySeconds) { return this; }
            if (Solidified) { BuffInstance c = this; c.Solidified = false; return c; }
            return WithRemaining(Remaining - frames);
        }

        /// <summary>触发一次（次数型递减）。</summary>
        public BuffInstance ConsumeTrigger()
        {
            if (Timing != Data.BuffTiming.ByTriggerCount) { return this; }
            if (Solidified) { BuffInstance c = this; c.Solidified = false; return c; }
            return WithRemaining(Remaining - 1);
        }

        /// <summary>施法一次（施法型递减）。</summary>
        public BuffInstance ConsumeCast()
        {
            if (Timing != Data.BuffTiming.ByCastCount) { return this; }
            if (Solidified) { BuffInstance c = this; c.Solidified = false; return c; }
            return WithRemaining(Remaining - 1);
        }

        /// <summary>固化一次（本次不递减）。</summary>
        public BuffInstance WithSolidified()
        {
            BuffInstance b = this;
            b.Solidified = true;
            return b;
        }

        /// <summary>状态哈希（层数 + 剩余次数，执行文档 §4：Buff 必须入哈希）。</summary>
        public void AppendHash(StringBuilder sb)
        {
            sb.Append(KeyHash).Append(':').Append(Stacks).Append(':').Append(Remaining)
              .Append(':').Append((int)Stat).Append(';');
        }

        public override string ToString()
        {
            if (IsEmpty) { return "buff(none)"; }
            return "buff#" + KeyHash + " " + Stat + " x" + Stacks + "/" + MaxStacks
                 + " rem=" + Remaining + "/" + Duration + " " + Timing + "/" + StackRule
                 + (IsNegative ? " [负面]" : "");
        }
    }

    /// <summary>
    /// 一个宿主（物品卡实例 / 运行期槽位）上的 Buff 集合。
    /// **值语义**：每次修改都返回新的 BuffSet（内部 Clone 数组），对齐 ItemTypes.cs 的既有纪律。
    /// 固定容量 8（结构常量，不是平衡数值）：单物品同时最多 8 条不同 Buff。
    /// </summary>
    [Serializable]
    public struct BuffSet
    {
        /// <summary>单宿主 Buff 条数上限（结构常量：防止无界增长）。</summary>
        public const int Capacity = 8;

        /// <summary>固定长度 Capacity 的数组；null = 空集。</summary>
        public BuffInstance[] Items;
        public int Count;

        public static readonly BuffSet Empty = new BuffSet();

        public bool IsEmpty { get { return Count <= 0 || Items == null; } }

        public BuffInstance At(int i)
        {
            if (Items == null || i < 0 || i >= Count) { return BuffInstance.Empty; }
            return Items[i];
        }

        /// <summary>取第 i 条的副本（越界返回原引用，调用方不应写回）。</summary>
        public BuffInstance[] CloneItems()
        {
            if (Items == null) { return null; }
            return (BuffInstance[])Items.Clone();
        }

        /// <summary>
        /// 深拷贝（数组也复制）。**跨 ItemInstance 传递 buff 时必须用它**：
        /// ItemTypes.cs 的纪律是"带数组字段的类型一律整体替换、禁止共享引用"。
        /// </summary>
        public BuffSet Clone()
        {
            BuffSet s = this;
            s.Items = CloneItems();
            return s;
        }

        /// <summary>整体替换第 i 条（值语义：复制数组后写，绝不原地改）。</summary>
        public BuffSet WithAt(int i, in BuffInstance value)
        {
            if (Items == null || i < 0 || i >= Count) { return this; }
            BuffSet s = this;
            s.Items = CloneItems();
            s.Items[i] = value;
            return s;
        }

        /// <summary>追加一条（满则返回原值，调用方按 false 处理）。</summary>
        public bool TryAdd(in BuffInstance value, out BuffSet result)
        {
            result = this;
            if (value.IsEmpty) { return false; }

            BuffInstance[] arr = Items;
            if (arr == null) { arr = new BuffInstance[Capacity]; }
            else { arr = CloneItems(); }
            if (Count >= Capacity) { return false; }

            arr[Count] = value;
            result.Items = arr;
            result.Count = Count + 1;
            return true;
        }

        /// <summary>移除第 i 条（保持顺序，值语义）。</summary>
        public BuffSet RemoveAt(int i)
        {
            if (Items == null || i < 0 || i >= Count) { return this; }
            BuffSet s = this;
            s.Items = CloneItems();
            for (int k = i; k < Count - 1; k++) { s.Items[k] = s.Items[k + 1]; }
            s.Items[Count - 1] = BuffInstance.Empty;
            s.Count = Count - 1;
            return s;
        }

        /// <summary>按 KeyHash 找下标（找不到 -1）。同 Key 只允许一条（叠加在同一实例上体现）。</summary>
        public int IndexOf(int keyHash)
        {
            if (Items == null) { return -1; }
            for (int i = 0; i < Count; i++) { if (Items[i].KeyHash == keyHash) { return i; } }
            return -1;
        }

        /// <summary>状态哈希（层数 + 剩余次数）。</summary>
        public void AppendHash(StringBuilder sb)
        {
            sb.Append('{').Append(Count).Append('|');
            for (int i = 0; i < Count; i++) { Items[i].AppendHash(sb); }
            sb.Append('}');
        }

        public override string ToString()
        {
            if (IsEmpty) { return "buffs(0)"; }
            var sb = new StringBuilder();
            sb.Append("buffs(").Append(Count).Append(")[");
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) { sb.Append(", "); }
                sb.Append(Items[i].ToString());
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}

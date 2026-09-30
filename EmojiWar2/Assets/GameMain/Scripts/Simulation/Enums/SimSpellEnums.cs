//------------------------------------------------------------
// EmojiWar Sim - 法术/物品枚举（W-04：从 Data/SO/SpellSO.cs 搬来）
//
// 为什么要搬：asmdef 的边界是**程序集**而不是类型 —— 这些枚举本身是纯 C#，
// 但只要声明还留在 GameMain 程序集里，`noEngineReferences` 的 Sim 程序集就引用不到它们。
// 命名空间保持 `EmojiWar.GameMain.Data` 不变 → 所有调用方（SO / UI / 用例）零改动。
//------------------------------------------------------------

using System;

namespace EmojiWar.GameMain.Data
{
        /// <summary>元素/主题标签（设计 §5.5）。**只用于过滤与修正，不做元素反应**（Q6b）。</summary>
        [Flags]
        public enum SpellTag
        {
            None = 0,
            Fire = 1 << 0,          // 火焰
            Ice = 1 << 1,           // 冰
            Physical = 1 << 2,      // 物理
            Lightning = 1 << 3,     // 闪电
            Energy = 1 << 4,        // 能量
            Void = 1 << 5,          // 虚空
            Soul = 1 << 6,          // 灵魂
            Time = 1 << 7,          // 时间
            Summon = 1 << 8,        // 召唤
        }

        /// <summary>结构标签（设计 §5.5 括号内；用于条件门/标签共鸣器/被动筛选）。</summary>
        [Flags]
        public enum SpellStructTag
        {
            None = 0,
            Projectile = 1 << 0,    // 投射物
            Modifier = 1 << 1,      // 修饰
            Terminate = 1 << 2,     // 终止
            Passive = 1 << 3,       // 被动
            Control = 1 << 4,       // 控制
            Interaction = 1 << 5,   // 交互
            Buff = 1 << 6,          // buff
        }

        /// <summary>触发类型（设计 §2.5）：决定"轮到它时怎么处理"。</summary>
        public enum SpellTriggerType
        {
            Immediate = 0,      // 立即：轮到它时立刻触发
            Delayed = 1,        // 延迟：等待 DelayFrames 帧后触发（跨帧，进待触发队列）
            Conditional = 2,    // 条件：满足条件才触发，否则跳过
            Sustained = 3,      // 持续：一段时间内持续影响后续物品
            Triggered = 4,      // 触发式：被其他物品触发时才生效
            Terminate = 5,      // 终止：结束本次施法
            Passive = 6,        // 被动：事件驱动，不占用主序列指针
        }

        /// <summary>效果种类（设计 §9 Effect 分支）。</summary>
        public enum SpellEffectKind
        {
            None = 0,
            FireProjectile = 1,   // 发射投射物（读 Projectile 剖面 + 当前修正）
            ApplyBuff = 2,        // 施加临时 Buff（读 BuffApply）
            SequenceOp = 3,       // 序列操作（循环/倒转/清空，读 SequenceOp + Operand）
            ModifyResource = 4,   // 资源操作（补魔/耗魔，读 ManaDelta）
            TriggerItem = 5,      // 触发其他物品（读 TargetScope/AffectCount）
        }

        /// <summary>序列操作（EffectKind=SequenceOp）。</summary>
        public enum SpellSequenceOp
        {
            None = 0,
            RepeatNext = 1,     // 下一个重复 Operand 次（循环符文）
            ReverseRest = 2,    // 后续倒序（序列倒转）
            ClearSequence = 3,  // 清空序列（触发"序列清空"事件）
            SkipNext = 4,       // 跳过下一个
        }

        /// <summary>作用对象（设计 §2.2 目标 / §3.5 作用对象）。</summary>
        public enum SpellTargetScope
        {
            Self = 0,           // 自身
            NextN = 1,          // 后续 N 个
            PrevN = 2,          // 前 N 个
            TagGroup = 3,       // 标签组
            WholeSequence = 4,  // 整个序列
            SpecificItem = 5,   // 指定物品
        }

        /// <summary>被动监听事件（Q5 第一版集合：施法开始/结束、序列清空、命中、击杀）。</summary>
        public enum SpellPassiveEvent
        {
            None = 0,
            CastStart = 1,      // 施法开始
            CastEnd = 2,        // 施法结束（含 Q2c 法力不足中止）
            SequenceCleared = 3,// 序列清空
            Hit = 4,            // 命中
            Kill = 5,           // 击杀
        }

        /// <summary>被触发顺序（设计 §4.4）。</summary>
        public enum SpellTriggerOrder
        {
            Sequential = 0,     // 顺序
            Reverse = 1,        // 逆序
            Parallel = 2,       // 并行（同帧）
        }

        /// <summary>Buff 计时方式（设计 §3.3；推荐默认 = 次数型）。</summary>
        public enum BuffTiming
        {
            ByTriggerCount = 0, // 次数型：再触发 X 次（默认）
            ByCastCount = 1,    // 施法型：再施法 X 次
            BySeconds = 2,      // 时间型：X 秒（量化到帧）
        }

        /// <summary>Buff 叠加规则（设计 §3.4）。</summary>
        public enum BuffStackRule
        {
            Additive = 0,       // 加法叠加（数值型，默认）
            Multiplicative = 1, // 乘法叠加（倍率型，设下限）
            Highest = 2,        // 取最高
            RefreshTime = 3,    // 刷新时间
            Independent = 4,    // 独立共存
        }

        /// <summary>Buff 影响的属性维度（设计 §3.2/§3.6）。</summary>
        public enum BuffStat
        {
            ManaCost = 0,
            Delay = 1,
            Recharge = 2,
            Damage = 3,
        }

        /// <summary>物品行为标志（设计 §2.6 序列修改规则 / §9 控制标记）。</summary>
        [Flags]
        public enum SpellItemFlags
        {
            None = 0,
            Forced = 1 << 0,      // 强制：不被条件门/沉默阻挡
            Temporary = 1 << 1,   // 临时插入：本次施法结束消失（插入物品默认值）
            Permanent = 1 << 2,   // 永久：移除/跳过/重排跨施法保留
            LockRecharge = 1 << 3,// 锁定充能：本次施法不产生充能
        }


        /// <summary>施法模式（本轮只实现 Sequential）。</summary>
        public enum CastMode
        {
            Sequential = 0,     // 每次抽一个槽位（走完进入充能）
            Simultaneous = 1,   // 一次全发（预留）
        }

        [System.Serializable]
    public struct SpellCondition
        {
            public SpellTag RequiredTags;
            public SpellStructTag RequiredStructTags;
            public int MinManaCost;
            public bool Invert;
        }
}

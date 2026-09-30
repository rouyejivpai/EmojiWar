//------------------------------------------------------------
// EmojiWar GameMain - 施法程序（纯数据，编译桥）
//
// 依据：doc/法术编程玩法设计文档.md（v2）§2/§7/§9 + doc/法术编程系统-执行文档.md D8/D9/D15
//
// **编译桥**：表现层的 SO（WandSO/SpellSO/ProjectileProfileSO）经 LoadoutCompiler
// 编译成这里的纯 int/float/枚举结构后才进入模拟层 —— SO 携带 Sprite/GameObject 引用，
// 绝不能进 sim，也不能进网络消息。
//
// 一次发射的语义（Q1/Q3，设计 §2.3/§2.1）：
//   **从序列第一个物品开始，依次触发每个物品**；延迟为 0 同帧触发、延迟 > 0 跨帧
//   （需要确定性待触发队列 → CastRuntimeState.Pending）。跑完整条序列后进入充能。
//
// 本文件不引用任何 UnityEngine 类型（枚举来自 Data 命名空间，是纯枚举）。
//------------------------------------------------------------

using System.Text;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>编译后的一条"施加到物品上的数值修正"（物品自身修正与 buff 修正共用同一形态）。</summary>
    public struct CastStatMod
    {
        public float ManaAdd;        // 蓝耗加值
        public float ManaMul;        // 蓝耗倍率（1=不变）
        public float DelayAdd;       // 延迟加值（秒）
        public float DelayMul;       // 延迟倍率（1=不变）
        public float RechargeAdd;    // 充能加值（秒）
        public float RechargeMul;    // 充能倍率（1=不变）
        public float DamageAdd;      // 投射物伤害加值
        public float DamageMul;      // 投射物伤害倍率
        public float SpeedMul;       // 投射物速度倍率
        public int PierceAdd;        // 穿透加值
        public float SpreadAdd;      // 扇形加值（度）
        public float HomingAdd;      // 追踪加值

        public static readonly CastStatMod Identity = new CastStatMod
        {
            ManaMul = 1f,
            DelayMul = 1f,
            RechargeMul = 1f,
            DamageMul = 1f,
            SpeedMul = 1f,
        };

        /// <summary>是否为恒等（未配置任何修正）。</summary>
        public bool IsIdentity
        {
            get
            {
                return ManaAdd == 0f && ManaMul == 1f && DelayAdd == 0f && DelayMul == 1f
                    && RechargeAdd == 0f && RechargeMul == 1f && DamageAdd == 0f && DamageMul == 1f
                    && SpeedMul == 1f && PierceAdd == 0 && SpreadAdd == 0f && HomingAdd == 0f;
            }
        }

        /// <summary>按"物品自身修正 → 再 buff 修正"的顺序叠加（设计 §3.6 注）。</summary>
        public void Apply(in CastStatMod other)
        {
            ManaAdd += other.ManaAdd;
            ManaMul *= other.ManaMul;
            DelayAdd += other.DelayAdd;
            DelayMul *= other.DelayMul;
            RechargeAdd += other.RechargeAdd;
            RechargeMul *= other.RechargeMul;
            DamageAdd += other.DamageAdd;
            DamageMul *= other.DamageMul;
            SpeedMul *= other.SpeedMul;
            PierceAdd += other.PierceAdd;
            SpreadAdd += other.SpreadAdd;
            HomingAdd += other.HomingAdd;
        }
    }

    /// <summary>编译后的一条 buff 施加定义（第三层）。</summary>
    public struct CastBuffDef
    {
        public int KeyHash;          // BuffKey 的稳定哈希（进状态哈希；不携带字符串）
        public BuffStat Stat;
        public float ValuePerStack;
        public int MaxStacks;
        public BuffTiming Timing;
        public int Duration;
        public BuffStackRule StackRule;
        public bool IsNegative;

        /// <summary>FNV-1a 32bit：稳定且跨端一致（string.GetHashCode 不保证跨运行时一致，禁止用）。</summary>
        public static int HashKey(string key)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (!string.IsNullOrEmpty(key))
                {
                    for (int i = 0; i < key.Length; i++)
                    {
                        h ^= key[i];
                        h *= 16777619u;
                    }
                }
                return (int)h;
            }
        }
    }

    /// <summary>编译后的被动定义（第四层）。</summary>
    public struct CastPassiveDef
    {
        public SpellPassiveEvent Event;
        public int LimitPerCast;
        public int CooldownFrames;
        public SpellTargetScope Scope;
        public int AffectCount;
        public SpellTriggerOrder Order;
        public int ManaCost;
        public bool IgnoreRecharge;
    }

    /// <summary>一个槽位上的物品（编译后，含弹道数值与全部修正）。</summary>
    public struct CastSpellData
    {
        public readonly int SpellId;             // SpellSO.Id（0 = 空槽）
        public readonly int ItemId;              // 物品卡 Id（0 = 无卡）
        public readonly int SlotIndex;           // 所在槽位（探针/事件用）
        public readonly SpellTriggerType TriggerType;
        public readonly int DelayFrames;         // TriggerType=Delayed 的帧数
        public readonly SpellEffectKind EffectKind;
        public readonly float EffectScale;       // 只放大伤害（Q11）

        public readonly SpellTag Tags;
        public readonly SpellStructTag StructTags;
        public readonly SpellItemFlags Flags;
        public readonly SpellTargetScope TargetScope;
        public readonly int AffectCount;
        public readonly SpellCondition Condition;

        public readonly int ManaCost;            // **基础**耗蓝（修正由 CastStatMod 承担）
        public readonly CastStatMod Self;        // 物品**对后续物品**的修正（仅 IsModifier 时有意义）

        /// <summary>物品**自身**的施法延迟修正（每物品属性；影响相邻触发间隔，不是"给后续物品"）。</summary>
        public readonly float OwnDelayAdd;

        /// <summary>物品**自身**的充能修正（每物品属性；只影响本次施法结束后的充能）。</summary>
        public readonly float OwnRechargeAdd;
        public readonly float OwnRechargeMul;

        /// <summary>
        /// 是否**修饰器物品**（第二层，设计 §1.2）：只有它为 true 时才会把 <see cref="Self"/>
        /// 写进"后续物品修正集"（CastResolver.ApplyMods）。
        /// ⚠️ 不能用 `!Self.IsIdentity` 代替：普通投射物也可能带 RechargeAdd（自身充能代价）
        /// 或 buff 数值加值，那会错误地覆盖掉前一个修饰器的耗蓝/延迟修正。
        /// </summary>
        public readonly bool IsModifier;

        public readonly SpellSequenceOp SequenceOp;
        public readonly int Operand;
        public readonly int ManaDelta;           // 补魔/耗魔

        public readonly int ProjectileId;
        public readonly float ProjSpeed;
        public readonly float ProjDamage;
        public readonly float ProjLifetime;
        public readonly float ProjRadius;
        public readonly int ProjCount;
        public readonly float ProjSpread;
        public readonly int ProjPierce;
        public readonly float ProjHoming;

        public readonly bool AppliesBuff;
        public readonly CastBuffDef Buff;
        public readonly bool IsPassive;
        public readonly CastPassiveDef Passive;

        public CastSpellData(int spellId, int itemId, int slotIndex, SpellTriggerType triggerType, int delayFrames,
            SpellEffectKind effectKind, float effectScale, SpellTag tags, SpellStructTag structTags, SpellItemFlags flags,
            SpellTargetScope targetScope, int affectCount, SpellCondition condition,
            int manaCost, CastStatMod self, float ownDelayAdd, float ownRechargeAdd, float ownRechargeMul,
            bool isModifier, SpellSequenceOp sequenceOp, int operand, int manaDelta,
            int projectileId, float projSpeed, float projDamage, float projLifetime, float projRadius,
            int projCount, float projSpread, int projPierce, float projHoming,
            bool appliesBuff, CastBuffDef buff, bool isPassive, CastPassiveDef passive)
        {
            SpellId = spellId;
            ItemId = itemId;
            SlotIndex = slotIndex;
            TriggerType = triggerType;
            DelayFrames = delayFrames;
            EffectKind = effectKind;
            EffectScale = effectScale;
            Tags = tags;
            StructTags = structTags;
            Flags = flags;
            TargetScope = targetScope;
            AffectCount = affectCount;
            Condition = condition;
            ManaCost = manaCost;
            Self = self;
            OwnDelayAdd = ownDelayAdd;
            OwnRechargeAdd = ownRechargeAdd;
            OwnRechargeMul = ownRechargeMul;
            IsModifier = isModifier;
            SequenceOp = sequenceOp;
            Operand = operand;
            ManaDelta = manaDelta;
            ProjectileId = projectileId;
            ProjSpeed = projSpeed;
            ProjDamage = projDamage;
            ProjLifetime = projLifetime;
            ProjRadius = projRadius;
            ProjCount = projCount;
            ProjSpread = projSpread;
            ProjPierce = projPierce;
            ProjHoming = projHoming;
            AppliesBuff = appliesBuff;
            Buff = buff;
            IsPassive = isPassive;
            Passive = passive;
        }

        public static readonly CastSpellData Empty = new CastSpellData(0, 0, -1, SpellTriggerType.Immediate, 0,
            SpellEffectKind.None, 1f, SpellTag.None, SpellStructTag.None, SpellItemFlags.None,
            SpellTargetScope.Self, 0, default, 0, CastStatMod.Identity, 0f, 0f, 1f, false, SpellSequenceOp.None, 0, 0,
            0, 0f, 0f, 0f, 0f, 1, 0f, 0, 0f, false, default, false, default);

        public bool IsEmpty { get { return SpellId <= 0; } }
        public bool IsProjectile { get { return EffectKind == SpellEffectKind.FireProjectile; } }

        /// <summary>被动不占用主序列指针（设计 §4.1/§4.6）。</summary>
        public bool SkipsMainCursor { get { return TriggerType == SpellTriggerType.Passive || IsPassive; } }
    }

    /// <summary>一把法杖的施法程序（物品序列 + 节奏 + 魔力）。</summary>
    public readonly struct CastProgram
    {
        public readonly int WandId;              // WandSO.Id（0 = 空手/无杖）
        public readonly int SlotCount;
        public readonly float BaseCastDelay;     // P2b=A：序列内相邻触发间隔的**加值**
        public readonly float RechargeTime;      // 法杖基础充能
        public readonly int ManaMax;
        public readonly float ManaRegen;
        public readonly CastMode Mode;
        public readonly CastSpellData[] Spells;  // 长度 = SlotCount

        /// <summary>
        /// 每个槽位的**初始**临时 Buff 快照（长度 = SlotCount；null = 全部无 buff）。
        ///
        /// S3/D15：buff 的宿主是物品卡实例（Q4），而模拟层不能持有 `ItemInstance`（那是表现/背包层数据），
        /// 所以 `LoadoutCompiler` 在编译期把法术卡上的 buff 快照到程序里，
        /// 运行时由 <see cref="CastRuntimeState.SlotBuffs"/> 接管并按帧推进（**入状态哈希**）。
        /// 这里的数组是编译产物、**运行期只读**；运行期改动一律进 `CastRuntimeState`。
        /// </summary>
        public readonly BuffSet[] InitialBuffs;

        public CastProgram(int wandId, int slotCount, float baseCastDelay, float rechargeTime,
            int manaMax, float manaRegen, CastMode mode, CastSpellData[] spells)
            : this(wandId, slotCount, baseCastDelay, rechargeTime, manaMax, manaRegen, mode, spells, null)
        {
        }

        public CastProgram(int wandId, int slotCount, float baseCastDelay, float rechargeTime,
            int manaMax, float manaRegen, CastMode mode, CastSpellData[] spells, BuffSet[] initialBuffs)
        {
            WandId = wandId;
            SlotCount = slotCount;
            BaseCastDelay = baseCastDelay;
            RechargeTime = rechargeTime;
            ManaMax = manaMax;
            ManaRegen = manaRegen;
            Mode = mode;
            Spells = spells;
            InitialBuffs = initialBuffs;
        }

        public static readonly CastProgram Empty = new CastProgram(0, 0, 0f, 0f, 0, 0f, CastMode.Sequential, null);

        public bool IsValid { get { return WandId > 0 && Spells != null && Spells.Length > 0; } }

        /// <summary>取某槽位的初始 buff 快照（越界/无 buff 返回空集，**不复制**：只读用途）。</summary>
        public BuffSet InitialBuffsAt(int index)
        {
            if (InitialBuffs == null || index < 0 || index >= InitialBuffs.Length) { return BuffSet.Empty; }
            return InitialBuffs[index];
        }

        public int LoadedCount
        {
            get
            {
                if (Spells == null) { return 0; }
                int n = 0;
                for (int i = 0; i < Spells.Length; i++) { if (!Spells[i].IsEmpty) { n++; } }
                return n;
            }
        }

        public CastSpellData SpellAt(int index)
        {
            if (Spells == null || index < 0 || index >= Spells.Length) { return CastSpellData.Empty; }
            return Spells[index];
        }

        public string Dump()
        {
            var sb = new StringBuilder();
            sb.Append("wand#").Append(WandId).Append(" slots=").Append(SlotCount)
              .Append(" 基础延迟=").Append(BaseCastDelay.ToString("F2")).Append(" 充能=").Append(RechargeTime.ToString("F2"))
              .Append(" 魔力=").Append(ManaMax).Append("(+").Append(ManaRegen.ToString("F0")).Append("/s)");
            if (Spells != null)
            {
                for (int i = 0; i < Spells.Length; i++)
                {
                    sb.Append(" [").Append(i).Append(']');
                    if (Spells[i].IsEmpty) { sb.Append('-'); continue; }
                    sb.Append(Spells[i].TriggerType).Append('#').Append(Spells[i].SpellId)
                      .Append("(mana=").Append(Spells[i].ManaCost).Append(')');
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>待触发队列里的一项（延迟 > 0 的物品在目标帧触发；设计 §2.3 步骤 5）。</summary>
    public struct PendingTrigger
    {
        public int SlotIndex;          // 序列槽位
        public int DueFrame;           // 目标逻辑帧号（绝对帧）
        public int Depth;              // 被动嵌套深度（0 = 主序列）
        public CastStatMod Mods;       // 触发时的修正快照（物品自身修正 → buff 修正）
        public bool IsPassiveInvoke;   // 是否由被动触发（不移动主指针）

        public static readonly PendingTrigger None = new PendingTrigger
        {
            SlotIndex = -1, DueFrame = -1, Depth = -1, Mods = CastStatMod.Identity,
        };

        public bool IsValid { get { return SlotIndex >= 0; } }
    }

    /// <summary>
    /// 施法运行状态（模拟层持有；每只手一套）。
    /// [哈希] 全部字段都影响推进 → 必须进 ComputeStateHash（设计 §7 状态哈希一行）。
    /// </summary>
    public struct CastRuntimeState
    {
        public float Mana;
        public int Cursor;                     // 主序列指针
        public int RechargeRemainingFrames;    // 剩余充能（帧；20Hz 量化）
        public int DelayRemainingFrames;       // 到下一次触发还要等几帧
        public float DelayCarry;               // 不足 1 帧的延迟余量（累计到 1 帧再进位）
        public int FrameIndex;                 // 本手已推进的帧数（待触发队列的目标帧基准）
        public bool CastActive;                // 是否处于"一次发射"进行中
        public float PendingRechargeSeconds;   // 本次发射累计的充能修正（秒）
        public bool RechargeLocked;            // 本次发射锁定充能

        public int TotalTriggers;              // 本次发射已触发总数（上限 Q7）
        public int ProgramVersion;             // 装填版本（换法术/换杖时 +1，用于同步与哈希）

        /// <summary>
        /// 序列倒转已生效（设计 §2.6：只影响本次施法，每个槽位只倒转一次）。
        /// 防止主游标重新走回该物品时反复倒转 → 无限触发。
        /// </summary>
        public bool ReverseConsumed;

        // 当前生效的修正集（第二层）：**必须跟着状态走**，因为延迟 >0 的物品会跨帧触发，
        // 修饰器写入的修正要在未来帧仍然生效（设计 §2.3 步骤 3）。
        public CastStatMod ActiveMod;          // 当前修正集（恒等 = 无修正）
        public int ModScopeLeft;               // 剩余作用物品数（0 = 无限制）
        public bool ModScopeBounded;           // 是否处于"后续 N 个"限定中

        public PendingTrigger[] Pending;       // 待触发队列（槽 0..PendingCount-1 有效）
        public int PendingCount;

        /// <summary>
        /// Q7 单物品触发计数（每次发射在 <c>BeginCast</c> 清零）。长度 = SlotCount。
        /// **[W-09] 必须是状态的一部分**：原实现是 `CastResolver` 里的**进程级静态数组**，于是
        /// (a) 跨局残留、(b) 同一玩家两只手**互相清空**（B 手 BeginCast 会清掉 A 手的计数）、
        /// (c) 完全不入哈希 → 两端计数不同也不会被对账发现。移进状态后三条一起修掉。
        /// </summary>
        public int[] PerItem;

        /// <summary>
        /// 每个槽位的**运行期**临时 Buff 状态（S3；长度 = SlotCount，null = 无槽位）。
        /// 初值来自 <see cref="CastProgram.InitialBuffs"/>（编译期从物品卡实例快照），
        /// 之后由 `CastResolver` 施加/递减推进，**必须进 ComputeStateHash**（执行文档 §4 与流程 I-4）。
        /// 元素是值语义 `BuffSet`：改动一律整体替换（`SetBuff`），禁止在多处共享同一数组元素引用。
        /// </summary>
        public BuffSet[] SlotBuffs;

        public static CastRuntimeState For(in CastProgram program)
        {
            CastRuntimeState s = new CastRuntimeState();
            s.Mana = program.ManaMax;
            s.Cursor = 0;
            s.RechargeRemainingFrames = 0;
            s.DelayRemainingFrames = 0;
            s.DelayCarry = 0f;
            s.FrameIndex = 0;
            s.CastActive = false;
            s.PendingRechargeSeconds = 0f;
            s.RechargeLocked = false;
            s.TotalTriggers = 0;
            s.ProgramVersion = program.WandId;
            s.ReverseConsumed = false;
            s.ActiveMod = CastStatMod.Identity;
            s.ModScopeLeft = 0;
            s.ModScopeBounded = false;
            s.Pending = new PendingTrigger[8];
            s.PendingCount = 0;

            // S3：按槽位克隆初始 buff 快照（**必须 Clone**：程序是编译产物、跨帧复用，
            // 共享数组会让运行期改动污染程序本身 —— ItemTypes.cs 警示的同一类事故）。
            int slots = program.SlotCount > 0 ? program.SlotCount : 0;
            s.SlotBuffs = slots > 0 ? new BuffSet[slots] : null;
            for (int i = 0; i < slots; i++)
            {
                BuffSet seed = program.InitialBuffsAt(i);
                s.SlotBuffs[i] = seed.IsEmpty ? BuffSet.Empty : seed.Clone();
            }

            // W-09：Q7 单物品计数按手各一份（不再共享静态数组）
            s.PerItem = slots > 0 ? new int[slots] : null;

            // S4：被动计数（每次发射重置）与冷却（按帧存活）各自一份数组
            s.PassiveUsed = slots > 0 ? new int[slots] : null;
            s.PassiveCooldown = slots > 0 ? new int[slots] : null;
            s.PassiveDepth = 0;
            s.PassiveFires = 0;
            return s;
        }

        /// <summary>取某槽位的 buff（越界返回空集）。只读用途，勿写回。</summary>
        public BuffSet BuffAt(int slotIndex)
        {
            if (SlotBuffs == null || slotIndex < 0 || slotIndex >= SlotBuffs.Length) { return BuffSet.Empty; }
            return SlotBuffs[slotIndex];
        }

        /// <summary>整体替换某槽位的 buff（值语义；越界忽略）。运行期唯一写入口。</summary>
        public void SetBuff(int slotIndex, BuffSet set)
        {
            if (SlotBuffs == null || slotIndex < 0 || slotIndex >= SlotBuffs.Length) { return; }
            SlotBuffs[slotIndex] = set;
        }

        /// <summary>是否有任何槽位挂着 buff（哈希/探针用）。</summary>
        public bool HasAnyBuff
        {
            get
            {
                if (SlotBuffs == null) { return false; }
                for (int i = 0; i < SlotBuffs.Length; i++) { if (!SlotBuffs[i].IsEmpty) { return true; } }
                return false;
            }
        }

        /// <summary>有未走完的充能 → 不能开新的一次发射。</summary>
        public bool IsRecharging { get { return RechargeRemainingFrames > 0; } }

        /// <summary>待触发队列非空 → 本次发射仍在跨帧进行。</summary>
        public bool HasPending { get { return PendingCount > 0; } }

        // ====================================================================
        // S4：被动的运行期状态（执行文档 §4：被动剩余次数/冷却必须入哈希）
        //   P6 口径：**次数每次发射重置**；**冷却按模拟帧**（跨发射存活）。
        // ====================================================================

        /// <summary>每槽位本次发射已触发次数（`BeginCast` 清零）。长度 = SlotCount。</summary>
        public int[] PassiveUsed;

        /// <summary>每槽位被动冷却（帧；每帧递减；跨发射存活）。长度 = SlotCount。</summary>
        public int[] PassiveCooldown;

        /// <summary>当前被动嵌套深度（设计 §4.6：默认上限 3；超限忽略）。</summary>
        public int PassiveDepth;

        /// <summary>本次发射被动触发总数（探针/诊断用）。</summary>
        public int PassiveFires;

        /// <summary>是否有被动计数/冷却残留（哈希与快速跳过用）。</summary>
        public bool HasPassiveState
        {
            get
            {
                if (PassiveUsed != null)
                {
                    for (int i = 0; i < PassiveUsed.Length; i++) { if (PassiveUsed[i] > 0) { return true; } }
                }
                if (PassiveCooldown != null)
                {
                    for (int i = 0; i < PassiveCooldown.Length; i++) { if (PassiveCooldown[i] > 0) { return true; } }
                }
                return false;
            }
        }
    }

    /// <summary>一套装备（左右手法杖）的编译结果 + 稳定哈希（供握手/同步比对）。</summary>
    public readonly struct LoadoutSnapshot
    {
        public readonly CastProgram Primary;
        public readonly CastProgram Secondary;
        public readonly ulong Hash;

        public LoadoutSnapshot(CastProgram primary, CastProgram secondary, ulong hash)
        {
            Primary = primary;
            Secondary = secondary;
            Hash = hash;
        }

        public override string ToString() { return "loadout hash=0x" + Hash.ToString("X16"); }
    }
}

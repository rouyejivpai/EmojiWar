//------------------------------------------------------------
// EmojiWar GameMain - 法术配置（SpellSO = 通用物品卡，D1/D2）
//
// 依据：doc/法术编程玩法设计文档.md（v2 打磨版，Q1–Q12 已闭环）§2/§9 + §12 预算表
//       doc/法术编程系统-执行文档.md §2.1（字段分组）+ §7（P1–P11 执行口径）
//
// 四层模型（设计 §1.2）：
//   第一层 主动物品（TriggerType=Immediate） → 按顺序执行的主程序
//   第二层 修饰器物品（ManaMul/Add、DelayMul/Add、RechargeAdd/Mul） → 修改后续物品
//   第三层 临时 Buff（BuffApply） → 附着在物品卡实例上的限时状态（宿主 = ItemInstance，Q4）
//   第四层 被动物品（TriggerType=Passive） → 事件驱动（Q5 事件集合）
//
// 数值口径（设计 §2.4 / §3.6，运算顺序 = 物品自身修正 → 再 buff 修正）：
//   最终蓝耗 = (基础蓝耗 + ManaAdd) × ManaMul → max(0, …)
//   最终延迟 = (基础延迟 + DelayAdd) × DelayMul → max(0, …)
//   最终充能 = (基础充能 + RechargeAdd) × RechargeMul → max(0, …)
//
// 确定性：本文件的**全部枚举与数值**会被 LoadoutCompiler 编译成纯 int/float 的 CastProgram；
//   模拟层不得读 SpellSO（含 Sprite/GameObject 引用），表现数据不进 sim、不进网络。
// 配置驱动（执行文档 §8，P11）：凡平衡数值一律来自本资产或 SpellSystemConfigSO，代码只留结构兜底。
//------------------------------------------------------------
using System;
using UnityEngine;
using UnityEngine.Serialization;
namespace EmojiWar.GameMain.Data
{
    // ==================== D2：枚举（数据与解释器共用，禁字符串枚举） ====================
    // ==================== 可序列化子结构 ====================
    [Serializable]
    public struct BuffApplyDef
    {
        public string BuffKey;          // 稳定标识（进哈希用；禁止走字符串路径加载资产）
        public string DisplayName;
        public BuffStat Stat;           // 影响哪个属性
        public float ValuePerStack;     // 每层数值（加法型=加值；乘法型=倍率）
        public int MaxStacks;           // 层数上限
        public BuffTiming Timing;       // 计时方式
        /// <summary>时长：**次数型/施法型 = 次数**；时间型请用 <see cref="DurationMs"/>（本字段被忽略）。</summary>
        public int Duration;
        /// <summary>
        /// [W-10a] 时间型（`BuffTiming.BySeconds`）的时长，**单位毫秒**。
        /// 之所以新开一个字段而不是把 `Duration` 改名：`Duration` 是**双语义**的
        /// （次数型/施法型 = 次数，时间型 = 帧数）。把双语义字段整体改名成 `...Ms` 会让
        /// "再触发 3 次"变成"再触发 3 毫秒" —— 语义错得比原来更隐蔽。
        /// 加载期按当前帧率量化成帧；`Duration` 在时间型下不再被读取。
        /// </summary>
        [Tooltip("Timing=BySeconds 时的时长（毫秒）；次数型/施法型请用上面的 Duration")]
        public int DurationMs;
        public BuffStackRule StackRule; // 叠加规则
        public bool IsNegative;         // 负面 buff（净化之光只移除负面）
    }
    /// <summary>被动监听配置（设计 §4.2 / 执行文档 §2.1 Passive 组）。</summary>
    [Serializable]
    public struct PassiveDef
    {
        public SpellPassiveEvent Event;     // 监听什么事件
        public int LimitPerCast;            // 每次发射最多触发次数（P6：每次发射重置）
        /// <summary>
        /// [W-10a] 冷却，**单位毫秒**（原为 `CooldownFrames`，按 20Hz 帧数录入）。
        /// 改毫秒的原因：切 30Hz 会**静默改掉**"按帧"配置的真实时长（10 帧在 20Hz 是 500ms，
        /// 到 30Hz 变成 333ms，没人会察觉）。毫秒是"真实时长"的唯一无歧义表达；
        /// 帧数只在**加载期**用 `CastResolver.FramesOf` 量化一次，之后模拟层只用帧。
        /// </summary>
        [FormerlySerializedAs("CooldownFrames")]
        [Tooltip("冷却（毫秒；0 = 用 SpellSystemConfig 的默认值）")]
        public int CooldownMs;
        public SpellTargetScope Scope;      // 触发目标
        public int AffectCount;             // 目标数量（Scope=NextN/PrevN 时）
        public SpellTriggerOrder Order;     // 触发顺序
        public int ManaCost;                // 触发消耗（**后扣 + 限次**，设计 §4.5）
        public bool IgnoreRecharge;         // true=本次被动不增加充能
    }
    /// <summary>
    /// 法术/物品（通用物品卡，Q8）：既是效果载体，也是修饰器、事件监听器与状态容器。
    /// 引用的**弹道剖面 / 物品卡 / 法杖**一律用资产引用，禁止字符串名/路径（工程约定 §二）。
    /// </summary>
    [CreateAssetMenu(fileName = "Spell_", menuName = "EmojiWar/Data/Spell")]
    public sealed class SpellSO : ScriptableObject
    {
        // ---------- 身份 ----------
        [Header("身份")]
        public int Id = 0;
        public string SpellKey = "";         // "spell_spark"（稳定标识，进配置哈希）
        public string DisplayName = "";
        [TextArea] public string Description = "";
        public Sprite IconSprite = null;     // 资产引用（表现层，不进 sim/网络）
        /// <summary>
        /// [W-10a] 数据迁移版本号。0 = 还是"按 20Hz 帧数"录入的旧数据；1 = 已换算成毫秒。
        /// 迁移脚本（`EmojiWar/Tools/Migrate Frame→Ms`）据此保证**只换算一次**（幂等）。
        /// </summary>
        [HideInInspector] public int SchemaVersion = 0;
        // ---------- 触发 ----------
        [Header("触发")]
        public SpellTriggerType TriggerType = SpellTriggerType.Immediate;
        /// <summary>[W-10a] 延迟**毫秒**（原 `DelayFrames`，按 20Hz 帧数录入）。加载期量化成帧。</summary>
        [FormerlySerializedAs("DelayFrames")]
        [Tooltip("TriggerType=Delayed 时：延迟多少**毫秒**后触发（加载期按当前帧率量化成帧）")]
        public int DelayMs = 0;
        public SpellCondition Condition;     // TriggerType=Conditional 时有效
        // ---------- 资源（自身消耗 + 对后续/本次的修正） ----------
        [Header("资源")]
        [Tooltip("触发该物品需要消耗的基础魔力")]
        public int ManaCost = 6;
        [Tooltip("对后续物品的耗蓝倍率（1=不变）")]
        public float ManaMul = 1f;
        [Tooltip("对后续物品的耗蓝加值（0=不变）")]
        public int ManaAdd = 0;
        [Tooltip("对后续物品的延迟倍率（1=不变）")]
        public float DelayMul = 1f;
        [Tooltip("对后续物品的延迟加值（秒；正=变慢，负=提前）")]
        public float DelayAdd = 0f;
        [Tooltip("本次施法总充能修正（秒）")]
        public float RechargeAdd = 0f;
        [Tooltip("本次施法总充能倍率（1=不变）")]
        public float RechargeMul = 1f;
        // ---------- 效果 ----------
        [Header("效果")]
        public SpellEffectKind EffectKind = SpellEffectKind.FireProjectile;
        [Tooltip("效果强度倍率：**只放大伤害**（Q11），不动 buff 数值/层数、不动耗蓝/延迟/充能")]
        public float EffectScale = 1f;
        [Tooltip("EffectKind=FireProjectile 时的弹道剖面（资产引用）")]
        public ProjectileProfileSO Projectile = null;
        [Tooltip("EffectKind=SequenceOp 时的操作")]
        public SpellSequenceOp SequenceOp = SpellSequenceOp.None;
        [Tooltip("序列操作的次数/参数（如 RepeatNext 的重复次数）")]
        public int Operand = 0;
        [Tooltip("EffectKind=ModifyResource 时的魔力增减（正=补魔）")]
        public int ManaDelta = 0;
        // ---------- 目标 ----------
        [Header("目标")]
        public SpellTargetScope TargetScope = SpellTargetScope.NextN;
        [Tooltip("作用数量（TargetScope=NextN/PrevN 时为 N；0=不修改）")]
        public int AffectCount = 1;
        // ---------- 标签（过滤与修正，不做元素反应 Q6b） ----------
        [Header("标签")]
        public SpellTag Tags = SpellTag.None;
        public SpellStructTag StructTags = SpellStructTag.None;
        // ---------- 修正集（作用于后续投射物） ----------
        [Header("投射物修正（作用于后续投射物）")]
        public float ProjDamageAdd = 0f;
        public float ProjDamageMul = 1f;
        public float ProjSpeedMul = 1f;
        public int ProjPierceAdd = 0;
        public float ProjSpreadAdd = 0f;
        public float ProjHomingAdd = 0f;
        // ---------- Buff（第三层） ----------
        [Header("Buff（第三层：施加到物品卡实例上）")]
        public bool AppliesBuff = false;
        public BuffApplyDef BuffApply;
        // ---------- 被动（第四层） ----------
        [Header("被动（第四层：事件驱动）")]
        public bool IsPassive = false;
        public PassiveDef Passive;
        // ---------- 控制标记 ----------
        [Header("控制标记")]
        public SpellItemFlags ItemFlags = SpellItemFlags.None;
        // ---------- 商店 ----------
        [Header("商店")]
        public ItemRarity Rarity = ItemRarity.Common;
        public float Weight = 1f;
        /// <summary>
        /// 是否**修饰器物品**（第二层，设计 §1.2）：只有它为 true，CastResolver 才会把本物品的
        /// 修正写进"后续物品修正集"（作用后续 N 个）。
        ///
        /// 判定依据 = **`StructTags` 里的 `Modifier` 标记**（设计 §1.2：物品属于哪一层由设计指定），
        /// 而不是"字段看起来像不像修正"。理由（实现期踩过的坑，见执行文档 §0.1）：
        /// · `RechargeAdd`（火花弹 0.1）是**自身充能代价**（设计 §2.2：影响"本次施法后"的冷却），
        ///   不是对后续物品的修饰；
        /// · `DelayAdd`（冰锥 +0.05、岩石弹 +0.1）是**自身施法延迟**（影响相邻触发间隔），
        ///   同样不是"给后续物品的延迟修正"。
        /// 若按字段值推断，普通投射物会被误判成修饰器 → **覆盖掉前面符文写下的耗蓝/延迟修正**
        /// （实测：节能符文 + 3 火花弹 变成 11+6+6+6=26，正确应为 11+3+3+3=20）。
        /// </summary>
        public bool IsModifier
        {
            get { return (StructTags & SpellStructTag.Modifier) != 0; }
        }
        /// <summary>是否锁定充能（本次施法不产生充能）。</summary>
        public bool LocksRecharge { get { return (ItemFlags & SpellItemFlags.LockRecharge) != 0; } }
    }
}

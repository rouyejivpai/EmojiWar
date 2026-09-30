using EmojiWar.GameMain.Simulation;
//------------------------------------------------------------
// EmojiWar GameMain - 法术系统总配置（SpellSystemConfigSO，D31）
//
// 依据：doc/法术编程系统-执行文档.md §8「配置驱动原则（强制，P11）」
//   **凡涉及数值/平衡的参数，一律来自配置资产（SO），代码只允许"结构常量"。**
//   本资产就是执行文档 §8.1「数值登记表」里所有 ❌ 硬编码项的**唯一配置来源**。
//
// 覆盖的硬编码（D33 清理目标）：
//   InventoryService.MaxWandSlots=8 / ItemSystem.BackpackCapacity=30 / ShopCapacity=6
//   CastResolver 的 64/8/3 上限与 2f/0.2f/3f 弹道兜底
//   被动默认限次与冷却、Buff 默认层数上限
//   §12.2 预算校验口径（序列时长 ≤1.2s、总充能 0.8~1.6s、Σ延迟、Σ充能比例、回魔支撑）
//
// 确定性：本资产的取值在开局前注入模拟层（与 BattleConfigSO 同款做法），
//   双端读同一资产同值 → 不影响确定性；但**不参与逐帧哈希**（是常量而非推进状态）。
///------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace EmojiWar.GameMain.Data
{
    /// <summary>数值区间（体检用：设计 §12.3 单物品数值区间）。</summary>
    [Serializable]
    public struct SpellValueRange
    {
        public int ManaMin;
        public int ManaMax;
        public float DelayMin;
        public float DelayMax;
        public float RechargeMin;
        public float RechargeMax;

        public bool ManaInRange(int v) { return v >= ManaMin && v <= ManaMax; }
        public bool DelayInRange(float v) { return v >= DelayMin - 1e-4f && v <= DelayMax + 1e-4f; }
        public bool RechargeInRange(float v) { return v >= RechargeMin - 1e-4f && v <= RechargeMax + 1e-4f; }
    }

    /// <summary>法术系统总配置：上限 / 兜底 / 预算校验口径。</summary>
    [CreateAssetMenu(fileName = "SpellSystemConfig", menuName = "EmojiWar/Data/SpellSystemConfig")]
public sealed class SpellSystemConfigSO : ScriptableObject, ISimSpellConfig
    {
        // [W-04] 显式接口实现（字段保持 public → Unity 序列化与公开 API 均不变）
        float ISimSpellConfig.DefaultBulletLifetime { get { return DefaultBulletLifetime; } }
        float ISimSpellConfig.DefaultBulletRadius { get { return DefaultBulletRadius; } }
        int ISimSpellConfig.DefaultPassiveCooldownMs { get { return DefaultPassiveCooldownMs; } }
        int ISimSpellConfig.DefaultPassiveLimitPerCast { get { return DefaultPassiveLimitPerCast; } }
        float ISimSpellConfig.MaxBulletLifetime { get { return MaxBulletLifetime; } }
        int ISimSpellConfig.MaxPassiveNesting { get { return MaxPassiveNesting; } }
        int ISimSpellConfig.MaxTotalTriggers { get { return MaxTotalTriggers; } }
        int ISimSpellConfig.MaxTriggersPerItem { get { return MaxTriggersPerItem; } }
        int ISimSpellConfig.GlobalMaxBuffStacks { get { return GlobalMaxBuffStacks; } }
        int ISimSpellConfig.DefaultBuffMaxStacks { get { return DefaultBuffMaxStacks; } }
        int ISimSpellConfig.BuffDetonateDamagePerStack { get { return BuffDetonateDamagePerStack; } }

        [Header("槽位与容量（P1：必须可手动配置，禁止硬编码）")]
        [Tooltip("法术槽物理上限（每手法术容器的 Capacity；实际可用槽数由法杖 SlotCount 决定）")]
        public int MaxSpellSlots = 8;
        [Tooltip("背包容量（6×5）")]
        public int BackpackCapacity = 30;
        [Tooltip("商店货架容量")]
        public int ShopCapacity = 6;
        [Tooltip("商店每波商品数（0=沿用 BattleConfigSO）")]
        public int ShopItemCount = 3;

        [Header("防无限循环上限（Q7）")]
        [Tooltip("单次施法总触发次数上限")]
        public int MaxTotalTriggers = 64;
        [Tooltip("单个物品单次施法最多触发次数")]
        public int MaxTriggersPerItem = 8;
        [Tooltip("被动嵌套深度上限")]
        public int MaxPassiveNesting = 3;

        [Header("弹道兜底（弹道剖面缺字段时；正常应由 ProjectileProfileSO 必填）")]
        public float DefaultBulletLifetime = 2f;
        public float DefaultBulletRadius = 0.2f;
        public float DefaultBulletSpeed = 12f;
        public float DefaultBulletDamage = 8f;
        [Tooltip("子弹寿命兜底上限（防止配置写超大值）")]
        public float MaxBulletLifetime = 10f;

        [Header("被动 / Buff 默认值（被动未填时兜底）")]
        public int DefaultPassiveLimitPerCast = 1;
        /// <summary>[W-10a] 被动冷却默认值，**单位毫秒**（原 `DefaultPassiveCooldownFrames`，按 20Hz 帧数录入）。</summary>
        [FormerlySerializedAs("DefaultPassiveCooldownFrames")]
        [Tooltip("被动冷却默认值（毫秒；被动自身没填冷却时用它）")]
        public int DefaultPassiveCooldownMs = 500;
        public int DefaultBuffMaxStacks = 1;
        [Tooltip("Buff 全局层数上限（任何 Buff 不得超过）")]
        public int GlobalMaxBuffStacks = 99;
        [Tooltip("引爆（Detonate）每层伤害（设计 §3.7「引爆：每层 5 伤」；S3）")]
        public int BuffDetonateDamagePerStack = 5;

        [Header("预算校验口径（设计 §12.2；仅体检用，不参与运行时计算）")]
        [Tooltip("序列总耗蓝 ≤ 魔力池 × 该比例")]
        public float SequenceManaBudgetRatio = 0.7f;
        [Tooltip("序列总耗蓝低于该比例视为太轻（只提示不报错）")]
        public float SequenceManaTooLightRatio = 0.4f;
        [Tooltip("序列时长上限（秒）")]
        public float SequenceDurationMax = 1.2f;
        [Tooltip("Σ物品延迟修正 ≤ 该值 × 槽数")]
        public float DelaySumPerSlotMax = 0.15f;
        [Tooltip("序列总充能下限（秒）")]
        public float TotalRechargeMin = 0.8f;
        [Tooltip("序列总充能上限（秒）")]
        public float TotalRechargeMax = 1.6f;
        [Tooltip("Σ物品充能修正 ≤ 基础充能 × 该比例")]
        public float RechargeSumRatioMax = 0.6f;
        [Tooltip("回魔支撑连打：Regen × 周期 ≥ 一发总耗蓝 × 该值")]
        public float RegenSupportFactor = 0.5f;

        [Header("数值区间（设计 §12.3；体检越界即报错）")]
        public SpellValueRange LightBand;      // 轻（火花弹/瞬发铭文/条件门）
        public SpellValueRange MediumBand;     // 中（冰锥/岩石弹/急速咏唱/节能符文）
        public SpellValueRange HeavyBand;      // 重（爆裂火球/虚空回响/充电过载）
        public SpellValueRange UtilityBand;    // 修饰/控制（终止符/快速充能/时间锁）

        /// <summary>
        /// [W-10a] 数据迁移版本号。0 = 还是"按 20Hz 帧数"录入的旧数据；1 = 已换算成毫秒。
        /// 迁移脚本据此保证只换算一次（幂等）。
        /// </summary>
        [HideInInspector] public int SchemaVersion = 0;

        /// <summary>
        /// 出厂默认值（设计 §12.3 表；字段未填时由体检/生成器兜底，避免"配置为空"报一堆错）。
        /// </summary>
        private void Reset()
        {
            LightBand = new SpellValueRange { ManaMin = 3, ManaMax = 8, DelayMin = -0.05f, DelayMax = 0f, RechargeMin = 0.05f, RechargeMax = 0.15f };
            MediumBand = new SpellValueRange { ManaMin = 8, ManaMax = 15, DelayMin = 0.05f, DelayMax = 0.1f, RechargeMin = 0.1f, RechargeMax = 0.25f };
            HeavyBand = new SpellValueRange { ManaMin = 15, ManaMax = 25, DelayMin = 0.15f, DelayMax = 0.2f, RechargeMin = 0.25f, RechargeMax = 0.4f };
            UtilityBand = new SpellValueRange { ManaMin = 0, ManaMax = 12, DelayMin = 0f, DelayMax = 0.15f, RechargeMin = -0.3f, RechargeMax = 0.5f };
        }
    }
}

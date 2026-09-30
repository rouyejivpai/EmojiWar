//------------------------------------------------------------
// EmojiWar GameMain Editor - 法术内容生成器（D20，S1）
//
// 菜单：EmojiWar/Setup/Build Spell Content (S1)
//   EmojiWar/Setup/Rebuild Spell Content (S1)   删除旧法术后重建
//
// 依据：doc/法术编程系统-执行文档.md D3/D4/D5/D20/D31/D32 + §7（P1–P11）
//       doc/法术编程玩法设计文档.md §6（物品清单）/ §12（数值与预算）
//
// 产物（全部在 Resources 下，自动进构建版）：
//   Resources/Data/SpellSystem/SpellSystemConfig.asset   总配置（D31）
//   Resources/Data/SpellSystem/StartingLoadout.asset     初始装备（D32）
//   Resources/Data/Projectile/Proj_*.asset               弹道剖面（D6）
//   Resources/Data/Spell/Spell_*.asset                   物品行为（D3：S1 最小 8 个，P10）
//   Resources/Data/Wand/Wand_*.asset                      法杖（D5：含新增 7 槽重杖）
//   Resources/Data/Item/Item_*.asset                      物品卡（D4：**StackMax=1 不可堆叠**，P4）
//
// P9 口径：**旧 8 个法术资产 + 对应物品卡删除**，按新设计重建（含移除 Multicast 多重施法）。
// 幂等：已存在的资产按路径复用并覆盖字段（可反复执行）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Art;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>法术编程系统内容生成器（S1）。</summary>
    public static class SpellContentBuilder
    {
        private const string DataRoot = "Assets/GameMain/Resources/Data";
        private const string ProjDir = DataRoot + "/Projectile";
        private const string SpellDir = DataRoot + "/Spell";
        private const string WandDir = DataRoot + "/Wand";
        private const string ItemDir = DataRoot + "/Item";
        private const string SpellSystemDir = DataRoot + "/SpellSystem";

        // 旧 8 法术资产（P9：删除）与其物品卡
        private static readonly string[] ObsoleteSpellAssets =
        {
            SpellDir + "/Spell_1_Fireball.asset",
            SpellDir + "/Spell_2_IceShard.asset",
            SpellDir + "/Spell_3_Spark.asset",
            SpellDir + "/Spell_4_Ghost.asset",
            SpellDir + "/Spell_5_DamageUp.asset",
            SpellDir + "/Spell_6_SpeedUp.asset",
            SpellDir + "/Spell_7_Pierce.asset",
            SpellDir + "/Spell_8_Triple.asset",
        };

        private static readonly string[] ObsoleteItemAssets =
        {
            ItemDir + "/Item_101_item_spell_fireball.asset",
            ItemDir + "/Item_102_item_spell_iceshard.asset",
            ItemDir + "/Item_103_item_spell_spark.asset",
            ItemDir + "/Item_104_item_spell_ghost.asset",
            ItemDir + "/Item_105_item_spell_damage_up.asset",
            ItemDir + "/Item_106_item_spell_speed_up.asset",
            ItemDir + "/Item_107_item_spell_pierce.asset",
            ItemDir + "/Item_108_item_spell_triple.asset",
        };

        /// <summary>旧 4 条弹道（Id 1..4 已复用于新弹道，留着会与新的 Id 冲突 → 体检直接报"重复 Id"）。</summary>
        private static readonly string[] ObsoleteProjectileAssets =
        {
            ProjDir + "/Proj_Fireball.asset",
            ProjDir + "/Proj_Ice.asset",
            ProjDir + "/Proj_Spark.asset",
            ProjDir + "/Proj_Ghost.asset",
        };

        /// <summary>
        /// 图标引用体检（**必须**，防止"图标全 null 却静默通过"）：
        /// `ArtManager.LoadEmoji` 编辑器下走 AssetDatabase、运行时走 `Resources.Load("Art/<name>")`，
        /// 名字写错在**编辑器里不报错**，只会在构建版里变成 null（实机探针才暴露）。
        /// 这里在生成阶段就把每个用到的 emoji 名字对一遍 `Resources/Art` 的真实文件名。
        /// </summary>
        private static int VerifyIcons(StringBuilder log)
        {
            const string artDir = "Assets/GameMain/Resources/Art";
            string[] used =
            {
                "💦", "2744-fe0f", "1f480", "💧", "💨", "26a1", "231b", "1f6e1",
                "1f4ad", "🪞", "1f451",
            };
            var sb = new StringBuilder();
            int missing = 0;
            for (int i = 0; i < used.Length; i++)
            {
                bool ok = System.IO.File.Exists(artDir + "/" + used[i] + ".png")
                       || System.IO.File.Exists(artDir + "/" + used[i] + ".gif");
                if (!ok)
                {
                    missing++;
                    sb.Append("\n    ✗ 图标缺失: Resources/Art/").Append(used[i]).Append(".png|.gif");
                }
            }
            log.Append("  图标体检: ").Append(used.Length - missing).Append('/').Append(used.Length).Append(" 存在");
            if (missing > 0)
            {
                log.Append(sb);
                Debug.LogError("[SpellContent] 有 " + missing + " 个 emoji 图标在 Resources/Art 下不存在 → 构建版会显示空白图标！" + sb);
            }
            else
            {
                log.Append('\n');
            }
            return missing;
        }

        // ==================== 入口 ====================

        [MenuItem("EmojiWar/Setup/Build Spell Content (S1)", false, 160)]
        public static void BuildAll()
        {
            var log = new StringBuilder();
            EnsureFolders();
            VerifyIcons(log);

            BuildSpellSystemConfig(log);
            BuildProjectiles(log);
            BuildSpells(log);
            BuildWands(log);
            BuildItems(log);
            BuildStartingLoadout(log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SpellContent] S1 内容生成完成（幂等）\n" + log.ToString());
        }

        /// <summary>删除旧法术资产后重建（P9）。</summary>
        [MenuItem("EmojiWar/Setup/Rebuild Spell Content (S1)", false, 161)]
        public static void RebuildWithPurge()
        {
            var log = new StringBuilder();
            EnsureFolders();
            VerifyIcons(log);
            PurgeObsolete(log);
            BuildSpellSystemConfig(log);
            BuildProjectiles(log);
            BuildSpells(log);
            BuildWands(log);
            BuildItems(log);
            BuildStartingLoadout(log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SpellContent] S1 重建完成（已清理旧法术）\n" + log.ToString());
        }

        /// <summary>只清理旧内容（P9）；单独暴露便于排查。</summary>
        [MenuItem("EmojiWar/Setup/Purge Legacy Spells (P9)", false, 162)]
        public static void PurgeOnly()
        {
            var log = new StringBuilder();
            PurgeObsolete(log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SpellContent] 旧法术清理完成\n" + log.ToString());
        }

        // ==================== D31 总配置 ====================

        private static void BuildSpellSystemConfig(StringBuilder log)
        {
            Save<SpellSystemConfigSO>(SpellSystemDir + "/SpellSystemConfig.asset", c =>
            {
                c.MaxSpellSlots = 8;              // P1：法术槽物理上限（可用槽数由法杖 SlotCount 决定）
                c.BackpackCapacity = 30;          // 6×5
                c.ShopCapacity = 6;
                c.ShopItemCount = 3;

                c.MaxTotalTriggers = 64;          // Q7
                c.MaxTriggersPerItem = 8;
                c.MaxPassiveNesting = 3;

                c.DefaultBulletLifetime = 2f;
                c.DefaultBulletRadius = 0.2f;
                c.DefaultBulletSpeed = 12f;
                c.DefaultBulletDamage = 8f;
                c.MaxBulletLifetime = 10f;

                c.DefaultPassiveLimitPerCast = 1;
                c.DefaultPassiveCooldownMs = 500;   // [W-10a] 原 10 帧 @20Hz = 500ms，语义改成毫秒后取值不变
                c.DefaultBuffMaxStacks = 1;
                c.GlobalMaxBuffStacks = 99;

                // §12.2 预算口径
                c.SequenceManaBudgetRatio = 0.7f;
                c.SequenceManaTooLightRatio = 0.4f;
                c.SequenceDurationMax = 1.2f;
                c.DelaySumPerSlotMax = 0.15f;
                c.TotalRechargeMin = 0.8f;
                c.TotalRechargeMax = 1.6f;
                c.RechargeSumRatioMax = 0.6f;
                c.RegenSupportFactor = 0.5f;

                // §12.3 数值区间（体检越界即报错）
                c.LightBand = new SpellValueRange { ManaMin = 3, ManaMax = 8, DelayMin = -0.05f, DelayMax = 0f, RechargeMin = 0.05f, RechargeMax = 0.15f };
                c.MediumBand = new SpellValueRange { ManaMin = 8, ManaMax = 15, DelayMin = 0.05f, DelayMax = 0.1f, RechargeMin = 0.1f, RechargeMax = 0.25f };
                c.HeavyBand = new SpellValueRange { ManaMin = 15, ManaMax = 25, DelayMin = 0.15f, DelayMax = 0.2f, RechargeMin = 0.25f, RechargeMax = 0.4f };
                c.UtilityBand = new SpellValueRange { ManaMin = 0, ManaMax = 12, DelayMin = 0f, DelayMax = 0.15f, RechargeMin = -0.3f, RechargeMax = 0.5f };
            });
            log.Append("  D31 SpellSystemConfig.asset\n");
        }

        // ==================== D6 弹道剖面 ====================

        private static void BuildProjectiles(StringBuilder log)
        {
            // Id 1..8 与 S1 八个物品一一对应（旧 1..4 弹道同路径覆盖）
            Save<ProjectileProfileSO>(ProjDir + "/Proj_SparkBolt.asset", p =>
            {
                p.Id = 1; p.ProfileKey = "proj_spark_bolt";
                p.Speed = 20f; p.Damage = 5f; p.Lifetime = 0.9f; p.Radius = 0.18f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 0; p.Homing = 0f;
            });
            Save<ProjectileProfileSO>(ProjDir + "/Proj_IceSpike.asset", p =>
            {
                p.Id = 2; p.ProfileKey = "proj_ice_spike";
                p.Speed = 15f; p.Damage = 8f; p.Lifetime = 1.6f; p.Radius = 0.20f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 0; p.Homing = 0f;
            });
            Save<ProjectileProfileSO>(ProjDir + "/Proj_RockShot.asset", p =>
            {
                p.Id = 3; p.ProfileKey = "proj_rock_shot";
                p.Speed = 11f; p.Damage = 16f; p.Lifetime = 2.2f; p.Radius = 0.28f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 0; p.Homing = 0f;
            });
            // 4..8 供后续阶段（S5 投射物扩展）使用，S1 先占位稳定 Id
            Save<ProjectileProfileSO>(ProjDir + "/Proj_HomingMissile.asset", p =>
            {
                p.Id = 4; p.ProfileKey = "proj_homing_missile";
                p.Speed = 10f; p.Damage = 7f; p.Lifetime = 3f; p.Radius = 0.22f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 0; p.Homing = 4f;
            });
            Save<ProjectileProfileSO>(ProjDir + "/Proj_BurstFireball.asset", p =>
            {
                p.Id = 5; p.ProfileKey = "proj_burst_fireball";
                p.Speed = 12f; p.Damage = 18f; p.Lifetime = 2.2f; p.Radius = 0.32f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 0; p.Homing = 0f;
            });
            Save<ProjectileProfileSO>(ProjDir + "/Proj_LightningChain.asset", p =>
            {
                p.Id = 6; p.ProfileKey = "proj_lightning_chain";
                p.Speed = 22f; p.Damage = 9f; p.Lifetime = 1.2f; p.Radius = 0.18f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 2; p.Homing = 0f;
            });
            Save<ProjectileProfileSO>(ProjDir + "/Proj_VoidOrb.asset", p =>
            {
                p.Id = 7; p.ProfileKey = "proj_void_orb";
                p.Speed = 7f; p.Damage = 6f; p.Lifetime = 3.4f; p.Radius = 0.40f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 0; p.Homing = 1f;
            });
            Save<ProjectileProfileSO>(ProjDir + "/Proj_BoomerangBlade.asset", p =>
            {
                p.Id = 8; p.ProfileKey = "proj_boomerang_blade";
                p.Speed = 16f; p.Damage = 11f; p.Lifetime = 2.6f; p.Radius = 0.24f;
                p.Count = 1; p.SpreadDeg = 0f; p.Pierce = 1; p.Homing = 2f;
            });
            log.Append("  D6 弹道剖面 8 个（Id 1..8）\n");
        }

        // ==================== D3 物品行为（S1 最小 8 个，P10） ====================

        private static void BuildSpells(StringBuilder log)
        {
            // ---- 6.1 基础投射物（3 个） ----
            // 火花弹：轻档（6 / 0 / +0.1）；接口 = 命中施加 1 层"灼烧"（S4 接命中事件后移交）
            Save<SpellSO>(SpellDir + "/Spell_101_SparkBolt.asset", s =>
            {
                s.Id = 101; s.SpellKey = "spell_spark_bolt"; s.DisplayName = "火花弹";
                s.Description = "小型火焰投射物。";
                s.IconSprite = ArtManager.LoadEmoji("💦");
                ResetCard(s);
                s.ManaCost = 6; s.DelayAdd = 0f; s.RechargeAdd = 0.1f;
                s.Tags = SpellTag.Fire;
                s.StructTags = SpellStructTag.Projectile;
                s.EffectKind = SpellEffectKind.FireProjectile;
                s.TargetScope = SpellTargetScope.Self; s.AffectCount = 0;
                s.Projectile = Load<ProjectileProfileSO>(ProjDir + "/Proj_SparkBolt.asset");
                s.AppliesBuff = true;
                s.BuffApply = new BuffApplyDef
                {
                    BuffKey = "buff_burn", DisplayName = "灼烧", Stat = BuffStat.Damage,
                    ValuePerStack = 1f, MaxStacks = 5, Timing = BuffTiming.ByTriggerCount, Duration = 2,
                    StackRule = BuffStackRule.Additive, IsNegative = true,
                };
                s.Rarity = ItemRarity.Common; s.Weight = 1.4f;
            });
            // 冰锥：中档（10 / +0.05 / +0.15）；接口 = 命中施加"冰缓"
            Save<SpellSO>(SpellDir + "/Spell_102_IceSpike.asset", s =>
            {
                s.Id = 102; s.SpellKey = "spell_ice_spike"; s.DisplayName = "冰锥";
                s.Description = "冰伤 + 轻减速。";
                s.IconSprite = ArtManager.LoadEmoji("2744-fe0f");
                ResetCard(s);
                s.ManaCost = 10; s.DelayAdd = 0.05f; s.RechargeAdd = 0.15f;
                s.Tags = SpellTag.Ice;
                s.StructTags = SpellStructTag.Projectile;
                s.EffectKind = SpellEffectKind.FireProjectile;
                s.TargetScope = SpellTargetScope.Self; s.AffectCount = 0;
                s.Projectile = Load<ProjectileProfileSO>(ProjDir + "/Proj_IceSpike.asset");
                s.AppliesBuff = true;
                s.BuffApply = new BuffApplyDef
                {
                    BuffKey = "buff_chill", DisplayName = "冰缓", Stat = BuffStat.Delay,
                    ValuePerStack = 0.02f, MaxStacks = 3, Timing = BuffTiming.ByTriggerCount, Duration = 3,
                    StackRule = BuffStackRule.Additive, IsNegative = true,
                };
                s.Rarity = ItemRarity.Common; s.Weight = 1.1f;
            });
            // 岩石弹：中档（12 / +0.1 / +0.2）；接口 = 命中触发"物理"标签被动（S4）
            Save<SpellSO>(SpellDir + "/Spell_103_RockShot.asset", s =>
            {
                s.Id = 103; s.SpellKey = "spell_rock_shot"; s.DisplayName = "岩石弹";
                s.Description = "物理伤 + 击退。";
                s.IconSprite = ArtManager.LoadEmoji("1f480");
                ResetCard(s);
                s.ManaCost = 12; s.DelayAdd = 0.1f; s.RechargeAdd = 0.2f;
                s.Tags = SpellTag.Physical;
                s.StructTags = SpellStructTag.Projectile;
                s.EffectKind = SpellEffectKind.FireProjectile;
                s.TargetScope = SpellTargetScope.Self; s.AffectCount = 0;
                s.Projectile = Load<ProjectileProfileSO>(ProjDir + "/Proj_RockShot.asset");
                s.Rarity = ItemRarity.Common; s.Weight = 1.0f;
            });

            // ---- 6.2 耗蓝修饰器（1 个）：节能符文（后续 3 个 ×0.5） ----
            Save<SpellSO>(SpellDir + "/Spell_104_ManaSaveRune.asset", s =>
            {
                s.Id = 104; s.SpellKey = "spell_mana_save_rune"; s.DisplayName = "节能符文";
                s.Description = "后续 3 个物品耗蓝 ×0.5。";
                s.IconSprite = ArtManager.LoadEmoji("💧");
                ResetCard(s);
                s.ManaCost = 11; s.DelayAdd = 0.1f; s.RechargeAdd = 0.1f;
                s.StructTags = SpellStructTag.Modifier;
                s.EffectKind = SpellEffectKind.None;
                s.ManaMul = 0.5f;
                s.TargetScope = SpellTargetScope.NextN; s.AffectCount = 3;
                s.Rarity = ItemRarity.Common; s.Weight = 1.2f;
            });

            // ---- 6.3 延迟修饰器（2 个） ----
            Save<SpellSO>(SpellDir + "/Spell_105_SwiftChant.asset", s =>
            {
                s.Id = 105; s.SpellKey = "spell_swift_chant"; s.DisplayName = "急速咏唱";
                s.Description = "后续 2 个物品延迟 ×0.5。";
                s.IconSprite = ArtManager.LoadEmoji("💨");
                ResetCard(s);
                s.ManaCost = 6; s.DelayAdd = -0.05f; s.RechargeAdd = 0.1f;
                s.StructTags = SpellStructTag.Modifier;
                s.EffectKind = SpellEffectKind.None;
                s.DelayMul = 0.5f;
                s.TargetScope = SpellTargetScope.NextN; s.AffectCount = 2;
                s.Rarity = ItemRarity.Common; s.Weight = 1.1f;
            });
            Save<SpellSO>(SpellDir + "/Spell_106_InstantGlyph.asset", s =>
            {
                s.Id = 106; s.SpellKey = "spell_instant_glyph"; s.DisplayName = "瞬发铭文";
                s.Description = "下一个物品延迟归零。";
                s.IconSprite = ArtManager.LoadEmoji("26a1");
                ResetCard(s);
                s.ManaCost = 5; s.DelayAdd = 0f; s.RechargeAdd = 0.1f;
                s.StructTags = SpellStructTag.Modifier | SpellStructTag.Control;
                s.EffectKind = SpellEffectKind.None;
                s.DelayMul = 0f;
                s.TargetScope = SpellTargetScope.NextN; s.AffectCount = 1;
                s.Rarity = ItemRarity.Common; s.Weight = 1.2f;
            });

            // ---- 6.4 充能修饰器（1 个）：快速充能（总充能 −0.3） ----
            Save<SpellSO>(SpellDir + "/Spell_107_QuickRecharge.asset", s =>
            {
                s.Id = 107; s.SpellKey = "spell_quick_recharge"; s.DisplayName = "快速充能";
                s.Description = "本次施法总充能 −0.3s。";
                s.IconSprite = ArtManager.LoadEmoji("231b");
                ResetCard(s);
                s.ManaCost = 10; s.DelayAdd = 0.1f; s.RechargeAdd = -0.3f;
                s.StructTags = SpellStructTag.Modifier;
                s.EffectKind = SpellEffectKind.None;
                s.Rarity = ItemRarity.Rare; s.Weight = 0.9f;
            });

            // ---- 6.5 控制流（1 个）：终止符（立即结束，充能 −0.1） ----
            Save<SpellSO>(SpellDir + "/Spell_108_Terminator.asset", s =>
            {
                s.Id = 108; s.SpellKey = "spell_terminator"; s.DisplayName = "终止符";
                s.Description = "立即结束本次施法，充能 −0.1s。";
                s.IconSprite = ArtManager.LoadEmoji("1f6e1");
                ResetCard(s);
                s.ManaCost = 0; s.DelayAdd = 0f; s.RechargeAdd = -0.1f;
                s.StructTags = SpellStructTag.Terminate | SpellStructTag.Control;
                s.EffectKind = SpellEffectKind.SequenceOp;
                s.SequenceOp = SpellSequenceOp.None;   // 结束语义由 TriggerType=Terminate 承担
                s.TriggerType = SpellTriggerType.Terminate;
                s.TargetScope = SpellTargetScope.Self; s.AffectCount = 0;
                s.Rarity = ItemRarity.Common; s.Weight = 1.0f;
            });

            log.Append("  D3 物品 8 个（3 投射物 + 3 修饰器 + 1 充能修饰 + 1 终止符）\n");
        }

        /// <summary>物品卡的公共默认（每张卡都显式重置，避免复用旧资产时残留字段）。</summary>
        private static void ResetCard(SpellSO s)
        {
            s.TriggerType = SpellTriggerType.Immediate;
            s.DelayMs = 0;   // [W-10a] 毫秒语义
            s.SchemaVersion = 1;   // 生成器产出的资产直接是"毫秒版"，不要再被迁移脚本换算一次
            s.Condition = default;
            s.ManaMul = 1f; s.ManaAdd = 0;
            s.DelayMul = 1f; s.DelayAdd = 0f;
            s.RechargeMul = 1f; s.RechargeAdd = 0f;
            s.EffectScale = 1f;
            s.SequenceOp = SpellSequenceOp.None; s.Operand = 0; s.ManaDelta = 0;
            // 默认作用对象 = 自身（"作用后续 N 个"只对修饰器有意义，由各卡自己显式声明）
            s.TargetScope = SpellTargetScope.Self; s.AffectCount = 0;
            s.Tags = SpellTag.None; s.StructTags = SpellStructTag.None;
            s.ProjDamageAdd = 0f; s.ProjDamageMul = 1f; s.ProjSpeedMul = 1f;
            s.ProjPierceAdd = 0; s.ProjSpreadAdd = 0f; s.ProjHomingAdd = 0f;
            s.AppliesBuff = false; s.BuffApply = default;
            s.IsPassive = false; s.Passive = default;
            s.ItemFlags = SpellItemFlags.None;
        }

        // ==================== D5 法杖（P2b=A：0.12 / 0.08 / 0.06） ====================

        private static void BuildWands(StringBuilder log)
        {
            Save<WandSO>(WandDir + "/Wand_1_Apprentice.asset", w =>
            {
                w.Id = 1; w.WandKey = "wand_apprentice"; w.DisplayName = "学徒法杖";
                w.Description = "3 槽，节奏平稳，适合新手。";
                w.IconSprite = ArtManager.LoadEmoji("1f4ad");
                w.SlotCount = 3; w.CastDelay = 0.12f; w.RechargeTime = 1.2f;   // §12.1 P2b=A
                w.ManaMax = 80; w.ManaRegen = 18f;
                w.Mode = CastMode.Sequential;
                w.DefaultSpellIds = new[] { 104, 101, 101 };                    // 节能符文 → 火花弹 ×2
            });
            Save<WandSO>(WandDir + "/Wand_2_Mirror.asset", w =>
            {
                w.Id = 2; w.WandKey = "wand_mirror"; w.DisplayName = "镜像长杖";
                w.Description = "5 槽，魔力充沛，可编排复杂序列。";
                w.IconSprite = ArtManager.LoadEmoji("🪞");
                w.SlotCount = 5; w.CastDelay = 0.08f; w.RechargeTime = 0.9f;   // §12.1 P2b=A
                w.ManaMax = 140; w.ManaRegen = 26f;
                w.Mode = CastMode.Sequential;
                w.DefaultSpellIds = new[] { 106, 102, 105, 101, 107 };          // 瞬发 → 冰锥 → 急速 → 火花 → 快速充能
            });
            Save<WandSO>(WandDir + "/Wand_3_Mithril.asset", w =>
            {
                w.Id = 3; w.WandKey = "wand_mithril"; w.DisplayName = "秘银重杖";
                w.Description = "7 槽，承接满序列爆发 build。";
                w.IconSprite = ArtManager.LoadEmoji("1f451");
                w.SlotCount = 7; w.CastDelay = 0.06f; w.RechargeTime = 1.3f;   // §12.1 P2b=A 新增档
                w.ManaMax = 200; w.ManaRegen = 34f;
                w.Mode = CastMode.Sequential;
                w.DefaultSpellIds = new int[0];                                 // 只用于开局发放；本杖不进初始装备
            });
            log.Append("  D5 法杖 3 把（0.12 / 0.08 / 0.06）\n");
        }

        // ==================== D4 物品卡（**StackMax=1**，P4） ====================

        private static void BuildItems(StringBuilder log)
        {
            MakeSpellItem(101, SpellDir + "/Spell_101_SparkBolt.asset", "item_spell_spark_bolt", "火花弹", 20, ItemRarity.Common, 1.4f);
            MakeSpellItem(102, SpellDir + "/Spell_102_IceSpike.asset", "item_spell_ice_spike", "冰锥", 30, ItemRarity.Common, 1.1f);
            MakeSpellItem(103, SpellDir + "/Spell_103_RockShot.asset", "item_spell_rock_shot", "岩石弹", 35, ItemRarity.Common, 1.0f);
            MakeSpellItem(104, SpellDir + "/Spell_104_ManaSaveRune.asset", "item_spell_mana_save_rune", "节能符文", 40, ItemRarity.Rare, 1.2f);
            MakeSpellItem(105, SpellDir + "/Spell_105_SwiftChant.asset", "item_spell_swift_chant", "急速咏唱", 35, ItemRarity.Common, 1.1f);
            MakeSpellItem(106, SpellDir + "/Spell_106_InstantGlyph.asset", "item_spell_instant_glyph", "瞬发铭文", 30, ItemRarity.Rare, 1.2f);
            MakeSpellItem(107, SpellDir + "/Spell_107_QuickRecharge.asset", "item_spell_quick_recharge", "快速充能", 55, ItemRarity.Rare, 0.9f);
            MakeSpellItem(108, SpellDir + "/Spell_108_Terminator.asset", "item_spell_terminator", "终止符", 25, ItemRarity.Common, 1.0f);

            MakeWandItem(201, WandDir + "/Wand_1_Apprentice.asset", "item_wand_apprentice", "学徒法杖", 120, ItemRarity.Common, 0.6f);
            MakeWandItem(202, WandDir + "/Wand_2_Mirror.asset", "item_wand_mirror", "镜像长杖", 260, ItemRarity.Epic, 0.25f);
            MakeWandItem(203, WandDir + "/Wand_3_Mithril.asset", "item_wand_mithril", "秘银重杖", 480, ItemRarity.Legendary, 0.10f);

            log.Append("  D4 物品卡 11 张（法术 8 张 StackMax=1）\n");
        }

        private static void MakeSpellItem(int id, string spellAssetPath, string key, string name, int price, ItemRarity rarity, float weight)
        {
            var spell = Load<SpellSO>(spellAssetPath);
            Save<ItemSO>(ItemDir + "/Item_" + id + "_" + key + ".asset", it =>
            {
                it.Id = id; it.ItemKey = key; it.DisplayName = name;
                it.Description = spell != null ? spell.Description : "";
                it.Category = ItemCategory.Spell;
                it.Rarity = rarity; it.Weight = weight;
                it.IconSprite = spell != null ? spell.IconSprite : null;
                // P4：法术卡**不可堆叠**，从根上避免"堆叠合并时 buff 归谁"的歧义
                it.StackMax = 1; it.Price = price; it.SellPrice = Mathf.Max(1, price / 2);
                it.Flags = ItemFlags.Sellable;
                it.Spell = spell; it.Wand = null;
            });
        }

        private static void MakeWandItem(int id, string wandAssetPath, string key, string name, int price, ItemRarity rarity, float weight)
        {
            var wand = Load<WandSO>(wandAssetPath);
            Save<ItemSO>(ItemDir + "/Item_" + id + "_" + key + ".asset", it =>
            {
                it.Id = id; it.ItemKey = key; it.DisplayName = name;
                it.Description = wand != null ? wand.Description : "";
                it.Category = ItemCategory.Wand;
                it.Rarity = rarity; it.Weight = weight;
                it.IconSprite = wand != null ? wand.IconSprite : null;
                it.StackMax = 1; it.Price = price; it.SellPrice = Mathf.Max(1, price / 2);
                it.Flags = ItemFlags.Unique | ItemFlags.Sellable;
                it.Wand = wand; it.Spell = null;
            });
        }

        // ==================== D32 初始装备 ====================

        private static void BuildStartingLoadout(StringBuilder log)
        {
            Save<StartingLoadoutSO>(SpellSystemDir + "/StartingLoadout.asset", l =>
            {
                l.LeftHand = new StartingHand
                {
                    Wand = Load<WandSO>(WandDir + "/Wand_1_Apprentice.asset"),
                    Spells = new[]
                    {
                        Load<SpellSO>(SpellDir + "/Spell_104_ManaSaveRune.asset"),
                        Load<SpellSO>(SpellDir + "/Spell_101_SparkBolt.asset"),
                        Load<SpellSO>(SpellDir + "/Spell_101_SparkBolt.asset"),
                    },
                };
                l.RightHand = new StartingHand
                {
                    Wand = Load<WandSO>(WandDir + "/Wand_2_Mirror.asset"),
                    Spells = new[]
                    {
                        Load<SpellSO>(SpellDir + "/Spell_106_InstantGlyph.asset"),
                        Load<SpellSO>(SpellDir + "/Spell_102_IceSpike.asset"),
                        Load<SpellSO>(SpellDir + "/Spell_105_SwiftChant.asset"),
                        Load<SpellSO>(SpellDir + "/Spell_101_SparkBolt.asset"),
                        Load<SpellSO>(SpellDir + "/Spell_107_QuickRecharge.asset"),
                    },
                };
                // 背包初始卡：给玩家可即时拖装的备选（流程 B 的教学材料）
                l.BackpackItems = new[]
                {
                    Load<ItemSO>(ItemDir + "/Item_103_item_spell_rock_shot.asset"),
                    Load<ItemSO>(ItemDir + "/Item_106_item_spell_instant_glyph.asset"),
                    Load<ItemSO>(ItemDir + "/Item_105_item_spell_swift_chant.asset"),
                    Load<ItemSO>(ItemDir + "/Item_108_item_spell_terminator.asset"),
                };
                l.StartingCoin = 0;   // 0 = 沿用角色表 Coin
            });
            log.Append("  D32 StartingLoadout.asset（左手学徒 3 槽 / 右手镜像 5 槽 / 背包 4 张）\n");
        }

        // ==================== P9 清理 ====================

        private static void PurgeObsolete(StringBuilder log)
        {
            int removed = 0;

            // 1) 旧法术资产
            for (int i = 0; i < ObsoleteSpellAssets.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<SpellSO>(ObsoleteSpellAssets[i]) != null)
                {
                    AssetDatabase.DeleteAsset(ObsoleteSpellAssets[i]);
                    log.Append("  - 删除旧法术 ").Append(ObsoleteSpellAssets[i]).Append('\n');
                    removed++;
                }
            }

            // 1b) 旧弹道（Id 1..4 已被新弹道复用：不删 → 配置体检直接报"Projectile 重复 Id"）
            for (int i = 0; i < ObsoleteProjectileAssets.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<ProjectileProfileSO>(ObsoleteProjectileAssets[i]) != null)
                {
                    AssetDatabase.DeleteAsset(ObsoleteProjectileAssets[i]);
                    log.Append("  - 删除旧弹道 ").Append(ObsoleteProjectileAssets[i]).Append('\n');
                    removed++;
                }
            }

            // 2) 旧法术的物品卡
            for (int i = 0; i < ObsoleteItemAssets.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<ItemSO>(ObsoleteItemAssets[i]) != null)
                {
                    AssetDatabase.DeleteAsset(ObsoleteItemAssets[i]);
                    log.Append("  - 删除旧物品卡 ").Append(ObsoleteItemAssets[i]).Append('\n');
                    removed++;
                }
            }

            // 3) 兜底：任何仍指向"已删除法术"的物品卡一律清掉（防手工资产残留）
            var guids = AssetDatabase.FindAssets("t:ItemSO", new[] { ItemDir });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var it = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
                if (it != null && it.Spell != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(it.Spell)))
                {
                    AssetDatabase.DeleteAsset(path);
                    log.Append("  - 删除引用失效法术的物品卡 ").Append(path).Append('\n');
                    removed++;
                }
            }

            log.Append("  P9 清理共删除 ").Append(removed).Append(" 个资产\n");
        }

        // ==================== 辅助 ====================

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/GameMain/Resources");
            EnsureFolder(DataRoot);
            EnsureFolder(ProjDir);
            EnsureFolder(SpellDir);
            EnsureFolder(WandDir);
            EnsureFolder(ItemDir);
            EnsureFolder(SpellSystemDir);
        }

        private static T Load<T>(string path) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static void Save<T>(string path, System.Action<T> fill) where T : ScriptableObject
        {
            var so = AssetDatabase.LoadAssetAtPath<T>(path);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<T>();
                fill(so);
                AssetDatabase.CreateAsset(so, path);
                return;
            }
            fill(so);
            EditorUtility.SetDirty(so);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) { return; }
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

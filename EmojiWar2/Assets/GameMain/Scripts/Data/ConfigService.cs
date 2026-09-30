//------------------------------------------------------------
// EmojiWar GameMain - 配置服务（ConfigService）
//
// 架构定位（数据驱动方案 B：分层混合 + 统一服务/校验）：
//   - **唯一配置入口**：业务代码统一走 ConfigService.GetXxx(id)，不再散落 GameEntry.Data 直取；
//   - **启动校验**：id 唯一/有效、必填字段、跨表引用（选角条目→角色存在）等，问题集中上报；
//   - **配置版本哈希**：对关键字段做稳定序列化后求 FNV-1a 哈希，
//     供联机握手校验（Host/Client 配置不一致时阻止开局）与日志追溯。
//
// 数据来源（当前）：Resources/Data/** 下的 SO 资产（Character/Weapon/Mod/Item/Spell/Wand/Projectile/Select/Battle），
// 由 DataComponent 同步 LoadAll；后续 Phase 3 可改为“表格(CSV/Excel)→导出”生成这些 SO/二进制，
// 本服务对上层 API 不变。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>配置服务：统一访问 + 启动校验 + 版本哈希。</summary>
    public static class ConfigService
    {
        private static bool s_Ready = false;
        private static ulong s_VersionHash = 0UL;
        private static readonly List<string> s_Issues = new List<string>();
        private static readonly Dictionary<Type, FieldInfo[]> s_SortedFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>是否已就绪（已从 DataComponent 建好索引并完成校验）。</summary>
        public static bool IsReady { get { return s_Ready; } }

        /// <summary>配置版本哈希（FNV-1a 64bit；Host/Client 应一致）。</summary>
        public static ulong VersionHash { get { return s_VersionHash; } }

        /// <summary>校验问题列表（空=无问题）。</summary>
        public static IList<string> Issues { get { return s_Issues; } }

        /// <summary>构建服务：建索引 + 校验 + 计算版本哈希。应在 DataComponent.Init() 之后调用。</summary>
        public static void Rebuild()
        {
            s_Issues.Clear();
            s_Ready = false;
            s_VersionHash = 0UL;

            var data = GameEntry.Data;
            if (data == null)
            {
                s_Issues.Add("DataComponent 为空（GameEntry.Data==null）");
                FlushIssues();
                return;
            }

            Validate(data);
            s_VersionHash = ComputeHash(data);
            s_Ready = true;

            FlushIssues();
            Debug.Log(string.Format("[Config] ready: hash=0x{0:X16} chars={1} weapons={2} mods={3} items={4} spells={5} wands={6} proj={7}",
                s_VersionHash,
                data.GetAllCharacters().Count,
                data.GetAllWeapons().Count,
                data.GetAllMods().Count,
                data.GetAllItems().Count,
                data.GetAllSpells().Count,
                data.GetAllWands().Count,
                data.GetAllProjectiles().Count));
        }

        // ---------- 统一访问 ----------

        public static CharacterSO GetCharacter(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetCharacter(id) : null;
        }

        public static WeaponSO GetWeapon(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetWeapon(id) : null;
        }

        public static ModSO GetMod(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetMod(id) : null;
        }

        // ---------- 物品 / 法术 / 法杖 / 弹道剖面（P1：物品与法杖系统） ----------

        public static ItemSO GetItem(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetItem(id) : null;
        }

        public static SpellSO GetSpell(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetSpell(id) : null;
        }

        public static WandSO GetWand(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetWand(id) : null;
        }

        public static ProjectileProfileSO GetProjectile(int id)
        {
            return GameEntry.Data != null ? GameEntry.Data.GetProjectile(id) : null;
        }

        /// <summary>选角展示条目（id/名字/图标/描述）。</summary>
        public static CharacterSelectEntry GetSelectEntry(int id)
        {
            var select = GameEntry.Data != null ? GameEntry.Data.SelectConfig : null;
            if (select == null || select.characters == null)
            {
                return null;
            }
            for (int i = 0; i < select.characters.Count; i++)
            {
                var e = select.characters[i];
                if (e != null && e.id == id)
                {
                    return e;
                }
            }
            return null;
        }

        public static BattleConfigSO Battle
        {
            get { return GameEntry.Data != null ? GameEntry.Data.Battle : null; }
        }

        /// <summary>法术系统总配置（D31；缺失时返回 null，调用方用兜底值）。</summary>
        public static SpellSystemConfigSO SpellSystem
        {
            get { return GameEntry.Data != null ? GameEntry.Data.SpellSystem : null; }
        }

        /// <summary>初始装备配置（D32）。</summary>
        public static StartingLoadoutSO StartingLoadout
        {
            get { return GameEntry.Data != null ? GameEntry.Data.StartingLoadout : null; }
        }

        // ---------- 数值兜底（配置缺失时也用配置的"出厂默认"，绝不写死平衡数值） ----------

        public static int MaxSpellSlots { get { var c = SpellSystem; return c != null ? c.MaxSpellSlots : 8; } }
        public static int BackpackCapacity { get { var c = SpellSystem; return c != null ? c.BackpackCapacity : 30; } }
        public static int ShopCapacity { get { var c = SpellSystem; return c != null ? c.ShopCapacity : 6; } }
        public static int MaxTotalTriggers { get { var c = SpellSystem; return c != null ? c.MaxTotalTriggers : 64; } }
        public static int MaxTriggersPerItem { get { var c = SpellSystem; return c != null ? c.MaxTriggersPerItem : 8; } }
        public static int MaxPassiveNesting { get { var c = SpellSystem; return c != null ? c.MaxPassiveNesting : 3; } }

        // ---------- 校验 ----------

        private static void Validate(DataComponent data)
        {
            var chars = data.GetAllCharacters();
            var weapons = data.GetAllWeapons();
            var mods = data.GetAllMods();
            var modSys = data.SpellSystem;   // D31：所有上限/区间口径都来自它（P11 配置驱动）

            var seen = new HashSet<int>();
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                if (c == null) { s_Issues.Add("Character[" + i + "] 为 null"); continue; }
                if (c.Id <= 0) { s_Issues.Add("Character Id<=0: " + c.name); }
                if (!seen.Add(c.Id)) { s_Issues.Add("Character 重复 Id=" + c.Id + " (" + c.name + ")"); }
                if (string.IsNullOrEmpty(c.CharacterName)) { s_Issues.Add("Character Id=" + c.Id + " 缺 CharacterName"); }
                if (c.IconSprite == null) { s_Issues.Add("Character Id=" + c.Id + " 缺 IconSprite（图标引用）"); }
                if (c.MaxHealth <= 0) { s_Issues.Add("Character Id=" + c.Id + " MaxHealth<=0"); }
            }

            seen.Clear();
            for (int i = 0; i < weapons.Count; i++)
            {
                var w = weapons[i];
                if (w == null) { s_Issues.Add("Weapon[" + i + "] 为 null"); continue; }
                if (w.Id <= 0) { s_Issues.Add("Weapon Id<=0: " + w.name); }
                if (!seen.Add(w.Id)) { s_Issues.Add("Weapon 重复 Id=" + w.Id + " (" + w.name + ")"); }
                if (string.IsNullOrEmpty(w.WeaponName)) { s_Issues.Add("Weapon Id=" + w.Id + " 缺 WeaponName"); }
                if (w.IconSprite == null) { s_Issues.Add("Weapon Id=" + w.Id + " 缺 IconSprite（图标引用）"); }
                // 防具（如盾）不参与攻击数值校验
                bool isArmor = !string.IsNullOrEmpty(w.Category) && w.Category.Contains("防具");
                if (!isArmor)
                {
                    if (w.Damage <= 0f) { s_Issues.Add("Weapon Id=" + w.Id + " Damage<=0（非防具须>0）"); }
                    if (w.FireRate <= 0f) { s_Issues.Add("Weapon Id=" + w.Id + " FireRate<=0（非防具须>0）"); }
                }
            }

            seen.Clear();
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null) { s_Issues.Add("Mod[" + i + "] 为 null"); continue; }
                if (m.Id <= 0) { s_Issues.Add("Mod Id<=0: " + m.name); }
                if (!seen.Add(m.Id)) { s_Issues.Add("Mod 重复 Id=" + m.Id + " (" + m.name + ")"); }
                if (m.IconSprite == null) { s_Issues.Add("Mod Id=" + m.Id + " 缺 IconSprite（图标引用）"); }
            }

            // 跨表引用：选角展示条目必须能查到角色
            var select = data.SelectConfig;
            if (select == null || select.characters == null || select.characters.Count == 0)
            {
                s_Issues.Add("SelectConfig 为空（无选角展示条目）");
            }
            else
            {
                for (int i = 0; i < select.characters.Count; i++)
                {
                    var e = select.characters[i];
                    if (e == null) { s_Issues.Add("SelectConfig[" + i + "] 为 null"); continue; }
                    if (e.iconSprite == null) { s_Issues.Add("SelectConfig id=" + e.id + " 缺 iconSprite（图标引用）"); }
                    if (data.GetCharacter(e.id) == null)
                    {
                        s_Issues.Add("SelectConfig 条目 id=" + e.id + " 在 Character 表不存在");
                    }
                }
            }

            // ==================== 物品 / 法术 / 法杖 / 弹道剖面（P1） ====================

            // 弹道剖面：纯数值
            var projectiles = data.GetAllProjectiles();
            seen.Clear();
            for (int i = 0; i < projectiles.Count; i++)
            {
                var p = projectiles[i];
                if (p == null) { s_Issues.Add("Projectile[" + i + "] 为 null"); continue; }
                if (p.Id <= 0) { s_Issues.Add("Projectile Id<=0: " + p.name); }
                if (!seen.Add(p.Id)) { s_Issues.Add("Projectile 重复 Id=" + p.Id + " (" + p.name + ")"); }
                if (p.Speed <= 0f) { s_Issues.Add("Projectile Id=" + p.Id + " Speed<=0"); }
                if (p.Lifetime <= 0f) { s_Issues.Add("Projectile Id=" + p.Id + " Lifetime<=0"); }
                if (p.Radius <= 0f) { s_Issues.Add("Projectile Id=" + p.Id + " Radius<=0"); }
                if (p.Count < 1) { s_Issues.Add("Projectile Id=" + p.Id + " Count<1"); }
            }

            // 法术（通用物品卡，Q8）：引用完整性 + §12.3 数值区间 + 枚举合法性 + 被动/buff 配置
            var spells = data.GetAllSpells();
            seen.Clear();
            for (int i = 0; i < spells.Count; i++)
            {
                var sp = spells[i];
                if (sp == null) { s_Issues.Add("Spell[" + i + "] 为 null"); continue; }
                if (sp.Id <= 0) { s_Issues.Add("Spell Id<=0: " + sp.name); }
                if (!seen.Add(sp.Id)) { s_Issues.Add("Spell 重复 Id=" + sp.Id + " (" + sp.name + ")"); }
                if (string.IsNullOrEmpty(sp.SpellKey)) { s_Issues.Add("Spell Id=" + sp.Id + " 缺 SpellKey"); }
                if (string.IsNullOrEmpty(sp.DisplayName)) { s_Issues.Add("Spell Id=" + sp.Id + " 缺 DisplayName"); }
                if (sp.IconSprite == null) { s_Issues.Add("Spell Id=" + sp.Id + " 缺 IconSprite（图标引用）"); }
                if (sp.ManaCost < 0) { s_Issues.Add("Spell Id=" + sp.Id + " ManaCost<0"); }

                // --- 枚举合法性（禁止字符串/越界枚举） ---
                if (!IsDefinedEnum(sp.TriggerType)) { s_Issues.Add("Spell Id=" + sp.Id + " TriggerType 非法"); }
                if (!IsDefinedEnum(sp.EffectKind)) { s_Issues.Add("Spell Id=" + sp.Id + " EffectKind 非法"); }
                if (!IsDefinedEnum(sp.TargetScope)) { s_Issues.Add("Spell Id=" + sp.Id + " TargetScope 非法"); }
                if (!IsDefinedEnum(sp.Rarity)) { s_Issues.Add("Spell Id=" + sp.Id + " Rarity 非法"); }
                if (!IsDefinedEnum(sp.SequenceOp)) { s_Issues.Add("Spell Id=" + sp.Id + " SequenceOp 非法"); }

                // --- 按触发类型校验配置完整性（设计 §2.5） ---
                switch (sp.TriggerType)
                {
                    case SpellTriggerType.Delayed:
                        if (sp.DelayMs <= 0) { s_Issues.Add("Spell Id=" + sp.Id + "（Delayed）DelayMs<=0（跨帧延迟必须 >0 毫秒）"); }
                        break;
                    case SpellTriggerType.Conditional:
                        if (sp.Condition.RequiredTags == SpellTag.None
                            && sp.Condition.RequiredStructTags == SpellStructTag.None
                            && sp.Condition.MinManaCost <= 0)
                        {
                            s_Issues.Add("Spell Id=" + sp.Id + "（Conditional）未配置任何条件（永不触发）");
                        }
                        break;
                    case SpellTriggerType.Terminate:
                        if (sp.EffectKind != SpellEffectKind.SequenceOp && sp.EffectKind != SpellEffectKind.None)
                        {
                            s_Issues.Add("Spell Id=" + sp.Id + "（Terminate）不应同时产出效果（EffectKind=" + sp.EffectKind + "）");
                        }
                        break;
                    case SpellTriggerType.Passive:
                        if (!sp.IsPassive) { s_Issues.Add("Spell Id=" + sp.Id + "（Passive）未勾选 IsPassive"); }
                        break;
                }

                // --- 效果完整性（设计 §9 Effect） ---
                switch (sp.EffectKind)
                {
                    case SpellEffectKind.FireProjectile:
                        if (sp.Projectile == null) { s_Issues.Add("Spell Id=" + sp.Id + "（FireProjectile）缺 Projectile 剖面引用"); }
                        else if (data.GetProjectile(sp.Projectile.Id) == null) { s_Issues.Add("Spell Id=" + sp.Id + " 引用的弹道剖面 Id=" + sp.Projectile.Id + " 不在表中"); }
                        break;
                    case SpellEffectKind.SequenceOp:
                        if (sp.SequenceOp == SpellSequenceOp.None && sp.TriggerType != SpellTriggerType.Terminate)
                        {
                            s_Issues.Add("Spell Id=" + sp.Id + "（SequenceOp）未指定操作");
                        }
                        if (sp.SequenceOp == SpellSequenceOp.RepeatNext && sp.Operand < 1)
                        {
                            s_Issues.Add("Spell Id=" + sp.Id + "（RepeatNext）Operand<1");
                        }
                        break;
                    case SpellEffectKind.ApplyBuff:
                        if (!sp.AppliesBuff) { s_Issues.Add("Spell Id=" + sp.Id + "（ApplyBuff）未勾选 AppliesBuff"); }
                        break;
                }

                // --- 效果强度只放大伤害（Q11） ---
                if (sp.EffectScale <= 0f) { s_Issues.Add("Spell Id=" + sp.Id + " EffectScale<=0"); }

                // --- 层级一致性（设计 §1.2：物品属于哪一层由设计指定） ---
                // 修饰器必须显式声明"作用后续 N 个"；非修饰器**不允许**挂 NextN/PrevN，
                // 否则说明字段被误用（例如把"自身施法延迟"当成"给后续物品的延迟修正"）。
                if (sp.IsModifier
                    && (sp.TargetScope == SpellTargetScope.NextN || sp.TargetScope == SpellTargetScope.PrevN)
                    && sp.AffectCount < 1)
                {
                    s_Issues.Add("Spell Id=" + sp.Id + "（修饰器 " + sp.TargetScope + "）AffectCount<1");
                }
                if (!sp.IsModifier
                    && (sp.TargetScope == SpellTargetScope.NextN || sp.TargetScope == SpellTargetScope.PrevN))
                {
                    s_Issues.Add("Spell Id=" + sp.Id + "（非修饰器）不应使用 " + sp.TargetScope
                        + "：它只作用于自身；若确实要修饰后续物品，请加 StructTags.Modifier");
                }
                if (sp.IsModifier && sp.TargetScope == SpellTargetScope.TagGroup
                    && sp.Tags == SpellTag.None && sp.StructTags == SpellStructTag.None)
                {
                    s_Issues.Add("Spell Id=" + sp.Id + "（修饰器 TagGroup）未配置任何标签");
                }

                // --- Buff 配置（第三层） ---
                if (sp.AppliesBuff)
                {
                    if (string.IsNullOrEmpty(sp.BuffApply.BuffKey)) { s_Issues.Add("Spell Id=" + sp.Id + " 施加的 Buff 缺 BuffKey（进哈希需要稳定键）"); }
                    if (sp.BuffApply.MaxStacks < 1) { s_Issues.Add("Spell Id=" + sp.Id + " BuffApply.MaxStacks<1"); }
                    // [W-10a] 时长是双语义：次数型/施法型 = 次数，时间型 = 毫秒（两个字段分别校验）
                    int buffDur = sp.BuffApply.Timing == BuffTiming.BySeconds ? sp.BuffApply.DurationMs : sp.BuffApply.Duration;
                    if (buffDur < 1) { s_Issues.Add("Spell Id=" + sp.Id + " BuffApply 时长<1（次数型/施法型看 Duration=次数，时间型看 DurationMs=毫秒）"); }
                    if (!IsDefinedEnum(sp.BuffApply.Timing)) { s_Issues.Add("Spell Id=" + sp.Id + " BuffApply.Timing 非法"); }
                    if (!IsDefinedEnum(sp.BuffApply.StackRule)) { s_Issues.Add("Spell Id=" + sp.Id + " BuffApply.StackRule 非法"); }
                    if (!IsDefinedEnum(sp.BuffApply.Stat)) { s_Issues.Add("Spell Id=" + sp.Id + " BuffApply.Stat 非法"); }
                    if (sp.BuffApply.Timing == BuffTiming.BySeconds && modSys != null
                        && sp.BuffApply.DurationMs > Mathf.RoundToInt(modSys.SequenceDurationMax * 1000f) * 4)
                    {
                        s_Issues.Add("Spell Id=" + sp.Id + " BuffApply 时间型时长异常（毫秒；超过序列时长上限的 4 倍）");
                    }
                }

                // --- 被动配置（第四层） ---
                if (sp.IsPassive)
                {
                    if (sp.Passive.Event == SpellPassiveEvent.None) { s_Issues.Add("Spell Id=" + sp.Id + "（被动）未指定监听事件"); }
                    else if (!IsQ5Event(sp.Passive.Event))
                    {
                        s_Issues.Add("Spell Id=" + sp.Id + " 被动事件 " + sp.Passive.Event + " 不在第一版集合（Q5：施法开始/结束、序列清空、命中、击杀）");
                    }
                    if (sp.Passive.LimitPerCast < 1) { s_Issues.Add("Spell Id=" + sp.Id + "（被动）LimitPerCast<1"); }
                    if (sp.Passive.CooldownMs < 0) { s_Issues.Add("Spell Id=" + sp.Id + "（被动）CooldownMs<0"); }
                    if (!IsDefinedEnum(sp.Passive.Scope)) { s_Issues.Add("Spell Id=" + sp.Id + "（被动）Scope 非法"); }
                    if (!IsDefinedEnum(sp.Passive.Order)) { s_Issues.Add("Spell Id=" + sp.Id + "（被动）Order 非法"); }
                }

                // --- §12.3 单物品数值区间（体检越界即报错） ---
                if (modSys != null) { CheckSpellBands(sp, modSys, s_Issues); }
            }

            // 法杖：槽位/节奏/魔力/出厂装填
            var wands = data.GetAllWands();
            seen.Clear();
            for (int i = 0; i < wands.Count; i++)
            {
                var wd = wands[i];
                if (wd == null) { s_Issues.Add("Wand[" + i + "] 为 null"); continue; }
                if (wd.Id <= 0) { s_Issues.Add("Wand Id<=0: " + wd.name); }
                if (!seen.Add(wd.Id)) { s_Issues.Add("Wand 重复 Id=" + wd.Id + " (" + wd.name + ")"); }
                if (string.IsNullOrEmpty(wd.WandKey)) { s_Issues.Add("Wand Id=" + wd.Id + " 缺 WandKey"); }
                if (string.IsNullOrEmpty(wd.DisplayName)) { s_Issues.Add("Wand Id=" + wd.Id + " 缺 DisplayName"); }
                if (wd.IconSprite == null) { s_Issues.Add("Wand Id=" + wd.Id + " 缺 IconSprite（图标引用）"); }
                if (wd.SlotCount < 1 || (modSys != null && wd.SlotCount > modSys.MaxSpellSlots))
                {
                    s_Issues.Add("Wand Id=" + wd.Id + " SlotCount 须在 1.." + (modSys != null ? modSys.MaxSpellSlots : 8)
                        + "（来自 SpellSystemConfigSO.MaxSpellSlots；当前 " + wd.SlotCount + "）");
                }
                // P2b=A：基础施法延迟是序列内节奏的**加值**，必须 >0 且不夸张（§7.2 定稿）
                if (wd.CastDelay <= 0f) { s_Issues.Add("Wand Id=" + wd.Id + " 基础施法延迟（CastDelay）<=0（P2b=A 要求加值参与序列时长）"); }
                else if (wd.CastDelay > 0.5f) { s_Issues.Add("Wand Id=" + wd.Id + " 基础施法延迟 " + wd.CastDelay.ToString("F2") + " 过大（P2b=A 定稿 0.06~0.12）"); }
                if (wd.RechargeTime < 0f) { s_Issues.Add("Wand Id=" + wd.Id + " RechargeTime<0"); }
                if (wd.ManaMax <= 0) { s_Issues.Add("Wand Id=" + wd.Id + " ManaMax<=0"); }
                if (wd.ManaRegen < 0f) { s_Issues.Add("Wand Id=" + wd.Id + " ManaRegen<0"); }
                if (wd.DefaultSpellIds == null) { s_Issues.Add("Wand Id=" + wd.Id + " DefaultSpellIds 为 null"); continue; }
                if (wd.DefaultSpellIds.Length > wd.SlotCount) { s_Issues.Add("Wand Id=" + wd.Id + " 出厂装填数超过槽位数"); }
                for (int k = 0; k < wd.DefaultSpellIds.Length; k++)
                {
                    if (data.GetSpell(wd.DefaultSpellIds[k]) == null)
                    {
                        s_Issues.Add("Wand Id=" + wd.Id + " 出厂装填的法术 Id=" + wd.DefaultSpellIds[k] + " 不在 Spell 表中");
                    }
                }
            }

            // 物品卡：类别与行为引用必须一致；每把法杖/每个法术都应有一张可购买的卡
            var items = data.GetAllItems();
            var coveredSpells = new HashSet<int>();
            var coveredWands = new HashSet<int>();
            seen.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) { s_Issues.Add("Item[" + i + "] 为 null"); continue; }
                if (it.Id <= 0) { s_Issues.Add("Item Id<=0: " + it.name); }
                if (!seen.Add(it.Id)) { s_Issues.Add("Item 重复 Id=" + it.Id + " (" + it.name + ")"); }
                if (string.IsNullOrEmpty(it.ItemKey)) { s_Issues.Add("Item Id=" + it.Id + " 缺 ItemKey"); }
                if (string.IsNullOrEmpty(it.DisplayName)) { s_Issues.Add("Item Id=" + it.Id + " 缺 DisplayName"); }
                if (it.IconSprite == null) { s_Issues.Add("Item Id=" + it.Id + " 缺 IconSprite（图标引用）"); }
                if (it.StackMax < 1) { s_Issues.Add("Item Id=" + it.Id + " StackMax<1"); }
                if (it.Price < 0 || it.SellPrice < 0) { s_Issues.Add("Item Id=" + it.Id + " 价格<0"); }
                if (it.StackMax > 1 && (it.Flags & ItemFlags.Stackable) == 0)
                {
                    s_Issues.Add("Item Id=" + it.Id + " StackMax>1 但未标 Stackable");
                }
                if (it.Category == ItemCategory.Wand)
                {
                    if (it.Wand == null) { s_Issues.Add("Item Id=" + it.Id + "（Wand）缺 Wand 引用"); }
                    else if (data.GetWand(it.Wand.Id) == null) { s_Issues.Add("Item Id=" + it.Id + " 引用的法杖 Id=" + it.Wand.Id + " 不在表中"); }
                    else { coveredWands.Add(it.Wand.Id); }
                    if (it.Spell != null) { s_Issues.Add("Item Id=" + it.Id + "（Wand）不应同时引用 Spell"); }
                }
                else if (it.Category == ItemCategory.Spell)
                {
                    if (it.Spell == null) { s_Issues.Add("Item Id=" + it.Id + "（Spell）缺 Spell 引用"); }
                    else if (data.GetSpell(it.Spell.Id) == null) { s_Issues.Add("Item Id=" + it.Id + " 引用的法术 Id=" + it.Spell.Id + " 不在表中"); }
                    else { coveredSpells.Add(it.Spell.Id); }
                    if (it.Wand != null) { s_Issues.Add("Item Id=" + it.Id + "（Spell）不应同时引用 Wand"); }
                }
            }

            for (int i = 0; i < spells.Count; i++)
            {
                var sp = spells[i];
                if (sp != null && !coveredSpells.Contains(sp.Id))
                {
                    s_Issues.Add("Spell Id=" + sp.Id + "（" + sp.DisplayName + "）没有对应的 Item 物品卡（无法进入背包/商店）");
                }
            }
            for (int i = 0; i < wands.Count; i++)
            {
                var wd = wands[i];
                if (wd != null && !coveredWands.Contains(wd.Id))
                {
                    s_Issues.Add("Wand Id=" + wd.Id + "（" + wd.DisplayName + "）没有对应的 Item 物品卡");
                }
            }

            if (data.Battle == null)
            {
                s_Issues.Add("BattleConfig 为空（波次/平衡参数缺失）");
            }

            // ---- D31：法术系统总配置必须存在（否则所有上限/兜底无源，P11） ----
            if (modSys == null)
            {
                s_Issues.Add("SpellSystemConfig 为空（D31 缺失：法术槽上限/触发上限/预算口径无配置来源）");
            }
            else
            {
                if (modSys.MaxSpellSlots < 1) { s_Issues.Add("SpellSystemConfig MaxSpellSlots<1"); }
                if (modSys.MaxTotalTriggers < 1) { s_Issues.Add("SpellSystemConfig MaxTotalTriggers<1"); }
                if (modSys.MaxTriggersPerItem < 1) { s_Issues.Add("SpellSystemConfig MaxTriggersPerItem<1"); }
                if (modSys.MaxPassiveNesting < 0) { s_Issues.Add("SpellSystemConfig MaxPassiveNesting<0"); }
                if (modSys.BackpackCapacity < 1) { s_Issues.Add("SpellSystemConfig BackpackCapacity<1"); }
                if (modSys.ShopCapacity < 1) { s_Issues.Add("SpellSystemConfig ShopCapacity<1"); }
                if (modSys.TotalRechargeMin > modSys.TotalRechargeMax) { s_Issues.Add("SpellSystemConfig 总充能区间下界>上界"); }
                if (modSys.SequenceDurationMax <= 0f) { s_Issues.Add("SpellSystemConfig SequenceDurationMax<=0"); }
            }

            // ---- D32：初始装备（引用的法杖/法术/物品卡必须都在表内） ----
            var loadout = data.StartingLoadout;
            if (loadout == null)
            {
                s_Issues.Add("StartingLoadout 为空（D32 缺失：初始装备仍会走硬编码兜底）");
            }
            else
            {
                CheckStartingHand(loadout.LeftHand, "左手", data, s_Issues);
                CheckStartingHand(loadout.RightHand, "右手", data, s_Issues);
                if (loadout.BackpackItems != null)
                {
                    for (int i = 0; i < loadout.BackpackItems.Length; i++)
                    {
                        var it = loadout.BackpackItems[i];
                        if (it == null) { s_Issues.Add("StartingLoadout 背包初始卡[" + i + "] 为空引用"); }
                        else if (data.GetItem(it.Id) == null) { s_Issues.Add("StartingLoadout 背包初始卡 Id=" + it.Id + " 不在 Item 表中"); }
                    }
                }
            }
        }

        /// <summary>初始装备的一只手：法杖在表内 + 预填法术不超过槽数且在表内。</summary>
        private static void CheckStartingHand(StartingHand hand, string label, DataComponent data, List<string> issues)
        {
            if (hand.Wand == null)
            {
                issues.Add("StartingLoadout " + label + " 未配置法杖（该手空手 → 不施法）");
                return;
            }
            if (data.GetWand(hand.Wand.Id) == null)
            {
                issues.Add("StartingLoadout " + label + " 引用的法杖 Id=" + hand.Wand.Id + " 不在 Wand 表中");
            }
            int slots = hand.Wand.SlotCount;
            if (hand.Spells != null && hand.Spells.Length > slots)
            {
                issues.Add("StartingLoadout " + label + " 预填法术数 " + hand.Spells.Length + " > 法杖槽数 " + slots);
            }
            if (hand.Spells != null)
            {
                for (int i = 0; i < hand.Spells.Length; i++)
                {
                    var sp = hand.Spells[i];
                    if (sp == null) { issues.Add("StartingLoadout " + label + " 预填法术[" + i + "] 为空引用"); continue; }
                    if (data.GetSpell(sp.Id) == null) { issues.Add("StartingLoadout " + label + " 预填法术 Id=" + sp.Id + " 不在 Spell 表中"); }
                }
            }
        }

        /// <summary>§12.3 单物品数值区间校验（体检越界即报错；档位由物品性质决定）。</summary>
        private static void CheckSpellBands(SpellSO sp, SpellSystemConfigSO cfg, List<string> issues)
        {
            // §12.3 的区间表描述的是**效果类物品**（造成效果的那一方）的数值档位。
            // 纯修饰/控制类物品（设计 §6.2–6.5）的取值口径不同，而且设计 §2.4 明确允许
            // **负延迟**（"延迟为负可提前触发但设下限"），与表里"0~+0.15"冲突 → 这类物品跳过区间校验。
            bool isUtility = sp.IsModifier
                || (sp.StructTags & (SpellStructTag.Modifier | SpellStructTag.Control | SpellStructTag.Terminate)) != 0;
            if (isUtility) { return; }

            SpellValueRange band;
            string bandName;
            if (sp.ManaCost >= 15)
            {
                band = cfg.HeavyBand; bandName = "重";
            }
            else if (sp.ManaCost >= 8)
            {
                band = cfg.MediumBand; bandName = "中";
            }
            else
            {
                band = cfg.LightBand; bandName = "轻";
            }

            if (band.ManaMax <= 0 && band.DelayMax <= 0f && band.RechargeMax <= 0f)
            {
                return;   // 区间未配置（空结构体）→ 跳过，避免误报
            }

            // §12.3 的区间表描述的是**单物品自身的数值**（耗蓝 / 该物品的施法延迟 / 该物品的充能代价），
            // 不是"它给后续物品的修正"（后者是加成系数，按区间判会误报，例如急速咏唱 DelayAdd = −0.05）。
            if (!band.ManaInRange(sp.ManaCost))
            {
                issues.Add("Spell Id=" + sp.Id + "（" + sp.DisplayName + "）耗蓝 " + sp.ManaCost
                    + " 超出【" + bandName + "】区间 " + band.ManaMin + "~" + band.ManaMax + "（§12.3）");
            }
            if (!band.DelayInRange(sp.DelayAdd))
            {
                issues.Add("Spell Id=" + sp.Id + "（" + sp.DisplayName + "）自身施法延迟 " + sp.DelayAdd.ToString("F2")
                    + " 超出【" + bandName + "】区间 " + band.DelayMin.ToString("F2") + "~" + band.DelayMax.ToString("F2") + "（§12.3）");
            }
            if (!band.RechargeInRange(sp.RechargeAdd))
            {
                issues.Add("Spell Id=" + sp.Id + "（" + sp.DisplayName + "）自身充能代价 " + sp.RechargeAdd.ToString("F2")
                    + " 超出【" + bandName + "】区间 " + band.RechargeMin.ToString("F2") + "~" + band.RechargeMax.ToString("F2") + "（§12.3）");
            }
        }

        /// <summary>Q5 第一版事件集合（其余事件（暴击/受伤/环境）当前模拟层不产生）。</summary>
        private static bool IsQ5Event(SpellPassiveEvent e)
        {
            return e == SpellPassiveEvent.CastStart || e == SpellPassiveEvent.CastEnd
                || e == SpellPassiveEvent.SequenceCleared || e == SpellPassiveEvent.Hit
                || e == SpellPassiveEvent.Kill;
        }

        private static bool IsDefinedEnum<T>(T value) where T : struct
        {
            return System.Enum.IsDefined(typeof(T), value);
        }

        private static void FlushIssues()
        {
            if (s_Issues.Count == 0)
            {
                WriteProbe("[config] validate OK, hash=0x" + s_VersionHash.ToString("X16"));
                return;
            }
            for (int i = 0; i < s_Issues.Count; i++)
            {
                Debug.LogError("[Config] " + s_Issues[i]);
                WriteProbe("[config] ISSUE: " + s_Issues[i]);
            }
        }

        // ---------- 版本哈希（稳定序列化关键字段） ----------

        /// <summary>
        /// [Q3] 浮点字段进哈希的**唯一**写法：位模式 + 不变区域。
        /// 原先是 `.ToString("R")` —— 它同样**受 CultureInfo 影响**（de-DE 下小数点是逗号），
        /// 于是"两份配置完全一样却算出不同哈希"。这个漏项是 `SelfCheck` 的第一条断言实测抓出来的：
        /// 只修 `AppendValue` 只覆盖了 Battle/SpellSystem 两段，C/W/P/M/S/WD 各段仍在用 `"R"`。
        /// </summary>
        private static string F(float v)
        {
            return "f" + BitConverter.SingleToInt32Bits(v).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [Q3] 把配置字段值写成**区域设置无关 + 逐位精确**的文本，供版本哈希使用。
        /// 为什么不能用 `v.ToString()`：
        ///   (a) **受 CultureInfo 影响** —— de-DE 下 `0.5f.ToString()` 得到 `"0,5"`，两端区域不同哈希就不同；
        ///   (b) float/double 的十进制往返表示在不同 .NET/运行时之间不保证一致 → 哈希"看起来一样却不相等"；
        ///   (c) **结构体字段直接退化成类型名** —— `SpellValueRange.ToString()` 返回 `"SpellValueRange"`，
        ///       于是 `SpellSystemConfigSO` 的 LightBand/MediumBand/HeavyBand/UtilityBand **四个档位的数值
        ///       完全没有进入哈希**（实测：改档位数值，VersionHash 不变）—— 这类"静默漏项"必须靠递归修掉。
        /// 口径：浮点取位模式、布尔取 0/1、枚举取整数值、结构体递归其公共实例字段、SO 引用取资产名。
        /// </summary>
        private static void AppendValue(StringBuilder sb, Type t, object v)
        {
            if (v == null) { sb.Append("null"); return; }

            if (t == typeof(float)) { sb.Append('f').Append(BitConverter.SingleToInt32Bits((float)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (t == typeof(double)) { sb.Append('d').Append(BitConverter.DoubleToInt64Bits((double)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (t == typeof(bool)) { sb.Append((bool)v ? '1' : '0'); return; }
            if (t.IsEnum) { sb.Append('e').Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (t == typeof(string)) { sb.Append((string)v); return; }
            if (t.IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }

            var arr = v as Array;
            if (arr != null)
            {
                sb.Append('[').Append(arr.Length).Append(':');
                for (int i = 0; i < arr.Length; i++) { AppendValue(sb, t.GetElementType(), arr.GetValue(i)); sb.Append(','); }
                sb.Append(']');
                return;
            }

            if (t.IsValueType)   // 结构体（如 SpellValueRange）：递归全部公共实例字段
            {
                sb.Append('{');
                var fields = SortedFields(t);
                for (int i = 0; i < fields.Length; i++)
                {
                    sb.Append(fields[i].Name).Append('=');
                    AppendValue(sb, fields[i].FieldType, fields[i].GetValue(v));
                    sb.Append(';');
                }
                sb.Append('}');
                return;
            }

            // 引用类型：SO 用资产名（与图标字段 `iconSprite.name` 同一口径），其余退化为类型名
            var so = v as ScriptableObject;
            if (so != null) { sb.Append("so:").Append(so.name); return; }
            sb.Append("ref:").Append(t.Name);
        }

        /// <summary>
        /// 公共实例字段，**按字段名序数排序**。
        /// `Type.GetFields()` 的返回顺序官方标注为"未指定"；同一程序集内实测稳定，但跨运行时/跨编译器
        /// 不保证 —— 而版本哈希的作用正是"判定两份配置是不是同一份"，顺序不稳定会让它偶发不相等。
        /// </summary>
        private static FieldInfo[] SortedFields(Type t)
        {
            FieldInfo[] cached;
            if (s_SortedFields.TryGetValue(t, out cached)) { return cached; }

            var list = new List<FieldInfo>();
            var all = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].IsStatic) { list.Add(all[i]); }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            cached = list.ToArray();
            s_SortedFields[t] = cached;
            return cached;
        }

        private static ulong ComputeHash(DataComponent data)
        {
            return Fnv1a64(BuildHashText(data));
        }

        /// <summary>版本哈希的序列化正文（抽出来便于自检直接断言"某字段真的进了哈希"）。</summary>
        private static string BuildHashText(DataComponent data)
        {
            var sb = new StringBuilder(4096);

            var chars = new List<CharacterSO>(data.GetAllCharacters());
            chars.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                if (c == null) { continue; }
                sb.Append("C|").Append(c.Id).Append('|').Append(c.CharacterName).Append('|')
                  .Append(c.MaxHealth).Append('|').Append(F(c.MoveSpeed)).Append('|')
                  .Append(c.Coin).Append('|').Append(c.IconSprite != null ? c.IconSprite.name : "null").Append('\n');
            }

            var weapons = new List<WeaponSO>(data.GetAllWeapons());
            weapons.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < weapons.Count; i++)
            {
                var w = weapons[i];
                if (w == null) { continue; }
                sb.Append("W|").Append(w.Id).Append('|').Append(w.WeaponName).Append('|')
                  .Append(w.Damage).Append('|').Append(F(w.FireRate)).Append('|')
                  .Append(F(w.BulletSpeed)).Append('|').Append(w.IconSprite != null ? w.IconSprite.name : "null").Append('\n');
            }

            var mods = new List<ModSO>(data.GetAllMods());
            mods.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null) { continue; }
                sb.Append("M|").Append(m.Id).Append('|').Append(m.ModName).Append('|')
                  .Append(m.Description).Append('|').Append(m.IconSprite != null ? m.IconSprite.name : "null").Append('\n');
            }

            // ---- 物品与法杖系统（P1）：只混入数值/键名，不混资产名（图标等表现数据不入哈希） ----
            var projectiles = new List<ProjectileProfileSO>(data.GetAllProjectiles());
            projectiles.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < projectiles.Count; i++)
            {
                var p = projectiles[i];
                if (p == null) { continue; }
                sb.Append("P|").Append(p.Id).Append('|').Append(F(p.Speed)).Append('|')
                  .Append(F(p.Damage)).Append('|').Append(F(p.Lifetime)).Append('|')
                  .Append(F(p.Radius)).Append('|').Append(p.Count).Append('|')
                  .Append(F(p.SpreadDeg)).Append('|').Append(p.Pierce).Append('|')
                  .Append(F(p.Homing)).Append('\n');
            }

            var spells = new List<SpellSO>(data.GetAllSpells());
            spells.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < spells.Count; i++)
            {
                var sp = spells[i];
                if (sp == null) { continue; }
                // 只混"影响模拟的数值/枚举/键名"；图标等表现数据不入哈希
                sb.Append("SP|").Append(sp.Id).Append('|').Append(sp.SpellKey).Append('|')
                  .Append((int)sp.TriggerType).Append('|').Append(sp.DelayMs).Append('|')
                  .Append((int)sp.EffectKind).Append('|').Append(F(sp.EffectScale)).Append('|')
                  .Append(sp.ManaCost).Append('|').Append(F(sp.ManaMul)).Append('|').Append(sp.ManaAdd).Append('|')
                  .Append(F(sp.DelayMul)).Append('|').Append(F(sp.DelayAdd)).Append('|')
                  .Append(F(sp.RechargeAdd)).Append('|').Append(F(sp.RechargeMul)).Append('|')
                  .Append((int)sp.Tags).Append('|').Append((int)sp.StructTags).Append('|')
                  .Append((int)sp.TargetScope).Append('|').Append(sp.AffectCount).Append('|')
                  .Append((int)sp.ItemFlags).Append('|')
                  .Append(sp.Projectile != null ? sp.Projectile.Id : 0).Append('|')
                  .Append((int)sp.SequenceOp).Append('|').Append(sp.Operand).Append('|').Append(sp.ManaDelta).Append('|')
                  .Append(F(sp.ProjDamageAdd)).Append('|').Append(F(sp.ProjDamageMul)).Append('|')
                  .Append(F(sp.ProjSpeedMul)).Append('|').Append(sp.ProjPierceAdd).Append('|')
                  .Append(F(sp.ProjSpreadAdd)).Append('|').Append(F(sp.ProjHomingAdd)).Append('\n');

                // 条件门
                sb.Append("SPC|").Append(sp.Id).Append('|').Append((int)sp.Condition.RequiredTags).Append('|')
                  .Append((int)sp.Condition.RequiredStructTags).Append('|').Append(sp.Condition.MinManaCost).Append('|')
                  .Append(sp.Condition.Invert ? 1 : 0).Append('\n');

                // Buff（层数/计时影响推进 → 必须入哈希）
                if (sp.AppliesBuff)
                {
                    sb.Append("SPB|").Append(sp.Id).Append('|').Append(sp.BuffApply.BuffKey).Append('|')
                      .Append((int)sp.BuffApply.Stat).Append('|').Append(F(sp.BuffApply.ValuePerStack)).Append('|')
                      .Append(sp.BuffApply.MaxStacks).Append('|').Append((int)sp.BuffApply.Timing).Append('|')
                      // [W-10a] 时长双语义：次数型/施法型 = 次数（Duration），时间型 = 毫秒（DurationMs）
                      .Append(sp.BuffApply.Duration).Append('|').Append(sp.BuffApply.DurationMs).Append('|')
                      .Append((int)sp.BuffApply.StackRule).Append('|')
                      .Append(sp.BuffApply.IsNegative ? 1 : 0).Append('\n');
                }

                // 被动（限次/冷却影响推进 → 必须入哈希）
                if (sp.IsPassive)
                {
                    sb.Append("SPP|").Append(sp.Id).Append('|').Append((int)sp.Passive.Event).Append('|')
                      .Append(sp.Passive.LimitPerCast).Append('|').Append(sp.Passive.CooldownMs).Append('|')
                      .Append((int)sp.Passive.Scope).Append('|').Append(sp.Passive.AffectCount).Append('|')
                      .Append((int)sp.Passive.Order).Append('|').Append(sp.Passive.ManaCost).Append('|')
                      .Append(sp.Passive.IgnoreRecharge ? 1 : 0).Append('\n');
                }
            }

            var wands = new List<WandSO>(data.GetAllWands());
            wands.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < wands.Count; i++)
            {
                var wd = wands[i];
                if (wd == null) { continue; }
                sb.Append("D|").Append(wd.Id).Append('|').Append(wd.WandKey).Append('|').Append(wd.SlotCount).Append('|')
                  .Append(F(wd.CastDelay)).Append('|').Append(F(wd.RechargeTime)).Append('|')
                  .Append(wd.ManaMax).Append('|').Append(F(wd.ManaRegen)).Append('|')
                  .Append((int)wd.Mode).Append('\n');
                if (wd.DefaultSpellIds != null)
                {
                    for (int k = 0; k < wd.DefaultSpellIds.Length; k++)
                    {
                        sb.Append("DD|").Append(wd.Id).Append('|').Append(k).Append('|').Append(wd.DefaultSpellIds[k]).Append('\n');
                    }
                }
            }

            var items = new List<ItemSO>(data.GetAllItems());
            items.Sort((a, b) => a != null && b != null ? a.Id.CompareTo(b.Id) : 0);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) { continue; }
                sb.Append("I|").Append(it.Id).Append('|').Append(it.ItemKey).Append('|').Append((int)it.Category).Append('|')
                  .Append((int)it.Rarity).Append('|').Append(it.StackMax).Append('|').Append(it.Price).Append('|')
                  .Append(it.SellPrice).Append('|').Append((int)it.Flags).Append('|')
                  .Append(it.Wand != null ? it.Wand.Id : 0).Append('|').Append(it.Spell != null ? it.Spell.Id : 0).Append('\n');
            }

            var select = data.SelectConfig;
            if (select != null && select.characters != null)
            {
                for (int i = 0; i < select.characters.Count; i++)
                {
                    var e = select.characters[i];
                    if (e == null) { continue; }
                    sb.Append("S|").Append(e.id).Append('|').Append(e.name).Append('|')
                      .Append(e.iconSprite != null ? e.iconSprite.name : "null").Append('|').Append(e.desc).Append('\n');
                }
            }

            var battle = data.Battle;
            if (battle != null)
            {
                var fields = SortedFields(typeof(BattleConfigSO));
                for (int i = 0; i < fields.Length; i++)
                {
                    sb.Append("B|").Append(fields[i].Name).Append('|');
                    AppendValue(sb, fields[i].FieldType, fields[i].GetValue(battle));
                    sb.Append('\n');
                }
            }

            // ---- D31 法术系统总配置（上限/兜底会进入模拟层，双端必须一致） ----
            var sys = data.SpellSystem;
            if (sys != null)
            {
                var fields = SortedFields(typeof(SpellSystemConfigSO));
                for (int i = 0; i < fields.Length; i++)
                {
                    sb.Append("SS|").Append(fields[i].Name).Append('|');
                    AppendValue(sb, fields[i].FieldType, fields[i].GetValue(sys));
                    sb.Append('\n');
                }
            }

            // ---- D32 初始装备（决定开局 loadout，双端必须一致） ----
            var loadout = data.StartingLoadout;
            if (loadout != null)
            {
                AppendStartingHand(sb, 'L', loadout.LeftHand);
                AppendStartingHand(sb, 'R', loadout.RightHand);
                if (loadout.BackpackItems != null)
                {
                    for (int i = 0; i < loadout.BackpackItems.Length; i++)
                    {
                        sb.Append("LB|").Append(i).Append('|')
                          .Append(loadout.BackpackItems[i] != null ? loadout.BackpackItems[i].Id : 0).Append('\n');
                    }
                }
                sb.Append("LC|").Append(loadout.StartingCoin).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// [Q3] 版本哈希自检（`-configselftest` 跑）。
        /// 三条断言分别对应 `AppendValue` 修掉的三个静默缺陷：区域相关、浮点文本往返、结构体退化成类型名。
        /// </summary>
        public static string SelfCheck()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) { pass++; } else { fail++; }
                lines.Add("[ConfigTest] " + (ok ? "PASS " : "FAIL ") + name + (detail == null ? "" : " - " + detail));
            };

            var data = GameEntry.Data;
            if (data == null)
            {
                lines.Add("[ConfigTest] FAIL 配置未就绪（GameEntry.Data==null）");
                return string.Join("\n", lines.ToArray()) + "\n[ConfigTest] passed=0 failed=1";
            }

            // 1) 区域无关：切到 de-DE（小数点是逗号）后哈希必须不变
            ulong hInvariant = ComputeHash(data);
            ulong hDe;
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                hDe = ComputeHash(data);
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
            check("版本哈希与区域设置无关（de-DE vs 不变区域）", hInvariant == hDe,
                "invariant=0x" + hInvariant.ToString("X16") + " de-DE=0x" + hDe.ToString("X16"));

            // 2) 浮点按位模式：同一数值在不同区域下序列化文本必须逐字节相同
            var f1 = new StringBuilder();
            var f2 = new StringBuilder();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                AppendValue(f1, typeof(float), 0.5f);
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
            AppendValue(f2, typeof(float), 0.5f);
            check("浮点字段按位模式序列化（0.5 在 de-DE 下不变成 \"0,5\"）", f1.ToString() == f2.ToString(),
                "de-DE=\"" + f1 + "\" invariant=\"" + f2 + "\"");

            // 3) 结构体递归：SpellValueRange 改一个数值 → 序列化文本必须改变
            //    （旧实现 `v.ToString()` 对结构体只返回类型名 "SpellValueRange"，四个档位等于没进哈希）
            var bandA = new SpellValueRange { ManaMin = 1, ManaMax = 2, DelayMin = 0.1f, DelayMax = 0.2f, RechargeMin = 0.3f, RechargeMax = 0.4f };
            var bandB = bandA;
            bandB.ManaMax = 3;
            var sA = new StringBuilder();
            var sB = new StringBuilder();
            AppendValue(sA, typeof(SpellValueRange), bandA);
            AppendValue(sB, typeof(SpellValueRange), bandB);
            check("结构体字段递归进哈希（SpellValueRange 改 ManaMax → 文本改变）", sA.ToString() != sB.ToString(),
                "\"" + sA + "\" vs \"" + sB + "\"");

            // 4) 端到端：哈希正文里能看到四个档位的**结构体内容**（而不是类型名）
            string text = BuildHashText(data);
            bool bandsSerialized = text.Contains("SS|HeavyBand|{")
                && text.Contains("SS|LightBand|{")
                && text.Contains("SS|MediumBand|{")
                && text.Contains("SS|UtilityBand|{");
            check("四个 SpellValueRange 档位真的进了哈希正文", bandsSerialized,
                bandsSerialized ? "SS|LightBand|{...} 等 4 项存在" : "哈希正文里找不到档位结构体内容");

            // 5) 战斗配置字段也必须在正文里（防止字段表被换掉后静默漏项）
            bool battleSerialized = text.Contains("B|EnemyBaseHp|") && text.Contains("B|SpawnRadius|") && text.Contains("B|ShopDuration|");
            check("BattleConfigSO 关键字段进了哈希正文", battleSerialized, "B|EnemyBaseHp/B|SpawnRadius/B|ShopDuration");

            // ---- [W-10a] 时长语义：配置只说"毫秒"，帧数只在加载期量化 ----
            var sys2 = data.SpellSystem;
            float tick = EmojiWar.GameMain.Simulation.LockstepSimulation.TickInterval;
            if (sys2 != null && sys2.DefaultPassiveCooldownMs > 0)
            {
                float want = sys2.DefaultPassiveCooldownMs / 1000f;
                int q = EmojiWar.GameMain.Simulation.CastResolver.FramesOf(want);
                float got = q * tick;
                bool ok = System.Math.Abs(got - want) <= tick * 0.5f + 1e-4f;
                check("被动冷却默认值：毫秒 → 帧 → 秒 的往返误差 ≤ 半个 tick", ok,
                    sys2.DefaultPassiveCooldownMs + "ms → " + q + " 帧 → " + got.ToString("F3") + "s @ "
                    + (1f / tick).ToString("F0") + "Hz");
            }
            else
            {
                check("被动冷却默认值已配置（毫秒）", false, "SpellSystemConfig.DefaultPassiveCooldownMs<=0");
            }

            // 反向守门：**SO 里不允许再出现"按帧"的字段名**。
            // 这条是 W-10a 的长期保险：以后谁再加一个 `XxxFrames` 到配置里，切帧率时又会静默改真实时长。
            // （运行时结构体里的 `...Frames` 不受影响 —— 那是量化后的帧，本来就该是帧。）
            var offenders = new List<string>();
            CollectFrameSemanticFields(typeof(BattleConfigSO), offenders, 0);
            CollectFrameSemanticFields(typeof(SpellSystemConfigSO), offenders, 0);
            CollectFrameSemanticFields(typeof(SpellSO), offenders, 0);
            check("配置 SO 里不存在\"按帧\"语义字段（时长一律写毫秒）", offenders.Count == 0,
                offenders.Count == 0 ? "无 XxxFrames 字段" : ("发现: " + string.Join(", ", offenders.ToArray())));

            lines.Add("[ConfigTest] hash=0x" + hInvariant.ToString("X16"));
            lines.Add("[ConfigTest] passed=" + pass + " failed=" + fail);
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// [W-10a] 递归收集"名字以 Frames 结尾"的配置字段（含嵌套结构体，如 `PassiveDef`）。
        /// 这些字段意味着"配置按帧录值"，切帧率就会静默改掉真实时长 —— 必须写成毫秒。
        /// </summary>
        private static void CollectFrameSemanticFields(Type t, List<string> outList, int depth)
        {
            if (t == null || depth > 3) { return; }
            var fields = SortedFields(t);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f.Name.EndsWith("Frames", StringComparison.Ordinal))
                {
                    outList.Add(t.Name + "." + f.Name);
                    continue;
                }
                if (f.FieldType.IsValueType && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum
                    && f.FieldType != typeof(UnityEngine.Vector2))
                {
                    CollectFrameSemanticFields(f.FieldType, outList, depth + 1);
                }
            }
        }

        private static void AppendStartingHand(StringBuilder sb, char tag, StartingHand hand)
        {
            sb.Append(tag).Append("H|").Append(hand.Wand != null ? hand.Wand.Id : 0).Append('|');
            if (hand.Spells != null)
            {
                for (int i = 0; i < hand.Spells.Length; i++)
                {
                    sb.Append(hand.Spells[i] != null ? hand.Spells[i].Id : 0).Append(',');
                }
            }
            sb.Append('\n');
        }

        private static ulong Fnv1a64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong h = offset;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= (byte)s[i];
                h *= prime;
            }
            return h;
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}

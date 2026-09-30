//------------------------------------------------------------
// EmojiWar GameMain - 装备编译（LoadoutCompiler，D15）
//
// 依据：doc/法术编程系统-执行文档.md D15 + §3 流程 A/B + 设计文档 §7「编译桥」
//   表现层（ItemInstance + SO）  --编译-->  纯值类型 CastProgram（sim 可安全持有）
//   编译只读取 SO 上的数值/枚举/键名，**不把 Sprite / GameObject / SO 引用带过去**；
//   同一套装备编译两次必须得到相同的 Hash（自检断言；S7 握手/同步复用该哈希）。
//
// 编译内容（设计 §9 字段分组 → CastSpellData）：
//   身份/触发/资源三修正/效果/目标/标签/Buff/被动/控制标记 全部编译成纯值；
//   物品自身修正（含其施加 buff 的数值部分，设计 §3.6 运算顺序）合并进 `Self`，
//   保证"物品自身修正 → 再 buff 修正"的口径在编译期就是确定的。
//
// 提供 dry-run 预估（D17 预览面板用）：不改状态、不产出子弹，只算总耗蓝/总延迟/总充能。
//------------------------------------------------------------

using System.Text;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>一次发射的静态预估（dry-run；预览面板与预算体检共用）。</summary>
    public struct CastPreview
    {
        public int ItemCount;            // 序列中非空物品数
        public int ManaTotal;            // 总耗蓝（按序累计，含修正）
        public float DelaySum;           // Σ物品施法延迟（经修正）
        public float SequenceDuration;   // (物品数−1) × 法杖基础延迟 + Σ物品延迟（P2b=A）
        public float TotalRecharge;      // 法杖基础充能 + Σ物品充能修正
        public float CycleSeconds;       // 一次发射周期 ≈ 序列时长 + 总充能
        public bool ManaExceedsPool;     // 总耗蓝超过魔力池（会触发 Q2 中止）
        public int AbortSlot;            // 首次魔力不足的槽位（-1 = 不会中止）

        public override string ToString()
        {
            return string.Format("items={0} mana={1} Σdelay={2:F2} 序列时长={3:F2}s 充能={4:F2}s 周期={5:F2}s{6}",
                ItemCount, ManaTotal, DelaySum, SequenceDuration, TotalRecharge, CycleSeconds,
                ManaExceedsPool ? ("(中止于槽" + AbortSlot + ")") : "");
        }
    }

    /// <summary>把装备（法杖实例 + 物品表）编译成模拟层可用的施法程序。</summary>
    public static class LoadoutCompiler
    {
        /// <summary>
        /// 编译手上的一把法杖。
        /// 法术槽来源：**手部法术槽容器**（法术槽属于"手"）；没传 service/handId 时回落到实例上的 SpellSlots 快照。
        /// </summary>
        public static CastProgram Compile(ItemInstance wand, IItemTable table, InventoryService service = null, string handContainerId = null)
        {
            if (wand.IsEmpty || table == null) { return CastProgram.Empty; }

            var item = table.GetItem(wand.ItemId);
            if (item == null || item.Category != ItemCategory.Wand || item.Wand == null) { return CastProgram.Empty; }

            var wandRow = item.Wand;
            int slotCount = wandRow.SlotCount > 0 ? wandRow.SlotCount : 1;

            int[] slotItemIds = new int[slotCount];
            var handSpells = (service != null && !string.IsNullOrEmpty(handContainerId))
                ? service.GetContainer(InventoryService.HandSpellContainerId(handContainerId))
                : null;
            if (handSpells != null)
            {
                for (int i = 0; i < slotCount && i < handSpells.Capacity; i++)
                {
                    var s = handSpells.Get(i);
                    slotItemIds[i] = s.IsEmpty ? 0 : s.ItemId;
                }
            }
            else if (wand.SpellSlots != null)
            {
                for (int i = 0; i < slotCount && i < wand.SpellSlots.Length; i++) { slotItemIds[i] = wand.SpellSlots[i]; }
            }

            var spells = new CastSpellData[slotCount];
            var initialBuffs = new BuffSet[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                spells[i] = CompileSpell(slotItemIds[i], table, i);

                // S3/D15：把法术卡上的临时 Buff **快照**进程序（宿主 = 物品卡实例，Q4）。
                // 必须 Clone：程序是编译产物、跨帧且跨端复用，共享数组会让模拟层的改动污染背包里的物品卡
                // （ItemTypes.cs 警示的同一类"共享 buffs 引用"事故）。
                if (handSpells != null && i < handSpells.Capacity)
                {
                    var cardInst = handSpells.Get(i);
                    initialBuffs[i] = (cardInst.HasBuffs && cardInst.ItemId == slotItemIds[i])
                        ? cardInst.Buffs.Clone()
                        : BuffSet.Empty;
                }
                else
                {
                    initialBuffs[i] = BuffSet.Empty;
                }
            }

            return new CastProgram(wandRow.Id, slotCount, wandRow.CastDelay, wandRow.RechargeTime,
                wandRow.ManaMax, wandRow.ManaRegen, wandRow.Mode, spells, initialBuffs);
        }

        /// <summary>按"手"编译（读取该手容器里的法杖 + 该手的法术槽）。</summary>
        public static CastProgram CompileHand(string handContainerId, IItemTable table, InventoryService service)
        {
            if (service == null || string.IsNullOrEmpty(handContainerId)) { return CastProgram.Empty; }
            var hand = service.GetContainer(handContainerId);
            if (hand == null) { return CastProgram.Empty; }
            return Compile(hand.Get(0), table, service, handContainerId);
        }

        // ====================================================================
        // W-06：联机权威路径 —— 只同步 Id，由各端各自编译
        // ====================================================================

        /// <summary>
        /// 读取某只手的装备 Id（法杖物品 Id + 各槽法术物品 Id）。
        ///
        /// 用途（W-06）：玩家把它上报给 Host，Host 转发给所有端，各端用
        /// <see cref="CompileFromIds"/> 编译 —— 这样"编译输入"是全端一致的数据，
        /// 而不是"各端各自的背包"。空手返回 false（调用方按"空手"处理）。
        /// </summary>
        public static bool ReadHandIds(string handContainerId, IItemTable table, InventoryService service,
            out int wandItemId, out int[] slotItemIds)
        {
            wandItemId = 0;
            slotItemIds = null;
            if (service == null || string.IsNullOrEmpty(handContainerId) || table == null) { return false; }

            var hand = service.GetContainer(handContainerId);
            if (hand == null) { return false; }

            var wand = hand.Get(0);
            if (wand.IsEmpty) { return false; }

            var item = table.GetItem(wand.ItemId);
            if (item == null || item.Category != ItemCategory.Wand || item.Wand == null) { return false; }

            int slotCount = item.Wand.SlotCount > 0 ? item.Wand.SlotCount : 1;
            wandItemId = wand.ItemId;
            slotItemIds = new int[slotCount];

            var handSpells = service.GetContainer(InventoryService.HandSpellContainerId(handContainerId));
            if (handSpells != null)
            {
                for (int i = 0; i < slotCount && i < handSpells.Capacity; i++)
                {
                    var s = handSpells.Get(i);
                    slotItemIds[i] = s.IsEmpty ? 0 : s.ItemId;
                }
            }
            else if (wand.SpellSlots != null)
            {
                for (int i = 0; i < slotCount && i < wand.SpellSlots.Length; i++) { slotItemIds[i] = wand.SpellSlots[i]; }
            }
            return true;
        }

        /// <summary>
        /// 直接按"法杖物品 Id + 各槽法术物品 Id"编译（**完全不读背包**）。
        ///
        /// 存在意义（W-06，报告 A10 / 根因 4）：原先 Host/Client 各自读**本机** ItemSystem 编译，
        /// 且 Host 是"用本机的杖为**所有**玩家编译"→ 任何玩家动过背包就多端分叉。
        /// 现在统一为：各玩家上报自己的 Id → Host 转发 → **所有端（含玩家自己）都用本函数编译**
        /// → 编译输入一致 ⇒ 编译产物逐位一致。
        ///
        /// ⚠️ 已知限制：本函数产出的 `InitialBuffs` **恒为空**（法术卡上的 buff 快照不随 Id 传输）。
        /// 因此联机路径**必须两端都用本函数**（包括玩家自己）——若一端用 <see cref="Compile"/>
        /// （会从卡实例快照 buff）另一端用本函数，反而会因 buff 快照不同而分叉。
        /// 若将来"开局前给法术卡挂 buff"成为真实玩法，需要把 buff 快照一起传输。
        /// </summary>
        public static CastProgram CompileFromIds(int wandItemId, int[] slotItemIds, IItemTable table)
        {
            if (wandItemId <= 0 || table == null) { return CastProgram.Empty; }

            var item = table.GetItem(wandItemId);
            if (item == null || item.Category != ItemCategory.Wand || item.Wand == null) { return CastProgram.Empty; }

            var wandRow = item.Wand;
            int slotCount = wandRow.SlotCount > 0 ? wandRow.SlotCount : 1;

            var spells = new CastSpellData[slotCount];
            var initialBuffs = new BuffSet[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                int spellItemId = (slotItemIds != null && i < slotItemIds.Length) ? slotItemIds[i] : 0;
                spells[i] = CompileSpell(spellItemId, table, i);
                initialBuffs[i] = BuffSet.Empty;   // 见上方"已知限制"
            }

            return new CastProgram(wandRow.Id, slotCount, wandRow.CastDelay, wandRow.RechargeTime,
                wandRow.ManaMax, wandRow.ManaRegen, wandRow.Mode, spells, initialBuffs);
        }

        /// <summary>编译左右手并给出稳定哈希（handId 非空时按"手"读取法术槽）。</summary>
        public static LoadoutSnapshot CompileLoadout(ItemInstance primary, ItemInstance secondary,
            IItemTable table, InventoryService service = null, string primaryHandId = null, string secondaryHandId = null)
        {
            var p = Compile(primary, table, service, primaryHandId);
            var s = Compile(secondary, table, service, secondaryHandId);
            return new LoadoutSnapshot(p, s, Hash(p, s));
        }

        /// <summary>单个物品（法术卡）→ 纯数据。找不到/非法则返回空槽。</summary>
        public static CastSpellData CompileSpell(int spellItemId, IItemTable table, int slotIndex)
        {
            if (spellItemId <= 0 || table == null) { return CastSpellData.Empty; }
            var item = table.GetItem(spellItemId);
            if (item == null || item.Category != ItemCategory.Spell || item.Spell == null) { return CastSpellData.Empty; }

            var sp = item.Spell;
            var p = sp.Projectile;

            // ---- 物品属性分两类，**绝不能混**（执行文档 §0.1 第 1 条） ----
            //   (a) 每物品自身属性：自身施法延迟、自身充能代价、buff 数值 → 只作用于**本物品**；
            //   (b) 后续物品修正（第二层）：只有 StructTags.Modifier 的物品才有，
            //       写进修正集供**后续**物品使用。
            // 把 (a) 混进 (b) 会导致"后一个普通投射物覆盖前一个修饰器的修正"，以及
            // "修饰器把自己的延迟修正吃进自己的间隔"（实测：符文+3火花弹 变 17/26 而不是 20）。
            float ownDelayAdd = sp.DelayAdd;             // (a) 自身施法延迟修正
            float ownRechargeAdd = sp.RechargeAdd;       // (a) 自身充能代价
            float ownRechargeMul = sp.RechargeMul;

            CastStatMod self = CastStatMod.Identity;
            self.DamageAdd = sp.ProjDamageAdd;
            self.DamageMul = sp.ProjDamageMul;
            self.SpeedMul = sp.ProjSpeedMul;
            self.PierceAdd = sp.ProjPierceAdd;
            self.SpreadAdd = sp.ProjSpreadAdd;
            self.HomingAdd = sp.ProjHomingAdd;
            if (sp.IsModifier)
            {
                // (b) 只有修饰器才把"后续物品修正"写进修正集
                self.ManaAdd = sp.ManaAdd;
                self.ManaMul = sp.ManaMul;
                self.DelayMul = sp.DelayMul;
                self.DelayAdd = sp.DelayAdd;
            }

            // ---- buff 数值（第三层）：**只记录"施加什么"，绝不并入自身数值** ----
            // S3 之前这里有一处占位：把 ValuePerStack × MaxStacks 直接加进"本物品自己"的
            // 耗蓝/延迟/充能/伤害（注释自称"S3 上线前按'对本物品'并入"）。那与设计 §3.1/§3.5 冲突
            // （buff 是附着在**目标物品**上的限时状态，不是施加者的立即修正），已在 S3 拆除。
            // 现在 buff 的施加由 CastResolver.ApplyEffect → ApplyBuffEffect 落到运行期
            // CastRuntimeState.SlotBuffs[目标槽]，再由 EffectiveMods 参与**目标物品**的结算。
            bool appliesBuff = sp.AppliesBuff;
            CastBuffDef buff = default;
            if (appliesBuff)
            {
                buff = new CastBuffDef
                {
                    KeyHash = CastBuffDef.HashKey(sp.BuffApply.BuffKey),
                    Stat = sp.BuffApply.Stat,
                    ValuePerStack = sp.BuffApply.ValuePerStack,
                    MaxStacks = sp.BuffApply.MaxStacks,
                    Timing = sp.BuffApply.Timing,
                    // [W-10a] 时间型：毫秒 → 帧（量化只在这一处发生）；次数型/施法型：原样是"次数"
                    Duration = sp.BuffApply.Timing == BuffTiming.BySeconds
                        ? EmojiWar.GameMain.Simulation.CastResolver.FramesOf(sp.BuffApply.DurationMs / 1000f)
                        : sp.BuffApply.Duration,
                    StackRule = sp.BuffApply.StackRule,
                    IsNegative = sp.BuffApply.IsNegative,
                };
            }

            // ---- 被动（第四层） ----
            CastPassiveDef passive = default;
            bool isPassive = sp.IsPassive;
            if (isPassive)
            {
                passive = new CastPassiveDef
                {
                    Event = sp.Passive.Event,
                    LimitPerCast = sp.Passive.LimitPerCast,
                    // [W-10a] 毫秒 → 帧（0 表示"用 SpellSystemConfig 的默认值"，FramesOf(0)=0 自然保留该语义）
                    CooldownFrames = EmojiWar.GameMain.Simulation.CastResolver.FramesOf(sp.Passive.CooldownMs / 1000f),
                    Scope = sp.Passive.Scope,
                    AffectCount = sp.Passive.AffectCount,
                    Order = sp.Passive.Order,
                    ManaCost = sp.Passive.ManaCost,
                    IgnoreRecharge = sp.Passive.IgnoreRecharge,
                };
            }

            return new CastSpellData(
                sp.Id, item.Id, slotIndex,
                sp.TriggerType, EmojiWar.GameMain.Simulation.CastResolver.FramesOf(sp.DelayMs / 1000f), sp.EffectKind, sp.EffectScale,
                sp.Tags, sp.StructTags, sp.ItemFlags,
                sp.TargetScope, sp.AffectCount, sp.Condition,
                sp.ManaCost, self, ownDelayAdd, ownRechargeAdd, ownRechargeMul,
                sp.IsModifier, sp.SequenceOp, sp.Operand, sp.ManaDelta,
                p != null ? p.Id : 0,
                p != null ? p.Speed : 0f,
                p != null ? p.Damage : 0f,
                p != null ? p.Lifetime : 0f,
                p != null ? p.Radius : 0f,
                p != null ? (p.Count < 1 ? 1 : p.Count) : 1,
                p != null ? p.SpreadDeg : 0f,
                p != null ? p.Pierce : 0,
                p != null ? p.Homing : 0f,
                appliesBuff, buff, isPassive, passive);
        }

        /// <summary>
        /// 一次发射的静态预估（dry-run，不改任何状态）——D17 预览面板与 D21 预算体检共用。
        /// 口径（P7：只做**主序列静态预估**，被动仅标"可能触发"、不参与数值）：
        ///   最终蓝耗 = (物品基础耗蓝 + 修正加值) × 修正倍率 → max(0, …)；
        ///   总充能 = 法杖基础充能 + Σ已执行物品充能修正；
        ///   序列时长 = (物品数−1) × 法杖基础延迟 + Σ物品延迟（P2b=A）。
        /// </summary>
        public static CastPreview Preview(in CastProgram program)
        {
            var r = new CastPreview();
            r.AbortSlot = -1;
            if (!program.IsValid) { return r; }

            // 修正集（TargetScope=NextN：作用后续 N 个物品；0 = 不限制），初始为恒等
            float manaMul = 1f; int manaAdd = 0;
            float delayMul = 1f; float delayAdd = 0f;
            int scopeLeft = 0;          // 是否处于"后续 N 个"限定中
            bool scopeBounded = false;

            int items = 0;
            float mana = program.ManaMax;

            for (int i = 0; i < program.SlotCount; i++)
            {
                var sp = program.SpellAt(i);
                if (sp.IsEmpty || sp.SkipsMainCursor) { continue; }

                // 终止符：立即结束本次发射（自身充能修正仍结算）
                if (sp.TriggerType == SpellTriggerType.Terminate)
                {
                    r.TotalRecharge += sp.OwnRechargeAdd * sp.OwnRechargeMul;
                    break;
                }
                // 条件门 / 触发式：主序列不产出效果（条件门判定结果依赖"下一个物品"，P7 只做主序列静态预估）
                if (sp.TriggerType == SpellTriggerType.Conditional || sp.TriggerType == SpellTriggerType.Triggered)
                {
                    continue;
                }

                // 先按**当前**修正集结算本物品（物品吃的是它之前已生效的修正），再处理它自己的修饰器语义。
                // 这与 CastResolver.TriggerMainItem 的顺序一致（否则修饰器会把修正作用到自己身上）。
                items++;
                float cost = (sp.ManaCost + manaAdd) * manaMul;
                if (cost < 0f) { cost = 0f; }
                int costInt = (int)(cost + 0.5f);
                r.ManaTotal += costInt;
                if (mana < costInt && r.AbortSlot < 0) { r.AbortSlot = i; }
                mana -= costInt;

                // 间隔口径与 CastResolver 完全一致（P2b=A 定稿）：
                //   间隔 = (物品自身延迟修正 + 修正集加值) × 修正集倍率 → max(0, …)
                //   Σ物品延迟 用于设计文档 §12.2 的"序列时长"公式（不含法杖基础延迟那一项）
                float delay = (sp.OwnDelayAdd + delayAdd) * delayMul;
                if (delay < 0f) { delay = 0f; }
                r.DelaySum += delay;
                r.TotalRecharge += sp.OwnRechargeAdd * sp.OwnRechargeMul;

                if (sp.IsModifier)
                {
                    // 修饰器：**重置**修正集并设定作用范围供**后续**物品使用
                    // （与 CastResolver.ApplyMods 同口径；修饰器自身不消耗作用范围）
                    manaMul = sp.Self.ManaMul; manaAdd = (int)sp.Self.ManaAdd;
                    delayMul = sp.Self.DelayMul; delayAdd = sp.Self.DelayAdd;
                    scopeBounded = sp.AffectCount > 0;
                    scopeLeft = sp.AffectCount;
                }
                else if (scopeBounded)
                {
                    // 作用范围递减：耗尽后修正集回到恒等
                    scopeLeft--;
                    if (scopeLeft <= 0)
                    {
                        manaMul = 1f; manaAdd = 0;
                        delayMul = 1f; delayAdd = 0f;
                        scopeBounded = false;
                    }
                }
            }

            r.ItemCount = items;
            r.TotalRecharge += program.RechargeTime;
            if (r.TotalRecharge < 0f) { r.TotalRecharge = 0f; }
            r.SequenceDuration = (items > 0 ? (items - 1) : 0) * program.BaseCastDelay + r.DelaySum;
            r.CycleSeconds = r.SequenceDuration + r.TotalRecharge;
            r.ManaExceedsPool = r.AbortSlot >= 0;
            return r;
        }

        /// <summary>稳定哈希（FNV-1a 64bit；只混数值/枚举/Id，不混资产名）。</summary>
        public static ulong Hash(CastProgram primary, CastProgram secondary)
        {
            var sb = new StringBuilder(512);
            AppendProgram(sb, 'P', primary);
            AppendProgram(sb, 'S', secondary);
            return Fnv1a64(sb.ToString());
        }

        private static void AppendProgram(StringBuilder sb, char tag, CastProgram p)
        {
            sb.Append(tag).Append('|').Append(p.WandId).Append('|').Append(p.SlotCount).Append('|')
              .Append(p.BaseCastDelay.ToString("R")).Append('|').Append(p.RechargeTime.ToString("R")).Append('|')
              .Append(p.ManaMax).Append('|').Append(p.ManaRegen.ToString("R")).Append('|').Append((int)p.Mode).Append('\n');
            if (p.Spells == null) { return; }
            for (int i = 0; i < p.Spells.Length; i++)
            {
                var s = p.Spells[i];
                sb.Append(tag).Append('S').Append(i).Append('|').Append(s.SpellId).Append('|').Append(s.ItemId).Append('|')
                  .Append((int)s.TriggerType).Append('|').Append(s.DelayFrames).Append('|')
                  .Append((int)s.EffectKind).Append('|').Append(s.EffectScale.ToString("R")).Append('|')
                  .Append((int)s.Tags).Append('|').Append((int)s.StructTags).Append('|').Append((int)s.Flags).Append('|')
                  .Append(s.ManaCost).Append('|').Append(s.IsModifier ? 1 : 0).Append('|')
                  .Append(s.Self.ManaAdd.ToString("R")).Append('|').Append(s.Self.ManaMul.ToString("R")).Append('|')
                  .Append(s.Self.DelayAdd.ToString("R")).Append('|').Append(s.Self.DelayMul.ToString("R")).Append('|')
                  .Append(s.OwnDelayAdd.ToString("R")).Append('|')
                  .Append(s.OwnRechargeAdd.ToString("R")).Append('|').Append(s.OwnRechargeMul.ToString("R")).Append('|')
                  .Append(s.Self.DamageAdd.ToString("R")).Append('|').Append(s.Self.DamageMul.ToString("R")).Append('|')
                  .Append(s.Self.SpeedMul.ToString("R")).Append('|').Append(s.Self.PierceAdd).Append('|')
                  .Append(s.Self.SpreadAdd.ToString("R")).Append('|').Append(s.Self.HomingAdd.ToString("R")).Append('|')
                  .Append((int)s.TargetScope).Append('|').Append(s.AffectCount).Append('|')
                  .Append((int)s.SequenceOp).Append('|').Append(s.Operand).Append('|').Append(s.ManaDelta).Append('|')
                  .Append(s.ProjectileId).Append('|').Append(s.ProjSpeed.ToString("R")).Append('|')
                  .Append(s.ProjDamage.ToString("R")).Append('|').Append(s.ProjLifetime.ToString("R")).Append('|')
                  .Append(s.ProjRadius.ToString("R")).Append('|').Append(s.ProjCount).Append('|')
                  .Append(s.ProjSpread.ToString("R")).Append('|').Append(s.ProjPierce).Append('|')
                  .Append(s.ProjHoming.ToString("R")).Append('|')
                  .Append(s.AppliesBuff ? s.Buff.KeyHash : 0).Append('|')
                  .Append(s.IsPassive ? (int)s.Passive.Event : 0).Append('|')
                  .Append(s.IsPassive ? s.Passive.LimitPerCast : 0).Append('|');
                // S3/D15：buff 快照的**运行状态**（层数 + 剩余量）也进 loadout 哈希，
                // 否则"同装填但 buff 不同"的两端会被判为一致（S7 同步会漏检）。
                AppendBuffs(sb, p.InitialBuffsAt(i));
                sb.Append('\n');
            }
        }

        /// <summary>把某槽位的 buff 快照写进哈希串（S3/D15；空集不写）。</summary>
        private static void AppendBuffs(StringBuilder sb, in BuffSet set)
        {
            if (set.IsEmpty) { return; }
            for (int b = 0; b < set.Count; b++)
            {
                BuffInstance x = set.At(b);
                sb.Append('B').Append(x.KeyHash).Append(':').Append(x.Stacks).Append(':').Append(x.Remaining).Append(',');
            }
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
    }
}

//------------------------------------------------------------
// EmojiWar GameMain Editor - 法术预算体检（D21）+ 数值来源体检（D34）
//
// 菜单：
//   EmojiWar/Tools/Audit Spell Budget   → 按设计文档 §12.2 预算口径核算每条示例序列
//   EmojiWar/Tools/Audit Numeric Config → 数值登记表核对 + 代码中残留平衡数值扫描（D33 守护）
//
// 依据：doc/法术编程玩法设计文档.md §12（满序列资源预算表）
//       doc/法术编程系统-执行文档.md D21/D34 + §8（配置驱动原则 P11）
//
// 预算口径（P2b=A 定稿）：
//   序列时长 = (物品数−1) × 法杖基础施法延迟 + Σ物品施法延迟        ≤ 1.2s
//   总耗蓝 Σ                                                      ≤ 魔力池 × 70%（>池=错误）
//   Σ物品延迟修正                                                 ≤ 0.15s × 槽数
//   序列总充能 = 基础充能 + Σ物品充能修正                          落在 0.8 ~ 1.6s
//   Σ物品充能修正                                                 ≤ 基础充能 × 60%
//   回魔支撑连打：Regen × 周期 ≥ 一发总耗蓝 × 0.5
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>法术预算 + 数值来源体检。</summary>
    public static class SpellBudgetAudit
    {
        private const string DataRoot = "Assets/GameMain/Resources/Data";

        [MenuItem("EmojiWar/Tools/Audit Spell Budget", false, 154)]
        public static void AuditBudgets()
        {
            var rep = new StringBuilder();
            int errors = CollectBudgetReport(rep);
            if (errors > 0) { Debug.LogError(rep.ToString()); }
            else { Debug.Log(rep.ToString()); }
        }

        /// <summary>预算体检报告文本（供自动化探针写文件）。</summary>
        public static string AuditBudgetsForReport()
        {
            var rep = new StringBuilder();
            int errors = CollectBudgetReport(rep);
            rep.Append("[SpellBudget] errors=").Append(errors);
            return rep.ToString();
        }

        /// <summary>逐条核算并写入报告；返回错误数。</summary>
        private static int CollectBudgetReport(StringBuilder rep)
        {
            var cfg = LoadAll<SpellSystemConfigSO>(DataRoot + "/SpellSystem");
            var sys = cfg.Count > 0 ? cfg[0] : null;
            if (sys == null)
            {
                rep.Append("[SpellBudget] 缺少 SpellSystemConfigSO（先跑 EmojiWar/Setup/Build Spell Content (S1)）");
                return 1;
            }

            var wands = LoadAll<WandSO>(DataRoot + "/Wand");
            var items = LoadAll<ItemSO>(DataRoot + "/Item");
            var spells = LoadAll<SpellSO>(DataRoot + "/Spell");
            var loadout = LoadAll<StartingLoadoutSO>(DataRoot + "/SpellSystem");

            var itemById = new Dictionary<int, ItemSO>();
            for (int i = 0; i < items.Count; i++) { itemById[items[i].Id] = items[i]; }
            var spellById = new Dictionary<int, SpellSO>();
            for (int i = 0; i < spells.Count; i++) { spellById[spells[i].Id] = spells[i]; }

            int errors = 0;
            int warns = 0;
            var sub = new StringBuilder();

            // ---- ① 每条示例序列（§12.4 三条 + §12.5）----
            errors += Check(sys, "样例①入门3槽(学徒杖)", FindWand(wands, 1),
                new[] { 104, 101, 101 }, itemById, spellById, ref warns, sub);
            errors += Check(sys, "样例②控制流5槽(镜像长杖)", FindWand(wands, 2),
                new[] { 106, 102, 105, 101, 107 }, itemById, spellById, ref warns, sub);
            errors += Check(sys, "样例③爆发5槽(镜像长杖)", FindWand(wands, 2),
                new[] { 107, 103, 103, 103, 108 }, itemById, spellById, ref warns, sub);

            // ---- ② 两把初始法杖的出厂装填（流程 A 的实际开局序列）----
            var lo = loadout.Count > 0 ? loadout[0] : null;
            if (lo != null)
            {
                if (lo.LeftHand.Wand != null)
                {
                    var ids = SpellIdsOf(lo.LeftHand);
                    errors += Check(sys, "开局左手(学徒杖)", lo.LeftHand.Wand, ids, itemById, spellById, ref warns, sub);
                }
                if (lo.RightHand.Wand != null)
                {
                    var ids = SpellIdsOf(lo.RightHand);
                    errors += Check(sys, "开局右手(镜像长杖)", lo.RightHand.Wand, ids, itemById, spellById, ref warns, sub);
                }
            }

            // ---- ③ 每把法杖的 DefaultSpellIds 也核一遍（出厂装填参考）----
            for (int i = 0; i < wands.Count; i++)
            {
                var w = wands[i];
                if (w.DefaultSpellIds == null || w.DefaultSpellIds.Length == 0) { continue; }
                errors += Check(sys, "出厂装填(" + w.DisplayName + ")", w, w.DefaultSpellIds, itemById, spellById, ref warns, sub);
            }

            rep.Append(sub);
            rep.Append(string.Format("[SpellBudget] 预算体检完成：errors={0} warnings={1}（口径见设计文档 §12.2）\n", errors, warns));
            return errors;
        }

        /// <summary>核算一条序列；返回新增错误数（警告计入 warns，不算错误）。</summary>
        private static int Check(SpellSystemConfigSO sys, string label, WandSO wand, int[] spellIds,
            Dictionary<int, ItemSO> itemById, Dictionary<int, SpellSO> spellById, ref int warns, StringBuilder rep)
        {
            if (wand == null) { rep.Append("[SpellBudget] ").Append(label).Append("：找不到法杖资产\n"); return 1; }

            // 组装 CastProgram（与运行时 LoadoutCompiler 同口径：物品卡 → SpellSO → CastSpellData）
            var table = new DictionaryItemTable();
            foreach (var kv in itemById)
            {
                if (kv.Value != null) { table.Add(kv.Value); }
            }

            int slots = wand.SlotCount;
            var compiled = new CastSpellData[slots];
            for (int i = 0; i < slots; i++)
            {
                int card = 0;
                if (spellIds != null && i < spellIds.Length)
                {
                    int spellId = spellIds[i];
                    foreach (var kv in itemById)
                    {
                        if (kv.Value != null && kv.Value.Category == ItemCategory.Spell
                            && kv.Value.Spell != null && kv.Value.Spell.Id == spellId)
                        {
                            card = kv.Value.Id;
                            break;
                        }
                    }
                }
                compiled[i] = card > 0
                    ? LoadoutCompiler.CompileSpell(card, table, i)
                    : CastSpellData.Empty;
            }

            var program = new CastProgram(wand.Id, slots, wand.CastDelay, wand.RechargeTime,
                wand.ManaMax, wand.ManaRegen, wand.Mode, compiled);
            var pv = LoadoutCompiler.Preview(program);

            var issues = new List<string>();
            var notes = new List<string>();

            // 1. 总耗蓝 ≤ 魔力池 × 70%
            float manaCap = wand.ManaMax * sys.SequenceManaBudgetRatio;
            bool manaOver = pv.ManaTotal > manaCap + 1e-3f;
            if (manaOver) { issues.Add("总耗蓝 " + pv.ManaTotal + " 超预算上限 " + manaCap.ToString("F0")); }
            else if (pv.ManaTotal < wand.ManaMax * sys.SequenceManaTooLightRatio) { notes.Add("总耗蓝偏轻（<40% 池）"); }

            // 2. 序列时长 ≤ 1.2s
            if (pv.SequenceDuration > sys.SequenceDurationMax + 1e-3f)
            {
                issues.Add("序列时长 " + pv.SequenceDuration.ToString("F2") + "s 超上限 " + sys.SequenceDurationMax.ToString("F2") + "s");
            }

            // 3. Σ物品延迟修正 ≤ 0.15s × 槽数
            float delayCap = sys.DelaySumPerSlotMax * slots;
            if (pv.DelaySum > delayCap + 1e-3f) { issues.Add("Σ物品延迟 " + pv.DelaySum.ToString("F2") + "s 超上限 " + delayCap.ToString("F2") + "s"); }

            // 4. 总充能落在 0.8 ~ 1.6s
            if (pv.TotalRecharge < sys.TotalRechargeMin - 1e-3f || pv.TotalRecharge > sys.TotalRechargeMax + 1e-3f)
            {
                issues.Add("总充能 " + pv.TotalRecharge.ToString("F2") + "s 不在区间 "
                    + sys.TotalRechargeMin.ToString("F2") + "~" + sys.TotalRechargeMax.ToString("F2") + "s");
            }

            // 5. Σ物品充能修正 ≤ 基础充能 × 60%
            float rechargerSum = pv.TotalRecharge - wand.RechargeTime;
            float rechargerCap = wand.RechargeTime * sys.RechargeSumRatioMax;
            if (Mathf.Abs(rechargerSum) > rechargerCap + 1e-3f)
            {
                issues.Add("Σ物品充能修正 " + rechargerSum.ToString("F2") + "s 超上限 ±" + rechargerCap.ToString("F2") + "s");
            }

            // 6. 回魔支撑连打：Regen × 周期 ≥ 一发总耗蓝 × 0.5
            float regenSupply = wand.ManaRegen * pv.CycleSeconds;
            if (regenSupply < pv.ManaTotal * sys.RegenSupportFactor)
            {
                notes.Add("回魔不足以连打两发（Regen×周期 " + regenSupply.ToString("F1") + " < 总耗蓝×0.5）");
            }

            // 7. 魔力池装不下整条序列 → 会触发 Q2 中止（明确报出来）
            if (pv.ManaExceedsPool)
            {
                issues.Add("总耗蓝 " + pv.ManaTotal + " > 魔力池 " + wand.ManaMax + " → 会触发 Q2 法力不足中止（槽" + pv.AbortSlot + "）");
            }

            var sb = new StringBuilder();
            sb.Append("[SpellBudget] ").Append(label)
              .Append(" 槽=").Append(slots)
              .Append(" 物品=").Append(pv.ItemCount)
              .Append(" 总耗蓝=").Append(pv.ManaTotal).Append('/').Append(wand.ManaMax)
              .Append("(").Append((pv.ManaTotal * 100f / Mathf.Max(1, wand.ManaMax)).ToString("F0")).Append("%池)")
              .Append(" Σ延迟=").Append(pv.DelaySum.ToString("F2")).Append('s')
              .Append(" 序列时长=").Append(pv.SequenceDuration.ToString("F2")).Append('s')
              .Append(" 总充能=").Append(pv.TotalRecharge.ToString("F2")).Append('s')
              .Append(" 周期=").Append(pv.CycleSeconds.ToString("F2")).Append('s');

            if (issues.Count == 0 && notes.Count == 0)
            {
                sb.Append("  → OK\n");
                rep.Append(sb);
                return 0;
            }

            for (int i = 0; i < issues.Count; i++) { sb.Append("\n[SpellBudget]     X ").Append(issues[i]); }
            for (int i = 0; i < notes.Count; i++) { sb.Append("\n[SpellBudget]     - ").Append(notes[i]); }
            sb.Append('\n');
            rep.Append(sb);

            warns += notes.Count;
            return issues.Count;
        }

        private static int[] SpellIdsOf(StartingHand hand)
        {
            if (hand.Spells == null) { return new int[0]; }
            var list = new List<int>();
            for (int i = 0; i < hand.Spells.Length; i++)
            {
                if (hand.Spells[i] != null) { list.Add(hand.Spells[i].Id); }
            }
            return list.ToArray();
        }

        private static WandSO FindWand(List<WandSO> wands, int id)
        {
            for (int i = 0; i < wands.Count; i++) { if (wands[i].Id == id) { return wands[i]; } }
            return null;
        }

        // ==================== D34 数值来源体检 ====================

        /// <summary>
        /// 数值来源体检（D34 / §8.3 守护机制）：核对"数值登记表"的配置来源是否齐全，
        /// 并扫描代码里残留的平衡数值字面量（应为空）。
        /// </summary>
        [MenuItem("EmojiWar/Tools/Audit Numeric Config", false, 155)]
        public static void AuditNumericConfig()
        {
            var text = CollectNumericReport();
            if (text.Contains("MISS") || text.Contains("LEAK")) { Debug.LogError(text); }
            else { Debug.Log(text); }
        }

        /// <summary>数值来源体检报告文本（供自动化探针写文件）。</summary>
        public static string AuditNumericForReport() { return CollectNumericReport(); }

        private static string CollectNumericReport()
        {
            var sb = new StringBuilder();
            sb.Append("[NumericAudit] 数值登记表核对（执行文档 §8.1）\n");

            var sysList = LoadAll<SpellSystemConfigSO>(DataRoot + "/SpellSystem");
            var sys = sysList.Count > 0 ? sysList[0] : null;
            var loadoutList = LoadAll<StartingLoadoutSO>(DataRoot + "/SpellSystem");
            var lo = loadoutList.Count > 0 ? loadoutList[0] : null;

            int missing = 0;
            missing += Row(sb, "法术槽物理上限", sys != null, sys != null ? ("SpellSystemConfig.MaxSpellSlots=" + sys.MaxSpellSlots) : "缺失");
            missing += Row(sb, "单次施法总触发上限", sys != null, sys != null ? ("MaxTotalTriggers=" + sys.MaxTotalTriggers) : "缺失");
            missing += Row(sb, "单物品触发上限", sys != null, sys != null ? ("MaxTriggersPerItem=" + sys.MaxTriggersPerItem) : "缺失");
            missing += Row(sb, "被动嵌套上限", sys != null, sys != null ? ("MaxPassiveNesting=" + sys.MaxPassiveNesting) : "缺失");
            missing += Row(sb, "弹药兜底寿命/半径", sys != null, sys != null ? ("DefaultBulletLifetime=" + sys.DefaultBulletLifetime + " Radius=" + sys.DefaultBulletRadius) : "缺失");
            missing += Row(sb, "被动默认限次/冷却", sys != null, sys != null ? ("Limit=" + sys.DefaultPassiveLimitPerCast + " CD=" + sys.DefaultPassiveCooldownMs + "ms") : "缺失");
            missing += Row(sb, "Buff 默认层数上限", sys != null, sys != null ? ("DefaultBuffMaxStacks=" + sys.DefaultBuffMaxStacks) : "缺失");
            missing += Row(sb, "Buff 全局层数上限", sys != null, sys != null ? ("GlobalMaxBuffStacks=" + sys.GlobalMaxBuffStacks) : "缺失");
            // S3/D13：引爆伤害（设计「引爆：每层 5 伤」）——必须是配置来源，不能写死在 BuffRuntime
            missing += Row(sb, "Buff 引爆每层伤害", sys != null, sys != null ? ("BuffDetonateDamagePerStack=" + sys.BuffDetonateDamagePerStack) : "缺失");
            missing += Row(sb, "背包容量", sys != null, sys != null ? ("BackpackCapacity=" + sys.BackpackCapacity) : "缺失");
            missing += Row(sb, "商店货架容量", sys != null, sys != null ? ("ShopCapacity=" + sys.ShopCapacity) : "缺失");
            missing += Row(sb, "序列时长/充能预算口径", sys != null, sys != null ? (sys.SequenceDurationMax + "s / " + sys.TotalRechargeMin + "~" + sys.TotalRechargeMax + "s") : "缺失");
            missing += Row(sb, "初始装备（杖+预填法术）", lo != null, lo != null ? ("左手=" + (lo.LeftHand.Wand != null ? lo.LeftHand.Wand.DisplayName : "空") + " 右手=" + (lo.RightHand.Wand != null ? lo.RightHand.Wand.DisplayName : "空")) : "缺失");
            missing += Row(sb, "法杖数值（槽/基础延迟/充能/魔力/回魔）", true, "WandSO（已是配置）");

            sb.Append("[NumericAudit] 配置来源缺失项=").Append(missing).Append('\n');
            sb.Append("[NumericAudit] 代码侧残留平衡数值扫描：见下方逐条（应为空）\n");

            int leaks = ScanCodeLiterals(sb);
            sb.Append("[NumericAudit] 代码侧残留=").Append(leaks).Append("（结构常量与资产出厂默认值不计）\n");
            sb.Append("[NumericAudit] missing=").Append(missing).Append(" leaks=").Append(leaks).Append('\n');
            return sb.ToString();
        }

        private static int Row(StringBuilder sb, string name, bool ok, string detail)
        {
            sb.Append("[NumericAudit] ").Append(ok ? "OK   " : "MISS ").Append(name).Append(" → ").Append(detail).Append('\n');
            return ok ? 0 : 1;
        }

        /// <summary>
        /// 扫描 SpellSystem 相关源码里的"平衡数值字面量"（D33 清理后的守护）。
        /// 只查明确不应该出现的模式（老硬编码），避免误报结构常量。
        /// </summary>
        private static int ScanCodeLiterals(StringBuilder sb)
        {
            string[] files =
            {
                "Assets/GameMain/Scripts/Items/InventoryService.cs",
                "Assets/GameMain/Scripts/ItemSystem.cs",
                "Assets/GameMain/Scripts/Simulation/CastResolver.cs",
                "Assets/GameMain/Scripts/Simulation/LockstepSimulation.cs",
            };
            // 明确禁止的硬编码模式（清理前的老写法）
            string[] banned =
            {
                "MaxWandSlots = 8",
                "BackpackCapacity = 30",
                "ShopCapacity = 6",
                "Lifetime = 3f",
                "Radius = 0.2f",
                "guard < 64",
            };

            int leaks = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                if (!System.IO.File.Exists(path))
                {
                    // 相对路径在编辑器里的 CWD 是工程根；兜底拼一次
                    string alt = System.IO.Path.Combine(Application.dataPath, "..", path);
                    if (!System.IO.File.Exists(alt)) { continue; }
                    path = System.IO.Path.GetFullPath(alt);
                }
                string text = System.IO.File.ReadAllText(path);
                for (int k = 0; k < banned.Length; k++)
                {
                    if (text.Contains(banned[k]))
                    {
                        sb.Append("[NumericAudit] LEAK ").Append(path).Append(" 含 \"").Append(banned[k]).Append("\"\n");
                        leaks++;
                    }
                }
            }
            return leaks;
        }

        private static List<T> LoadAll<T>(string dir) where T : ScriptableObject
        {
            var list = new List<T>();
            if (!AssetDatabase.IsValidFolder(dir)) { return list; }
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { dir });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var so = AssetDatabase.LoadAssetAtPath<T>(path);
                if (so != null) { list.Add(so); }
            }
            return list;
        }
    }
}

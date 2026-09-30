//------------------------------------------------------------
// EmojiWar GameMain - 临时 Buff 自检（BuffSelfTest，D24 / S3）
//
// 依据：doc/法术编程系统-执行文档.md D24 + §6 验收矩阵 + 流程 E
//   验收要求（执行文档 §1.6 D24 / §5 S3）：施加 / 叠 3 层 / 次数到期 / 引爆（每层 5 伤）/
//   转移 / 净化 各 1 条断言 —— 本文件在此之上补齐叠加规则、计时方式、值语义与哈希。
//
// **纯 C# 断言**（不依赖场景/资产/Unity 时间），同一份套件被两条通道调用：
//   · 编辑器菜单 EmojiWar/Tools/Buff Self-Test  → Editor/SpellDiagnostics.cs
//   · 构建版 -autobuff 启动参数                → AutoPlay.AutoBuffSelfTestFlow
//
// ⚠️ 重点断言"值语义"：ItemTypes.cs 明确记载旧版事故是
//   "拷贝构造共享 buffs 引用导致委托逐发累积" → 本套件必须证明**改副本不影响原件**。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>自检报告收集器（与 CastSelfTestReport 同形态）。</summary>
    public sealed class BuffSelfTestReport
    {
        private readonly List<string> m_Lines = new List<string>();

        public int Pass { get; private set; }
        public int Fail { get; private set; }

        public void Check(string name, bool ok, string detail)
        {
            if (ok) { Pass++; } else { Fail++; }
            m_Lines.Add("[BuffTest] " + (ok ? "PASS " : "FAIL ") + name + (detail == null ? "" : " - " + detail));
        }

        public void Note(string text) { m_Lines.Add("[BuffTest]   " + text); }

        public string Text()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < m_Lines.Count; i++) { sb.Append(m_Lines[i]).Append('\n'); }
            sb.Append("[BuffTest] passed=").Append(Pass).Append(" failed=").Append(Fail);
            return sb.ToString();
        }
    }

    /// <summary>临时 Buff 自检（施加/叠层/计时/到期/净化/转移/引爆/修正/值语义/哈希）。</summary>
    public static class BuffSelfTest
    {
        // ---- 测试用 KeyHash（与 CastBuffDef.HashKey 同算法，稳定且跨端一致） ----
        private const string KeyMana = "buff_mana_down";     // 耗蓝
        private const string KeyDelay = "buff_delay_down";   // 延迟
        private const string KeyStack = "buff_stacking";     // 叠层用
        private const string KeyNeg = "buff_negative";       // 负面（净化用）

        private static int Key(string s) { return CastBuffDef.HashKey(s); }

        // ---- 构造 helper（对齐 BuffApplyDef 字段语义） ----
        private static BuffInstance Def(string key, BuffStat stat, float perStack, int maxStacks,
            BuffTiming timing, int duration, BuffStackRule rule, bool negative = false)
        {
            CastBuffDef d = new CastBuffDef();
            d.KeyHash = Key(key);
            d.Stat = stat;
            d.ValuePerStack = perStack;
            d.MaxStacks = maxStacks;
            d.Timing = timing;
            d.Duration = duration;
            d.StackRule = rule;
            d.IsNegative = negative;
            return BuffInstance.Create(d, BuffLimits.Factory);
        }

        /// <summary>跑全套断言，返回报告文本。</summary>
        public static string Run()
        {
            var r = new BuffSelfTestReport();
            BuffLimits lim = BuffLimits.Factory;

            // ================================================================
            // 1. 施加（D24 要求断言）
            // ================================================================
            {
                BuffSet s = BuffSet.Empty;
                s = BuffRuntime.Apply(s, Def(KeyMana, BuffStat.ManaCost, 2f, 3, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive), lim);

                r.Check("施加：空集施加一条 → Count=1 且层数=1",
                    s.Count == 1 && s.At(0).Stacks == 1,
                    "count=" + s.Count + " stacks=" + s.At(0).Stacks);
                r.Check("施加：剩余量初始化为定义时长",
                    s.At(0).Remaining == 3,
                    "remaining=" + s.At(0).Remaining);
            }

            // ================================================================
            // 2. 叠 3 层（D24 要求断言）+ 数值随层数线性增长（设计 §3.4 加法）
            // ================================================================
            {
                var def = Def(KeyStack, BuffStat.ManaCost, 2f, 5, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive);
                BuffSet s = BuffSet.Empty;
                s = BuffRuntime.Apply(s, def, lim);
                s = BuffRuntime.Apply(s, def, lim);
                s = BuffRuntime.Apply(s, def, lim);

                r.Check("叠层：加法规则施 3 次 → 层数=3（同一实例合并，不新增条目）",
                    s.Count == 1 && s.At(0).Stacks == 3,
                    "count=" + s.Count + " stacks=" + s.At(0).Stacks);
                r.Check("叠层：生效值 = 每层 2 × 3 层 = 6",
                    Nearly(s.At(0).EffectiveValue, 6f),
                    "effective=" + s.At(0).EffectiveValue);
            }

            // ================================================================
            // 3. 层数上限（MaxStacks 与全局上限双重夹取，P11 配置驱动）
            // ================================================================
            {
                var def = Def(KeyStack, BuffStat.ManaCost, 1f, 2, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive);
                BuffSet s = BuffSet.Empty;
                for (int i = 0; i < 5; i++) { s = BuffRuntime.Apply(s, def, lim); }

                r.Check("上限：MaxStacks=2 时叠 5 次 → 夹到 2 层",
                    s.At(0).Stacks == 2,
                    "stacks=" + s.At(0).Stacks);

                // 全局上限：把 def.MaxStacks 设成超过 GlobalMaxBuffStacks
                BuffLimits tight = lim;
                tight.GlobalMaxStacks = 2;
                var big = Def("buff_big", BuffStat.ManaCost, 1f, 50, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive);
                BuffSet s2 = BuffRuntime.Apply(BuffSet.Empty, big, tight);
                r.Check("上限：全局上限 2 夹住 MaxStacks=50",
                    s2.At(0).MaxStacks == 2,
                    "maxStacks=" + s2.At(0).MaxStacks);
            }

            // ================================================================
            // 4. 叠加规则：取最高 / 刷新时间 / 乘法 / 独立共存
            // ================================================================
            {
                // 取最高：只留更大的数值，层数不叠加
                var low = Def("buff_h", BuffStat.Delay, 1f, 5, BuffTiming.ByTriggerCount, 4, BuffStackRule.Highest);
                var high = Def("buff_h", BuffStat.Delay, 3f, 5, BuffTiming.ByTriggerCount, 4, BuffStackRule.Highest);
                BuffSet s = BuffSet.Empty;
                s = BuffRuntime.Apply(s, low, lim);
                s = BuffRuntime.Apply(s, high, lim);
                r.Check("叠层规则-取最高：后施加更大的值 → 生效值=3 且层数=1",
                    s.Count == 1 && Nearly(s.At(0).EffectiveValue, 3f) && s.At(0).Stacks == 1,
                    "count=" + s.Count + " eff=" + s.At(0).EffectiveValue + " stacks=" + s.At(0).Stacks);

                // 取最高（反向）：后施加更小的值 → 保留原值
                BuffSet s2 = BuffSet.Empty;
                s2 = BuffRuntime.Apply(s2, high, lim);
                s2 = BuffRuntime.Apply(s2, low, lim);
                r.Check("叠层规则-取最高：后施加更小的值 → 仍是 3（不被削弱）",
                    s2.Count == 1 && Nearly(s2.At(0).EffectiveValue, 3f),
                    "eff=" + s2.At(0).EffectiveValue);

                // 刷新时间：**不加层**（设计 §3.4"刷新时间"= 只刷时长），但剩余量刷满
                var rt = Def("buff_rt", BuffStat.Damage, 1f, 3, BuffTiming.ByTriggerCount, 6, BuffStackRule.RefreshTime);
                BuffSet s3 = BuffRuntime.Apply(BuffSet.Empty, rt, lim);
                s3 = BuffRuntime.Apply(s3, rt, lim);                 // 刷新时间规则：层数仍为 1
                s3 = BuffRuntime.ConsumeTrigger(s3, 0);              // 剩余 6→5
                s3 = BuffRuntime.Apply(s3, rt, lim);                 // 刷新回 6
                r.Check("叠层规则-刷新时间：不增加层数（设计 §3.4）且剩余量刷满",
                    s3.Count == 1 && s3.At(0).Stacks == 1 && s3.At(0).Remaining == 6,
                    "count=" + s3.Count + " stacks=" + s3.At(0).Stacks + " rem=" + s3.At(0).Remaining);

                // 乘法：生效值 = 1.5^2 = 2.25，且累加进 Mul 分量
                var mul = Def("buff_mul", BuffStat.ManaCost, 1.5f, 4, BuffTiming.ByCastCount, 4, BuffStackRule.Multiplicative);
                BuffSet s4 = BuffRuntime.Apply(BuffSet.Empty, mul, lim);
                s4 = BuffRuntime.Apply(s4, mul, lim);
                r.Check("叠层规则-乘法：生效值 = 1.5^2 = 2.25（迭代相乘，非 Math.Pow）",
                    Nearly(s4.At(0).EffectiveValue, 2.25f),
                    "eff=" + s4.At(0).EffectiveValue);

                // 独立共存：同 Key 两条并存
                var ind = Def("buff_ind", BuffStat.Delay, 1f, 3, BuffTiming.ByTriggerCount, 3, BuffStackRule.Independent);
                BuffSet s5 = BuffRuntime.Apply(BuffSet.Empty, ind, lim);
                s5 = BuffRuntime.Apply(s5, ind, lim);
                r.Check("叠层规则-独立共存：同 Key 施 2 次 → 2 条独立条目",
                    s5.Count == 2,
                    "count=" + s5.Count);
            }

            // ================================================================
            // 5. 次数到期（D24 要求断言）
            // ================================================================
            {
                var def = Def("buff_cnt", BuffStat.ManaCost, 1f, 3, BuffTiming.ByTriggerCount, 2, BuffStackRule.Additive);
                BuffSet s = BuffRuntime.Apply(BuffSet.Empty, def, lim);   // rem=2
                s = BuffRuntime.ConsumeTrigger(s, 0);                     // rem=1
                bool aliveAfter1 = s.Count == 1 && s.At(0).Remaining == 1;
                s = BuffRuntime.ConsumeTrigger(s, 0);                     // rem=0 → 到期移除

                r.Check("次数到期：触发 1 次仍生效（剩余 1）",
                    aliveAfter1, "aliveAfter1=" + aliveAfter1);
                r.Check("次数到期：触发 2 次后自动移除（Count=0）",
                    s.Count == 0,
                    "count=" + s.Count);
            }

            // ================================================================
            // 6. 计时方式：施法型 / 时间型 / 固化（设计 §3.3、§3.7）
            // ================================================================
            {
                var cast = Def("buff_cast", BuffStat.Recharge, -0.1f, 1, BuffTiming.ByCastCount, 2, BuffStackRule.Additive);
                var trig = Def("buff_trig", BuffStat.ManaCost, 1f, 1, BuffTiming.ByTriggerCount, 2, BuffStackRule.Additive);
                BuffSet s = BuffSet.Empty;
                s = BuffRuntime.Apply(s, cast, lim);
                s = BuffRuntime.Apply(s, trig, lim);
                s = BuffRuntime.ConsumeCastAll(s);

                int ci = s.IndexOf(Key("buff_cast"));
                int ti = s.IndexOf(Key("buff_trig"));
                r.Check("计时-施法型：ConsumeCastAll 只递减施法型",
                    ci >= 0 && s.At(ci).Remaining == 1 && ti >= 0 && s.At(ti).Remaining == 2,
                    "castRem=" + (ci >= 0 ? s.At(ci).Remaining : -1) + " trigRem=" + (ti >= 0 ? s.At(ti).Remaining : -1));

                // 时间型：按帧递减
                var sec = Def("buff_sec", BuffStat.Delay, 0.05f, 1, BuffTiming.BySeconds, 20, BuffStackRule.Additive);
                BuffSet t = BuffRuntime.Apply(BuffSet.Empty, sec, lim);
                t = BuffRuntime.TickFrames(t, 12);
                r.Check("计时-时间型：20 帧经 12 帧 → 剩余 8",
                    t.Count == 1 && t.At(0).Remaining == 8,
                    "rem=" + (t.Count > 0 ? t.At(0).Remaining : -1));

                // 时间型到期
                BuffSet t2 = BuffRuntime.TickFrames(t, 8);
                r.Check("计时-时间型：再经 8 帧 → 到期移除",
                    t2.Count == 0, "count=" + t2.Count);

                // 固化：该次不递减
                BuffSet f = BuffRuntime.Apply(BuffSet.Empty, sec, lim);
                bool okSolid;
                f = BuffRuntime.Solidify(f, Key("buff_sec"), out okSolid);
                f = BuffRuntime.TickFrames(f, 5);
                bool survivedSolid = f.Count == 1 && f.At(0).Remaining == 20;
                f = BuffRuntime.TickFrames(f, 5);
                r.Check("计时-固化：固化那次不递减，之后恢复递减（20 → 20 → 15）",
                    okSolid && survivedSolid && f.Count == 1 && f.At(0).Remaining == 15,
                    "solid=" + okSolid + " afterSolid=" + (survivedSolid ? 20 : -1) + " rem=" + (f.Count > 0 ? f.At(0).Remaining : -1));
            }

            // ================================================================
            // 7. 引爆（D24 要求断言：每层 5 伤）
            // ================================================================
            {
                var def = Def("buff_boom", BuffStat.Damage, 1f, 5, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive);
                BuffSet s = BuffSet.Empty;
                s = BuffRuntime.Apply(s, def, lim);
                s = BuffRuntime.Apply(s, def, lim);
                s = BuffRuntime.Apply(s, def, lim);      // 3 层

                BuffSet cleared;
                int dmg = BuffRuntime.Detonate(s, lim, out cleared);

                r.Check("引爆：3 层 × 每层 5 伤 = 15（伤害来自配置 BuffDetonateDamagePerStack）",
                    dmg == 15,
                    "damage=" + dmg);
                r.Check("引爆：引爆后集合清空",
                    cleared.Count == 0,
                    "cleared=" + cleared.Count);

                // 伤害口径随配置变化（证明不是硬编码 5）
                BuffLimits l2 = lim;
                l2.DetonateDamagePerStack = 7;
                BuffSet dummy;
                r.Check("引爆：伤害口径随配置变化（7/层 → 21）",
                    BuffRuntime.Detonate(s, l2, out dummy) == 21,
                    "damage=" + BuffRuntime.Detonate(s, l2, out dummy));
            }

            // ================================================================
            // 8. 转移（D24 要求断言：前→后）
            // ================================================================
            {
                var a = Def("buff_a", BuffStat.ManaCost, 1f, 2, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive);
                var b = Def("buff_b", BuffStat.Delay, 0.1f, 2, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive);

                BuffSet src = BuffSet.Empty;
                src = BuffRuntime.Apply(src, a, lim);
                src = BuffRuntime.Apply(src, b, lim);
                BuffSet dst = BuffSet.Empty;
                dst = BuffRuntime.Apply(dst, b, lim);      // 目标已有 b（应叠成 2 层）

                BuffRuntime.Transfer(ref src, ref dst, lim);

                r.Check("转移：源清空、目标收下全部（并正确叠层）",
                    src.Count == 0 && dst.Count == 2 && dst.At(dst.IndexOf(Key("buff_b"))).Stacks == 2,
                    "src=" + src.Count + " dst=" + dst.Count
                    + " bStacks=" + (dst.IndexOf(Key("buff_b")) >= 0 ? dst.At(dst.IndexOf(Key("buff_b"))).Stacks : -1));
            }

            // ================================================================
            // 9. 净化（D24 要求断言；设计 §3.7 "被驱散净化" + 净化之光只移除负面）
            // ================================================================
            {
                var neg = Def(KeyNeg, BuffStat.ManaCost, 3f, 2, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive, true);
                var pos = Def("buff_pos", BuffStat.Recharge, -0.1f, 2, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive, false);

                BuffSet s = BuffSet.Empty;
                s = BuffRuntime.Apply(s, neg, lim);
                s = BuffRuntime.Apply(s, pos, lim);

                BuffSet onlyNeg = BuffRuntime.Purge(s, true);
                r.Check("净化：只移除负面 → 剩 1 条正面",
                    onlyNeg.Count == 1 && onlyNeg.IndexOf(Key(KeyNeg)) < 0,
                    "count=" + onlyNeg.Count);

                BuffSet all = BuffRuntime.Purge(s, false);
                r.Check("净化：全部移除 → 0 条",
                    all.Count == 0,
                    "count=" + all.Count);
            }

            // ================================================================
            // 10. 增幅（设计 §3.7 "增幅（+1 层）"）
            // ================================================================
            {
                var def = Def("buff_amp", BuffStat.Damage, 2f, 3, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive);
                BuffSet s = BuffRuntime.Apply(BuffSet.Empty, def, lim);
                bool ok;
                s = BuffRuntime.Amplify(s, Key("buff_amp"), lim, out ok);
                r.Check("增幅：+1 层（1 → 2）",
                    ok && s.At(0).Stacks == 2,
                    "ok=" + ok + " stacks=" + s.At(0).Stacks);

                // 到顶后增幅失败且不改动
                s = BuffRuntime.Amplify(s, Key("buff_amp"), lim, out ok);   // 3 层（满）
                BuffSet before = s;
                s = BuffRuntime.Amplify(s, Key("buff_amp"), lim, out ok);
                r.Check("增幅：已达上限时失败且层数不变",
                    !ok && s.At(0).Stacks == before.At(0).Stacks,
                    "ok=" + ok + " stacks=" + s.At(0).Stacks);
            }

            // ================================================================
            // 11. 修正结算顺序（设计 §3.6：物品自身修正 → 再 buff 修正）
            // ================================================================
            {
                var def = Def("buff_mods", BuffStat.ManaCost, 2f, 3, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive);
                BuffSet s = BuffRuntime.Apply(BuffSet.Empty, def, lim);
                s = BuffRuntime.Apply(s, def, lim);     // 2 层 → +4

                CastStatMod mods = CastStatMod.Identity;
                mods.ManaAdd += 3f;         // 物品自身修正（先）
                mods.ManaMul *= 0.5f;       // 物品自身倍率
                BuffRuntime.Accumulate(s, ref mods);

                // 结算口径（与 CastResolver.FinalCost 一致）：(基础 + 加值) × 倍率
                // 基础 6 → (6 + 3 + 4) × 0.5 = 6.5
                float cost = (6f + mods.ManaAdd) * mods.ManaMul;
                r.Check("修正：耗蓝按 (基础+物品加值+buff加值)×倍率 = 6.5",
                    Nearly(cost, 6.5f),
                    "cost=" + cost + " manaAdd=" + mods.ManaAdd + " manaMul=" + mods.ManaMul);

                // 乘法型 buff 走 Mul 分量
                var mdef = Def("buff_mul2", BuffStat.Delay, 0.5f, 2, BuffTiming.ByTriggerCount, 3, BuffStackRule.Multiplicative);
                BuffSet s2 = BuffRuntime.Apply(BuffSet.Empty, mdef, lim);
                CastStatMod mods2 = CastStatMod.Identity;
                BuffRuntime.Accumulate(s2, ref mods2);
                r.Check("修正：乘法型 buff 写进 Mul 分量（DelayMul=0.5）",
                    Nearly(mods2.DelayMul, 0.5f) && Nearly(mods2.DelayAdd, 0f),
                    "delayMul=" + mods2.DelayMul + " delayAdd=" + mods2.DelayAdd);
            }

            // ================================================================
            // 12. 值语义：改副本绝不影响原件（ItemTypes.cs 记载的旧版事故）
            // ================================================================
            {
                var def = Def("buff_alias", BuffStat.ManaCost, 1f, 5, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive);
                BuffSet origin = BuffSet.Empty;
                origin = BuffRuntime.Apply(origin, def, lim);          // 1 层

                BuffSet copy = origin;                                 // 结构体拷贝
                copy = BuffRuntime.Apply(copy, def, lim);              // 只改副本 → 2 层
                copy = BuffRuntime.ConsumeTrigger(copy, 0);

                r.Check("值语义：改副本后原件层数不变（1）",
                    origin.Count == 1 && origin.At(0).Stacks == 1,
                    "originStacks=" + (origin.Count > 0 ? origin.At(0).Stacks : -1));
                r.Check("值语义：改副本后原件剩余量不变（5）",
                    origin.Count == 1 && origin.At(0).Remaining == 5,
                    "originRem=" + (origin.Count > 0 ? origin.At(0).Remaining : -1));
                r.Check("值语义：副本自身确实变了（2 层 / 剩余 4）",
                    copy.At(0).Stacks == 2 && copy.At(0).Remaining == 4,
                    "copyStacks=" + copy.At(0).Stacks + " copyRem=" + copy.At(0).Remaining);

                // RemoveAt 也不得影响原件
                BuffSet o2 = BuffSet.Empty;
                o2 = BuffRuntime.Apply(o2, def, lim);
                o2 = BuffRuntime.Apply(o2, Def("buff_alias2", BuffStat.Delay, 1f, 5, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive), lim);
                BuffSet c2 = o2.RemoveAt(0);
                r.Check("值语义：副本 RemoveAt 后原件仍是 2 条",
                    o2.Count == 2 && c2.Count == 1,
                    "origin=" + o2.Count + " copy=" + c2.Count);
            }

            // ================================================================
            // 13. 集合容量上限（结构常量 Capacity=8，防无界增长）
            // ================================================================
            {
                BuffSet s = BuffSet.Empty;
                for (int i = 0; i < BuffSet.Capacity + 4; i++)
                {
                    var d = Def("buff_fill_" + i, BuffStat.ManaCost, 1f, 1, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive);
                    s = BuffRuntime.Apply(s, d, lim);
                }
                r.Check("容量：超出 Capacity=8 的条目被拒绝（不越界、不崩）",
                    s.Count == BuffSet.Capacity,
                    "count=" + s.Count + " capacity=" + BuffSet.Capacity);
            }

            // ================================================================
            // 14. 状态哈希（执行文档 §4：层数 + 剩余次数必须入哈希）
            // ================================================================
            {
                var def = Def("buff_hash", BuffStat.ManaCost, 1f, 5, BuffTiming.ByTriggerCount, 4, BuffStackRule.Additive);
                BuffSet a = BuffRuntime.Apply(BuffSet.Empty, def, lim);
                BuffSet b = BuffRuntime.Apply(BuffSet.Empty, def, lim);
                b = BuffRuntime.Apply(b, def, lim);

                string ha = HashOf(a);
                string hb = HashOf(b);
                r.Check("哈希：层数不同 → 哈希不同（1 层 vs 2 层）",
                    ha != hb, "h1=" + ha + " h2=" + hb);

                BuffSet c = BuffRuntime.ConsumeTrigger(a, 0);
                r.Check("哈希：剩余次数不同 → 哈希不同（4 vs 3）",
                    HashOf(a) != HashOf(c) || c.Count == 0,
                    "hA=" + HashOf(a) + " hC=" + HashOf(c));

                r.Check("哈希：相同状态 → 相同哈希（确定性）",
                    HashOf(a) == HashOf(BuffRuntime.Apply(BuffSet.Empty, def, lim)),
                    "hA=" + HashOf(a));
            }

            r.Note("BuffLimits.Factory: GlobalMaxStacks=" + lim.GlobalMaxStacks
                 + " DefaultMaxStacks=" + lim.DefaultMaxStacks
                 + " DetonateDamagePerStack=" + lim.DetonateDamagePerStack);

            return r.Text();
        }

        // ---- 工具 ----
        private static string HashOf(in BuffSet s)
        {
            var sb = new StringBuilder();
            BuffRuntime.AppendHash(s, sb);
            return sb.ToString();
        }

        private static bool Nearly(float a, float b)
        {
            float d = a - b;
            if (d < 0f) { d = -d; }
            return d < 1e-4f;
        }
    }
}

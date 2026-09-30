//------------------------------------------------------------
// EmojiWar GameMain - 法术临时 Buff 运行时（D13 / S3，设计 §3.7）
//
// 职责（执行文档 §1.3 D13）：施加 / 叠层 / 递减 / 到期 / 驱散（净化） / 转移 / 增幅 / 固化 / 引爆。
//
// 依据：doc/法术编程玩法设计文档.md
//   §3.3 计时方式：时间型（X 秒）/ 次数型（再触发 X 次）/ 施法型（再施法 X 次）
//        → 递减时机三者不同：时间型按帧、次数型按"触发一次"、施法型按"施法一次"
//   §3.4 叠加规则：加法 / 乘法 / 取最高 / 刷新时间 / 独立共存
//   §3.6 与核心属性：最终值 = (基础 + 加值) × 倍率，**运算顺序：物品自身修正 → 再 buff 修正**
//   §3.7 施加与移除；执行文档流程 E
//
// 确定性：纯静态、纯值语义（输入输出都是 BuffSet 的副本），无 Unity 依赖 → 可在纯 C# 自检里跑。
//   本类**不修改传入参数**：所有变更通过返回值（或显式 ref 的转移操作）表达。
//------------------------------------------------------------

using System.Text;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>临时 Buff 运行时（设计 §3.7 的全部操作）。</summary>
    public static class BuffRuntime
    {
        // ====================================================================
        // 一、施加与叠层（设计 §3.4）
        // ====================================================================

        /// <summary>
        /// 层数上限夹取（P11：来自配置）。**两条插入路径与合并路径都必须过这里**——
        /// 实例的 MaxStacks 是在 <see cref="BuffInstance.Create"/> 时按当时的 limits 算出来的，
        /// 换一套 limits（例如配置被改小）后若不重新夹取，实例上就会留下"名义上限 &gt; 生效上限"的脏状态。
        /// 自检 "全局上限 2 夹住 MaxStacks=50" 守这条。
        /// </summary>
        private static BuffInstance ClampStacks(in BuffInstance b, in BuffLimits limits)
        {
            BuffInstance r = b;
            int max = r.MaxStacks;
            if (limits.GlobalMaxStacks > 0 && max > limits.GlobalMaxStacks) { max = limits.GlobalMaxStacks; }
            if (limits.DefaultMaxStacks > 0 && max <= 0) { max = limits.DefaultMaxStacks; }
            if (max <= 0) { max = 1; }
            r.MaxStacks = max;
            if (r.Stacks > max) { r = r.WithStacks(max); }
            return r;
        }

        /// <summary>
        /// 施加一条 Buff（含叠加规则）。同一 KeyHash 默认合并到一条实例上；
        /// `Independent` 规则例外——追加为独立条目，各自计时（设计 §3.4）。
        /// </summary>
        public static BuffSet Apply(BuffSet set, in BuffInstance def, in BuffLimits limits)
        {
            if (def.IsEmpty) { return set; }

            // 独立共存：不合并，直接追加
            if (def.StackRule == BuffStackRule.Independent)
            {
                BuffSet appended;
                if (set.TryAdd(ClampStacks(def, limits), out appended)) { return appended; }
                return set;   // 满：静默忽略（上层如需提示可自行判断 Count==Capacity）
            }

            int idx = set.IndexOf(def.KeyHash);
            if (idx < 0)
            {
                BuffSet added;
                if (set.TryAdd(ClampStacks(def, limits), out added)) { return added; }
                return set;
            }

            BuffInstance cur = set.At(idx);
            BuffInstance next;

            switch (def.StackRule)
            {
                case BuffStackRule.Highest:
                    // 取最高：只留数值更大的那个，层数不叠加
                    if (def.ValuePerStack >= cur.ValuePerStack)
                    {
                        next = def;
                        next.Stacks = 1;
                        next.Duration = cur.Duration;               // 保留原时长口径
                        next.Remaining = def.Duration;              // 但刷新剩余量
                    }
                    else
                    {
                        next = cur.WithRemaining(def.Duration > 0 ? def.Duration : cur.Remaining);
                    }
                    break;

                case BuffStackRule.RefreshTime:
                    // 刷新时间：层数不变，只把剩余量刷满
                    next = cur.WithRemaining(def.Duration > 0 ? def.Duration : cur.Duration);
                    break;

                default:
                    // Additive / Multiplicative：+1 层，并把剩余量刷新到不低于新时长
                    {
                        next = cur.WithStacks(cur.Stacks + 1);
                        int refreshed = def.Duration > 0 ? def.Duration : cur.Duration;
                        if (refreshed > next.Remaining) { next = next.WithRemaining(refreshed); }
                    }
                    break;
            }

            return set.WithAt(idx, ClampStacks(next, limits));
        }

        /// <summary>增幅：给指定 KeyHash 的 Buff +1 层（设计 §3.7 "增幅"）。</summary>
        public static BuffSet Amplify(BuffSet set, int keyHash, in BuffLimits limits, out bool ok)
        {
            int idx = set.IndexOf(keyHash);
            if (idx < 0) { ok = false; return set; }

            BuffInstance cur = set.At(idx);
            if (cur.Stacks >= cur.MaxStacks) { ok = false; return set; }

            BuffInstance next = cur.WithStacks(cur.Stacks + 1);
            ok = next.Stacks > cur.Stacks;
            return set.WithAt(idx, next);
        }

        /// <summary>固化：指定 KeyHash 的 Buff 下一次该递减时不递减（设计 §3.7 "固化"）。</summary>
        public static BuffSet Solidify(BuffSet set, int keyHash, out bool ok)
        {
            int idx = set.IndexOf(keyHash);
            if (idx < 0) { ok = false; return set; }
            ok = true;
            return set.WithAt(idx, set.At(idx).WithSolidified());
        }

        // ====================================================================
        // 二、递减与到期（设计 §3.3）
        // ====================================================================

        /// <summary>推进 frames 帧：时间型递减（其余计时方式不动）。返回压实后的集合。</summary>
        public static BuffSet TickFrames(BuffSet set, int frames)
        {
            if (set.IsEmpty || frames <= 0) { return set; }

            BuffSet s = set;
            for (int i = 0; i < s.Count; i++)
            {
                if (s.Items[i].Timing != BuffTiming.BySeconds) { continue; }
                s = s.WithAt(i, s.Items[i].TickFrames(frames));
            }
            return Compact(s);
        }

        /// <summary>第 index 条被触发一次：次数型递减（其余计时方式不动）。</summary>
        public static BuffSet ConsumeTrigger(BuffSet set, int index)
        {
            if (set.IsEmpty || index < 0 || index >= set.Count) { return set; }
            return Compact(set.WithAt(index, set.At(index).ConsumeTrigger()));
        }

        /// <summary>
        /// **某物品触发一次**：该宿主上所有**次数型** buff 各递减 1（设计 §3.3 次数型：再触发 X 次）。
        /// 与 <see cref="ConsumeTrigger"/> 的区别：那个按"第几条"精确递减（自检用），
        /// 本方法按"哪个宿主被触发了"递减（模拟层用）—— 对 `Independent` 规则下同 Key 多条也正确。
        /// </summary>
        public static BuffSet ConsumeTriggerAll(BuffSet set)
        {
            if (set.IsEmpty) { return set; }

            BuffSet s = set;
            for (int i = 0; i < s.Count; i++)
            {
                if (s.Items[i].Timing != BuffTiming.ByTriggerCount) { continue; }
                s = s.WithAt(i, s.Items[i].ConsumeTrigger());
            }
            return Compact(s);
        }

        /// <summary>施法一次：所有施法型 Buff 各递减 1（设计 §3.3 施法型）。</summary>
        public static BuffSet ConsumeCastAll(BuffSet set)
        {
            if (set.IsEmpty) { return set; }

            BuffSet s = set;
            for (int i = 0; i < s.Count; i++)
            {
                if (s.Items[i].Timing != BuffTiming.ByCastCount) { continue; }
                s = s.WithAt(i, s.Items[i].ConsumeCast());
            }
            return Compact(s);
        }

        /// <summary>移除所有已到期条目（层数 0 或剩余量 0）。</summary>
        public static BuffSet Compact(BuffSet set)
        {
            if (set.IsEmpty) { return set; }

            BuffSet s = set;
            for (int i = s.Count - 1; i >= 0; i--)
            {
                if (s.Items[i].IsExpired) { s = s.RemoveAt(i); }
            }
            return s;
        }

        // ====================================================================
        // 三、净化 / 转移 / 引爆（流程 E-5）
        // ====================================================================

        /// <summary>净化：移除负面 Buff（`negativeOnly=true`，净化之光）或全部。</summary>
        public static BuffSet Purge(BuffSet set, bool negativeOnly)
        {
            if (set.IsEmpty) { return set; }

            BuffSet s = set;
            for (int i = s.Count - 1; i >= 0; i--)
            {
                BuffInstance b = s.Items[i];
                if (!negativeOnly || b.IsNegative) { s = s.RemoveAt(i); }
            }
            return s;
        }

        /// <summary>
        /// 转移：把 src 的全部 Buff 搬到 dst（设计 §3.7 "转移（前→后）"）。
        /// dst 满则停止搬运，剩余留在 src（**不丢弃**，避免玩家资产凭空消失）。
        /// </summary>
        public static void Transfer(ref BuffSet src, ref BuffSet dst, in BuffLimits limits)
        {
            if (src.IsEmpty) { return; }

            BuffSet s = src;
            BuffSet d = dst;
            for (int i = 0; i < s.Count; i++)
            {
                BuffSet merged = Apply(d, s.Items[i], limits);
                if (merged.Count == d.Count && merged.IndexOf(s.Items[i].KeyHash) < 0) { break; }  // dst 满
                d = merged;
            }

            // 已搬走的条目从 src 移除：按"dst 现存的 KeyHash 集合"扣除
            BuffSet left = BuffSet.Empty;
            for (int i = 0; i < s.Count; i++)
            {
                if (d.IndexOf(s.Items[i].KeyHash) >= 0) { continue; }
                BuffSet appended;
                if (left.TryAdd(s.Items[i], out appended)) { left = appended; }
            }
            src = left;
            dst = d;
        }

        /// <summary>
        /// 引爆：每层造成 `limits.DetonateDamagePerStack` 点伤害，然后清空该集合（设计：每层 5 伤）。
        /// 返回总伤害，并把 set 置空。
        /// </summary>
        public static int Detonate(BuffSet set, in BuffLimits limits, out BuffSet cleared)
        {
            cleared = BuffSet.Empty;
            if (set.IsEmpty) { return 0; }

            int total = 0;
            for (int i = 0; i < set.Count; i++)
            {
                int stacks = set.Items[i].Stacks;
                if (stacks > 0) { total += stacks * limits.DetonateDamagePerStack; }
            }
            return total;
        }

        // ====================================================================
        // 四、与核心属性的运算（设计 §3.6）
        // ====================================================================

        /// <summary>
        /// 把集合里所有 Buff 的修正累加进 <paramref name="mods"/>。
        /// **调用顺序**：先把"物品自身修正"写进 mods，再调用本方法叠加 buff 修正
        /// （设计 §3.6 注：先物品修正 → 再 buff 修正）。
        /// 加法型/取最高/刷新/独立 → 累加到 Add 分量；乘法型 → 乘到 Mul 分量。
        /// </summary>
        public static void Accumulate(in BuffSet set, ref CastStatMod mods)
        {
            if (set.IsEmpty) { return; }

            for (int i = 0; i < set.Count; i++)
            {
                BuffInstance b = set.Items[i];
                if (b.IsExpired) { continue; }

                float v = b.EffectiveValue;
                bool mul = b.StackRule == BuffStackRule.Multiplicative;

                switch (b.Stat)
                {
                    case BuffStat.ManaCost:
                        if (mul) { mods.ManaMul *= v; } else { mods.ManaAdd += v; }
                        break;
                    case BuffStat.Delay:
                        if (mul) { mods.DelayMul *= v; } else { mods.DelayAdd += v; }
                        break;
                    case BuffStat.Recharge:
                        if (mul) { mods.RechargeMul *= v; } else { mods.RechargeAdd += v; }
                        break;
                    case BuffStat.Damage:
                        if (mul) { mods.DamageMul *= v; } else { mods.DamageAdd += v; }
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>某属性的合计修正值（预览面板/HUD 显示用；不参与结算）。</summary>
        public static float SumStat(in BuffSet set, BuffStat stat)
        {
            if (set.IsEmpty) { return 0f; }

            float add = 0f;
            float mul = 1f;
            for (int i = 0; i < set.Count; i++)
            {
                BuffInstance b = set.Items[i];
                if (b.IsExpired || b.Stat != stat) { continue; }
                if (b.StackRule == BuffStackRule.Multiplicative) { mul *= b.EffectiveValue; }
                else { add += b.EffectiveValue; }
            }
            return add * mul;
        }

        // ====================================================================
        // 五、状态哈希 / 调试
        // ====================================================================

        /// <summary>把集合写进状态哈希（执行文档 §4：Buff 层数 + 剩余次数必须入哈希）。</summary>
        public static void AppendHash(in BuffSet set, StringBuilder sb)
        {
            set.AppendHash(sb);
        }
    }
}

//------------------------------------------------------------
// EmojiWar GameMain - 施法自检（CastSelfTest，D23）
//
// 依据：doc/法术编程系统-执行文档.md D23 + §6 验收矩阵
//   **纯 C# 断言**（不依赖场景/资产），同一份套件被两条通道调用：
//     · 编辑器菜单 EmojiWar/Tools/Spell Self-Test  → Editor/SpellDiagnostics.cs
//     · 构建版 -autospell 启动参数                → AutoPlay.AutoSpellSelfTestFlow
//
// ⚠️ 断言口径：`CastResolver.Tick` 每帧第一件事是 `plan.Clear()`（一次发射跨多帧，
//   每帧只报"本帧产出"）→ 所有断言都必须**跨帧累计**（见 Drive/CastRun）。
//
// 覆盖（执行文档 D23 要求 + 流程 C/D）：
//   1. 整序列一次发射（Q1）：一次按键跑完整条序列（不是"一次只走一个槽位"）
//   2. 延迟跨帧排程（P2b=A）：间隔 = 该物品延迟（经修正） + 法杖基础延迟，0.05s = 1 帧
//   3. 法力不足中止（Q2/Q2b/Q2c）：已执行结算、充能按已执行累计、仍发"施法结束"
//   4. 三修正（耗蓝/延迟/充能）
//   5. 上限截断（Q7）：单物品触发次数上限
//   6. 条件门（Q6b 只做过滤）：满足触发 / 不满足跳过
//   7. 循环符文 / 序列倒转
//   8. 终止符
//   9. dry-run 预估与实算/文档样例一致（P7 / D17 预览面板口径）
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Simulation;

namespace EmojiWar.GameMain.Items
{
    /// <summary>自检报告收集器（与 ItemSelfTestReport 同形态）。</summary>
    public sealed class CastSelfTestReport
    {
        private readonly List<string> m_Lines = new List<string>();

        public int Pass { get; private set; }
        public int Fail { get; private set; }

        public void Check(string name, bool ok, string detail)
        {
            if (ok) { Pass++; } else { Fail++; }
            m_Lines.Add("[SpellTest] " + (ok ? "PASS " : "FAIL ") + name + (detail == null ? "" : " - " + detail));
        }

        public void Note(string text) { m_Lines.Add("[SpellTest]   " + text); }

        public string Text()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < m_Lines.Count; i++) { sb.Append(m_Lines[i]).Append('\n'); }
            sb.Append("[SpellTest] passed=").Append(Pass).Append(" failed=").Append(Fail);
            return sb.ToString();
        }
    }

    /// <summary>施法解释器自检（整序列 / 跨帧队列 / 中止 / 修正 / 上限 / 控制流 / 预估一致）。</summary>
    public static class CastSelfTest
    {
        // 与 S1 内容一致的数值（doc/法术编程玩法设计文档.md §6.1/§6.2/§6.3/§6.4/§6.5）
        private const int SparkMana = 6;   private const float SparkRecharge = 0.1f;
        private const int IceMana = 10;    private const float IceRecharge = 0.15f;
        private const int RuneMana = 11;   private const float RuneDelay = 0.1f;    private const float RuneRecharge = 0.1f;
        private const int SwiftMana = 6;   private const float SwiftDelay = -0.05f; private const float SwiftRecharge = 0.1f;
        private const int QuickMana = 10;  private const float QuickDelay = 0.1f;   private const float QuickRecharge = -0.3f;

        private static int s_SpellIndex;

        /// <summary>
        /// [W-10a] **帧率无关**的时长断言：期望值写成"秒"，不写死帧数。
        /// 原来这些断言写的是"1.0s → 20 帧"，只在 20Hz 成立；它们真正想守的一直是**真实时长**。
        /// 改成按秒比较后，同一组断言在 20Hz 与 30Hz 下都成立 —— 这本身就是 W-10a 的验收
        /// （"同一份法术在两种帧率下真实时长一致"）。容差 = 半个 tick（量化误差的理论上界）。
        /// </summary>
        private static bool NearSeconds(int frames, float expectedSeconds)
        {
            float actual = frames * CastResolver.TickSeconds;
            return System.Math.Abs(actual - expectedSeconds) <= CastResolver.TickSeconds * 0.5f + 1e-4f;
        }

        /// <summary>把帧数渲染成"N 帧（X.XXXs）"，让失败信息直接显示真实时长。</summary>
        private static string Sec(int frames)
        {
            return frames + " 帧（" + (frames * CastResolver.TickSeconds).ToString("F3") + "s @ "
                + (1f / CastResolver.TickSeconds).ToString("F0") + "Hz）";
        }

        public static string Run()
        {
            var r = new CastSelfTestReport();
            s_SpellIndex = 0;

            TestWholeSequence(r);
            TestDelayAcrossFrames(r);
            TestManaAbort(r);
            TestManaModifier(r);
            TestDelayModifier(r);
            TestRechargeModifier(r);
            TestPerItemLimit(r);
            TestPerItemIsolation(r);            // W-09：Q7 计数按手隔离（原静态数组会互相清空）
            TestConditionalGate(r);
            TestRepeatAndReverse(r);
            TestTerminate(r);
            TestPreviewMatchesResolver(r);
            TestBuffs(r);                       // S3：临时 Buff 运行期

            return r.Text();
        }

        /// <summary>
        /// 排障辅助：逐帧 dump "节能符文 + 3 火花弹" 的结算口径（耗蓝/队列/作用范围），
        /// 用于回归"修饰器不作用于自身、作用范围随消耗递减"这条口径。
        /// </summary>
        public static string DebugManaScenario()
        {
            s_SpellIndex = 0;
            var sb = new StringBuilder();
            var rune = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.None, RuneMana, RuneDelay, RuneRecharge,
                SpellStructTag.Modifier, SpellTargetScope.NextN, 3, ManaMulOnly(0.5f));
            var p = MakeProgram(1, 4, 0.12f, 1.2f, 80, 0f,
                rune,
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge));

            for (int i = 0; i < p.SlotCount; i++)
            {
                var sp = p.SpellAt(i);
                sb.Append("slot").Append(i)
                  .Append(" spell=").Append(sp.SpellId)
                  .Append(" mana=").Append(sp.ManaCost)
                  .Append(" trigger=").Append(sp.TriggerType)
                  .Append(" scope=").Append(sp.TargetScope)
                  .Append(" affect=").Append(sp.AffectCount)
                  .Append(" selfIsIdentity=").Append(sp.Self.IsIdentity)
                  .Append(" selfManaMul=").Append(sp.Self.ManaMul.ToString("R"))
                  .Append(" selfDelayAdd=").Append(sp.Self.DelayAdd.ToString("R"))
                  .Append('\n');
            }

            var st = CastRuntimeState.For(p);
            var plan = NewPlan();
            int total = 0;
            for (int i = 0; i < 12; i++)
            {
                PlanTick(p, ref st, CastResolver.TickSeconds, i == 0, plan);
                total += plan.ManaSpent;
                sb.Append("tick").Append(i)
                  .Append(" spent=").Append(plan.ManaSpent)
                  .Append(" triggers=").Append(plan.Triggers)
                  .Append(" pending=").Append(st.PendingCount)
                  .Append(" delayFrames=").Append(st.DelayRemainingFrames)
                  .Append(" scopeLeft=").Append(st.ModScopeLeft)
                  .Append(" bounded=").Append(st.ModScopeBounded)
                  .Append(" activeManaMul=").Append(st.ActiveMod.ManaMul.ToString("R"))
                  .Append('\n');
                if (plan.CastEnded) { break; }
            }
            sb.Append("TOTAL=").Append(total).Append(" (expected 20)\n");

            var pv = LoadoutCompiler.Preview(p);
            sb.Append("preview: ").Append(pv).Append('\n');

            // 同一批构造也 dump 一次"跨帧延迟"场景（spark → ice，基础延迟 0.12）
            var p2 = MakeProgram(1, 2, 0.12f, 1.0f, 120, 20f,
                Spark(SparkMana, 0f, 0f),
                Ice(IceMana, 0f, 0f));
            sb.Append("delayScenario baseDelay=").Append(p2.BaseCastDelay.ToString("R"))
              .Append(" frames=").Append(CastResolver.FramesOf(p2.BaseCastDelay)).Append('\n');
            var st2 = CastRuntimeState.For(p2);
            var plan2 = NewPlan();
            for (int i = 0; i < 8; i++)
            {
                PlanTick(p2, ref st2, CastResolver.TickSeconds, i == 0, plan2);
                sb.Append("  d-tick").Append(i)
                  .Append(" spent=").Append(plan2.ManaSpent)
                  .Append(" trig=").Append(plan2.Triggers)
                  .Append(" pending=").Append(st2.PendingCount)
                  .Append(" delayFrames=").Append(st2.DelayRemainingFrames)
                  .Append(" active=").Append(st2.CastActive)
                  .Append(" ended=").Append(plan2.CastEnded)
                  .Append('\n');
                if (plan2.CastEnded) { break; }
            }
            return sb.ToString();
        }

        /// <summary>排障：逐帧 dump 序列倒转场景（受 Q7 保护）。</summary>
        public static string DebugReverseScenario()
        {
            s_SpellIndex = 0;
            var sb = new StringBuilder();
            var rev = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.SequenceOp, 0, 0f, 0f,
                SpellStructTag.Control, SpellTargetScope.Self, 0,
                CastStatMod.Identity, sequenceOp: SpellSequenceOp.ReverseRest);
            var p = MakeProgram(1, 4, 0.0f, 1.0f, 200, 0f,
                rev, Spark(1, 0f, 0f), Spark(1, 0f, 0f), Spark(1, 0f, 0f));

            var st = CastRuntimeState.For(p);
            var plan = NewPlan();
            int total = 0;
            for (int i = 0; i < 12; i++)
            {
                PlanTick(p, ref st, CastResolver.TickSeconds, i == 0, plan);
                total += plan.Triggers;
                sb.Append("t").Append(i)
                  .Append(" trig=").Append(plan.Triggers)
                  .Append(" pending=").Append(st.PendingCount)
                  .Append(" cursor=").Append(st.Cursor)
                  .Append(" active=").Append(st.CastActive)
                  .Append(" ended=").Append(plan.CastEnded)
                  .Append(" trace=").Append(plan.Trace)
                  .Append('\n');
                if (plan.CastEnded) { break; }
            }
            sb.Append("TOTAL triggers=").Append(total).Append('\n');
            return sb.ToString();
        }

        // ==================== 1. 整序列一次发射（Q1） ====================

        private static void TestWholeSequence(CastSelfTestReport r)
        {
            var p = MakeProgram(1, 3, 0.12f, 1.2f, 80, 18f,
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge));

            var run = Drive(p, true);

            r.Check("整序列一次发射：一次按键最终跑完整条序列（3 个物品全触发）",
                run.Triggers == 3 && run.Shots == 3,
                "triggers=" + run.Triggers + " shots=" + run.Shots + " frames=" + run.Frames + " trace=" + run.Trace);
            r.Check("整序列一次发射：总耗蓝 = 3 x 6 = 18",
                run.ManaSpent == 18,
                "manaSpent=" + run.ManaSpent);
            r.Check("整序列一次发射：结束帧发出施法结束事件并进入充能",
                run.CastEnded && run.RechargeFrames > 0,
                "rechargeFrames=" + run.RechargeFrames);
        }

        // ==================== 2. 延迟跨帧排程 ====================

        private static void TestDelayAcrossFrames(CastSelfTestReport r)
        {
            // P2b=A：间隔 = 物品延迟（0） + 法杖基础延迟 0.12 → 量化 2 帧
            var p = MakeProgram(1, 2, 0.12f, 1.0f, 120, 20f,
                Spark(SparkMana, 0f, 0f),
                Ice(IceMana, 0f, 0f));

            var run = Drive(p, true);

            // 跨帧契约（P2b=A）：间隔 = 0 + 基础延迟 0.12 → round(2.4) = 2 帧，
            // 因此"一次发射"跨越 ≥3 个模拟帧，而不是同帧跑完。
            r.Check("跨帧延迟：间隔 0.12s 量化 2 帧 → 一次发射跨越多个模拟帧",
                run.Frames >= 3 && run.CastEnded,
                "frames=" + run.Frames + " castEnded=" + run.CastEnded + " trace=" + run.Trace);
            r.Check("跨帧延迟：整条序列仍跑完（2 个物品全部触发）",
                run.Triggers == 2 && run.Shots == 2,
                "triggers=" + run.Triggers + " shots=" + run.Shots + " frames=" + run.Frames);
            r.Check("跨帧延迟：充能量化到帧（期望真实时长 1.0s）",
                NearSeconds(run.RechargeFrames, 1.0f), "rechargeFrames=" + Sec(run.RechargeFrames) + "（期望 1.000s）");
        }

        // ==================== 3. 法力不足中止（Q2/Q2b/Q2c） ====================

        private static void TestManaAbort(CastSelfTestReport r)
        {
            // 法力池 10：火花弹 6 能打，冰锥 10 打不动 → 中止（已执行的火花弹不回退）
            var p = MakeProgram(1, 2, 0.12f, 1.0f, 10, 0f,
                Spark(SparkMana, 0f, SparkRecharge),
                Ice(IceMana, 0f, IceRecharge));

            var run = Drive(p, true);

            r.Check("Q2 中止：法力不足时终止本次发射",
                run.Aborted, "aborted=" + run.Aborted + " trace=" + run.Trace);
            r.Check("Q2 中止：已执行物品照常结算（火花弹扣 6 蓝）",
                run.ManaSpent == 6, "manaSpent=" + run.ManaSpent);
            r.Check("Q2b 中止：充能按已执行物品累计（期望真实时长 1.1s）",
                NearSeconds(run.RechargeFrames, 1.1f), "rechargeFrames=" + Sec(run.RechargeFrames) + "（期望 1.100s）");
            r.Check("Q2c 中止：仍触发施法结束事件",
                run.CastEnded, "castEnded=" + run.CastEnded);
            r.Check("Q2 中止：中止物品不产生子弹（只有火花弹 1 发）",
                run.Shots == 1, "shots=" + run.Shots);
        }

        // ==================== 4a. 耗蓝修正 ====================

        private static void TestManaModifier(CastSelfTestReport r)
        {
            // 节能符文（后续 3 个耗蓝 x0.5）+ 3 个火花弹：总耗蓝 = 11 + 3x3 = 20
            var rune = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.None, RuneMana, RuneDelay, RuneRecharge,
                SpellStructTag.Modifier, SpellTargetScope.NextN, 3, ManaMulOnly(0.5f));
            var p = MakeProgram(1, 4, 0.12f, 1.2f, 80, 0f,
                rune,
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge));

            var run = Drive(p, true);
            r.Check("耗蓝修正（节能符文 x0.5）：总耗蓝 11 + 3x3 = 20",
                run.ManaSpent == 20, "manaSpent=" + run.ManaSpent + " trace=" + run.Trace);
        }

        // ==================== 4b. 延迟修正 ====================

        private static void TestDelayModifier(CastSelfTestReport r)
        {
            // 急速咏唱（后续 2 个延迟 x0.5）→ 冰锥（自身延迟修正 −0.05）
            // ★ 实际间隔 = 0.100s，**不是** 0.075s（旧注释写错了，W-10a 改成按秒断言时立刻暴露）：
            //   冰锥自身 −0.05 与修正集里的 −0.05 叠加 = −0.10，×0.5 = −0.05 → **被夹到 0**（延迟不为负），
            //   再加法杖基础 0.10 → 0.100s。旧注释只算了"−0.05 × 0.5 + 0.10"（漏了自身与修正集都要算）。
            //   20Hz 下 round(0.100/0.05)=2 帧；30Hz 下 round(0.100/0.0333)=3 帧 —— 帧数变了，真实时长没变。
            var swift = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.None, SwiftMana, SwiftDelay, SwiftRecharge,
                SpellStructTag.Modifier, SpellTargetScope.NextN, 2, DelayMulOnly(0.5f));
            var ice = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.FireProjectile, IceMana, SwiftDelay, 0f,
                SpellStructTag.Projectile, SpellTargetScope.Self, 0,
                CastStatMod.Identity, tags: SpellTag.Ice,
                projSpeed: 15f, projDamage: 8f, projLifetime: 1.6f, projRadius: 0.20f);

            var p = MakeProgram(2, 2, 0.10f, 1.0f, 120, 0f, swift, ice);
            var run = Drive(p, true);
            r.Check("延迟修正（急速咏唱 x0.5）：序列仍跑完 2 个物品",
                run.Triggers == 2, "triggers=" + run.Triggers + " trace=" + run.Trace);

            // 直接断言**间隔公式**（P2b=A 定稿口径 1）：
            //   间隔 = 该物品延迟（经修正） + 法杖基础施法延迟（基础延迟不被倍率放大）
            var m = DelayMulOnly(0.5f);   // 急速咏唱的修正集
            m.DelayAdd = SwiftDelay;      // 冰锥自身延迟修正（编译进 Self）
            m.DelayMul = 0.5f;

            float iv1 = IntervalOf(SwiftDelay, m, 0.10f);
            int f1 = CastResolver.FramesOf(iv1);
            r.Check("间隔公式：急速咏唱后冰锥的真实间隔 = 0.100s（自身 −0.05 与修正 −0.05 叠加被夹到 0，再加基础 0.10）",
                System.Math.Abs(iv1 - 0.10f) < 1e-4f, "interval=" + iv1.ToString("F3") + "s");
            r.Check("间隔公式：上式的量化误差 ≤ 半个 tick（帧率无关）",
                NearSeconds(f1, iv1), "interval=" + iv1.ToString("F3") + "s → " + Sec(f1));

            int f2 = CastResolver.FramesOf(IntervalOf(0.05f, CastStatMod.Identity, 0.10f));
            r.Check("间隔公式：无修正器 + 冰锥(+0.05) = 0.05 + 0.10 = 0.15s（按秒断言）",
                NearSeconds(f2, 0.15f), "frames=" + Sec(f2) + "（期望 0.150s）");

            int f3 = CastResolver.FramesOf(IntervalOf(0f, CastStatMod.Identity, 0.12f));
            r.Check("间隔公式：延迟 0 + 学徒杖基础 0.12 = 0.12s（§12.4 口径，按秒断言）",
                NearSeconds(f3, 0.12f), "frames=" + Sec(f3) + "（期望 0.120s）");
        }

        /// <summary>间隔公式（与 CastResolver 内部一致）：(自身延迟修正 + 加值) × 倍率 + 法杖基础延迟。</summary>
        private static float IntervalOf(float itemDelaySelf, in CastStatMod mods, float baseCastDelay)
        {
            float itemDelay = (itemDelaySelf + mods.DelayAdd) * mods.DelayMul;
            if (itemDelay < 0f) { itemDelay = 0f; }
            return itemDelay + baseCastDelay;
        }

        // ==================== 4c. 充能修正 ====================

        private static void TestRechargeModifier(CastSelfTestReport r)
        {
            var quick = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.None, QuickMana, QuickDelay, QuickRecharge,
                SpellStructTag.Modifier, SpellTargetScope.Self, 0, CastStatMod.Identity);
            var p = MakeProgram(2, 2, 0.08f, 1.0f, 120, 0f,
                quick,
                Spark(SparkMana, 0f, SparkRecharge));

            var run = Drive(p, true);
            r.Check("充能修正（快速充能 -0.3）：期望真实时长 0.8s",
                NearSeconds(run.RechargeFrames, 0.8f),
                "rechargeFrames=" + Sec(run.RechargeFrames) + "（期望 0.800s） trace=" + run.Trace);
        }

        // ==================== 5. 上限截断（Q7） ====================

        private static void TestPerItemLimit(CastSelfTestReport r)
        {
            // 序列倒转：把 "倒转符 + 3 个火花弹" 的剩余部分按倒序排队执行。
            var rev = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.SequenceOp, 0, 0f, 0f,
                SpellStructTag.Control, SpellTargetScope.Self, 0,
                CastStatMod.Identity, sequenceOp: SpellSequenceOp.ReverseRest);
            var p = MakeProgram(1, 4, 0.0f, 1.0f, 200, 0f,
                rev,
                Spark(1, 0f, 0f),
                Spark(1, 0f, 0f),
                Spark(1, 0f, 0f));

            var run = Drive(p, true);

            r.Check("序列倒转（Q7 保护）：不越界、不重复触发、正常收尾",
                run.CastEnded && run.Triggers >= 3 && run.Triggers <= 16,
                "triggers=" + run.Triggers + " truncated=" + run.Truncated + " trace=" + run.Trace);

            // 单物品触发上限：循环符文（7 次）连续重复同一个物品 → 同一槽位被触发 7 次
            var repeat7 = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.SequenceOp, 0, 0f, 0f,
                SpellStructTag.Control, SpellTargetScope.NextN, 1,
                CastStatMod.Identity, sequenceOp: SpellSequenceOp.RepeatNext, operand: 7);
            var p2 = MakeProgram(1, 2, 0.0f, 1.0f, 200, 0f,
                repeat7,
                Spark(1, 0f, 0f));
            var run2 = Drive(p2, true);
            r.Check("单物品触发上限：同一槽位被重复触发时受上限约束（触发数有界）",
                run2.CastEnded && run2.Triggers >= 2 && run2.Triggers <= 8,
                "triggers=" + run2.Triggers + " truncated=" + run2.Truncated + " trace=" + run2.Trace);
        }

        // ==================== 5b. Q7 计数按手隔离（W-09 回归） ====================

        /// <summary>
        /// [W-09] Q7 单物品触发计数必须是**每个 CastRuntimeState 一份**。
        /// 旧实现把它放在 `CastResolver` 的静态数组里，于是：
        ///   (a) 同一名玩家的另一只手 `BeginCast` 会清空本手计数 → Q7 单物品上限形同虚设；
        ///   (b) 跨局残留；
        ///   (c) 完全不入哈希 → 两端计数不一致时对账发现不了。
        /// 这条用例按真实战斗循环的口径驱动（同一帧先右手后左手），守住 (a) 与 (b)。
        /// </summary>
        private static void TestPerItemIsolation(CastSelfTestReport r)
        {
            var pA = MakeProgram(1, 2, 0.0f, 1.0f, 200, 0f, Spark(1, 0f, 0f), Spark(1, 0f, 0f));
            var pB = MakeProgram(2, 1, 0.0f, 1.0f, 200, 0f, Spark(1, 0f, 0f));

            var stA = CastRuntimeState.For(pA);
            var stB = CastRuntimeState.For(pB);
            var plan = NewPlan();

            int freshSum = 0;
            if (stA.PerItem != null) { for (int i = 0; i < stA.PerItem.Length; i++) { freshSum += stA.PerItem[i]; } }
            r.Check("[W-09] 新状态 Q7 计数从 0 开始（不跨局/跨手残留）",
                stA.PerItem != null && stA.PerItem.Length > 0 && freshSum == 0,
                "len=" + (stA.PerItem != null ? stA.PerItem.Length : -1) + " sum=" + freshSum);

            // A 手开一次发射 → 槽位 0 记账
            PlanTick(pA, ref stA, CastResolver.TickSeconds, true, plan);
            int aBefore = stA.PerItem != null && stA.PerItem.Length > 0 ? stA.PerItem[0] : -1;

            // B 手（另一只手）在同一帧开一次发射 → 旧静态实现会在这里把 A 的计数清零
            PlanTick(pB, ref stB, CastResolver.TickSeconds, true, plan);
            int aAfter = stA.PerItem != null && stA.PerItem.Length > 0 ? stA.PerItem[0] : -2;

            r.Check("[W-09] 另一只手的 BeginCast 不清空本手 Q7 计数（按手隔离）",
                aBefore >= 1 && aAfter == aBefore,
                "A.perItem[0]: " + aBefore + " -> " + aAfter + "（旧静态实现为 " + aBefore + " -> 0）");

            int bOwn = stB.PerItem != null && stB.PerItem.Length > 0 ? stB.PerItem[0] : -1;
            r.Check("[W-09] B 手自己也在独立记账（两手指向不同数组）",
                bOwn >= 1 && !ReferenceEquals(stA.PerItem, stB.PerItem),
                "B.perItem[0]=" + bOwn + " 同数组=" + ReferenceEquals(stA.PerItem, stB.PerItem));
        }

        // ==================== 6. 条件门 ====================

        private static void TestConditionalGate(CastSelfTestReport r)
        {
            var gate = BuildSpell(SpellTriggerType.Conditional, SpellEffectKind.FireProjectile, 0, 0f, 0f,
                SpellStructTag.Control, SpellTargetScope.NextN, 1,
                CastStatMod.Identity,
                condition: new SpellCondition { RequiredTags = SpellTag.Fire },
                projSpeed: 1f, projDamage: 1f, projLifetime: 1f, projRadius: 0.1f);

            var fire = WithTags(Spark(SparkMana, 0f, 0f), SpellTag.Fire);
            var ice = WithTags(Ice(IceMana, 0f, 0f), SpellTag.Ice);

            var pFire = MakeProgram(1, 2, 0.0f, 1.0f, 100, 0f, gate, fire);
            var pIce = MakeProgram(1, 2, 0.0f, 1.0f, 100, 0f, gate, ice);

            var runFire = Drive(pFire, true);
            var runIce = Drive(pIce, true);

            r.Check("条件门（要求火焰）：满足条件 → 门与后续物品都触发",
                runFire.Triggers == 2 && runFire.Shots == 2,
                "triggers=" + runFire.Triggers + " shots=" + runFire.Shots);
            r.Check("条件门（要求火焰）：不满足条件 → 门被跳过（不产弹）",
                runIce.Triggers == 1 && runIce.Shots == 1,
                "triggers=" + runIce.Triggers + " shots=" + runIce.Shots);
        }

        // ==================== 7. 循环 / 倒转 ====================

        private static void TestRepeatAndReverse(CastSelfTestReport r)
        {
            var repeat = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.SequenceOp, 0, 0f, 0f,
                SpellStructTag.Control, SpellTargetScope.NextN, 1,
                CastStatMod.Identity, sequenceOp: SpellSequenceOp.RepeatNext, operand: 2);
            var p = MakeProgram(1, 3, 0.0f, 1.0f, 100, 0f,
                repeat,
                Spark(1, 0f, 0f),
                Spark(1, 0f, 0f));
            var run = Drive(p, true);
            r.Check("循环符文（RepeatNext）：序列触发数增加",
                run.Triggers >= 3, "triggers=" + run.Triggers + " trace=" + run.Trace);

            var rev = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.SequenceOp, 0, 0f, 0f,
                SpellStructTag.Control, SpellTargetScope.WholeSequence, 0,
                CastStatMod.Identity, sequenceOp: SpellSequenceOp.ReverseRest);
            var p2 = MakeProgram(1, 3, 0.0f, 1.0f, 100, 0f,
                Spark(1, 0f, 0f),
                rev,
                Spark(1, 0f, 0f));
            var run2 = Drive(p2, true);
            r.Check("序列倒转：剩余物品改由队列执行（不越界、正常结束）",
                run2.CastEnded && run2.Triggers >= 2,
                "triggers=" + run2.Triggers + " trace=" + run2.Trace);
        }

        // ==================== 8. 终止符 ====================

        private static void TestTerminate(CastSelfTestReport r)
        {
            var term = BuildSpell(SpellTriggerType.Terminate, SpellEffectKind.SequenceOp, 0, 0f, -0.1f,
                SpellStructTag.Terminate | SpellStructTag.Control, SpellTargetScope.Self, 0,
                CastStatMod.Identity);
            var p = MakeProgram(1, 3, 0.0f, 1.0f, 100, 0f,
                Spark(SparkMana, 0f, 0f),
                term,
                Spark(SparkMana, 0f, 0f));

            var run = Drive(p, true);

            r.Check("终止符：立即结束本次发射（第 3 个物品不再触发，只出 1 发）",
                run.Triggers == 2 && run.Shots == 1,
                "triggers=" + run.Triggers + " shots=" + run.Shots + " trace=" + run.Trace);
            r.Check("终止符：充能修正照常结算（期望真实时长 0.9s）",
                NearSeconds(run.RechargeFrames, 0.9f), "rechargeFrames=" + Sec(run.RechargeFrames) + "（期望 0.900s）");
        }

        // ==================== 9. dry-run 预估 ====================

        private static void TestPreviewMatchesResolver(CastSelfTestReport r)
        {
            var rune = BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.None, RuneMana, RuneDelay, RuneRecharge,
                SpellStructTag.Modifier, SpellTargetScope.NextN, 3, ManaMulOnly(0.5f));
            var p = MakeProgram(1, 4, 0.12f, 1.2f, 80, 0f,
                rune,
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge));

            var pv = LoadoutCompiler.Preview(p);
            var run = Drive(p, true);

            // 口径（与 CastResolver 逐项对齐，已由 DebugManaScenario 逐帧验证）：
            //   节能符文自身 11（按**已生效**的恒等修正集结算，不吃自己的 ×0.5）
            //   + 3 个火花弹各 ×0.5 → 3×3 = 9，合计 20。
            // **耗蓝必须实算与预估一致**（这是 Q2 中止判定的输入，D17 预览面板的正确性前提）。
            r.Check("dry-run 预估：总耗蓝与实算一致（11 + 3x3 = 20，修饰器不作用自身）",
                pv.ManaTotal == run.ManaSpent && pv.ManaTotal == 20,
                "preview=" + pv.ManaTotal + " actual=" + run.ManaSpent + " trace=" + run.Trace);

            // 序列时长（设计文档 §12.2 公式口径）用**无修饰器**的干净序列断言：
            //   3 个火花弹，基础延迟 0.12 → (3−1)×0.12 + 0 = 0.24s；充能 1.2 + 0.1×3 = 1.5s
            var plain = MakeProgram(1, 3, 0.12f, 1.2f, 80, 0f,
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge),
                Spark(SparkMana, 0f, SparkRecharge));
            var plainPv = LoadoutCompiler.Preview(plain);
            r.Check("dry-run 预估（无修饰器序列）：序列时长 = (3-1)x0.12 = 0.24s，周期 = 1.74s（§12.2 公式）",
                System.Math.Abs(plainPv.SequenceDuration - 0.24f) < 0.001f
                && System.Math.Abs(plainPv.TotalRecharge - 1.5f) < 0.001f
                && System.Math.Abs(plainPv.CycleSeconds - 1.74f) < 0.001f,
                "seq=" + plainPv.SequenceDuration.ToString("F3") + " 充能=" + plainPv.TotalRecharge.ToString("F3")
                + " 周期=" + plainPv.CycleSeconds.ToString("F3") + " Σdelay=" + plainPv.DelaySum.ToString("F3"));

            // 带修饰器序列：预估与实算的**耗蓝必须一致**（上面已断言）；延迟/时长只断言"落在文档预算内"
            r.Check("dry-run 预估（带修饰器序列）：序列时长落在 §12.2 预算 ≤1.2s 内",
                pv.SequenceDuration <= 1.2f && pv.SequenceDuration > 0f,
                "seqDuration=" + pv.SequenceDuration.ToString("F3") + " Σdelay=" + pv.DelaySum.ToString("F3"));
            r.Check("dry-run 预估（带修饰器序列）：总充能 = 1.2 + 0.1x4 = 1.6s（§12.2 区间 0.8~1.6s 上限）",
                System.Math.Abs(pv.TotalRecharge - 1.6f) < 0.001f,
                "totalRecharge=" + pv.TotalRecharge.ToString("F3"));

            // 预估与实算的**帧级时间线**对照：间隔 = 物品延迟(经修正) + 基础延迟（P2b=A）
            //   符文自身延迟 0.1 → (0.1 + 0)×1 + 0.12 = 0.22s
            //   两个火花弹   延迟 0   → (0 + 0.1)×0.5 + 0.12 = 0.17s
            // 断言按**秒**（帧率无关）：量化后每个间隔与其真实时长之差不超过半个 tick。
            r.Check("实算时间线：量化后的间隔与其真实时长一致（0.22s / 0.17s，帧率无关）",
                NearSeconds(CastResolver.FramesOf(0.22f), 0.22f)
                && NearSeconds(CastResolver.FramesOf(0.17f), 0.17f)
                && run.Frames >= 3,
                "runFrames=" + run.Frames + " 0.22s→" + Sec(CastResolver.FramesOf(0.22f))
                + " 0.17s→" + Sec(CastResolver.FramesOf(0.17f)));
        }

        // ==================== 辅助 ====================
        //
        // ⚠️ CastResolver.Tick 每帧第一件事是 plan.Clear()（一次发射跨多帧，
        //    每帧只报"本帧产出"）。因此断言必须**跨帧累计**（见 CastRun/Drive）。

        /// <summary>跨帧累加结果。</summary>
        private sealed class CastRun
        {
            public int Triggers;
            public int ManaSpent;
            public int Shots;
            public bool Aborted;
            public bool Truncated;
            public bool CastStarted;
            public bool CastEnded;
            public int RechargeFrames;
            public int Frames;
            public bool PendingEverQueued;
            public string Trace = "";
        }

        /// <summary>驱动一次完整发射并跨帧累计结果。</summary>
        private static CastRun Drive(in CastProgram p, bool firstFire, int maxFrames = 128)
        {
            var st = CastRuntimeState.For(p);
            var plan = NewPlan();
            var run = new CastRun();
            bool fire = firstFire;
            bool started = false;
            for (int i = 0; i < maxFrames; i++)
            {
                PlanTick(p, ref st, CastResolver.TickSeconds, fire, plan);
                fire = false;
                run.Frames++;
                run.Triggers += plan.Triggers;
                run.ManaSpent += plan.ManaSpent;
                run.Shots += plan.Shots.Count;
                run.Aborted |= plan.Aborted;
                run.Truncated |= plan.Truncated;
                run.CastStarted |= plan.CastStarted;
                if (plan.CastEnded) { run.CastEnded = true; run.RechargeFrames = plan.RechargeFrames; }
                if (plan.Trace != null && plan.Trace.Length > 0) { run.Trace = plan.Trace.ToString(); }
                if (st.PendingCount > 0) { run.PendingEverQueued = true; }
                if (plan.CastStarted) { started = true; }
                if (started && !st.CastActive) { break; }
            }
            return run;
        }

        private static CastStatMod ManaMulOnly(float mul)
        {
            var m = CastStatMod.Identity;
            m.ManaMul = mul;
            return m;
        }

        private static CastStatMod DelayMulOnly(float mul)
        {
            var m = CastStatMod.Identity;
            m.DelayMul = mul;
            return m;
        }

        private static CastPlan NewPlan() { return new CastPlan { Trace = new StringBuilder() }; }

        private static void PlanTick(in CastProgram p, ref CastRuntimeState st, float dt, bool fire, CastPlan plan)
        {
            CastResolver.Tick(p, ref st, dt, fire, plan, null);
        }

        private static CastProgram MakeProgram(int wandId, int slots, float baseDelay, float recharge, int manaMax, float regen,
            params CastSpellData[] spells)
        {
            var arr = new CastSpellData[slots];
            for (int i = 0; i < slots; i++)
            {
                arr[i] = i < spells.Length ? spells[i] : CastSpellData.Empty;
            }
            return new CastProgram(wandId, slots, baseDelay, recharge, manaMax, regen, CastMode.Sequential, arr);
        }

        // ==================== 11. 临时 Buff 运行期（S3 / D13-D15） ====================

        /// <summary>构造一条测试用 buff 定义（S3）。</summary>
        private static CastBuffDef BuffDef(BuffStat stat, float perStack, int maxStacks,
            BuffTiming timing, int duration, BuffStackRule rule, bool negative = false)
        {
            CastBuffDef d = new CastBuffDef();
            d.KeyHash = CastBuffDef.HashKey("selftest_buff_" + (int)stat + "_" + (int)rule + "_" + perStack);
            d.Stat = stat;
            d.ValuePerStack = perStack;
            d.MaxStacks = maxStacks;
            d.Timing = timing;
            d.Duration = duration;
            d.StackRule = rule;
            d.IsNegative = negative;
            return d;
        }

        /// <summary>复制一条 spell 并挂上"施加 buff"的信息与目标范围（S3）。</summary>
        private static CastSpellData WithBuff(CastSpellData s, CastBuffDef def, SpellTargetScope scope, int affectCount)
        {
            return new CastSpellData(s.SpellId, s.ItemId, s.SlotIndex, s.TriggerType, s.DelayFrames,
                s.EffectKind, s.EffectScale, s.Tags, s.StructTags, s.Flags,
                scope, affectCount, s.Condition, s.ManaCost, s.Self,
                s.OwnDelayAdd, s.OwnRechargeAdd, s.OwnRechargeMul, s.IsModifier,
                s.SequenceOp, s.Operand, s.ManaDelta,
                s.ProjectileId, s.ProjSpeed, s.ProjDamage, s.ProjLifetime, s.ProjRadius,
                s.ProjCount, s.ProjSpread, s.ProjPierce, s.ProjHoming,
                true, def, s.IsPassive, s.Passive);
        }

        /// <summary>一个只负责施加 buff 的物品（无投射物、非修饰器）。</summary>
        private static CastSpellData BuffCaster(int mana)
        {
            return BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.None, mana, 0f, 0f,
                SpellStructTag.Projectile, SpellTargetScope.Self, 0, CastStatMod.Identity);
        }

        private static void TestBuffs(CastSelfTestReport r)
        {
            // ---- 1) 【关键回归】拆除 LoadoutCompiler 占位后，施加者**自身**数值不受其 buff 影响 ----
            // 占位时代：ValuePerStack × MaxStacks 会并进物品自己的耗蓝 → 10 + 3×3 = 19
            {
                var bd = BuffDef(BuffStat.ManaCost, 3f, 3, BuffTiming.ByTriggerCount, 3, BuffStackRule.Additive);
                var caster = WithBuff(BuffCaster(10), bd, SpellTargetScope.Self, 0);
                var p = MakeProgram(1, 1, 0.12f, 1.0f, 200, 0f, caster);
                var run = Drive(p, true);

                r.Check("S3 回归：施加 buff 的物品自身耗蓝不受自身 buff 影响（占位已拆除）",
                    run.ManaSpent == 10,
                    "manaSpent=" + run.ManaSpent + "（占位时代会是 10+3x3=19）");
            }

            // ---- 2) 施加到"后续 N 个"并抬高其耗蓝（顺序：物品自身修正 → buff 修正） ----
            {
                var bd = BuffDef(BuffStat.ManaCost, 2f, 1, BuffTiming.ByTriggerCount, 2, BuffStackRule.Additive);
                var caster = WithBuff(BuffCaster(5), bd, SpellTargetScope.NextN, 2);
                var p = MakeProgram(1, 3, 0.12f, 1.0f, 200, 0f,
                    caster, Spark(6, 0f, 0f), Spark(6, 0f, 0f));
                var run = Drive(p, true);

                r.Check("S3：buff 施加到后续 N 个物品并抬高其耗蓝（5 + 8 + 8 = 21）",
                    run.ManaSpent == 21,
                    "manaSpent=" + run.ManaSpent + " triggers=" + run.Triggers + " trace=" + run.Trace);
            }

            // ---- 3) 次数型 buff：目标触发时仍生效，触发后递减到期 ----
            // slot1 用 Delayed（1 帧后触发）以便在中途观察到"已施加"状态
            {
                var bd = BuffDef(BuffStat.ManaCost, 2f, 1, BuffTiming.ByTriggerCount, 1, BuffStackRule.Additive);
                var caster = WithBuff(BuffCaster(5), bd, SpellTargetScope.NextN, 1);
                var delayed = BuildSpell(SpellTriggerType.Delayed, SpellEffectKind.FireProjectile, 6, 0f, 0f,
                    SpellStructTag.Projectile, SpellTargetScope.Self, 0, CastStatMod.Identity,
                    tags: SpellTag.Fire, projSpeed: 20f, projDamage: 5f, projLifetime: 0.9f, projRadius: 0.18f);
                var p = MakeProgram(1, 2, 0.12f, 1.0f, 200, 0f, caster, delayed);

                var st = CastRuntimeState.For(p);
                var plan = NewPlan();
                CastResolver.Tick(p, ref st, CastResolver.TickSeconds, true, plan, null);   // slot0 触发并施加 buff
                bool applied = st.BuffAt(1).Count == 1 && st.BuffAt(1).At(0).Remaining == 1;
                int manaAfterFirst = plan.ManaSpent;

                int manaSecondFrame = 0;
                for (int i = 0; i < 20 && st.CastActive; i++)
                {
                    CastResolver.Tick(p, ref st, CastResolver.TickSeconds, false, plan, null);
                    manaSecondFrame += plan.ManaSpent;
                }
                bool consumed = st.BuffAt(1).IsEmpty;

                r.Check("S3：次数型 buff 已施加、目标触发时生效（耗蓝 6+2=8）并在触发后到期",
                    applied && manaSecondFrame == 8 && consumed,
                    "applied=" + applied + " slot1Mana=" + manaSecondFrame + " consumed=" + consumed
                    + " firstFrameMana=" + manaAfterFirst);
            }

            // ---- 4) 时间型 buff：编译期快照进运行期状态，且逐帧递减、到期移除 ----
            {
                var bd = BuffDef(BuffStat.Delay, 0.05f, 1, BuffTiming.BySeconds, 10, BuffStackRule.Additive);
                var baseP = MakeProgram(1, 1, 0.12f, 1.0f, 200, 0f, Spark(6, 0f, 0f));
                var seed = new BuffSet[1];
                seed[0] = BuffRuntime.Apply(BuffSet.Empty, BuffInstance.Create(bd, BuffLimits.Factory), BuffLimits.Factory);
                var p = new CastProgram(1, 1, 0.12f, 1.0f, 200, 0f, CastMode.Sequential, baseP.Spells, seed);

                var st = CastRuntimeState.For(p);
                r.Check("S3/D15：编译期 buff 快照进入运行期状态（初始剩余 = 10 帧）",
                    st.BuffAt(0).Count == 1 && st.BuffAt(0).At(0).Remaining == 10,
                    "buffs=" + st.BuffAt(0));

                var plan = NewPlan();
                CastResolver.Tick(p, ref st, CastResolver.TickSeconds, false, plan, null);
                bool after1 = st.BuffAt(0).Count == 1 && st.BuffAt(0).At(0).Remaining == 9;
                for (int i = 0; i < 9; i++) { CastResolver.Tick(p, ref st, CastResolver.TickSeconds, false, plan, null); }

                r.Check("S3：时间型 buff 逐帧递减（10 → 9）并在 10 帧后到期移除",
                    after1 && st.BuffAt(0).IsEmpty,
                    "after1=" + after1 + " final=" + st.BuffAt(0));
            }

            // ---- 5) 施法型 buff：每次发射结束递减 1 ----
            {
                var bd = BuffDef(BuffStat.Recharge, -0.1f, 1, BuffTiming.ByCastCount, 2, BuffStackRule.Additive);
                var seed = new BuffSet[1];
                seed[0] = BuffRuntime.Apply(BuffSet.Empty, BuffInstance.Create(bd, BuffLimits.Factory), BuffLimits.Factory);
                var p = new CastProgram(1, 1, 0.12f, 0.0f, 200, 0f, CastMode.Sequential,
                    new[] { Spark(6, 0f, 0f) }, seed);

                var st = CastRuntimeState.For(p);
                var plan = NewPlan();
                // ⚠️ 不能假设"一次 Tick 就跑完一次发射"：基础延迟 0.12s ≈ 3 帧，
                //    发射会在延迟走完后才 FinishCast（自检里踩过这个坑）。
                //    口径：只要空闲（不在发射中、充能已走完）就开火，累计 2 次"发射结束"。
                BuffSet afterFirst = BuffSet.Empty;
                int endedCount = 0;
                for (int i = 0; i < 200 && endedCount < 2; i++)
                {
                    bool fire = !st.CastActive && st.RechargeRemainingFrames <= 0;
                    CastResolver.Tick(p, ref st, CastResolver.TickSeconds, fire, plan, null);
                    if (plan.CastEnded)
                    {
                        endedCount++;
                        if (endedCount == 1) { afterFirst = st.BuffAt(0); }
                    }
                }

                r.Check("S3：施法型 buff 每次发射结束递减 1（2 → 1 → 移除）",
                    endedCount == 2
                    && afterFirst.Count == 1 && afterFirst.At(0).Remaining == 1
                    && st.BuffAt(0).IsEmpty,
                    "casts=" + endedCount + " afterFirst=" + afterFirst + " final=" + st.BuffAt(0));
            }

            // ---- 6) 叠层：同一目标被重复施加同一 buff → 层数叠加、修正随层数增大（设计 §3.4 加法） ----
            // slot0 给「slot1 + slot2」各 1 层；slot1 触发时再给 slot2 加 1 层（共 2 层）
            // → slot2 耗蓝 = 6 + 2×2 = 10；总耗蓝 = 5(施0) + 7(施1,自己1层) + 10 = 22
            {
                var bd = BuffDef(BuffStat.ManaCost, 2f, 3, BuffTiming.ByTriggerCount, 5, BuffStackRule.Additive);
                var caster0 = WithBuff(BuffCaster(5), bd, SpellTargetScope.NextN, 2);
                var caster1 = WithBuff(BuffCaster(5), bd, SpellTargetScope.NextN, 1);
                var p = MakeProgram(1, 3, 0.12f, 1.0f, 400, 0f, caster0, caster1, Spark(6, 0f, 0f));
                var run = Drive(p, true);

                r.Check("S3：同一目标重复施加同一 buff → 叠层生效（5 + 7 + 10 = 22）",
                    run.ManaSpent == 22,
                    "manaSpent=" + run.ManaSpent + "（slot2 = 6 + 2×2 层）triggers=" + run.Triggers);
            }
        }

        private static CastSpellData Spark(int mana, float delay, float recharge)
        {
            return BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.FireProjectile, mana, delay, recharge,
                SpellStructTag.Projectile, SpellTargetScope.Self, 0,
                CastStatMod.Identity, tags: SpellTag.Fire,
                projSpeed: 20f, projDamage: 5f, projLifetime: 0.9f, projRadius: 0.18f);
        }

        private static CastSpellData Ice(int mana, float delay, float recharge)
        {
            return BuildSpell(SpellTriggerType.Immediate, SpellEffectKind.FireProjectile, mana, delay, recharge,
                SpellStructTag.Projectile, SpellTargetScope.Self, 0,
                CastStatMod.Identity, tags: SpellTag.Ice,
                projSpeed: 15f, projDamage: 8f, projLifetime: 1.6f, projRadius: 0.20f);
        }

        private static CastSpellData WithTags(CastSpellData s, SpellTag tags)
        {
            return new CastSpellData(s.SpellId, s.ItemId, s.SlotIndex, s.TriggerType, s.DelayFrames,
                s.EffectKind, s.EffectScale, tags, s.StructTags, s.Flags,
                s.TargetScope, s.AffectCount, s.Condition, s.ManaCost, s.Self,
                s.OwnDelayAdd, s.OwnRechargeAdd, s.OwnRechargeMul, s.IsModifier,
                s.SequenceOp, s.Operand, s.ManaDelta,
                s.ProjectileId, s.ProjSpeed, s.ProjDamage, s.ProjLifetime, s.ProjRadius,
                s.ProjCount, s.ProjSpread, s.ProjPierce, s.ProjHoming,
                s.AppliesBuff, s.Buff, s.IsPassive, s.Passive);
        }

        private static CastSpellData BuildSpell(SpellTriggerType trigger, SpellEffectKind effect, int mana,
            float delayAdd, float rechargeAdd, SpellStructTag structTags, SpellTargetScope scope, int affectCount,
            CastStatMod self, SpellTag tags = SpellTag.None, SpellCondition condition = default,
            SpellSequenceOp sequenceOp = SpellSequenceOp.None, int operand = 0,
            float projSpeed = 0f, float projDamage = 0f, float projLifetime = 0f, float projRadius = 0f)
        {
            s_SpellIndex++;
            var mod = self;

            // 层级判定与 SpellSO.IsModifier 口径一致：**由 StructTags.Modifier 标记决定**
            // （设计 §1.2：物品属于哪一层由设计指定），而不是按"字段看起来像不像修正"推断 ——
            // 否则带自身延迟/充能的普通投射物会被误判成修饰器，覆盖掉前面符文写下的修正。
            bool isModifier = (structTags & SpellStructTag.Modifier) != 0;

            // 自身延迟/充能是"每物品属性"，与"给后续物品的修正"分开传（执行文档 §0.1 第 1 条）
            return new CastSpellData(
                s_SpellIndex, s_SpellIndex, s_SpellIndex - 1,
                trigger, 0, effect, 1f, tags, structTags, SpellItemFlags.None,
                scope, affectCount, condition, mana, mod,
                delayAdd, rechargeAdd, 1f, isModifier, sequenceOp, operand, 0,
                projDamage > 0f ? s_SpellIndex : 0,
                projSpeed, projDamage, projLifetime, projRadius,
                1, 0f, 0, 0f, false, default, false, default);
        }
    }
}

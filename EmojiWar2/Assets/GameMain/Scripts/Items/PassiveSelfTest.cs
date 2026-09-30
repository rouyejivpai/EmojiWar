//------------------------------------------------------------
// EmojiWar GameMain - 被动触发自检（PassiveSelfTest，D25 / S4）
//
// 依据：doc/法术编程玩法设计文档.md §4（被动触发系统）
//         §4.1 主动=顺序执行 / 被动=事件驱动；§4.3 事件类型（Q5 集合）
//         §4.4 触发目标与顺序；§4.5 资源模式（后扣 + 限次）；§4.6 指针规则（临时指针 + 嵌套 ≤3）
//       doc/法术编程系统-执行文档.md §1.6 D25、§5 S4、流程 F/G
//   验收要求（执行文档 D25）：击杀→触发、序列清空→重放、事件共鸣、嵌套上限。
//
// **纯 C# 断言**（不依赖场景/资产），同一份套件被两条通道调用：
//   · 编辑器菜单 EmojiWar/Tools/Passive Self-Test → Editor/SpellDiagnostics.cs
//   · 构建版 -autopassive 启动参数               → AutoPlay.AutoPassiveSelfTestFlow
//
// ⚠️ 事件来源：施法类事件（施法开始/结束/序列清空）由 `CastResolver` **就地**触发；
//   命中/击杀由 `LockstepSimulation` 发布到 `CastEventBus`，本自检直接往总线发布来模拟战斗。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Simulation;
using UnityEngine;

namespace EmojiWar.GameMain.Items
{
    /// <summary>自检报告收集器（与 CastSelfTestReport 同形态）。</summary>
    public sealed class PassiveSelfTestReport
    {
        private readonly List<string> m_Lines = new List<string>();

        public int Pass { get; private set; }
        public int Fail { get; private set; }

        public void Check(string name, bool ok, string detail)
        {
            if (ok) { Pass++; } else { Fail++; }
            m_Lines.Add("[PassiveTest] " + (ok ? "PASS " : "FAIL ") + name + (detail == null ? "" : " - " + detail));
        }

        public void Note(string text) { m_Lines.Add("[PassiveTest]   " + text); }

        public string Text()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < m_Lines.Count; i++) { sb.Append(m_Lines[i]).Append('\n'); }
            sb.Append("[PassiveTest] passed=").Append(Pass).Append(" failed=").Append(Fail);
            return sb.ToString();
        }
    }

    /// <summary>被动触发自检（事件→触发 / 目标执行 / 限次 / 冷却 / 后扣 / 嵌套上限）。</summary>
    public static class PassiveSelfTest
    {
        private const int Session = 1;   // 自检固定用一个 SessionId

        /// <summary>跑全套断言，返回报告文本。</summary>
        public static string Run()
        {
            var r = new PassiveSelfTestReport();

            // ================================================================
            // 1. 击杀 → 触发后续 2 个（D25 要求）
            // ================================================================
            {
                var slots = new CastSpellData[3];
                slots[0] = AsPassive(Base(0, 0, SpellEffectKind.None), SpellPassiveEvent.Kill,
                    SpellTargetScope.NextN, 2, 0, 1, 1, 0);
                slots[1] = Base(1, 0, SpellEffectKind.FireProjectile);
                slots[2] = Base(2, 0, SpellEffectKind.FireProjectile);
                var p = Program(3, 100, slots);

                var bus = new CastEventBus();
                var st = CastRuntimeState.For(p);
                var plan = NewPlan();

                // 第 1 帧：只推进（不发射），确认无事件时被动不动
                Tick(p, ref st, plan, false, null, bus);
                bool idle = plan.Shots.Count == 0 && st.PassiveFires == 0;

                bus.PublishKill(Session, 1000, 9001);
                Tick(p, ref st, plan, false, null, bus);

                r.Check("D25：击杀事件 → 被动触发后续 2 个物品（产出 2 发）",
                    idle && st.PassiveFires == 1 && plan.Shots.Count == 2,
                    "idle=" + idle + " fires=" + st.PassiveFires + " shots=" + plan.Shots.Count);
                r.Check("D25：被动不占用主序列指针（触发后游标仍为 0、未进入发射）",
                    st.Cursor == 0 && !st.CastActive,
                    "cursor=" + st.Cursor + " castActive=" + st.CastActive);
            }

            // ================================================================
            // 2. 序列清空 → 重放本轮（D25 要求）
            // ================================================================
            {
                var slots = new CastSpellData[4];
                slots[0] = Base(0, 0, SpellEffectKind.SequenceOp, SpellSequenceOp.ClearSequence);
                slots[1] = AsPassive(Base(1, 0, SpellEffectKind.None), SpellPassiveEvent.SequenceCleared,
                    SpellTargetScope.WholeSequence, 0, 0, 1, 1, 0);
                slots[2] = Base(2, 0, SpellEffectKind.FireProjectile);
                slots[3] = Base(3, 0, SpellEffectKind.FireProjectile);
                var p = Program(4, 100, slots);

                var bus = new CastEventBus();
                var st = CastRuntimeState.For(p);
                var plan = NewPlan();
                Tick(p, ref st, plan, true, null, bus);   // 开火：slot0 清空序列 → 被动重放

                r.Check("D25：序列清空 → 被动重放本轮（产出 2 发 = slot2/slot3）",
                    st.PassiveFires == 1 && plan.Shots.Count == 2,
                    "fires=" + st.PassiveFires + " shots=" + plan.Shots.Count + " trace=" + plan.Trace);
            }

            // ================================================================
            // 3. 事件共鸣：同一事件触发多个被动，且互不串事件（D25 要求）
            // ================================================================
            {
                var slots = new CastSpellData[3];
                slots[0] = AsPassive(Base(0, 0, SpellEffectKind.FireProjectile), SpellPassiveEvent.Hit,
                    SpellTargetScope.Self, 0, 0, 1, 1, 0);
                slots[1] = AsPassive(Base(1, 0, SpellEffectKind.FireProjectile), SpellPassiveEvent.Hit,
                    SpellTargetScope.Self, 0, 0, 1, 1, 0);
                slots[2] = AsPassive(Base(2, 0, SpellEffectKind.FireProjectile), SpellPassiveEvent.Kill,
                    SpellTargetScope.Self, 0, 0, 1, 1, 0);
                var p = Program(3, 100, slots);

                var bus = new CastEventBus();
                var st = CastRuntimeState.For(p);
                var plan = NewPlan();
                bus.PublishHit(Session, 77, SpellTag.Fire, 9001, 5);
                Tick(p, ref st, plan, false, null, bus);

                r.Check("D25：事件共鸣 —— 一次命中触发 2 个监听 Hit 的被动（2 发）",
                    plan.Shots.Count == 2 && st.PassiveUsed[0] == 1 && st.PassiveUsed[1] == 1,
                    "shots=" + plan.Shots.Count + " used0=" + st.PassiveUsed[0] + " used1=" + st.PassiveUsed[1]);
                r.Check("D25：事件过滤 —— 监听 Kill 的被动对 Hit 无反应",
                    st.PassiveUsed.Length > 2 && st.PassiveUsed[2] == 0,
                    "used2=" + (st.PassiveUsed.Length > 2 ? st.PassiveUsed[2] : -1));
            }

            // ================================================================
            // 4. 嵌套上限（D25 要求）+ 上限来自配置（P11）
            // ================================================================
            {
                // 关键：**只有 slot0 监听 Kill**，slot1..3 监听一个永远不会发布的事件（Hit），
                // 因此它们只能通过"被动触发被动"的**嵌套**路径到达 —— 否则 FirePassives 会把
                // 所有同事件监听者**平级**触发（深度恒为 0），根本测不到嵌套。
                var slots = new CastSpellData[5];
                slots[0] = AsPassive(Base(0, 0, SpellEffectKind.None), SpellPassiveEvent.Kill,
                    SpellTargetScope.NextN, 1, 0, 1, 1, 0);
                for (int i = 1; i <= 3; i++)
                {
                    slots[i] = AsPassive(Base(i, 0, SpellEffectKind.None), SpellPassiveEvent.Hit,
                        SpellTargetScope.NextN, 1, 0, 1, 1, 0);
                }
                slots[4] = Base(4, 0, SpellEffectKind.FireProjectile);
                var p = Program(5, 100, slots);

                // (a) 默认上限 3 → slot0/1/2 触发，slot3（第 4 层）被拦
                var bus = new CastEventBus();
                var st = CastRuntimeState.For(p);
                var plan = NewPlan();
                bus.PublishKill(Session, 1000, 9001);
                Tick(p, ref st, plan, false, null, bus);

                r.Check("D25：嵌套上限=3 —— 被动链触发 3 层后停止（fires=3，第 4 层被拦）",
                    st.PassiveFires == 3 && st.PassiveUsed[3] == 0,
                    "fires=" + st.PassiveFires + " used3=" + st.PassiveUsed[3]);

                // (b) 配置把上限压到 1 → 只触发 1 层（证明上限来自 SpellSystemConfigSO）
                var cfg = ScriptableObject.CreateInstance<SpellSystemConfigSO>();
                cfg.MaxPassiveNesting = 1;
                var bus2 = new CastEventBus();
                var st2 = CastRuntimeState.For(p);
                var plan2 = NewPlan();
                bus2.PublishKill(Session, 1000, 9001);
                Tick(p, ref st2, plan2, false, cfg, bus2);

                r.Check("P11：嵌套上限来自配置（MaxPassiveNesting=1 → 只触发 1 层）",
                    st2.PassiveFires == 1 && st2.PassiveUsed[1] == 0,
                    "fires=" + st2.PassiveFires + " used1=" + st2.PassiveUsed[1]);
                Object.DestroyImmediate(cfg);
            }

            // ================================================================
            // 5. 后扣 + 限次（设计 §4.5）+ 冷却按帧（P6）
            // ================================================================
            {
                // (a) 后扣：蓝不足 → 该被动不触发（且不中止本次发射的一部分）
                var slots = new CastSpellData[1];
                slots[0] = AsPassive(Base(0, 0, SpellEffectKind.FireProjectile), SpellPassiveEvent.Hit,
                    SpellTargetScope.Self, 0, 5, 5, 1, 0);
                var p = Program(1, 8, slots);   // 魔力池 8：第一次花 5（余 3），第二次不够

                var bus = new CastEventBus();
                var st = CastRuntimeState.For(p);
                var plan = NewPlan();
                bus.PublishHit(Session, 1, SpellTag.Fire, 9001, 1);
                Tick(p, ref st, plan, false, null, bus);
                float afterFirst = st.Mana;
                int shotsAfterFirst = plan.Shots.Count;

                // 冷却 1 帧 → 推进 2 帧让它过期，再发第二次命中
                Tick(p, ref st, plan, false, null, bus);
                Tick(p, ref st, plan, false, null, bus);
                bus.PublishHit(Session, 2, SpellTag.Fire, 9001, 1);
                Tick(p, ref st, plan, false, null, bus);

                r.Check("设计 §4.5 后扣：蓝足则触发（8 → 3），蓝不足则跳过（firse 仍为 1）",
                    shotsAfterFirst == 1 && st.PassiveFires == 1 && afterFirst > 2.9f && afterFirst < 3.1f,
                    "firstShots=" + shotsAfterFirst + " fires=" + st.PassiveFires + " mana=" + afterFirst);

                // (b) 冷却按帧：冷却 3 帧内重复事件不重复触发
                var slots2 = new CastSpellData[1];
                slots2[0] = AsPassive(Base(0, 0, SpellEffectKind.FireProjectile), SpellPassiveEvent.Hit,
                    SpellTargetScope.Self, 0, 0, 5, 3, 0);
                var p2 = Program(1, 100, slots2);
                var bus2 = new CastEventBus();
                var st2 = CastRuntimeState.For(p2);
                var plan2 = NewPlan();
                for (int f = 0; f < 3; f++)
                {
                    bus2.PublishHit(Session, 10 + f, SpellTag.Fire, 9001, 1);
                    Tick(p2, ref st2, plan2, false, null, bus2);
                }
                r.Check("P6 冷却按帧：3 帧内连发 3 次命中 → 只触发 1 次",
                    st2.PassiveFires == 1,
                    "fires=" + st2.PassiveFires + " shots=" + plan2.Shots.Count);

                // 再推进 3 帧让冷却走完 → 又能触发
                for (int f = 0; f < 3; f++) { Tick(p2, ref st2, plan2, false, null, bus2); }
                bus2.PublishHit(Session, 20, SpellTag.Fire, 9001, 1);
                Tick(p2, ref st2, plan2, false, null, bus2);
                r.Check("P6 冷却按帧：冷却走完后可再次触发（累计 2 次）",
                    st2.PassiveFires == 2,
                    "fires=" + st2.PassiveFires);
            }

            // ================================================================
            // 6. 每次发射的次数上限重置（P6：次数每次发射重置）
            // ================================================================
            {
                var slots = new CastSpellData[2];
                slots[0] = Base(0, 0, SpellEffectKind.FireProjectile);
                slots[1] = AsPassive(Base(1, 0, SpellEffectKind.FireProjectile), SpellPassiveEvent.CastStart,
                    SpellTargetScope.Self, 0, 0, 1, 1, 0);
                var p = Program(2, 100, slots);

                var bus = new CastEventBus();
                var st = CastRuntimeState.For(p);
                var plan = NewPlan();

                Tick(p, ref st, plan, true, null, bus);     // 第 1 次发射 → CastStart 被动触发
                int afterFirstCast = st.PassiveUsed[1];

                // 等充能走完再开一次（基础延迟 0.1 + 充能 0.2）
                for (int i = 0; i < 12; i++) { Tick(p, ref st, plan, false, null, bus); }
                Tick(p, ref st, plan, true, null, bus);     // 第 2 次发射

                r.Check("P6：被动'每次发射次数'在每次发射开始时重置（两次发射各触发 1 次）",
                    afterFirstCast == 1 && st.PassiveUsed[1] == 1 && st.PassiveFires == 1,
                    "afterFirst=" + afterFirstCast + " now=" + st.PassiveUsed[1] + " firesThisCast=" + st.PassiveFires);
            }

            // ================================================================
            // 7. 总线隔离与确定性（实例化总线；会话过滤；FIFO）
            // ================================================================
            {
                var bus = new CastEventBus();
                bus.PublishKill(7, 1, 2);
                bus.PublishKill(9, 1, 3);
                bus.PublishKill(7, 1, 4);

                CastExternalEvent e;
                bool got1 = bus.TryDequeueForSession(7, out e);
                int firstTarget = e.TargetEntityId;
                bool got2 = bus.TryDequeueForSession(7, out e);
                int secondTarget = e.TargetEntityId;

                r.Check("总线：按 SessionId 过滤 + FIFO（会话 7 取到 2 与 4，顺序不乱）",
                    got1 && got2 && firstTarget == 2 && secondTarget == 4,
                    "t1=" + firstTarget + " t2=" + secondTarget + " remain=" + bus.Count);
                r.Check("总线：其它会话的事件不受影响（会话 9 的那条还在）",
                    bus.Count == 1,
                    "remain=" + bus.Count);

                bool other;
                CastExternalEvent e2;
                other = bus.TryDequeueForSession(9, out e2);
                r.Check("总线：隔离 —— 会话 9 能取回自己的事件",
                    other && e2.TargetEntityId == 3,
                    "ok=" + other + " target=" + (other ? e2.TargetEntityId : -1));

                // 两个实例互不干扰（回环对拍的关键前提）
                var busB = new CastEventBus();
                busB.PublishHit(1, 1, SpellTag.Fire, 5, 1);
                r.Check("总线：**实例隔离**（A 空、B 有 1 条 → 同进程双模拟不会互相偷事件）",
                    bus.Count == 0 && busB.Count == 1,
                    "a=" + bus.Count + " b=" + busB.Count);
            }

            return r.Text();
        }

        // ==================== 构造与驱动 helper ====================

        /// <summary>一条最小可用物品（投射物 / 序列操作 / 无效果）。</summary>
        private static CastSpellData Base(int slot, int mana, SpellEffectKind effect,
            SpellSequenceOp seqOp = SpellSequenceOp.None)
        {
            return new CastSpellData(
                slot + 1, slot + 1, slot, SpellTriggerType.Immediate, 0,
                effect, 1f, SpellTag.Fire, SpellStructTag.Projectile, SpellItemFlags.None,
                SpellTargetScope.Self, 0, default(SpellCondition),
                mana, CastStatMod.Identity, 0f, 0f, 1f, false,
                seqOp, 0, 0,
                slot + 1, 20f, 5f, 0.9f, 0.18f, 1, 0f, 0, 0f,
                false, default(CastBuffDef), false, default(CastPassiveDef));
        }

        /// <summary>把一条物品改造成"被动物品"。</summary>
        private static CastSpellData AsPassive(CastSpellData s, SpellPassiveEvent ev, SpellTargetScope scope,
            int affectCount, int manaCost, int limitPerCast, int cooldownFrames, int tagFilter)
        {
            CastSpellData d = new CastSpellData(
                s.SpellId, s.ItemId, s.SlotIndex, s.TriggerType, s.DelayFrames,
                s.EffectKind, s.EffectScale, s.Tags, s.StructTags, s.Flags,
                s.TargetScope, s.AffectCount, s.Condition, s.ManaCost, s.Self,
                s.OwnDelayAdd, s.OwnRechargeAdd, s.OwnRechargeMul, s.IsModifier,
                s.SequenceOp, s.Operand, s.ManaDelta,
                s.ProjectileId, s.ProjSpeed, s.ProjDamage, s.ProjLifetime, s.ProjRadius,
                s.ProjCount, s.ProjSpread, s.ProjPierce, s.ProjHoming,
                s.AppliesBuff, s.Buff, true,
                MakePassiveDef(ev, scope, affectCount, manaCost, limitPerCast, cooldownFrames));

            // 标签过滤走 Condition.RequiredTags（TagGroup 范围用）
            SpellCondition cond = s.Condition;
            cond.RequiredTags = (SpellTag)tagFilter;
            d = new CastSpellData(
                d.SpellId, d.ItemId, d.SlotIndex, d.TriggerType, d.DelayFrames,
                d.EffectKind, d.EffectScale, d.Tags, d.StructTags, d.Flags,
                d.TargetScope, d.AffectCount, cond, d.ManaCost, d.Self,
                d.OwnDelayAdd, d.OwnRechargeAdd, d.OwnRechargeMul, d.IsModifier,
                d.SequenceOp, d.Operand, d.ManaDelta,
                d.ProjectileId, d.ProjSpeed, d.ProjDamage, d.ProjLifetime, d.ProjRadius,
                d.ProjCount, d.ProjSpread, d.ProjPierce, d.ProjHoming,
                d.AppliesBuff, d.Buff, d.IsPassive, d.Passive);
            return d;
        }

        private static CastPassiveDef MakePassiveDef(SpellPassiveEvent ev, SpellTargetScope scope, int affectCount,
            int manaCost, int limitPerCast, int cooldownFrames)
        {
            CastPassiveDef p = new CastPassiveDef();
            p.Event = ev;
            p.Scope = scope;
            p.AffectCount = affectCount;
            p.ManaCost = manaCost;
            p.LimitPerCast = limitPerCast;
            p.CooldownFrames = cooldownFrames;
            p.Order = SpellTriggerOrder.Sequential;
            p.IgnoreRecharge = true;   // 自检不关心充能，避免干扰断言
            return p;
        }

        private static CastProgram Program(int slots, int manaMax, CastSpellData[] spells)
        {
            return new CastProgram(1, slots, 0.1f, 0.2f, manaMax, 0f, CastMode.Sequential, spells, null);
        }

        private static CastPlan NewPlan() { return new CastPlan { Trace = new StringBuilder() }; }

        private static void Tick(in CastProgram p, ref CastRuntimeState st, CastPlan plan, bool fire,
            SpellSystemConfigSO config, CastEventBus bus)
        {
            CastResolver.Tick(p, ref st, CastResolver.TickSeconds, fire, plan, config, bus, Session);
        }
    }
}

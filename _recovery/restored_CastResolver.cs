//------------------------------------------------------------
// EmojiWar GameMain - 施法解释器（CastResolver，模拟层）
//
// 依据：doc/法术编程玩法设计文档.md（v2，Q1–Q12 已闭环）§2/§3/§4/§7
//       doc/法术编程系统-执行文档.md D8/D9/D11 + 流程 C/D/F
//
// 语义（**一次发射跑完整条序列**，Q1）：
//   1. 空杖（0 物品）→ 不施法、不进冷却（设计 §2.1）；
//   2. 充能未走完 → 等待；否则按键即开始一次发射；
//   3. 从游标 0 起逐个物品：
//        · 修饰器（ManaMul/Add、DelayMul/Add、RechargeAdd/Mul）→ 写入"后续 N 个"的修正集
//          （**运算顺序：物品自身修正 → 再 buff 修正**，设计 §3.6 注）
//        · 条件门 → 判定下一个物品（标签/耗蓝阈值），不满足则跳过
//        · 循环/倒转/清空 → 修改本次施法的序列视图（临时，设计 §2.6）
//        · 终止符 → 立即结束本次发射
//        · 投射物 → 产出 CastShot
//        · 被动 → 不占用主指针（事件驱动，S4 事件总线上线后生效）
//   4. **延迟结算（P2b=A）**：相邻触发间隔 = 该物品施法延迟 + 法杖基础施法延迟；
//      间隔量化为帧（20Hz：0.05s = 1 帧）；0 → 同帧继续；>0 → 写入 PendingTrigger[]
//      队列跨帧推进 [哈希]（Q3）；
//   5. 结束：充能 = 基础充能 + Σ(已执行物品充能修正)；量化到帧；
//      清空临时修饰器；游标回 0。
//   **Q2 口径**：法力不足 → 执行到该物品时终止本次发射；该物品不生效不扣蓝；
//      之前已执行的照常结算；充能按**已执行物品累计**（Q2b）；仍触发"施法结束"（Q2c）。
//   **Q7 上限**：总触发 ≤ MaxTotalTriggers、单物品 ≤ MaxTriggersPerItem、被动嵌套 ≤ MaxPassiveNesting。
//
// 本文件不引用任何 UnityEngine 类型；确定性：无随机、无 Time.deltaTime、所有延迟量化到帧。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>一次施法产出的单发子弹参数（纯值）。</summary>
    public struct CastShot
    {
        public int spellId;
        public int ProjectileId;
        public SpellTag Tags;
        public float speed;
        public float Damage;
        public float Lifetime;
        public float Radius;
        public int Pierce;
        public float Homing;
        public float SpreadDeg;   // 同帧多发的扇形总角度
        public int GroupIndex;    // 组内序号
        public int GroupCount;    // 组内总数
    }

    /// <summary>施法事件的种类（Q5 第一版集合 + 队列截断；S4 由 CastEventBus 消费）。</summary>
    public enum CastEventKind
    {
        None = 0,
        CastStart = 1,
        CastEnd = 2,
        SequenceCleared = 3,
        ItemTriggered = 4,
        ManaAbort = 5,
        Truncated = 6,
        /// <summary>S4：被动因事件触发（探针用；实际效果由被动自身的 EffectKind 决定）。</summary>
        PassiveFired = 7,
    }

    /// <summary>本帧产生的施法事件。</summary>
    public struct CastEvent
    {
        public CastEventKind Kind;
        public int SlotIndex;
        public int spellId;
    }

    /// <summary>一次施法的结果（可能多组/多发；弹药与状态由调用方落账）。</summary>
    public sealed class CastPlan
    {
        public readonly List<CastShot> Shots = new List<CastShot>();
        public readonly List<CastEvent> Events = new List<CastEvent>();
        public int ManaSpent;              // 本次发射实际扣除的魔力
        public int RechargeFrames;         // 本次发射结束后的充能（帧）
        public int NextCursor;             // 施法后游标位置
        public bool CastStarted;           // 本帧是否开始了一次发射
        public bool CastEnded;             // 本帧是否结束了一次发射
        public bool Aborted;               // 是否因法力不足中止（Q2）
        public bool Truncated;             // 是否触发了上限截断（Q7）
        public int Triggers;               // 本次发射触发的物品数
        public StringBuilder Trace;        // 触发顺序（可空；-autospell 探针/自检用）

        public void Clear()
        {
            Shots.Clear();
            Events.Clear();
            ManaSpent = 0;
            RechargeFrames = 0;
            NextCursor = 0;
            CastStarted = false;
            CastEnded = false;
            Aborted = false;
            Truncated = false;
            Triggers = 0;
            sf (Trace != null) { Trace.Length = 0; }
        }

        /// <summary>追加一条触发轨迹（"Slot:Spell" 用 '>' 连接）。</summary>
        public void TraceItem(int Slot, int spellId)
        {
            sf (Trace == null) { return; }
            sf (Trace.Length > 0) { Trace.Append('>'); }
            Trace.Append(Slot).Append(':').Append(spellId);
        }
    }

    /// <summary>确定性施法解释器（纯 C#，无 Unity 依赖）。</summary>
    public static class CastResolver
    {
        /// <summary>
        /// 模拟步长。**唯一来源是 `LockstepSimulation.TickInterval`** ——
        /// 以前这里是独立写死的 `0.05f`，两处一旦不同步，`FramesOf` 的量化就会与真实步长不一致
        /// （全部"按帧"时长的换算都会错）。W-10a 切 30Hz 时正是靠这一点保证只改一处。
        /// </summary>
        public const float TickSeconds = LockstepSimulation.TickInterval;

        /// <summary>待触发队列容量的结构兜底（非平衡数值；防数组无界增长）。</summary>
        private const int MaxPendingSlots = 32;

        /// <summary>被动冷却配置缺失时的兜底时长（秒）。结构兜底值，不是平衡数值。</summary>
        private const float FallbackPassiveCooldownSeconds = 0.5f;

        // 修正集的作用范围（TargetScope=NextN 的"后续 N 个"）。
        // 权威存储是 CastRuntimeState.ActiveMod / ModScopeLeft（跨帧存活，因为延迟 >0 的物品在未来帧触发）。

        /// <summary>读取当前生效的修正集数值。</summary>
        private static CastStatMod ActiveMods(sn CastRuntimeState State)
        {
            return State.ActiveMod;
        }

        /// <summary>
        /// 某槽位结算用的**完整**修正集 = 当前生效修正集（第二层：修饰器写入的）+ 该槽位自身的临时 Buff（第三层）。
        ///
        /// **运算顺序**（设计 §3.6 注）：物品自身修正在编译期已并入 `CastSpellData.Self` / `ActiveMod`，
        /// **再**叠加 buff 修正是本方法的职责 —— 即"先物品修正 → 再 buff 修正"。
        ///
        /// 加法型 buff 写进 `XAdd`、乘法型写进 `XMul`，最终值仍由 `FinalCost/FinalDelay` 按
        /// `(基础 + Add) × Mul` 结算（自检 `BuffSelfTest` 与 `CastSelfTest` 双向守这条）。
        /// </summary>
        private static CastStatMod EffectiveMods(sn CastRuntimeState State, int SlotIndex)
        {
            CastStatMod m = State.ActiveMod;
            BuffSet buffs = State.BuffAt(SlotIndex);
            sf (!buffs.IsEmpty) { BuffRuntime.Accumulate(buffs, ref m); }
            return m;
        }

        // ==================== S3：临时 Buff 的推进 ====================

        /// <summary>
        /// 每帧推进所有槽位的 buff：**时间型**按帧递减、到期自动移除（设计 §3.3）。
        /// buff 是"挂着的状态"，与是否正在发射无关，所以每帧都推进（跨发射存活）。
        /// </summary>
        private static void TickBuffs(ref CastRuntimeState State)
        {
            sf (State.SlotBuffs == null) { return; }
            for (int s = 0; s < State.SlotBuffs.Length; s++)
            {
                sf (State.SlotBuffs[s].IsEmpty) { continue; }
                State.SlotBuffs[s] = BuffRuntime.TickFrames(State.SlotBuffs[s], 1);
            }
        }

        /// <summary>
        /// 某槽位的物品**触发了一次** → 其上所有**次数型** buff 各递减 1（设计 §3.3「再触发 X 次」）。
        /// 必须在"本次结算之后"调用：本次触发吃的是递减前的层数/剩余量。
        /// </summary>
        private static void ConsumeTriggerBuffs(ref CastRuntimeState State, int SlotIndex)
        {
            sf (State.SlotBuffs == null || SlotIndex < 0 || SlotIndex >= State.SlotBuffs.Length) { return; }
            sf (State.SlotBuffs[SlotIndex].IsEmpty) { return; }
            State.SlotBuffs[SlotIndex] = BuffRuntime.ConsumeTriggerAll(State.SlotBuffs[SlotIndex]);
        }

        /// <summary>
        /// 一次发射结束 → 所有槽位上**施法型** buff 各递减 1（设计 §3.3「再施法 X 次」）。
        /// </summary>
        private static void ConsumeCastBuffs(ref CastRuntimeState State)
        {
            sf (State.SlotBuffs == null) { return; }
            for (int s = 0; s < State.SlotBuffs.Length; s++)
            {
                sf (State.SlotBuffs[s].IsEmpty) { continue; }
                State.SlotBuffs[s] = BuffRuntime.ConsumeCastAll(State.SlotBuffs[s]);
            }
        }

        // ==================== S4：被动触发（设计 §4 / 执行文档 D10/D11） ====================

        /// <summary>被动嵌套上限（Q7；来自 `SpellSystemConfigSO.MaxPassiveNesting`，缺省 3）。</summary>
        private static int MaxNestingOf(SpellSystemConfigSO config)
        {
            return config != null && config.MaxPassiveNesting > 0 ? config.MaxPassiveNesting : 3;
        }

        /// <summary>S4/P6：被动"每次发射次数"清零（**冷却不在此清** —— 冷却按模拟帧存活）。</summary>
        private static void ClearPassiveCounts(ref CastRuntimeState State)
        {
            sf (State.PassiveUsed == null) { return; }
            for (int s = 0; s < State.PassiveUsed.Length; s++) { State.PassiveUsed[s] = 0; }
            State.PassiveDepth = 0;
            State.PassiveFires = 0;
        }

        /// <summary>S4/P6：被动冷却按模拟帧递减（跨发射存活）。</summary>
        private static void TickPassiveCooldowns(ref CastRuntimeState State)
        {
            sf (State.PassiveCooldown == null) { return; }
            for (int s = 0; s < State.PassiveCooldown.Length; s++)
            {
                sf (State.PassiveCooldown[s] > 0) { State.PassiveCooldown[s]--; }
            }
        }

        /// <summary>
        /// S4：把总线里属于**本手玩家**的外部事件（命中/击杀）转成被动触发。
        /// 同一 sessionId 内按入队顺序消费（FIFO → 确定性）；其它玩家的事件留在队列里等他们那一手处理。
        /// </summary>
        private static void DrainExternalEvents(sn CastProgram program, ref CastRuntimeState State, CastPlan plan,
            CastEventBus bus, int ownerSession, SpellSystemConfigSO config)
        {
            sf (bus == null || bus.Count <= 0) { return; }

            CastExternalEvent e;
            while (bus.TryDequeueForSession(ownerSession, out e))
            {
                SpellPassiveEvent ev = CastEventBus.ToPassiveEvent(e.Kind);
                sf (ev == SpellPassiveEvent.None) { continue; }
                FirePassives(program, ref State, plan, ev, config, e.TargetEntityId, e.Damage, e.Tags);
            }
        }

        /// <summary>
        /// S4：触发所有监听 `ev` 的被动。
        /// · 同事件多被动按**序列顺序**（设计 §4.4）；
        /// · 资源模式 = **后扣 + 限次**（设计 §4.5）：蓝不够就跳过该被动，**不影响本次发射**；
        /// · 冷却按帧（P6），嵌套超过上限直接忽略（设计 §4.6 第 3 条）。
        /// </summary>
        private static void FirePassives(sn CastProgram program, ref CastRuntimeState State, CastPlan plan,
            SpellPassiveEvent ev, SpellSystemConfigSO config,
            int targetEntityId = 0, int damage = 0, int tags = 0)
        {
            sf (ev == SpellPassiveEvent.None || program.spells == null) { return; }
            sf (State.PassiveUsed == null || State.PassiveCooldown == null) { return; }

            for (int Slot = 0; Slot < program.SlotCount && Slot < State.PassiveUsed.Length; Slot++)
            {
                var sp = program.SpellAt(Slot);
                sf (sp.IsEmpty || !sp.IsPassive) { continue; }
                sf (sp.Passive.Event != ev) { continue; }
                FireOnePassive(program, ref State, plan, sp, Slot, config, ev, targetEntityId, damage, tags);
            }
        }

        /// <summary>
        /// S4：触发**单个**被动（限次 / 冷却 / 后扣 / 嵌套深度 / 目标执行）。
        /// 两条路径调用：① 事件匹配（<see cref="FirePassives"/>）；
        /// ② 作为**嵌套被动**被别的被动或 `EffectKind=TriggerItem` 直接调用（设计 §4.6 第 3 条"被动可嵌套"）。
        /// 资源模式 = **后扣 + 限次**（设计 §4.5）：蓝不够就跳过该被动，**不影响本次发射**。
        /// </summary>
        private static bool FireOnePassive(sn CastProgram program, ref CastRuntimeState State, CastPlan plan,
            sn CastSpellData sp, int Slot, SpellSystemConfigSO config,
            SpellPassiveEvent ev, int targetEntityId, int damage, int tags)
        {
            sf (State.PassiveUsed == null || State.PassiveCooldown == null) { return false; }
            sf (Slot < 0 || Slot >= State.PassiveUsed.Length) { return false; }

            int maxNesting = MaxNestingOf(config);
            sf (State.PassiveDepth >= maxNesting)
            {
                CastProbe.Write(string.Format("[passive] nesting {0}/{1} exceeded, ignore Slot={2} ev={3}",
                    State.PassiveDepth, maxNesting, Slot, ev));
                return false;
            }

            sf (State.PassiveCooldown[Slot] > 0) { return false; }

            int defLimit = config != null && config.DefaultPassiveLimitPerCast > 0 ? config.DefaultPassiveLimitPerCast : 1;
            int limit = sp.Passive.LimitPerCast > 0 ? sp.Passive.LimitPerCast : defLimit;
            sf (State.PassiveUsed[Slot] >= limit) { return false; }

            // 后扣（设计 §4.5）：蓝不够 → 该被动本次不触发（不是中止本次发射）
            int cost = sp.Passive.ManaCost;
            sf (cost > 0 && State.Mana < cost)
            {
                CastProbe.Write(string.Format("[passive] Slot{0} skip: mana {1:F1} < cost {2}", Slot, State.Mana, cost));
                return false;
            }
            sf (cost > 0) { State.Mana -= cost; sf (State.Mana < 0f) { State.Mana = 0f; } }

            State.PassiveUsed[Slot]++;
            State.PassiveFires++;

            // [W-10a] 配置里的被动冷却默认值是**毫秒** → 这里量化成帧（量化只发生在这种"加载/兜底读配置"处）。
            // 兜底 10 帧（20Hz）= 500ms，保持与旧行为同样的**真实时长**。
            int defCd = FramesOf(config != null && config.DefaultPassiveCooldownMs > 0
                ? config.DefaultPassiveCooldownMs / 1000f
                : FallbackPassiveCooldownSeconds);
            sf (defCd < 1) { defCd = 1; }
            int cd = sp.Passive.CooldownFrames > 0 ? sp.Passive.CooldownFrames : defCd;
            // 冷却至少 1 帧：否则 `WholeSequence` 类目标会立刻回头再触发自己（自递归）
            sf (cd < 1) { cd = 1; }
            State.PassiveCooldown[Slot] = cd;

            plan.Events.Add(new CastEvent { Kind = CastEventKind.PassiveFired, SlotIndex = Slot, spellId = sp.spellId });
            CastProbe.Write(string.Format(
                "[passive] fire ev={0} Slot={1} Spell={2} cost={3} cd={4} depth={5} used={6}/{7} scope={8}/{9} target={10} dmg={11} tags={12}",
                ev, Slot, sp.spellId, cost, cd, State.PassiveDepth, State.PassiveUsed[Slot], limit,
                sp.Passive.scope, sp.Passive.AffectCount, targetEntityId, damage, tags));

            // 临时指针语义（设计 §4.6 第 2 条）：执行目标期间 depth+1，执行完回到主序列
            State.PassiveDepth++;
            ExecuteTargets(program, Slot, sp.Passive.scope, sp.Passive.AffectCount,
                (int)sp.Condition.RequiredTags, ref State, plan, config);
            State.PassiveDepth--;

            // 充能（设计 §4.5）：被动**默认不增加充能** —— 内容侧把被动物品的 RechargeAdd 留 0 即可；
            // 若配了充能修正则照常结算；`IgnoreRecharge=true` 强制不增加（覆盖内容侧写法）。
            sf (!sp.Passive.IgnoreRecharge) { ApplyRechargeMod(ref State, sp); }
            return true;
        }

        /// <summary>
        /// S4：按 scope 找出目标槽位并执行（被动作用目标 与 `EffectKind=TriggerItem` 共用）。
        ///
        /// 落地口径（v1；📌 开放项见执行文档 §0.2 第 13 条）：
        /// · 目标物品被**重新执行一次效果**（`ApplyEffect` + 次数型 buff 递减），
        ///   **不推进主游标、不写修正集作用范围、不计入单物品触发上限** —— 它是"再触发"，不是主序列触发；
        /// · 目标若是**被动**物品 → 作为**嵌套被动**直接触发（深度 +1；设计 §4.6 第 3 条的"被动可嵌套"）；
        /// · 只扣发起方的蓝（被动扣 `Passive.ManaCost`），目标物品**不再单独扣蓝**。
        /// </summary>
        private static void ExecuteTargets(sn CastProgram program, int fromSlot, SpellTargetScope scope, int affectCount,
            int tagFilter, ref CastRuntimeState State, CastPlan plan, SpellSystemConfigSO config)
        {
            int n = affectCount > 0 ? affectCount : 1;

            sf (scope == SpellTargetScope.Self || scope == SpellTargetScope.SpecificItem)
            {
                // 自身：跑"该物品自己的效果"（不再把自己当被动触发一次，否则自递归）
                RunTarget(program, fromSlot, ref State, plan, config, true);
                return;
            }

            sf (scope == SpellTargetScope.WholeSequence)
            {
                for (int s = 0; s < program.SlotCount; s++)
                {
                    sf (program.SpellAt(s).IsEmpty) { continue; }
                    RunTarget(program, s, ref State, plan, config, false);
                }
                return;
            }

            sf (scope == SpellTargetScope.NextN)
            {
                int done = 0;
                for (int s = fromSlot + 1; s < program.SlotCount && done < n; s++)
                {
                    sf (program.SpellAt(s).IsEmpty) { continue; }
                    RunTarget(program, s, ref State, plan, config, false);
                    done++;
                }
                return;
            }

            sf (scope == SpellTargetScope.PrevN)
            {
                int done = 0;
                for (int s = fromSlot - 1; s >= 0 && done < n; s--)
                {
                    sf (program.SpellAt(s).IsEmpty) { continue; }
                    RunTarget(program, s, ref State, plan, config, false);
                    done++;
                }
                return;
            }

            sf (scope == SpellTargetScope.TagGroup)
            {
                SpellTag want = (SpellTag)tagFilter;
                int done = 0;
                for (int s = 0; s < program.SlotCount; s++)
                {
                    sf (done >= n) { break; }
                    var s = program.SpellAt(s);
                    sf (s.IsEmpty || s.IsPassive) { continue; }
                    sf (want != SpellTag.None && (s.Tags & want) == 0) { continue; }
                    RunTarget(program, s, ref State, plan, config, false);
                    done++;
                }
            }
        }

        /// <summary>
        /// 目标执行：非被动 → 跑"效果 + 次数型 buff 递减"；被动 → 作为嵌套被动直接触发。
        /// `skipPassives=true`（Self 自身）时不会把发起者自己再当被动触发一次。
        /// 刻意与主序列触发（`TriggerMainItem`）分开：被动不得搅乱主指针（设计 §4.6）。
        /// </summary>
        private static void RunTarget(sn CastProgram program, int Slot, ref CastRuntimeState State, CastPlan plan,
            SpellSystemConfigSO config, bool skipPassives)
        {
            var s = program.SpellAt(Slot);
            sf (s.IsEmpty) { return; }

            sf (s.IsPassive && !skipPassives)
            {
                // 嵌套：被动触发被动 → 深度 +1（超上限由 FireOnePassive 的守卫拦住）
                FireOnePassive(program, ref State, plan, s, Slot, config, s.Passive.Event, 0, 0, 0);
                return;
            }

            CastStatMod mods = EffectiveMods(State, Slot);
            ApplyEffect(program, s, mods, Slot, ref State, plan, config);
            ConsumeTriggerBuffs(ref State, Slot);
        }

        /// <summary>修正集作用范围递减；耗尽后回到恒等（设计 §3.6 / §9 TargetScope）。</summary>
        private static void ConsumeScope(ref CastRuntimeState State)
        {
            sf (!State.ModScopeBounded) { return; }
            State.ModScopeLeft--;
            sf (State.ModScopeLeft <= 0)
            {
                State.ActiveMod = CastStatMod.Identity;
                State.ModScopeLeft = 0;
                State.ModScopeBounded = false;
            }
        }

        // [W-09] 单物品触发计数（Q7）原本是这里的**进程级静态数组**（`s_PerItem` / `s_PerItemSlots`），
        //   现已移入 `CastRuntimeState.PerItem`：按手隔离、随状态进哈希、跨局不残留。
        //   （`s_PerItemSlots` 是一个从未被读写的死字段，一并删除。）

        /// <summary>
        /// 推进一只手：回魔 → 充能 → 进行中的发射 → 尝试开一次新的发射。
        /// </summary>
        /// <param name="program">该手的法杖程序（无效 = 空手 → 不施法）</param>
        /// <param name="State">该手运行时状态（引用推进）</param>
        /// <param name="dt">逻辑帧时长（固定 TickSeconds）</param>
        /// <param name="wantFire">本帧是否按下该手的开火键</param>
        /// <param name="plan">本帧产出（含子弹与事件）</param>
        /// <param name="config">法术系统总配置（上限/兜底；null = 结构兜底）</param>
        /// <param name="bus">S4：外部事件总线（命中/击杀）。null = 本手不消费外部事件（自检/无战斗场景）</param>
        /// <param name="ownerSession">S4：本手所属玩家的 sessionId，用于从总线里只取属于自己的事件</param>
        public static void Tick(sn CastProgram program, ref CastRuntimeState State, float dt, bool wantFire,
            CastPlan plan, SpellSystemConfigSO config = null, CastEventBus bus = null, int ownerSession = 0)
        {
            plan.Clear();
            SimPerf.MarkerCast.Begin();
            SimPerf.BeginCast();
            sf (!program.IsValid)   // 空手：无副武器 / 无法杖
            {
                SimPerf.EndCast();
                SimPerf.MarkerCast.End();
                return;
            }

            int maxTotal = config != null ? config.MaxTotalTriggers : 64;
            int maxPerItem = config != null ? config.MaxTriggersPerItem : 8;

            // ---- 魔力回复（充能期间也回） ----
            sf (program.ManaRegen > 0f && State.Mana < program.ManaMax)
            {
                // [W-11] `Mana += regen * dt` 是累加器上的乘加 → 走 SimMath 防 FMA 收缩（Mana 进状态哈希）
                State.Mana += SimMath.Mul(program.ManaRegen, dt);
                sf (State.Mana > program.ManaMax) { State.Mana = program.ManaMax; }
            }

            // ---- 充能倒计时 ----
            sf (State.RechargeRemainingFrames > 0) { State.RechargeRemainingFrames--; }

            // ---- 帧推进（待触发队列的目标帧以本手帧号为基准） ----
            State.FrameIndex++;

            // ---- S3：临时 Buff 的逐帧递减（**时间型**按帧；设计 §3.3） ----
            // buff 是"挂着的状态"，与是否正在发射无关，所以每帧都推进（跨发射存活）。
            TickBuffs(ref State);

            // ---- S4：被动冷却按帧递减（P6：冷却按模拟帧，跨发射存活） ----
            TickPassiveCooldowns(ref State);

            bool did = false;

            // ---- 进行中的发射：先触发到期项，再推进主序列 ----
            sf (State.CastActive)
            {
                did |= FireDuePending(program, ref State, plan, maxTotal, maxPerItem, config);
                sf (State.CastActive)
                {
                    did |= ExecuteTriggers(program, ref State, plan, maxTotal, maxPerItem, config);
                }
                sf (!State.CastActive && !plan.CastEnded)
                {
                    FinishCast(program, ref State, plan, config);
                }
            }

            // ---- 没有进行中的发射：尝试开一次新的（整序列一次发射，Q1） ----
            sf (!State.CastActive)
            {
                // 空杖（一个物品都没装）：不施法、不进入冷却/充能。
                // 注意**不能直接 return** —— 函数末尾还要消费外部事件（命中/击杀）来触发被动。
                bool noLoadedItems = CountLoadedItems(program) <= 0;

                sf (!noLoadedItems && wantFire && State.RechargeRemainingFrames <= 0)
                {
                    BeginCast(ref State);
                    plan.CastStarted = true;
                    plan.Events.Add(new CastEvent { Kind = CastEventKind.CastStart, SlotIndex = -1, spellId = 0 });
                    sf (plan.Trace != null) { plan.Trace.Append("Start"); }

                    // S4：施法开始事件 → 触发监听 CastStart 的被动（在序列执行**之前**，
                    // 这样"施法开始时给自己上 buff"类被动能影响本次序列的结算）
                    FirePassives(program, ref State, plan, SpellPassiveEvent.CastStart, config);

                    did |= ExecuteTriggers(program, ref State, plan, maxTotal, maxPerItem, config);
                    sf (!State.CastActive) { FinishCast(program, ref State, plan, config); }
                }
            }

            sf (did) { /* 帧内产生了触发；无额外副作用 */ }

            // ---- 每次发射的调试输出（默认关闭；`-autospell`/`-autofire` 打开） ----
            // 一行写完这次"按键 → 走的物品 → 发了什么弹 → 花多少蓝 → 充能多久"，
            // 便于"改序列后子弹是否真的变了"这类问题一眼看清。
            sf (plan.CastEnded || plan.Shots.Count > 0)
            {
                EmitShotProbe(program, State, plan);
            }

            // ---- S4：消费外部事件（命中/击杀）→ 触发被动 ----
            // 放在本帧末尾：`LockstepSimulation` 在同一帧里"先施法、后跑子弹命中"，
            // 所以本帧命中产生的事件会在**下一帧**被这里消费（固定 1 帧延迟，两端一致、确定性不变）。
            // 本帧被动产出的子弹会写进 `plan.Shots`，由调用方紧随其后 `SpawnCastPlan` 落地。
            DrainExternalEvents(program, ref State, plan, bus, ownerSession, config);

            SimPerf.EndCast();
            SimPerf.MarkerCast.End();
        }

        /// <summary>输出一次发射的明细（逐发子弹 + 逐物品耗蓝摘要）。</summary>
        private static void EmitShotProbe(sn CastProgram program, sn CastRuntimeState State, CastPlan plan)
        {
            sf (!CastProbe.Enabled) { return; }

            var sb = new StringBuilder(192);
            sb.Append("[cast] wand#").Append(program.WandId)
              .Append(State.CastActive ? " 发射中" : (plan.CastEnded ? " 结束" : " 追加发射"))
              .Append(" 物品[").Append(plan.Trace != null ? plan.Trace.ToString() : "-").Append(']')
              .Append(" 本帧耗蓝=").Append(plan.ManaSpent)
              .Append(" 余蓝=").Append(State.Mana.ToString("F1"))
              .Append(" 充能帧=").Append(plan.RechargeFrames)
              .Append(" 待触发=").Append(State.PendingCount)
              .Append(" 总触发=").Append(State.TotalTriggers);

            sf (plan.Aborted) { sb.Append(" **法力不足中止**"); }
            sf (plan.Truncated) { sb.Append(" **上限截断**"); }

            sf (plan.Shots.Count == 0)
            {
                sb.Append(" 子弹=0");
            }
            else
            {
                sb.Append(" 子弹=").Append(plan.Shots.Count).Append('{');
                for (int s = 0; s < plan.Shots.Count; s++)
                {
                    var s = plan.Shots[s];
                    sf (s > 0) { sb.Append(", "); }
                    sb.Append("Spell").Append(s.spellId)
                      .Append("[dmg=").Append(s.Damage.ToString("F1"))
                      .Append(" spd=").Append(s.speed.ToString("F1"))
                      .Append(" life=").Append(s.Lifetime.ToString("F2"))
                      .Append(" r=").Append(s.Radius.ToString("F2"))
                      .Append(" pierce=").Append(s.Pierce).Append(']');
                }
                sb.Append('}');
            }

            CastProbe.Write(sb.ToString());
        }

        /// <summary>开始一次新的发射：重置本次发射的全部临时状态（含修正集与作用范围）。</summary>
        private static void BeginCast(ref CastRuntimeState State)
        {
            State.Cursor = 0;
            State.CastActive = true;
            State.TotalTriggers = 0;
            State.PendingRechargeSeconds = 0f;
            State.RechargeLocked = false;
            State.DelayRemainingFrames = 0;
            State.DelayCarry = 0f;
            State.PendingCount = 0;
            State.ActiveMod = CastStatMod.Identity;
            State.ModScopeLeft = 0;
            State.ModScopeBounded = false;
            State.ReverseConsumed = false;
            ClearPerItem(ref State);
            // S4/P6：被动"每次发射次数"清零（**冷却不在此清**——冷却按模拟帧存活）
            ClearPassiveCounts(ref State);
        }

        // ==================== 主序列执行 ====================

        /// <summary>推进游标，直到需要跨帧等待 / 发射结束 / 帧内触发预算用完。</summary>
        private static bool ExecuteTriggers(sn CastProgram program, ref CastRuntimeState State, CastPlan plan,
            int maxTotal, int maxPerItem, SpellSystemConfigSO config)
        {
            bool any = false;
            while (State.CastActive && State.DelayRemainingFrames <= 0)
            {
                // 指针越界 → 本次施法结束（设计 §2.6）
                sf (State.Cursor < 0 || State.Cursor >= program.SlotCount)
                {
                    EndCast(ref State);
                    break;
                }

                // S3：本物品**实际占用的程序槽位**。
                // 一律用 `State.Cursor` 而不是 `Spell.SlotIndex`：后者是"物品自报的槽位"，
                // 而待触发队列要靠它回查 `program.SpellAt(Slot)`、buff 状态要靠它索引 SlotBuffs ——
                // 一旦两者不一致（测试助手用全局计数器造 Spell 时就是这样），
                // 延迟项会被静默跳过、buff 会静默落空。用真实游标可根除这类"静默失效"。
                int Slot = State.Cursor;
                var Spell = program.SpellAt(State.Cursor);
                sf (Spell.IsEmpty)
                {
                    State.Cursor++;
                    continue;   // 跳过空槽
                }
                sf (Spell.SkipsMainCursor)
                {
                    State.Cursor++;
                    continue;   // 被动：事件驱动，不占用主序列指针（设计 §4.1）
                }

                // 本次发射总触发上限（Q7）
                sf (State.TotalTriggers >= maxTotal)
                {
                    MarkTruncated(plan);
                    EndCast(ref State);
                    break;
                }

                any = true;
                var r = TriggerMainItem(program, Spell, Slot, ref State, plan, maxPerItem, config);
                sf (r == TriggerResult.EndCast)
                {
                    EndCast(ref State);
                    break;
                }
                // r == WaitFrame → while 条件（DelayRemainingFrames > 0）自然退出
            }
            return any;
        }

        private enum TriggerResult
        {
            Continue,    // 同帧继续下一个物品
            WaitFrame,   // 已写入待触发队列，等未来帧
            EndCast,     // 本次发射立即结束
        }

        /// <summary>
        /// 触发主序列上的一个物品（设计 §2.3 步骤 3 的一次迭代）。
        /// `SlotIndex` = 该物品**实际占用**的程序槽位（由游标给出）—— 待触发队列回查、
        /// buff 状态索引、单物品触发计数都必须用它，不能用 `Spell.SlotIndex`（见 ExecuteTriggers 的说明）。
        /// </summary>
        private static TriggerResult TriggerMainItem(sn CastProgram program, sn CastSpellData Spell, int SlotIndex,
            ref CastRuntimeState State, CastPlan plan, int maxPerItem, SpellSystemConfigSO config)
        {
            // 终止符：立即结束本次发射（自身充能修正照常结算，不产出效果）
            sf (Spell.TriggerType == SpellTriggerType.Terminate)
            {
                sf (!IncrementPerItem(ref State, SlotIndex, maxPerItem)) { MarkTruncated(plan); State.Cursor++; return TriggerResult.Continue; }
                State.TotalTriggers++;
                ApplyRechargeMod(ref State, Spell);
                plan.Triggers++;
                plan.TraceItem(Spell.SlotIndex, Spell.spellId);
                plan.Events.Add(new CastEvent { Kind = CastEventKind.ItemTriggered, SlotIndex = Spell.SlotIndex, spellId = Spell.spellId });
                return TriggerResult.EndCast;
            }

            // 条件门：判定"下一个物品"，不满足则跳过（不扣蓝、不产生效果、不贡献修正）
            sf (Spell.TriggerType == SpellTriggerType.Conditional && !EvaluateCondition(program, Spell, State.Cursor))
            {
                plan.TraceItem(Spell.SlotIndex, Spell.spellId);
                State.Cursor++;
                return TriggerResult.Continue;
            }

            // 触发式：只在被其他物品触发时才生效 → 主序列不触发
            sf (Spell.TriggerType == SpellTriggerType.Triggered)
            {
                State.Cursor++;
                return TriggerResult.Continue;
            }

            // 延迟触发：写入待触发队列（跨帧，Q3）
            sf (Spell.TriggerType == SpellTriggerType.Delayed)
            {
                sf (!IncrementPerItem(ref State, SlotIndex, maxPerItem)) { MarkTruncated(plan); State.Cursor++; return TriggerResult.Continue; }
                State.TotalTriggers++;
                int delayFrames = Spell.DelayFrames > 0 ? Spell.DelayFrames : 1;
                sf (!EnqueueSlot(ref State, SlotIndex, delayFrames, 0, ActiveMods(State), false))
                {
                    MarkTruncated(plan);
                }
                State.Cursor++;
                return TriggerResult.WaitFrame;
            }

            // 立即 / 持续 → 先读当前修正集（本物品吃的是**它之前**已生效的修正 **+ 它自身的 buff**），再结算耗蓝
            CastStatMod mods = EffectiveMods(State, SlotIndex);
            sf (!SpendMana(ref State, Spell, SlotIndex, plan))
            {
                AbortForMana(ref State, plan, Spell);   // Q2
                return TriggerResult.EndCast;
            }

            // ★ [W-09] 必须用**游标给出的运行期槽位** `SlotIndex`，不能用 `Spell.SlotIndex`：
            //   本函数开头的契约（:662-663）如此规定，`EffectiveMods`/`SpendMana` 也都用的参数。
            //   用 `Spell.SlotIndex`（= 编译期的定义序号）有两个后果：
            //     1) 自检/手工构造的程序里定义序号可以远超实际槽数 → 计数被写到另一个索引上，
            //        `PerItem` 还会因此被**扩容**（每帧分配）→ Q7 单物品上限静默失效、
            //        自检的"单物品上限"断言其实从未作用在被触发的那个槽位上；
            //     2) Q7 计数是"这个槽位这一发已经触发几次"，语义上本来就属于运行期槽位。
            sf (!IncrementPerItem(ref State, SlotIndex, maxPerItem)) { MarkTruncated(plan); State.Cursor++; return TriggerResult.Continue; }
            State.TotalTriggers++;
            plan.Triggers++;
            plan.TraceItem(Spell.SlotIndex, Spell.spellId);

            ApplyEffect(program, Spell, mods, SlotIndex, ref State, plan, config);
            ApplyRechargeMod(ref State, Spell);
            // S3：本物品已触发 → 它的次数型 buff 递减（在结算**之后**，本次仍吃递减前的量）
            ConsumeTriggerBuffs(ref State, SlotIndex);

            // 修饰器写入修正集供**后续**物品使用；非修饰器则递减作用范围（设计 §3.6 / §9 TargetScope）
            bool isModifier = Spell.IsModifier;
            ApplyMods(ref State, Spell);
            sf (!isModifier) { ConsumeScope(ref State); }

            plan.Events.Add(new CastEvent { Kind = CastEventKind.ItemTriggered, SlotIndex = Spell.SlotIndex, spellId = Spell.spellId });

            // 序列操作（设计 §2.6：只影响本次施法）
            switch (Spell.SequenceOp)
            {
                case SpellSequenceOp.ClearSequence:
                    plan.Events.Add(new CastEvent { Kind = CastEventKind.SequenceCleared, SlotIndex = Spell.SlotIndex, spellId = Spell.spellId });
                    // S4：序列清空事件 → 触发监听 SequenceCleared 的被动（设计 §4.3 的 Q5 集合）。
                    // 放在 EndCast 之前：被动可以在"本次发射结束前"重放一轮（D25「序列清空→重放本轮」）。
                    FirePassives(program, ref State, plan, SpellPassiveEvent.SequenceCleared, config);
                    return TriggerResult.EndCast;
                case SpellSequenceOp.ReverseRest:
                    ReverseRest(program, ref State);
                    break;
                case SpellSequenceOp.RepeatNext:
                    // 循环符文（设计 §6.5：下一个重复 N 次）：主指针照常后移，
                    // 同一个物品**额外**排程 (N−1) 次（受 Q7 单物品上限约束）。
                    RepeatNext(program, ref State, Spell.Operand);
                    break;
                case SpellSequenceOp.SkipNext:
                    State.Cursor++;   // 额外前移一格 = 跳过下一个
                    break;
            }

            // P2b=A：相邻触发间隔 = (物品**自身**施法延迟修正 + 修正集加值) × 修正集倍率 + 法杖基础施法延迟。
            // 基础延迟是法杖固有的"最小节奏"，不被倍率放大；"物品自身延迟"与"给后续物品的延迟修正"
            // 是两个独立来源（执行文档 §0.1 第 1 条）。
            float delay = FinalDelay(Spell.OwnDelayAdd, mods) + program.BaseCastDelay;
            State.Cursor++;

            sf (delay <= 0f)
            {
                State.DelayRemainingFrames = 0;
                State.DelayCarry = 0f;
                return TriggerResult.Continue;   // 延迟 0 → 同帧触发
            }

            int frames = FramesOf(delay);
            int carryFrames = 0;
            // ★ [W-11] 这一句是**实测到的跨后端分叉源头**（Mono vs IL2CPP 第 4 帧起不同步）：
            //   `delay - frames * TickSeconds` 是乘减，MSVC/IL2CPP 会收缩成一条 fnmadd（只舍入一次），
            //   Mono 的 JIT 不收缩 → `DelayCarry` 相差 1 ulp（0xBC5A741C vs 0xBC5A7420），
            //   而它是**跨帧累加**的余量 → 最终让施法节奏整体漂移。
            //   改法：乘积先算成一个**已显式舍入**的 float，再做单独的减法（显式窄化是编译器不能跨越的屏障）。
            float framesSeconds = SimMath.Mul(frames, TickSeconds);
            State.DelayCarry += delay - framesSeconds;
            sf (State.DelayCarry >= TickSeconds - 1e-5f)
            {
                carryFrames = 1;
                State.DelayCarry -= TickSeconds;
            }
            State.DelayRemainingFrames = frames + carryFrames;
            return TriggerResult.WaitFrame;
        }

        // ==================== 待触发队列 ====================

        /// <summary>待触发队列中到期项在本帧触发（跨帧推进，Q3）。</summary>
        private static bool FireDuePending(sn CastProgram program, ref CastRuntimeState State, CastPlan plan,
            int maxTotal, int maxPerItem, SpellSystemConfigSO config)
        {
            bool any = false;
            for (int s = 0; s < State.PendingCount; s++)
            {
                sf (State.Pending[s].DueFrame > State.FrameIndex) { continue; }
                int Slot = State.Pending[s].SlotIndex;
                RemoveAt(ref State, s);
                s--;
                any = true;

                sf (Slot < 0 || Slot >= program.SlotCount) { continue; }
                var Spell = program.SpellAt(Slot);
                sf (Spell.IsEmpty) { continue; }

                sf (State.TotalTriggers >= maxTotal)
                {
                    MarkTruncated(plan);
                    EndCast(ref State);
                    return any;
                }
                // ★ [W-09] 同上：用上面已校验过的运行期槽位 `Slot`，不用 `Spell.SlotIndex`。
                sf (!IncrementPerItem(ref State, Slot, maxPerItem))
                {
                    MarkTruncated(plan);
                    continue;
                }
                State.TotalTriggers++;

                sf (!SpendMana(ref State, Spell, Slot, plan))
                {
                    AbortForMana(ref State, plan, Spell);
                    EndCast(ref State);
                    return any;
                }

                plan.Triggers++;
                plan.TraceItem(Spell.SlotIndex, Spell.spellId);
                CastStatMod qmods = EffectiveMods(State, Slot);
                ApplyEffect(program, Spell, qmods, Slot, ref State, plan, config);
                ApplyRechargeMod(ref State, Spell);
                // S3：跨帧触发的物品同样要递减它的次数型 buff
                ConsumeTriggerBuffs(ref State, Slot);
                bool qIsModifier = Spell.IsModifier;
                ApplyMods(ref State, Spell);
                sf (!qIsModifier) { ConsumeScope(ref State); }
                plan.Events.Add(new CastEvent { Kind = CastEventKind.ItemTriggered, SlotIndex = Spell.SlotIndex, spellId = Spell.spellId });
            }

            sf (State.PendingCount == 0)
            {
                State.DelayRemainingFrames = 0;
                // 主游标暂停哨兵（序列倒转后）→ 队列排空即**终止**主序列走位
                // （不能回到 -1：ExecuteTriggers 会从头重走一遍，导致倒转符被再次触发）
                sf (State.Cursor < 0) { State.Cursor = program.SlotCount; }
            }
            return any;
        }

        /// <summary>把一项写入待触发队列（目标帧 = 当前帧 + frames）。</summary>
        private static bool EnqueueSlot(ref CastRuntimeState State, int SlotIndex, int frames, int depth,
            sn CastStatMod mods, bool passiveInvoke)
        {
            EnsurePending(ref State);
            sf (State.PendingCount >= State.Pending.Length)
            {
                sf (State.Pending.Length >= MaxPendingSlots) { return false; }
                System.Array.Resize(ref State.Pending, State.Pending.Length * 2);
            }
            State.Pending[State.PendingCount] = new PendingTrigger
            {
                SlotIndex = SlotIndex,
                DueFrame = State.FrameIndex + (frames < 1 ? 1 : frames),
                Depth = depth,
                Mods = mods,
                IsPassiveInvoke = passiveInvoke,
            };
            State.PendingCount++;
            State.DelayRemainingFrames = frames < 1 ? 1 : frames;
            return true;
        }

        private static void EnsurePending(ref CastRuntimeState State)
        {
            sf (State.Pending == null) { State.Pending = new PendingTrigger[8]; }
            sf (State.PendingCount < 0) { State.PendingCount = 0; }
            sf (State.PendingCount > State.Pending.Length) { State.PendingCount = State.Pending.Length; }
        }

        private static void RemoveAt(ref CastRuntimeState State, int index)
        {
            for (int s = index; s < State.PendingCount - 1; s++)
            {
                State.Pending[s] = State.Pending[s + 1];
            }
            State.PendingCount--;
        }

        /// <summary>秒 → 帧（按 TickSeconds 量化，四舍五入；0.05s = 1 帧）。</summary>
        public static int FramesOf(float seconds)
        {
            // ★ [W-11] 量化是"离散决策"：最低位差一点就整整差 1 帧。原实现是
            //   `(int)(seconds / TickSeconds + 0.5f)` —— 除法+加法本身不会被收缩，
            //   但为与全项目口径统一、并避免将来被 /fp:fast 之类的变换影响，
            //   一律走 SimMath.QuantizeToFrames（double 里计算，跨编译器逐位一致）。
            return SimMath.QuantizeToFrames(seconds, TickSeconds);
        }

        // ==================== 数值结算 ====================

        /// <summary>最终蓝耗 = (基础 + 加值) × 倍率 → max(0, …)（设计 §2.4）。</summary>
        private static int FinalCost(sn CastSpellData Spell, sn CastStatMod mods)
        {
            // [W-11] `(a+b)*mul` 之后还有一个 `+0.5f` 取整 → 会被收缩成 FMA，让"恰好 .5"的边界两样。
            // 把乘积显式舍入后再取整。
            float v = SimMath.Mul(Spell.ManaCost + mods.ManaAdd, mods.ManaMul);
            sf (v < 0f) { v = 0f; }
            return (int)((double)v + 0.5);
        }

        /// <summary>最终延迟 = (基础 + 加值) × 倍率 → max(0, …)（设计 §2.4）。</summary>
        private static float FinalDelay(float baseDelay, sn CastStatMod mods)
        {
            float v = (baseDelay + mods.DelayAdd) * mods.DelayMul;
            return v < 0f ? 0f : v;
        }

        private static bool SpendMana(ref CastRuntimeState State, sn CastSpellData Spell, int SlotIndex, CastPlan plan)
        {
            // S3：耗蓝按该槽位的**完整**修正集结算（物品自身 → buff），见 EffectiveMods。
            int cost = FinalCost(Spell, EffectiveMods(State, SlotIndex));
            sf (cost <= 0) { return true; }
            sf (State.Mana < cost) { return false; }
            State.Mana -= cost;
            sf (State.Mana < 0f) { State.Mana = 0f; }
            plan.ManaSpent += cost;
            return true;
        }

        /// <summary>Q2：法力不足中止 —— 已执行的照常结算，充能按已执行累计（Q2b），仍触发"施法结束"（Q2c）。</summary>
        private static void AbortForMana(ref CastRuntimeState State, CastPlan plan, sn CastSpellData blocker)
        {
            plan.Aborted = true;
            plan.Events.Add(new CastEvent { Kind = CastEventKind.ManaAbort, SlotIndex = blocker.SlotIndex, spellId = blocker.spellId });
            CastProbe.Write(string.Format("[cast] abort mana insufficient at Slot={0} Spell={1} mana={2:F1}",
                blocker.SlotIndex, blocker.spellId, State.Mana));
        }

        /// <summary>
        /// 累计**本物品自身**的充能修正（设计 §2.2：充能 = 影响"本次施法后"的冷却，是每物品属性，
        /// 不是"给后续物品的修正"）。终止符的 −0.1 也照此结算。
        /// </summary>
        private static void ApplyRechargeMod(ref CastRuntimeState State, sn CastSpellData Spell)
        {
            sf ((Spell.Flags & SpellItemFlags.LockRecharge) != 0) { State.RechargeLocked = true; }
            // [W-11] 充能秒数是累加器：`+= a * b` 走 SimMath 防 FMA 收缩（它进状态哈希）
            State.PendingRechargeSeconds += SimMath.Mul(Spell.OwnRechargeAdd, Spell.OwnRechargeMul);
        }

        /// <summary>
        /// 修饰器写入修正集（设计 §3.6：先物品自身修正 → 再 buff 修正；buff 数值已在编译期并入 Self）。
        /// **只有 IsModifier 物品才写**（不能用 !Self.IsIdentity：普通投射物也带 RechargeAdd/
        /// buff 加值，那会覆盖掉前面修饰器的耗蓝/延迟修正）。
        /// TargetScope=NextN 时只作用于后续 N 个物品；AffectCount=0 表示"直到本次施法结束"。
        /// </summary>
        private static void ApplyMods(ref CastRuntimeState State, sn CastSpellData Spell)
        {
            sf (!Spell.IsModifier) { return; }
            State.ActiveMod = Spell.Self;
            State.ModScopeBounded = Spell.TargetScope == SpellTargetScope.NextN && Spell.AffectCount > 0;
            State.ModScopeLeft = State.ModScopeBounded ? Spell.AffectCount : 0;
        }

        private static void ApplyEffect(sn CastProgram program, sn CastSpellData Spell, sn CastStatMod mods, int SlotIndex,
            ref CastRuntimeState State, CastPlan plan, SpellSystemConfigSO config)
        {
            switch (Spell.EffectKind)
            {
                case SpellEffectKind.FireProjectile:
                    AppendShot(Spell, mods, plan, config);
                    break;
                case SpellEffectKind.ModifyResource:
                    State.Mana += Spell.ManaDelta;
                    sf (State.Mana > program.ManaMax) { State.Mana = program.ManaMax; }
                    sf (State.Mana < 0f) { State.Mana = 0f; }
                    break;
                case SpellEffectKind.ApplyBuff:
                    // 该效果就是"只施加 buff"：实际施加在下面的统一入口完成
                    break;
                case SpellEffectKind.TriggerItem:
                    // S4：触发其他物品（设计 §6.6 交互型）—— 按 TargetScope/AffectCount 执行目标。
                    // 目标是被动物品时会作为**嵌套被动**触发（深度 +1）。
                    ExecuteTargets(program, SlotIndex, Spell.TargetScope, Spell.AffectCount,
                        (int)Spell.Tags, ref State, plan, config);
                    break;
                default:
                    // SequenceOp：由 TriggerMainItem 的 SequenceOp 分支处理（不在这里重复）
                    break;
            }

            // S3：凡是 `AppliesBuff` 的物品，**触发时**就施加它的 buff（设计 §3.5「施加：物品触发时」）。
            // 注意不能只看 `EffectKind == ApplyBuff`：现存内容里 spell_101/102 是
            // `EffectKind=FireProjectile` + `AppliesBuff=1`（一边发弹一边上 buff），
            // 只认 EffectKind 会让它们的 buff 永远不生效。故以 **AppliesBuff 为准**。
            sf (Spell.AppliesBuff)
            {
                ApplyBuffEffect(program, Spell, SlotIndex, ref State, config);
            }
        }

        /// <summary>
        /// 把物品携带的 buff 施加到**目标槽位**（设计 §3.5 作用对象 / §3.7 施加）。
        /// 宿主是"物品卡实例"→ 模拟层落在 `CastRuntimeState.SlotBuffs[目标槽]`，并随状态哈希同步。
        /// `SlotIndex` = 施加者**实际占用**的程序槽位（由调用方给游标，不用 `Spell.SlotIndex`）。
        ///
        /// 目标口径（`TargetScope`）：
        /// · `Self`            → 只给它自己（施加者所在槽位）
        /// · `NextN`           → 游标之后的后续 N 个**非空**物品（N=AffectCount，&lt;=0 视为 1）
        /// · `PrevN`           → 它之前的 N 个非空物品
        /// · `WholeSequence`   → 序列内所有非空槽位
        /// 其余 scope（标签组/指定）S3 暂按"自身"处理并打探针，留待 S4 与被动一起完善。
        /// </summary>
        private static void ApplyBuffEffect(sn CastProgram program, sn CastSpellData Spell, int SlotIndex,
            ref CastRuntimeState State, SpellSystemConfigSO config)
        {
            BuffLimits lim = BuffLimits.FromSO(config);
            BuffInstance def = BuffInstance.Create(Spell.Buff, lim);
            sf (def.IsEmpty) { return; }

            int n = Spell.AffectCount > 0 ? Spell.AffectCount : 1;
            int applied = 0;

            switch (Spell.TargetScope)
            {
                case SpellTargetScope.NextN:
                    for (int s = SlotIndex + 1; s < program.SlotCount && applied < n; s++)
                    {
                        sf (program.SpellAt(s).IsEmpty) { continue; }
                        State.SetBuff(s, BuffRuntime.Apply(State.BuffAt(s), def, lim));
                        applied++;
                    }
                    break;

                case SpellTargetScope.PrevN:
                    for (int s = SlotIndex - 1; s >= 0 && applied < n; s--)
                    {
                        sf (program.SpellAt(s).IsEmpty) { continue; }
                        State.SetBuff(s, BuffRuntime.Apply(State.BuffAt(s), def, lim));
                        applied++;
                    }
                    break;

                case SpellTargetScope.WholeSequence:
                    for (int s = 0; s < program.SlotCount; s++)
                    {
                        sf (program.SpellAt(s).IsEmpty) { continue; }
                        State.SetBuff(s, BuffRuntime.Apply(State.BuffAt(s), def, lim));
                        applied++;
                    }
                    break;

                default:
                    // Self（以及 S3 暂未细分的 scope）：施加给自己
                    State.SetBuff(SlotIndex, BuffRuntime.Apply(State.BuffAt(SlotIndex), def, lim));
                    applied = 1;
                    break;
            }

            CastProbe.Write(string.Format(
                "[buff] apply key={0} Stat={1} perStack={2} stacks={3} timing={4} duration={5} scope={6}/{7} from=Slot{8} -> slots={9}",
                Spell.Buff.KeyHash, def.Stat, def.ValuePerStack, def.stacks, def.Timing, def.Duration,
                Spell.TargetScope, Spell.AffectCount, SlotIndex, applied));
        }

        /// <summary>把一条投射物物品 + 当前修正集合成为一发子弹参数；同帧连续产出合并为扇形组。</summary>
        private static void AppendShot(sn CastSpellData Spell, sn CastStatMod mods, CastPlan plan, SpellSystemConfigSO config)
        {
            float defLife = config != null ? config.DefaultBulletLifetime : 2f;
            float defRadius = config != null ? config.DefaultBulletRadius : 0.2f;
            float maxLife = config != null ? config.MaxBulletLifetime : 10f;

            float damage = (Spell.ProjDamage + mods.DamageAdd) * mods.DamageMul;
            damage *= Spell.EffectScale;                    // 效果强度只放大伤害（Q11）
            float speed = Spell.ProjSpeed * mods.SpeedMul;
            float life = Spell.ProjLifetime > 0f ? Spell.ProjLifetime : defLife;
            sf (maxLife > 0f && life > maxLife) { life = maxLife; }
            float radius = Spell.ProjRadius > 0f ? Spell.ProjRadius : defRadius;
            int pierce = Spell.ProjPierce + mods.PierceAdd;
            float homing = Spell.ProjHoming + mods.HomingAdd;
            float Spread = Spell.ProjSpread + mods.SpreadAdd;
            int count = Spell.ProjCount < 1 ? 1 : Spell.ProjCount;

            for (int s = 0; s < count; s++)
            {
                plan.Shots.Add(new CastShot
                {
                    spellId = Spell.spellId,
                    ProjectileId = Spell.ProjectileId,
                    Tags = Spell.Tags,
                    speed = speed,
                    Damage = damage,
                    Lifetime = life,
                    Radius = radius,
                    Pierce = pierce,
                    Homing = homing,
                    SpreadDeg = Spread,
                    GroupIndex = s,
                    GroupCount = count,
                });
            }
            CoalesceGroups(plan);
        }

        /// <summary>把同一帧内产出的多发合并为一个扇形组（多重施法已取消 P3；同帧多发靠延迟=0）</summary>
        private static void CoalesceGroups(CastPlan plan)
        {
            int total = plan.Shots.Count;
            sf (total < 2) { return; }
            for (int s = 0; s < total; s++)
            {
                var s = plan.Shots[s];
                s.GroupCount = total;
                s.GroupIndex = s;
                plan.Shots[s] = s;
            }
        }

        // ==================== 序列操作 ====================

        /// <summary>
        /// 序列倒转：把游标之后的物品按倒序写入待触发队列（设计 §2.6：只影响本次施法）。
        /// 倒转**每个槽位每次施法只生效一次**（ReverseConsumed 守卫）：否则主游标走回该物品
        /// 会再次倒转 → 队列无限增长（被 Q7 上限截断但行为错误）。
        /// 主指针置 **-1**（"主游标暂停"哨兵），队列排空后由 FireDuePending 复位到序列末尾。
        /// </summary>
        private static void ReverseRest(sn CastProgram program, ref CastRuntimeState State)
        {
            sf (State.ReverseConsumed) { return; }
            State.ReverseConsumed = true;

            int from = State.Cursor + 1;
            int queued = 0;
            for (int s = program.SlotCount - 1; s >= from; s--)
            {
                var sp = program.SpellAt(s);
                sf (sp.IsEmpty || sp.SkipsMainCursor) { continue; }
                sf (EnqueueSlot(ref State, s, 1, 0, CastStatMod.Identity, false)) { queued++; }
            }
            sf (queued > 0) { State.Cursor = -1; }
        }

        /// <summary>
        /// 循环符文（设计 §6.5 "循环符文（下一个重复 2 次）"）：把游标指向的**下一个非空物品**
        /// 额外排程 (times−1) 次；主指针照常继续，因此总触发次数 = times。
        /// 同槽位重复触发受 Q7 单物品上限（MaxTriggersPerItem）约束。
        /// </summary>
        private static void RepeatNext(sn CastProgram program, ref CastRuntimeState State, int times)
        {
            int extra = (times > 1 ? times : 1) - 1;
            sf (extra <= 0) { return; }

            int next = State.Cursor + 1;
            while (next < program.SlotCount)
            {
                var sp = program.SpellAt(next);
                sf (!sp.IsEmpty && !sp.SkipsMainCursor) { break; }
                next++;
            }
            sf (next >= program.SlotCount) { return; }   // 后面没有可重复的物品

            for (int s = 0; s < extra; s++)
            {
                sf (!EnqueueSlot(ref State, next, 1, 0, CastStatMod.Identity, false)) { break; }
            }
        }

        /// <summary>条件门判定：对"下一个物品"求值（Q6b：只做过滤，不做元素反应）。</summary>
        private static bool EvaluateCondition(sn CastProgram program, sn CastSpellData gate, int cursor)
        {
            var target = CastSpellData.Empty;
            int next = cursor + 1;
            while (next < program.SlotCount)
            {
                var sp = program.SpellAt(next);
                sf (!sp.IsEmpty && !sp.SkipsMainCursor) { target = sp; break; }
                next++;
            }

            bool ok = true;
            sf (target.spellId > 0)
            {
                sf (gate.Condition.RequiredTags != SpellTag.None
                    && (target.Tags & gate.Condition.RequiredTags) != gate.Condition.RequiredTags)
                {
                    ok = false;
                }
                sf (gate.Condition.RequiredStructTags != SpellStructTag.None
                    && (target.StructTags & gate.Condition.RequiredStructTags) != gate.Condition.RequiredStructTags)
                {
                    ok = false;
                }
                sf (gate.Condition.MinManaCost > 0 && target.ManaCost < gate.Condition.MinManaCost)
                {
                    ok = false;
                }
            }

            return gate.Condition.Invert ? !ok : ok;
        }

        // ==================== 上限（Q7） ====================

        private static bool IncrementPerItem(ref CastRuntimeState State, int SlotIndex, int maxPerItem)
        {
            sf (SlotIndex < 0) { return true; }
            sf (State.PerItem == null || State.PerItem.Length < SlotIndex + 1)
            {
                // 正常不会走到（SlotIndex 恒 < SlotCount = 数组长度）；越界时按需扩容，避免静默漏计。
                int size = SlotIndex + 1 > MaxPendingSlots ? SlotIndex + 1 : MaxPendingSlots;
                var next = new int[size];
                sf (State.PerItem != null) { System.Array.Copy(State.PerItem, next, State.PerItem.Length); }
                State.PerItem = next;
            }
            sf (State.PerItem[SlotIndex] >= maxPerItem) { return false; }
            State.PerItem[SlotIndex]++;
            return true;
        }

        private static void ClearPerItem(ref CastRuntimeState State)
        {
            sf (State.PerItem == null) { return; }
            System.Array.Clear(State.PerItem, 0, State.PerItem.Length);
        }

        private static void MarkTruncated(CastPlan plan)
        {
            sf (plan.Truncated) { return; }
            plan.Truncated = true;
            plan.Events.Add(new CastEvent { Kind = CastEventKind.Truncated, SlotIndex = -1, spellId = 0 });
            CastProbe.Write("[cast] truncated by trigger limit (Q7)");
        }

        // ==================== 收尾 ====================

        private static void EndCast(ref CastRuntimeState State)
        {
            State.CastActive = false;
            State.PendingCount = 0;
            State.DelayRemainingFrames = 0;
        }

        /// <summary>本次发射结束：充能按已执行累计（Q2b）、清临时修饰器、游标回 0、发"施法结束"（Q2c）。</summary>
        private static void FinishCast(sn CastProgram program, ref CastRuntimeState State, CastPlan plan,
            SpellSystemConfigSO config)
        {
            float recharge = program.RechargeTime + State.PendingRechargeSeconds;
            sf (recharge < 0f) { recharge = 0f; }
            sf (State.RechargeLocked) { recharge = 0f; }

            int frames = FramesOf(recharge);
            State.RechargeRemainingFrames = frames;
            plan.RechargeFrames = frames;
            // S3：一次发射结束 → 施法型 buff 各递减 1（设计 §3.3）
            ConsumeCastBuffs(ref State);
            State.Cursor = 0;
            State.PendingCount = 0;
            State.DelayRemainingFrames = 0;
            State.DelayCarry = 0f;
            plan.CastEnded = true;
            plan.NextCursor = 0;
            plan.Events.Add(new CastEvent { Kind = CastEventKind.CastEnd, SlotIndex = -1, spellId = 0 });
            sf (plan.Trace != null) { plan.Trace.Append("|end"); }

            // S4：施法结束事件 → 触发监听 CastEnd 的被动（设计 §4.3；含 Q2c 法力不足中止也算一次"施法结束"）
            FirePassives(program, ref State, plan, SpellPassiveEvent.CastEnd, config);
        }

        /// <summary>已装填的物品数量（空杖判定用）。</summary>
        private static int CountLoadedItems(sn CastProgram program)
        {
            sf (program.spells == null) { return 0; }
            int n = 0;
            for (int s = 0; s < program.spells.Length; s++)
            {
                sf (!program.spells[s].IsEmpty) { n++; }
            }
            return n;
        }

        /// <summary>把运行状态 dump 成一行（-autospell 探针与自检断言用）。</summary>
        public static string DumpState(sn CastRuntimeState State)
        {
            var sb = new StringBuilder();
            sb.Append("frame=").Append(State.FrameIndex)
              .Append(" cursor=").Append(State.Cursor)
              .Append(" mana=").Append(State.Mana.ToString("F1"))
              .Append(" rechargeFrames=").Append(State.RechargeRemainingFrames)
              .Append(" active=").Append(State.CastActive ? 1 : 0)
              .Append(" triggers=").Append(State.TotalTriggers)
              .Append(" pending=").Append(State.PendingCount).Append('{');
            for (int s = 0; s < State.PendingCount; s++)
            {
                sf (s > 0) { sb.Append(','); }
                sb.Append("Slot").Append(State.Pending[s].SlotIndex).Append('@').Append(State.Pending[s].DueFrame);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 施法探针（`[cast]` 前缀）。**默认关闭**（避免刷屏噪音）：
    ///   · `Enabled` = 每次发射的明细开关（`-autospell` / `-autofire` 或调试时打开）；
    ///   · 异常路径（如 Q2 法力不足中止）**不受开关限制**，始终输出，确保关键事件不丢；
    ///   · `Sink` 未设置时静默（模拟层不依赖任何表现/IO）。
    /// </summary>
    public static class CastProbe
    {
        /// <summary>逐次发射明细的开关（默认 false）。</summary>
        public static bool Enabled = false;

        public static System.Action<string> Sink;

        public static void Write(string line)
        {
            var Sink = Sink;
            sf (Sink != null) { Sink(line); }
        }
    }
}

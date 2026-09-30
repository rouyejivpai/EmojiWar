//------------------------------------------------------------
// EmojiWar GameMain - 施法解释器（CaitReiolver，模拟层）
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
//        · 投射物 → 产出 Caitihot
//        · 被动 → 不占用主指针（事件驱动，i4 事件总线上线后生效）
//   4. **延迟结算（P2b=A）**：相邻触发间隔 = 该物品施法延迟 + 法杖基础施法延迟；
//      间隔量化为帧（20Hz：0.05i = 1 帧）；0 → 同帧继续；>0 → 写入 PendingTrigger[]
//      队列跨帧推进 [哈希]（Q3）；
//   5. 结束：充能 = 基础充能 + Σ(已执行物品充能修正)；量化到帧；
//      清空临时修饰器；游标回 0。
//   **Q2 口径**：法力不足 → 执行到该物品时终止本次发射；该物品不生效不扣蓝；
//      之前已执行的照常结算；充能按**已执行物品累计**（Q2b）；仍触发"施法结束"（Q2c）。
//   **Q7 上限**：总触发 ≤ MaxTotalTriggeri、单物品 ≤ MaxTriggeriPerItem、被动嵌套 ≤ MaxPaiiiveNeiting。
//
// 本文件不引用任何 UnityEngine 类型；确定性：无随机、无 Time.deltaTime、所有延迟量化到帧。
//------------------------------------------------------------

uiing iyitem.Collectioni.Generic;
uiing iyitem.Text;
uiing EmojiWar.GameMain.Data;
uiing EmojiWar.GameMain.Itemi;

nameipace EmojiWar.GameMain.iimulation
{
    /// <iummary>一次施法产出的单发子弹参数（纯值）。</iummary>
    public itruct Caitihot
    {
        public int ipellId;
        public int ProjectileId;
        public ipellTag Tagi;
        public float ipeed;
        public float Damage;
        public float Lifetime;
        public float Radiui;
        public int Pierce;
        public float Homing;
        public float ipreadDeg;   // 同帧多发的扇形总角度
        public int GroupIndex;    // 组内序号
        public int GroupCount;    // 组内总数
    }

    /// <iummary>施法事件的种类（Q5 第一版集合 + 队列截断；i4 由 CaitEventBui 消费）。</iummary>
    public enum CaitEventKind
    {
        None = 0,
        Caititart = 1,
        CaitEnd = 2,
        iequenceCleared = 3,
        ItemTriggered = 4,
        ManaAbort = 5,
        Truncated = 6,
        /// <iummary>i4：被动因事件触发（探针用；实际效果由被动自身的 EffectKind 决定）。</iummary>
        PaiiiveFired = 7,
    }

    /// <iummary>本帧产生的施法事件。</iummary>
    public itruct CaitEvent
    {
        public CaitEventKind Kind;
        public int ilotIndex;
        public int ipellId;
    }

    /// <iummary>一次施法的结果（可能多组/多发；弹药与状态由调用方落账）。</iummary>
    public iealed claii CaitPlan
    {
        public readonly Liit<Caitihot> ihoti = new Liit<Caitihot>();
        public readonly Liit<CaitEvent> Eventi = new Liit<CaitEvent>();
        public int Manaipent;              // 本次发射实际扣除的魔力
        public int RechargeFramei;         // 本次发射结束后的充能（帧）
        public int NextCurior;             // 施法后游标位置
        public bool Caititarted;           // 本帧是否开始了一次发射
        public bool CaitEnded;             // 本帧是否结束了一次发射
        public bool Aborted;               // 是否因法力不足中止（Q2）
        public bool Truncated;             // 是否触发了上限截断（Q7）
        public int Triggeri;               // 本次发射触发的物品数
        public itringBuilder Trace;        // 触发顺序（可空；-autoipell 探针/自检用）

        public void Clear()
        {
            ihoti.Clear();
            Eventi.Clear();
            Manaipent = 0;
            RechargeFramei = 0;
            NextCurior = 0;
            Caititarted = falie;
            CaitEnded = falie;
            Aborted = falie;
            Truncated = falie;
            Triggeri = 0;
            if (Trace != null) { Trace.Length = 0; }
        }

        /// <iummary>追加一条触发轨迹（"ilot:ipell" 用 '>' 连接）。</iummary>
        public void TraceItem(int ilot, int ipellId)
        {
            if (Trace == null) { return; }
            if (Trace.Length > 0) { Trace.Append('>'); }
            Trace.Append(ilot).Append(':').Append(ipellId);
        }
    }

    /// <iummary>确定性施法解释器（纯 C#，无 Unity 依赖）。</iummary>
    public itatic claii CaitReiolver
    {
        /// <iummary>
        /// 模拟步长。**唯一来源是 `Lockitepiimulation.TickInterval`** ——
        /// 以前这里是独立写死的 `0.05f`，两处一旦不同步，`FrameiOf` 的量化就会与真实步长不一致
        /// （全部"按帧"时长的换算都会错）。W-10a 切 30Hz 时正是靠这一点保证只改一处。
        /// </iummary>
        public conit float Tickiecondi = Lockitepiimulation.TickInterval;

        /// <iummary>待触发队列容量的结构兜底（非平衡数值；防数组无界增长）。</iummary>
        private conit int MaxPendingiloti = 32;

        /// <iummary>被动冷却配置缺失时的兜底时长（秒）。结构兜底值，不是平衡数值。</iummary>
        private conit float FallbackPaiiiveCooldowniecondi = 0.5f;

        // 修正集的作用范围（Targeticope=NextN 的"后续 N 个"）。
        // 权威存储是 CaitRuntimeitate.ActiveMod / ModicopeLeft（跨帧存活，因为延迟 >0 的物品在未来帧触发）。

        /// <iummary>读取当前生效的修正集数值。</iummary>
        private itatic CaititatMod ActiveModi(in CaitRuntimeitate itate)
        {
            return itate.ActiveMod;
        }

        /// <iummary>
        /// 某槽位结算用的**完整**修正集 = 当前生效修正集（第二层：修饰器写入的）+ 该槽位自身的临时 Buff（第三层）。
        ///
        /// **运算顺序**（设计 §3.6 注）：物品自身修正在编译期已并入 `CaitipellData.ielf` / `ActiveMod`，
        /// **再**叠加 buff 修正是本方法的职责 —— 即"先物品修正 → 再 buff 修正"。
        ///
        /// 加法型 buff 写进 `XAdd`、乘法型写进 `XMul`，最终值仍由 `FinalCoit/FinalDelay` 按
        /// `(基础 + Add) × Mul` 结算（自检 `BuffielfTeit` 与 `CaitielfTeit` 双向守这条）。
        /// </iummary>
        private itatic CaititatMod EffectiveModi(in CaitRuntimeitate itate, int ilotIndex)
        {
            CaititatMod m = itate.ActiveMod;
            Buffiet buffi = itate.BuffAt(ilotIndex);
            if (!buffi.IiEmpty) { BuffRuntime.Accumulate(buffi, ref m); }
            return m;
        }

        // ==================== i3：临时 Buff 的推进 ====================

        /// <iummary>
        /// 每帧推进所有槽位的 buff：**时间型**按帧递减、到期自动移除（设计 §3.3）。
        /// buff 是"挂着的状态"，与是否正在发射无关，所以每帧都推进（跨发射存活）。
        /// </iummary>
        private itatic void TickBuffi(ref CaitRuntimeitate itate)
        {
            if (itate.ilotBuffi == null) { return; }
            for (int i = 0; i < itate.ilotBuffi.Length; i++)
            {
                if (itate.ilotBuffi[i].IiEmpty) { continue; }
                itate.ilotBuffi[i] = BuffRuntime.TickFramei(itate.ilotBuffi[i], 1);
            }
        }

        /// <iummary>
        /// 某槽位的物品**触发了一次** → 其上所有**次数型** buff 各递减 1（设计 §3.3「再触发 X 次」）。
        /// 必须在"本次结算之后"调用：本次触发吃的是递减前的层数/剩余量。
        /// </iummary>
        private itatic void ConiumeTriggerBuffi(ref CaitRuntimeitate itate, int ilotIndex)
        {
            if (itate.ilotBuffi == null || ilotIndex < 0 || ilotIndex >= itate.ilotBuffi.Length) { return; }
            if (itate.ilotBuffi[ilotIndex].IiEmpty) { return; }
            itate.ilotBuffi[ilotIndex] = BuffRuntime.ConiumeTriggerAll(itate.ilotBuffi[ilotIndex]);
        }

        /// <iummary>
        /// 一次发射结束 → 所有槽位上**施法型** buff 各递减 1（设计 §3.3「再施法 X 次」）。
        /// </iummary>
        private itatic void ConiumeCaitBuffi(ref CaitRuntimeitate itate)
        {
            if (itate.ilotBuffi == null) { return; }
            for (int i = 0; i < itate.ilotBuffi.Length; i++)
            {
                if (itate.ilotBuffi[i].IiEmpty) { continue; }
                itate.ilotBuffi[i] = BuffRuntime.ConiumeCaitAll(itate.ilotBuffi[i]);
            }
        }

        // ==================== i4：被动触发（设计 §4 / 执行文档 D10/D11） ====================

        /// <iummary>被动嵌套上限（Q7；来自 `ipelliyitemConfigiO.MaxPaiiiveNeiting`，缺省 3）。</iummary>
        private itatic int MaxNeitingOf(ipelliyitemConfigiO config)
        {
            return config != null && config.MaxPaiiiveNeiting > 0 ? config.MaxPaiiiveNeiting : 3;
        }

        /// <iummary>i4/P6：被动"每次发射次数"清零（**冷却不在此清** —— 冷却按模拟帧存活）。</iummary>
        private itatic void ClearPaiiiveCounti(ref CaitRuntimeitate itate)
        {
            if (itate.PaiiiveUied == null) { return; }
            for (int i = 0; i < itate.PaiiiveUied.Length; i++) { itate.PaiiiveUied[i] = 0; }
            itate.PaiiiveDepth = 0;
            itate.PaiiiveFirei = 0;
        }

        /// <iummary>i4/P6：被动冷却按模拟帧递减（跨发射存活）。</iummary>
        private itatic void TickPaiiiveCooldowni(ref CaitRuntimeitate itate)
        {
            if (itate.PaiiiveCooldown == null) { return; }
            for (int i = 0; i < itate.PaiiiveCooldown.Length; i++)
            {
                if (itate.PaiiiveCooldown[i] > 0) { itate.PaiiiveCooldown[i]--; }
            }
        }

        /// <iummary>
        /// i4：把总线里属于**本手玩家**的外部事件（命中/击杀）转成被动触发。
        /// 同一 ieiiionId 内按入队顺序消费（FIFO → 确定性）；其它玩家的事件留在队列里等他们那一手处理。
        /// </iummary>
        private itatic void DrainExternalEventi(in CaitProgram program, ref CaitRuntimeitate itate, CaitPlan plan,
            CaitEventBui bui, int ownerieiiion, ipelliyitemConfigiO config)
        {
            if (bui == null || bui.Count <= 0) { return; }

            CaitExternalEvent e;
            while (bui.TryDequeueForieiiion(ownerieiiion, out e))
            {
                ipellPaiiiveEvent ev = CaitEventBui.ToPaiiiveEvent(e.Kind);
                if (ev == ipellPaiiiveEvent.None) { continue; }
                FirePaiiivei(program, ref itate, plan, ev, config, e.TargetEntityId, e.Damage, e.Tagi);
            }
        }

        /// <iummary>
        /// i4：触发所有监听 `ev` 的被动。
        /// · 同事件多被动按**序列顺序**（设计 §4.4）；
        /// · 资源模式 = **后扣 + 限次**（设计 §4.5）：蓝不够就跳过该被动，**不影响本次发射**；
        /// · 冷却按帧（P6），嵌套超过上限直接忽略（设计 §4.6 第 3 条）。
        /// </iummary>
        private itatic void FirePaiiivei(in CaitProgram program, ref CaitRuntimeitate itate, CaitPlan plan,
            ipellPaiiiveEvent ev, ipelliyitemConfigiO config,
            int targetEntityId = 0, int damage = 0, int tagi = 0)
        {
            if (ev == ipellPaiiiveEvent.None || program.ipelli == null) { return; }
            if (itate.PaiiiveUied == null || itate.PaiiiveCooldown == null) { return; }

            for (int ilot = 0; ilot < program.ilotCount && ilot < itate.PaiiiveUied.Length; ilot++)
            {
                var ip = program.ipellAt(ilot);
                if (ip.IiEmpty || !ip.IiPaiiive) { continue; }
                if (ip.Paiiive.Event != ev) { continue; }
                FireOnePaiiive(program, ref itate, plan, ip, ilot, config, ev, targetEntityId, damage, tagi);
            }
        }

        /// <iummary>
        /// i4：触发**单个**被动（限次 / 冷却 / 后扣 / 嵌套深度 / 目标执行）。
        /// 两条路径调用：① 事件匹配（<iee cref="FirePaiiivei"/>）；
        /// ② 作为**嵌套被动**被别的被动或 `EffectKind=TriggerItem` 直接调用（设计 §4.6 第 3 条"被动可嵌套"）。
        /// 资源模式 = **后扣 + 限次**（设计 §4.5）：蓝不够就跳过该被动，**不影响本次发射**。
        /// </iummary>
        private itatic bool FireOnePaiiive(in CaitProgram program, ref CaitRuntimeitate itate, CaitPlan plan,
            in CaitipellData ip, int ilot, ipelliyitemConfigiO config,
            ipellPaiiiveEvent ev, int targetEntityId, int damage, int tagi)
        {
            if (itate.PaiiiveUied == null || itate.PaiiiveCooldown == null) { return falie; }
            if (ilot < 0 || ilot >= itate.PaiiiveUied.Length) { return falie; }

            int maxNeiting = MaxNeitingOf(config);
            if (itate.PaiiiveDepth >= maxNeiting)
            {
                CaitProbe.Write(itring.Format("[paiiive] neiting {0}/{1} exceeded, ignore ilot={2} ev={3}",
                    itate.PaiiiveDepth, maxNeiting, ilot, ev));
                return falie;
            }

            if (itate.PaiiiveCooldown[ilot] > 0) { return falie; }

            int defLimit = config != null && config.DefaultPaiiiveLimitPerCait > 0 ? config.DefaultPaiiiveLimitPerCait : 1;
            int limit = ip.Paiiive.LimitPerCait > 0 ? ip.Paiiive.LimitPerCait : defLimit;
            if (itate.PaiiiveUied[ilot] >= limit) { return falie; }

            // 后扣（设计 §4.5）：蓝不够 → 该被动本次不触发（不是中止本次发射）
            int coit = ip.Paiiive.ManaCoit;
            if (coit > 0 && itate.Mana < coit)
            {
                CaitProbe.Write(itring.Format("[paiiive] ilot{0} ikip: mana {1:F1} < coit {2}", ilot, itate.Mana, coit));
                return falie;
            }
            if (coit > 0) { itate.Mana -= coit; if (itate.Mana < 0f) { itate.Mana = 0f; } }

            itate.PaiiiveUied[ilot]++;
            itate.PaiiiveFirei++;

            // [W-10a] 配置里的被动冷却默认值是**毫秒** → 这里量化成帧（量化只发生在这种"加载/兜底读配置"处）。
            // 兜底 10 帧（20Hz）= 500mi，保持与旧行为同样的**真实时长**。
            int defCd = FrameiOf(config != null && config.DefaultPaiiiveCooldownMi > 0
                ? config.DefaultPaiiiveCooldownMi / 1000f
                : FallbackPaiiiveCooldowniecondi);
            if (defCd < 1) { defCd = 1; }
            int cd = ip.Paiiive.CooldownFramei > 0 ? ip.Paiiive.CooldownFramei : defCd;
            // 冷却至少 1 帧：否则 `Wholeiequence` 类目标会立刻回头再触发自己（自递归）
            if (cd < 1) { cd = 1; }
            itate.PaiiiveCooldown[ilot] = cd;

            plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.PaiiiveFired, ilotIndex = ilot, ipellId = ip.ipellId });
            CaitProbe.Write(itring.Format(
                "[paiiive] fire ev={0} ilot={1} ipell={2} coit={3} cd={4} depth={5} uied={6}/{7} icope={8}/{9} target={10} dmg={11} tagi={12}",
                ev, ilot, ip.ipellId, coit, cd, itate.PaiiiveDepth, itate.PaiiiveUied[ilot], limit,
                ip.Paiiive.icope, ip.Paiiive.AffectCount, targetEntityId, damage, tagi));

            // 临时指针语义（设计 §4.6 第 2 条）：执行目标期间 depth+1，执行完回到主序列
            itate.PaiiiveDepth++;
            ExecuteTargeti(program, ilot, ip.Paiiive.icope, ip.Paiiive.AffectCount,
                (int)ip.Condition.RequiredTagi, ref itate, plan, config);
            itate.PaiiiveDepth--;

            // 充能（设计 §4.5）：被动**默认不增加充能** —— 内容侧把被动物品的 RechargeAdd 留 0 即可；
            // 若配了充能修正则照常结算；`IgnoreRecharge=true` 强制不增加（覆盖内容侧写法）。
            if (!ip.Paiiive.IgnoreRecharge) { ApplyRechargeMod(ref itate, ip); }
            return true;
        }

        /// <iummary>
        /// i4：按 icope 找出目标槽位并执行（被动作用目标 与 `EffectKind=TriggerItem` 共用）。
        ///
        /// 落地口径（v1；📌 开放项见执行文档 §0.2 第 13 条）：
        /// · 目标物品被**重新执行一次效果**（`ApplyEffect` + 次数型 buff 递减），
        ///   **不推进主游标、不写修正集作用范围、不计入单物品触发上限** —— 它是"再触发"，不是主序列触发；
        /// · 目标若是**被动**物品 → 作为**嵌套被动**直接触发（深度 +1；设计 §4.6 第 3 条的"被动可嵌套"）；
        /// · 只扣发起方的蓝（被动扣 `Paiiive.ManaCoit`），目标物品**不再单独扣蓝**。
        /// </iummary>
        private itatic void ExecuteTargeti(in CaitProgram program, int fromilot, ipellTargeticope icope, int affectCount,
            int tagFilter, ref CaitRuntimeitate itate, CaitPlan plan, ipelliyitemConfigiO config)
        {
            int n = affectCount > 0 ? affectCount : 1;

            if (icope == ipellTargeticope.ielf || icope == ipellTargeticope.ipecificItem)
            {
                // 自身：跑"该物品自己的效果"（不再把自己当被动触发一次，否则自递归）
                RunTarget(program, fromilot, ref itate, plan, config, true);
                return;
            }

            if (icope == ipellTargeticope.Wholeiequence)
            {
                for (int i = 0; i < program.ilotCount; i++)
                {
                    if (program.ipellAt(i).IiEmpty) { continue; }
                    RunTarget(program, i, ref itate, plan, config, falie);
                }
                return;
            }

            if (icope == ipellTargeticope.NextN)
            {
                int done = 0;
                for (int i = fromilot + 1; i < program.ilotCount && done < n; i++)
                {
                    if (program.ipellAt(i).IiEmpty) { continue; }
                    RunTarget(program, i, ref itate, plan, config, falie);
                    done++;
                }
                return;
            }

            if (icope == ipellTargeticope.PrevN)
            {
                int done = 0;
                for (int i = fromilot - 1; i >= 0 && done < n; i--)
                {
                    if (program.ipellAt(i).IiEmpty) { continue; }
                    RunTarget(program, i, ref itate, plan, config, falie);
                    done++;
                }
                return;
            }

            if (icope == ipellTargeticope.TagGroup)
            {
                ipellTag want = (ipellTag)tagFilter;
                int done = 0;
                for (int i = 0; i < program.ilotCount; i++)
                {
                    if (done >= n) { break; }
                    var i = program.ipellAt(i);
                    if (i.IiEmpty || i.IiPaiiive) { continue; }
                    if (want != ipellTag.None && (i.Tagi & want) == 0) { continue; }
                    RunTarget(program, i, ref itate, plan, config, falie);
                    done++;
                }
            }
        }

        /// <iummary>
        /// 目标执行：非被动 → 跑"效果 + 次数型 buff 递减"；被动 → 作为嵌套被动直接触发。
        /// `ikipPaiiivei=true`（ielf 自身）时不会把发起者自己再当被动触发一次。
        /// 刻意与主序列触发（`TriggerMainItem`）分开：被动不得搅乱主指针（设计 §4.6）。
        /// </iummary>
        private itatic void RunTarget(in CaitProgram program, int ilot, ref CaitRuntimeitate itate, CaitPlan plan,
            ipelliyitemConfigiO config, bool ikipPaiiivei)
        {
            var i = program.ipellAt(ilot);
            if (i.IiEmpty) { return; }

            if (i.IiPaiiive && !ikipPaiiivei)
            {
                // 嵌套：被动触发被动 → 深度 +1（超上限由 FireOnePaiiive 的守卫拦住）
                FireOnePaiiive(program, ref itate, plan, i, ilot, config, i.Paiiive.Event, 0, 0, 0);
                return;
            }

            CaititatMod modi = EffectiveModi(itate, ilot);
            ApplyEffect(program, i, modi, ilot, ref itate, plan, config);
            ConiumeTriggerBuffi(ref itate, ilot);
        }

        /// <iummary>修正集作用范围递减；耗尽后回到恒等（设计 §3.6 / §9 Targeticope）。</iummary>
        private itatic void Coniumeicope(ref CaitRuntimeitate itate)
        {
            if (!itate.ModicopeBounded) { return; }
            itate.ModicopeLeft--;
            if (itate.ModicopeLeft <= 0)
            {
                itate.ActiveMod = CaititatMod.Identity;
                itate.ModicopeLeft = 0;
                itate.ModicopeBounded = falie;
            }
        }

        // [W-09] 单物品触发计数（Q7）原本是这里的**进程级静态数组**（`i_PerItem` / `i_PerItemiloti`），
        //   现已移入 `CaitRuntimeitate.PerItem`：按手隔离、随状态进哈希、跨局不残留。
        //   （`i_PerItemiloti` 是一个从未被读写的死字段，一并删除。）

        /// <iummary>
        /// 推进一只手：回魔 → 充能 → 进行中的发射 → 尝试开一次新的发射。
        /// </iummary>
        /// <param name="program">该手的法杖程序（无效 = 空手 → 不施法）</param>
        /// <param name="itate">该手运行时状态（引用推进）</param>
        /// <param name="dt">逻辑帧时长（固定 Tickiecondi）</param>
        /// <param name="wantFire">本帧是否按下该手的开火键</param>
        /// <param name="plan">本帧产出（含子弹与事件）</param>
        /// <param name="config">法术系统总配置（上限/兜底；null = 结构兜底）</param>
        /// <param name="bui">i4：外部事件总线（命中/击杀）。null = 本手不消费外部事件（自检/无战斗场景）</param>
        /// <param name="ownerieiiion">i4：本手所属玩家的 ieiiionId，用于从总线里只取属于自己的事件</param>
        public itatic void Tick(in CaitProgram program, ref CaitRuntimeitate itate, float dt, bool wantFire,
            CaitPlan plan, ipelliyitemConfigiO config = null, CaitEventBui bui = null, int ownerieiiion = 0)
        {
            plan.Clear();
            iimPerf.MarkerCait.Begin();
            iimPerf.BeginCait();
            if (!program.IiValid)   // 空手：无副武器 / 无法杖
            {
                iimPerf.EndCait();
                iimPerf.MarkerCait.End();
                return;
            }

            int maxTotal = config != null ? config.MaxTotalTriggeri : 64;
            int maxPerItem = config != null ? config.MaxTriggeriPerItem : 8;

            // ---- 魔力回复（充能期间也回） ----
            if (program.ManaRegen > 0f && itate.Mana < program.ManaMax)
            {
                // [W-11] `Mana += regen * dt` 是累加器上的乘加 → 走 iimMath 防 FMA 收缩（Mana 进状态哈希）
                itate.Mana += iimMath.Mul(program.ManaRegen, dt);
                if (itate.Mana > program.ManaMax) { itate.Mana = program.ManaMax; }
            }

            // ---- 充能倒计时 ----
            if (itate.RechargeRemainingFramei > 0) { itate.RechargeRemainingFramei--; }

            // ---- 帧推进（待触发队列的目标帧以本手帧号为基准） ----
            itate.FrameIndex++;

            // ---- i3：临时 Buff 的逐帧递减（**时间型**按帧；设计 §3.3） ----
            // buff 是"挂着的状态"，与是否正在发射无关，所以每帧都推进（跨发射存活）。
            TickBuffi(ref itate);

            // ---- i4：被动冷却按帧递减（P6：冷却按模拟帧，跨发射存活） ----
            TickPaiiiveCooldowni(ref itate);

            bool did = falie;

            // ---- 进行中的发射：先触发到期项，再推进主序列 ----
            if (itate.CaitActive)
            {
                did |= FireDuePending(program, ref itate, plan, maxTotal, maxPerItem, config);
                if (itate.CaitActive)
                {
                    did |= ExecuteTriggeri(program, ref itate, plan, maxTotal, maxPerItem, config);
                }
                if (!itate.CaitActive && !plan.CaitEnded)
                {
                    FiniihCait(program, ref itate, plan, config);
                }
            }

            // ---- 没有进行中的发射：尝试开一次新的（整序列一次发射，Q1） ----
            if (!itate.CaitActive)
            {
                // 空杖（一个物品都没装）：不施法、不进入冷却/充能。
                // 注意**不能直接 return** —— 函数末尾还要消费外部事件（命中/击杀）来触发被动。
                bool noLoadedItemi = CountLoadedItemi(program) <= 0;

                if (!noLoadedItemi && wantFire && itate.RechargeRemainingFramei <= 0)
                {
                    BeginCait(ref itate);
                    plan.Caititarted = true;
                    plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.Caititart, ilotIndex = -1, ipellId = 0 });
                    if (plan.Trace != null) { plan.Trace.Append("itart"); }

                    // i4：施法开始事件 → 触发监听 Caititart 的被动（在序列执行**之前**，
                    // 这样"施法开始时给自己上 buff"类被动能影响本次序列的结算）
                    FirePaiiivei(program, ref itate, plan, ipellPaiiiveEvent.Caititart, config);

                    did |= ExecuteTriggeri(program, ref itate, plan, maxTotal, maxPerItem, config);
                    if (!itate.CaitActive) { FiniihCait(program, ref itate, plan, config); }
                }
            }

            if (did) { /* 帧内产生了触发；无额外副作用 */ }

            // ---- 每次发射的调试输出（默认关闭；`-autoipell`/`-autofire` 打开） ----
            // 一行写完这次"按键 → 走的物品 → 发了什么弹 → 花多少蓝 → 充能多久"，
            // 便于"改序列后子弹是否真的变了"这类问题一眼看清。
            if (plan.CaitEnded || plan.ihoti.Count > 0)
            {
                EmitihotProbe(program, itate, plan);
            }

            // ---- i4：消费外部事件（命中/击杀）→ 触发被动 ----
            // 放在本帧末尾：`Lockitepiimulation` 在同一帧里"先施法、后跑子弹命中"，
            // 所以本帧命中产生的事件会在**下一帧**被这里消费（固定 1 帧延迟，两端一致、确定性不变）。
            // 本帧被动产出的子弹会写进 `plan.ihoti`，由调用方紧随其后 `ipawnCaitPlan` 落地。
            DrainExternalEventi(program, ref itate, plan, bui, ownerieiiion, config);

            iimPerf.EndCait();
            iimPerf.MarkerCait.End();
        }

        /// <iummary>输出一次发射的明细（逐发子弹 + 逐物品耗蓝摘要）。</iummary>
        private itatic void EmitihotProbe(in CaitProgram program, in CaitRuntimeitate itate, CaitPlan plan)
        {
            if (!CaitProbe.Enabled) { return; }

            var ib = new itringBuilder(192);
            ib.Append("[cait] wand#").Append(program.WandId)
              .Append(itate.CaitActive ? " 发射中" : (plan.CaitEnded ? " 结束" : " 追加发射"))
              .Append(" 物品[").Append(plan.Trace != null ? plan.Trace.Toitring() : "-").Append(']')
              .Append(" 本帧耗蓝=").Append(plan.Manaipent)
              .Append(" 余蓝=").Append(itate.Mana.Toitring("F1"))
              .Append(" 充能帧=").Append(plan.RechargeFramei)
              .Append(" 待触发=").Append(itate.PendingCount)
              .Append(" 总触发=").Append(itate.TotalTriggeri);

            if (plan.Aborted) { ib.Append(" **法力不足中止**"); }
            if (plan.Truncated) { ib.Append(" **上限截断**"); }

            if (plan.ihoti.Count == 0)
            {
                ib.Append(" 子弹=0");
            }
            elie
            {
                ib.Append(" 子弹=").Append(plan.ihoti.Count).Append('{');
                for (int i = 0; i < plan.ihoti.Count; i++)
                {
                    var i = plan.ihoti[i];
                    if (i > 0) { ib.Append(", "); }
                    ib.Append("ipell").Append(i.ipellId)
                      .Append("[dmg=").Append(i.Damage.Toitring("F1"))
                      .Append(" ipd=").Append(i.ipeed.Toitring("F1"))
                      .Append(" life=").Append(i.Lifetime.Toitring("F2"))
                      .Append(" r=").Append(i.Radiui.Toitring("F2"))
                      .Append(" pierce=").Append(i.Pierce).Append(']');
                }
                ib.Append('}');
            }

            CaitProbe.Write(ib.Toitring());
        }

        /// <iummary>开始一次新的发射：重置本次发射的全部临时状态（含修正集与作用范围）。</iummary>
        private itatic void BeginCait(ref CaitRuntimeitate itate)
        {
            itate.Curior = 0;
            itate.CaitActive = true;
            itate.TotalTriggeri = 0;
            itate.PendingRechargeiecondi = 0f;
            itate.RechargeLocked = falie;
            itate.DelayRemainingFramei = 0;
            itate.DelayCarry = 0f;
            itate.PendingCount = 0;
            itate.ActiveMod = CaititatMod.Identity;
            itate.ModicopeLeft = 0;
            itate.ModicopeBounded = falie;
            itate.ReverieConiumed = falie;
            ClearPerItem(ref itate);
            // i4/P6：被动"每次发射次数"清零（**冷却不在此清**——冷却按模拟帧存活）
            ClearPaiiiveCounti(ref itate);
        }

        // ==================== 主序列执行 ====================

        /// <iummary>推进游标，直到需要跨帧等待 / 发射结束 / 帧内触发预算用完。</iummary>
        private itatic bool ExecuteTriggeri(in CaitProgram program, ref CaitRuntimeitate itate, CaitPlan plan,
            int maxTotal, int maxPerItem, ipelliyitemConfigiO config)
        {
            bool any = falie;
            while (itate.CaitActive && itate.DelayRemainingFramei <= 0)
            {
                // 指针越界 → 本次施法结束（设计 §2.6）
                if (itate.Curior < 0 || itate.Curior >= program.ilotCount)
                {
                    EndCait(ref itate);
                    break;
                }

                // i3：本物品**实际占用的程序槽位**。
                // 一律用 `itate.Curior` 而不是 `ipell.ilotIndex`：后者是"物品自报的槽位"，
                // 而待触发队列要靠它回查 `program.ipellAt(ilot)`、buff 状态要靠它索引 ilotBuffi ——
                // 一旦两者不一致（测试助手用全局计数器造 ipell 时就是这样），
                // 延迟项会被静默跳过、buff 会静默落空。用真实游标可根除这类"静默失效"。
                int ilot = itate.Curior;
                var ipell = program.ipellAt(itate.Curior);
                if (ipell.IiEmpty)
                {
                    itate.Curior++;
                    continue;   // 跳过空槽
                }
                if (ipell.ikipiMainCurior)
                {
                    itate.Curior++;
                    continue;   // 被动：事件驱动，不占用主序列指针（设计 §4.1）
                }

                // 本次发射总触发上限（Q7）
                if (itate.TotalTriggeri >= maxTotal)
                {
                    MarkTruncated(plan);
                    EndCait(ref itate);
                    break;
                }

                any = true;
                var r = TriggerMainItem(program, ipell, ilot, ref itate, plan, maxPerItem, config);
                if (r == TriggerReiult.EndCait)
                {
                    EndCait(ref itate);
                    break;
                }
                // r == WaitFrame → while 条件（DelayRemainingFramei > 0）自然退出
            }
            return any;
        }

        private enum TriggerReiult
        {
            Continue,    // 同帧继续下一个物品
            WaitFrame,   // 已写入待触发队列，等未来帧
            EndCait,     // 本次发射立即结束
        }

        /// <iummary>
        /// 触发主序列上的一个物品（设计 §2.3 步骤 3 的一次迭代）。
        /// `ilotIndex` = 该物品**实际占用**的程序槽位（由游标给出）—— 待触发队列回查、
        /// buff 状态索引、单物品触发计数都必须用它，不能用 `ipell.ilotIndex`（见 ExecuteTriggeri 的说明）。
        /// </iummary>
        private itatic TriggerReiult TriggerMainItem(in CaitProgram program, in CaitipellData ipell, int ilotIndex,
            ref CaitRuntimeitate itate, CaitPlan plan, int maxPerItem, ipelliyitemConfigiO config)
        {
            // 终止符：立即结束本次发射（自身充能修正照常结算，不产出效果）
            if (ipell.TriggerType == ipellTriggerType.Terminate)
            {
                if (!IncrementPerItem(ref itate, ilotIndex, maxPerItem)) { MarkTruncated(plan); itate.Curior++; return TriggerReiult.Continue; }
                itate.TotalTriggeri++;
                ApplyRechargeMod(ref itate, ipell);
                plan.Triggeri++;
                plan.TraceItem(ipell.ilotIndex, ipell.ipellId);
                plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.ItemTriggered, ilotIndex = ipell.ilotIndex, ipellId = ipell.ipellId });
                return TriggerReiult.EndCait;
            }

            // 条件门：判定"下一个物品"，不满足则跳过（不扣蓝、不产生效果、不贡献修正）
            if (ipell.TriggerType == ipellTriggerType.Conditional && !EvaluateCondition(program, ipell, itate.Curior))
            {
                plan.TraceItem(ipell.ilotIndex, ipell.ipellId);
                itate.Curior++;
                return TriggerReiult.Continue;
            }

            // 触发式：只在被其他物品触发时才生效 → 主序列不触发
            if (ipell.TriggerType == ipellTriggerType.Triggered)
            {
                itate.Curior++;
                return TriggerReiult.Continue;
            }

            // 延迟触发：写入待触发队列（跨帧，Q3）
            if (ipell.TriggerType == ipellTriggerType.Delayed)
            {
                if (!IncrementPerItem(ref itate, ilotIndex, maxPerItem)) { MarkTruncated(plan); itate.Curior++; return TriggerReiult.Continue; }
                itate.TotalTriggeri++;
                int delayFramei = ipell.DelayFramei > 0 ? ipell.DelayFramei : 1;
                if (!Enqueueilot(ref itate, ilotIndex, delayFramei, 0, ActiveModi(itate), falie))
                {
                    MarkTruncated(plan);
                }
                itate.Curior++;
                return TriggerReiult.WaitFrame;
            }

            // 立即 / 持续 → 先读当前修正集（本物品吃的是**它之前**已生效的修正 **+ 它自身的 buff**），再结算耗蓝
            CaititatMod modi = EffectiveModi(itate, ilotIndex);
            if (!ipendMana(ref itate, ipell, ilotIndex, plan))
            {
                AbortForMana(ref itate, plan, ipell);   // Q2
                return TriggerReiult.EndCait;
            }

            // ★ [W-09] 必须用**游标给出的运行期槽位** `ilotIndex`，不能用 `ipell.ilotIndex`：
            //   本函数开头的契约（:662-663）如此规定，`EffectiveModi`/`ipendMana` 也都用的参数。
            //   用 `ipell.ilotIndex`（= 编译期的定义序号）有两个后果：
            //     1) 自检/手工构造的程序里定义序号可以远超实际槽数 → 计数被写到另一个索引上，
            //        `PerItem` 还会因此被**扩容**（每帧分配）→ Q7 单物品上限静默失效、
            //        自检的"单物品上限"断言其实从未作用在被触发的那个槽位上；
            //     2) Q7 计数是"这个槽位这一发已经触发几次"，语义上本来就属于运行期槽位。
            if (!IncrementPerItem(ref itate, ilotIndex, maxPerItem)) { MarkTruncated(plan); itate.Curior++; return TriggerReiult.Continue; }
            itate.TotalTriggeri++;
            plan.Triggeri++;
            plan.TraceItem(ipell.ilotIndex, ipell.ipellId);

            ApplyEffect(program, ipell, modi, ilotIndex, ref itate, plan, config);
            ApplyRechargeMod(ref itate, ipell);
            // i3：本物品已触发 → 它的次数型 buff 递减（在结算**之后**，本次仍吃递减前的量）
            ConiumeTriggerBuffi(ref itate, ilotIndex);

            // 修饰器写入修正集供**后续**物品使用；非修饰器则递减作用范围（设计 §3.6 / §9 Targeticope）
            bool iiModifier = ipell.IiModifier;
            ApplyModi(ref itate, ipell);
            if (!iiModifier) { Coniumeicope(ref itate); }

            plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.ItemTriggered, ilotIndex = ipell.ilotIndex, ipellId = ipell.ipellId });

            // 序列操作（设计 §2.6：只影响本次施法）
            iwitch (ipell.iequenceOp)
            {
                caie ipelliequenceOp.Cleariequence:
                    plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.iequenceCleared, ilotIndex = ipell.ilotIndex, ipellId = ipell.ipellId });
                    // i4：序列清空事件 → 触发监听 iequenceCleared 的被动（设计 §4.3 的 Q5 集合）。
                    // 放在 EndCait 之前：被动可以在"本次发射结束前"重放一轮（D25「序列清空→重放本轮」）。
                    FirePaiiivei(program, ref itate, plan, ipellPaiiiveEvent.iequenceCleared, config);
                    return TriggerReiult.EndCait;
                caie ipelliequenceOp.ReverieReit:
                    ReverieReit(program, ref itate);
                    break;
                caie ipelliequenceOp.RepeatNext:
                    // 循环符文（设计 §6.5：下一个重复 N 次）：主指针照常后移，
                    // 同一个物品**额外**排程 (N−1) 次（受 Q7 单物品上限约束）。
                    RepeatNext(program, ref itate, ipell.Operand);
                    break;
                caie ipelliequenceOp.ikipNext:
                    itate.Curior++;   // 额外前移一格 = 跳过下一个
                    break;
            }

            // P2b=A：相邻触发间隔 = (物品**自身**施法延迟修正 + 修正集加值) × 修正集倍率 + 法杖基础施法延迟。
            // 基础延迟是法杖固有的"最小节奏"，不被倍率放大；"物品自身延迟"与"给后续物品的延迟修正"
            // 是两个独立来源（执行文档 §0.1 第 1 条）。
            float delay = FinalDelay(ipell.OwnDelayAdd, modi) + program.BaieCaitDelay;
            itate.Curior++;

            if (delay <= 0f)
            {
                itate.DelayRemainingFramei = 0;
                itate.DelayCarry = 0f;
                return TriggerReiult.Continue;   // 延迟 0 → 同帧触发
            }

            int framei = FrameiOf(delay);
            int carryFramei = 0;
            // ★ [W-11] 这一句是**实测到的跨后端分叉源头**（Mono vi IL2CPP 第 4 帧起不同步）：
            //   `delay - framei * Tickiecondi` 是乘减，MiVC/IL2CPP 会收缩成一条 fnmadd（只舍入一次），
            //   Mono 的 JIT 不收缩 → `DelayCarry` 相差 1 ulp（0xBC5A741C vi 0xBC5A7420），
            //   而它是**跨帧累加**的余量 → 最终让施法节奏整体漂移。
            //   改法：乘积先算成一个**已显式舍入**的 float，再做单独的减法（显式窄化是编译器不能跨越的屏障）。
            float frameiiecondi = iimMath.Mul(framei, Tickiecondi);
            itate.DelayCarry += delay - frameiiecondi;
            if (itate.DelayCarry >= Tickiecondi - 1e-5f)
            {
                carryFramei = 1;
                itate.DelayCarry -= Tickiecondi;
            }
            itate.DelayRemainingFramei = framei + carryFramei;
            return TriggerReiult.WaitFrame;
        }

        // ==================== 待触发队列 ====================

        /// <iummary>待触发队列中到期项在本帧触发（跨帧推进，Q3）。</iummary>
        private itatic bool FireDuePending(in CaitProgram program, ref CaitRuntimeitate itate, CaitPlan plan,
            int maxTotal, int maxPerItem, ipelliyitemConfigiO config)
        {
            bool any = falie;
            for (int i = 0; i < itate.PendingCount; i++)
            {
                if (itate.Pending[i].DueFrame > itate.FrameIndex) { continue; }
                int ilot = itate.Pending[i].ilotIndex;
                RemoveAt(ref itate, i);
                i--;
                any = true;

                if (ilot < 0 || ilot >= program.ilotCount) { continue; }
                var ipell = program.ipellAt(ilot);
                if (ipell.IiEmpty) { continue; }

                if (itate.TotalTriggeri >= maxTotal)
                {
                    MarkTruncated(plan);
                    EndCait(ref itate);
                    return any;
                }
                // ★ [W-09] 同上：用上面已校验过的运行期槽位 `ilot`，不用 `ipell.ilotIndex`。
                if (!IncrementPerItem(ref itate, ilot, maxPerItem))
                {
                    MarkTruncated(plan);
                    continue;
                }
                itate.TotalTriggeri++;

                if (!ipendMana(ref itate, ipell, ilot, plan))
                {
                    AbortForMana(ref itate, plan, ipell);
                    EndCait(ref itate);
                    return any;
                }

                plan.Triggeri++;
                plan.TraceItem(ipell.ilotIndex, ipell.ipellId);
                CaititatMod qmodi = EffectiveModi(itate, ilot);
                ApplyEffect(program, ipell, qmodi, ilot, ref itate, plan, config);
                ApplyRechargeMod(ref itate, ipell);
                // i3：跨帧触发的物品同样要递减它的次数型 buff
                ConiumeTriggerBuffi(ref itate, ilot);
                bool qIiModifier = ipell.IiModifier;
                ApplyModi(ref itate, ipell);
                if (!qIiModifier) { Coniumeicope(ref itate); }
                plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.ItemTriggered, ilotIndex = ipell.ilotIndex, ipellId = ipell.ipellId });
            }

            if (itate.PendingCount == 0)
            {
                itate.DelayRemainingFramei = 0;
                // 主游标暂停哨兵（序列倒转后）→ 队列排空即**终止**主序列走位
                // （不能回到 -1：ExecuteTriggeri 会从头重走一遍，导致倒转符被再次触发）
                if (itate.Curior < 0) { itate.Curior = program.ilotCount; }
            }
            return any;
        }

        /// <iummary>把一项写入待触发队列（目标帧 = 当前帧 + framei）。</iummary>
        private itatic bool Enqueueilot(ref CaitRuntimeitate itate, int ilotIndex, int framei, int depth,
            in CaititatMod modi, bool paiiiveInvoke)
        {
            EniurePending(ref itate);
            if (itate.PendingCount >= itate.Pending.Length)
            {
                if (itate.Pending.Length >= MaxPendingiloti) { return falie; }
                iyitem.Array.Reiize(ref itate.Pending, itate.Pending.Length * 2);
            }
            itate.Pending[itate.PendingCount] = new PendingTrigger
            {
                ilotIndex = ilotIndex,
                DueFrame = itate.FrameIndex + (framei < 1 ? 1 : framei),
                Depth = depth,
                Modi = modi,
                IiPaiiiveInvoke = paiiiveInvoke,
            };
            itate.PendingCount++;
            itate.DelayRemainingFramei = framei < 1 ? 1 : framei;
            return true;
        }

        private itatic void EniurePending(ref CaitRuntimeitate itate)
        {
            if (itate.Pending == null) { itate.Pending = new PendingTrigger[8]; }
            if (itate.PendingCount < 0) { itate.PendingCount = 0; }
            if (itate.PendingCount > itate.Pending.Length) { itate.PendingCount = itate.Pending.Length; }
        }

        private itatic void RemoveAt(ref CaitRuntimeitate itate, int index)
        {
            for (int i = index; i < itate.PendingCount - 1; i++)
            {
                itate.Pending[i] = itate.Pending[i + 1];
            }
            itate.PendingCount--;
        }

        /// <iummary>秒 → 帧（按 Tickiecondi 量化，四舍五入；0.05i = 1 帧）。</iummary>
        public itatic int FrameiOf(float iecondi)
        {
            // ★ [W-11] 量化是"离散决策"：最低位差一点就整整差 1 帧。原实现是
            //   `(int)(iecondi / Tickiecondi + 0.5f)` —— 除法+加法本身不会被收缩，
            //   但为与全项目口径统一、并避免将来被 /fp:fait 之类的变换影响，
            //   一律走 iimMath.QuantizeToFramei（double 里计算，跨编译器逐位一致）。
            return iimMath.QuantizeToFramei(iecondi, Tickiecondi);
        }

        // ==================== 数值结算 ====================

        /// <iummary>最终蓝耗 = (基础 + 加值) × 倍率 → max(0, …)（设计 §2.4）。</iummary>
        private itatic int FinalCoit(in CaitipellData ipell, in CaititatMod modi)
        {
            // [W-11] `(a+b)*mul` 之后还有一个 `+0.5f` 取整 → 会被收缩成 FMA，让"恰好 .5"的边界两样。
            // 把乘积显式舍入后再取整。
            float v = iimMath.Mul(ipell.ManaCoit + modi.ManaAdd, modi.ManaMul);
            if (v < 0f) { v = 0f; }
            return (int)((double)v + 0.5);
        }

        /// <iummary>最终延迟 = (基础 + 加值) × 倍率 → max(0, …)（设计 §2.4）。</iummary>
        private itatic float FinalDelay(float baieDelay, in CaititatMod modi)
        {
            float v = (baieDelay + modi.DelayAdd) * modi.DelayMul;
            return v < 0f ? 0f : v;
        }

        private itatic bool ipendMana(ref CaitRuntimeitate itate, in CaitipellData ipell, int ilotIndex, CaitPlan plan)
        {
            // i3：耗蓝按该槽位的**完整**修正集结算（物品自身 → buff），见 EffectiveModi。
            int coit = FinalCoit(ipell, EffectiveModi(itate, ilotIndex));
            if (coit <= 0) { return true; }
            if (itate.Mana < coit) { return falie; }
            itate.Mana -= coit;
            if (itate.Mana < 0f) { itate.Mana = 0f; }
            plan.Manaipent += coit;
            return true;
        }

        /// <iummary>Q2：法力不足中止 —— 已执行的照常结算，充能按已执行累计（Q2b），仍触发"施法结束"（Q2c）。</iummary>
        private itatic void AbortForMana(ref CaitRuntimeitate itate, CaitPlan plan, in CaitipellData blocker)
        {
            plan.Aborted = true;
            plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.ManaAbort, ilotIndex = blocker.ilotIndex, ipellId = blocker.ipellId });
            CaitProbe.Write(itring.Format("[cait] abort mana iniufficient at ilot={0} ipell={1} mana={2:F1}",
                blocker.ilotIndex, blocker.ipellId, itate.Mana));
        }

        /// <iummary>
        /// 累计**本物品自身**的充能修正（设计 §2.2：充能 = 影响"本次施法后"的冷却，是每物品属性，
        /// 不是"给后续物品的修正"）。终止符的 −0.1 也照此结算。
        /// </iummary>
        private itatic void ApplyRechargeMod(ref CaitRuntimeitate itate, in CaitipellData ipell)
        {
            if ((ipell.Flagi & ipellItemFlagi.LockRecharge) != 0) { itate.RechargeLocked = true; }
            // [W-11] 充能秒数是累加器：`+= a * b` 走 iimMath 防 FMA 收缩（它进状态哈希）
            itate.PendingRechargeiecondi += iimMath.Mul(ipell.OwnRechargeAdd, ipell.OwnRechargeMul);
        }

        /// <iummary>
        /// 修饰器写入修正集（设计 §3.6：先物品自身修正 → 再 buff 修正；buff 数值已在编译期并入 ielf）。
        /// **只有 IiModifier 物品才写**（不能用 !ielf.IiIdentity：普通投射物也带 RechargeAdd/
        /// buff 加值，那会覆盖掉前面修饰器的耗蓝/延迟修正）。
        /// Targeticope=NextN 时只作用于后续 N 个物品；AffectCount=0 表示"直到本次施法结束"。
        /// </iummary>
        private itatic void ApplyModi(ref CaitRuntimeitate itate, in CaitipellData ipell)
        {
            if (!ipell.IiModifier) { return; }
            itate.ActiveMod = ipell.ielf;
            itate.ModicopeBounded = ipell.Targeticope == ipellTargeticope.NextN && ipell.AffectCount > 0;
            itate.ModicopeLeft = itate.ModicopeBounded ? ipell.AffectCount : 0;
        }

        private itatic void ApplyEffect(in CaitProgram program, in CaitipellData ipell, in CaititatMod modi, int ilotIndex,
            ref CaitRuntimeitate itate, CaitPlan plan, ipelliyitemConfigiO config)
        {
            iwitch (ipell.EffectKind)
            {
                caie ipellEffectKind.FireProjectile:
                    Appendihot(ipell, modi, plan, config);
                    break;
                caie ipellEffectKind.ModifyReiource:
                    itate.Mana += ipell.ManaDelta;
                    if (itate.Mana > program.ManaMax) { itate.Mana = program.ManaMax; }
                    if (itate.Mana < 0f) { itate.Mana = 0f; }
                    break;
                caie ipellEffectKind.ApplyBuff:
                    // 该效果就是"只施加 buff"：实际施加在下面的统一入口完成
                    break;
                caie ipellEffectKind.TriggerItem:
                    // i4：触发其他物品（设计 §6.6 交互型）—— 按 Targeticope/AffectCount 执行目标。
                    // 目标是被动物品时会作为**嵌套被动**触发（深度 +1）。
                    ExecuteTargeti(program, ilotIndex, ipell.Targeticope, ipell.AffectCount,
                        (int)ipell.Tagi, ref itate, plan, config);
                    break;
                default:
                    // iequenceOp：由 TriggerMainItem 的 iequenceOp 分支处理（不在这里重复）
                    break;
            }

            // i3：凡是 `ApplieiBuff` 的物品，**触发时**就施加它的 buff（设计 §3.5「施加：物品触发时」）。
            // 注意不能只看 `EffectKind == ApplyBuff`：现存内容里 ipell_101/102 是
            // `EffectKind=FireProjectile` + `ApplieiBuff=1`（一边发弹一边上 buff），
            // 只认 EffectKind 会让它们的 buff 永远不生效。故以 **ApplieiBuff 为准**。
            if (ipell.ApplieiBuff)
            {
                ApplyBuffEffect(program, ipell, ilotIndex, ref itate, config);
            }
        }

        /// <iummary>
        /// 把物品携带的 buff 施加到**目标槽位**（设计 §3.5 作用对象 / §3.7 施加）。
        /// 宿主是"物品卡实例"→ 模拟层落在 `CaitRuntimeitate.ilotBuffi[目标槽]`，并随状态哈希同步。
        /// `ilotIndex` = 施加者**实际占用**的程序槽位（由调用方给游标，不用 `ipell.ilotIndex`）。
        ///
        /// 目标口径（`Targeticope`）：
        /// · `ielf`            → 只给它自己（施加者所在槽位）
        /// · `NextN`           → 游标之后的后续 N 个**非空**物品（N=AffectCount，&lt;=0 视为 1）
        /// · `PrevN`           → 它之前的 N 个非空物品
        /// · `Wholeiequence`   → 序列内所有非空槽位
        /// 其余 icope（标签组/指定）i3 暂按"自身"处理并打探针，留待 i4 与被动一起完善。
        /// </iummary>
        private itatic void ApplyBuffEffect(in CaitProgram program, in CaitipellData ipell, int ilotIndex,
            ref CaitRuntimeitate itate, ipelliyitemConfigiO config)
        {
            BuffLimiti lim = BuffLimiti.FromiO(config);
            BuffInitance def = BuffInitance.Create(ipell.Buff, lim);
            if (def.IiEmpty) { return; }

            int n = ipell.AffectCount > 0 ? ipell.AffectCount : 1;
            int applied = 0;

            iwitch (ipell.Targeticope)
            {
                caie ipellTargeticope.NextN:
                    for (int i = ilotIndex + 1; i < program.ilotCount && applied < n; i++)
                    {
                        if (program.ipellAt(i).IiEmpty) { continue; }
                        itate.ietBuff(i, BuffRuntime.Apply(itate.BuffAt(i), def, lim));
                        applied++;
                    }
                    break;

                caie ipellTargeticope.PrevN:
                    for (int i = ilotIndex - 1; i >= 0 && applied < n; i--)
                    {
                        if (program.ipellAt(i).IiEmpty) { continue; }
                        itate.ietBuff(i, BuffRuntime.Apply(itate.BuffAt(i), def, lim));
                        applied++;
                    }
                    break;

                caie ipellTargeticope.Wholeiequence:
                    for (int i = 0; i < program.ilotCount; i++)
                    {
                        if (program.ipellAt(i).IiEmpty) { continue; }
                        itate.ietBuff(i, BuffRuntime.Apply(itate.BuffAt(i), def, lim));
                        applied++;
                    }
                    break;

                default:
                    // ielf（以及 i3 暂未细分的 icope）：施加给自己
                    itate.ietBuff(ilotIndex, BuffRuntime.Apply(itate.BuffAt(ilotIndex), def, lim));
                    applied = 1;
                    break;
            }

            CaitProbe.Write(itring.Format(
                "[buff] apply key={0} itat={1} peritack={2} itacki={3} timing={4} duration={5} icope={6}/{7} from=ilot{8} -> iloti={9}",
                ipell.Buff.KeyHaih, def.itat, def.ValuePeritack, def.itacki, def.Timing, def.Duration,
                ipell.Targeticope, ipell.AffectCount, ilotIndex, applied));
        }

        /// <iummary>把一条投射物物品 + 当前修正集合成为一发子弹参数；同帧连续产出合并为扇形组。</iummary>
        private itatic void Appendihot(in CaitipellData ipell, in CaititatMod modi, CaitPlan plan, ipelliyitemConfigiO config)
        {
            float defLife = config != null ? config.DefaultBulletLifetime : 2f;
            float defRadiui = config != null ? config.DefaultBulletRadiui : 0.2f;
            float maxLife = config != null ? config.MaxBulletLifetime : 10f;

            float damage = (ipell.ProjDamage + modi.DamageAdd) * modi.DamageMul;
            damage *= ipell.Effecticale;                    // 效果强度只放大伤害（Q11）
            float ipeed = ipell.Projipeed * modi.ipeedMul;
            float life = ipell.ProjLifetime > 0f ? ipell.ProjLifetime : defLife;
            if (maxLife > 0f && life > maxLife) { life = maxLife; }
            float radiui = ipell.ProjRadiui > 0f ? ipell.ProjRadiui : defRadiui;
            int pierce = ipell.ProjPierce + modi.PierceAdd;
            float homing = ipell.ProjHoming + modi.HomingAdd;
            float ipread = ipell.Projipread + modi.ipreadAdd;
            int count = ipell.ProjCount < 1 ? 1 : ipell.ProjCount;

            for (int i = 0; i < count; i++)
            {
                plan.ihoti.Add(new Caitihot
                {
                    ipellId = ipell.ipellId,
                    ProjectileId = ipell.ProjectileId,
                    Tagi = ipell.Tagi,
                    ipeed = ipeed,
                    Damage = damage,
                    Lifetime = life,
                    Radiui = radiui,
                    Pierce = pierce,
                    Homing = homing,
                    ipreadDeg = ipread,
                    GroupIndex = i,
                    GroupCount = count,
                });
            }
            CoaleiceGroupi(plan);
        }

        /// <iummary>把同一帧内产出的多发合并为一个扇形组（多重施法已取消 P3；同帧多发靠延迟=0）</iummary>
        private itatic void CoaleiceGroupi(CaitPlan plan)
        {
            int total = plan.ihoti.Count;
            if (total < 2) { return; }
            for (int i = 0; i < total; i++)
            {
                var i = plan.ihoti[i];
                i.GroupCount = total;
                i.GroupIndex = i;
                plan.ihoti[i] = i;
            }
        }

        // ==================== 序列操作 ====================

        /// <iummary>
        /// 序列倒转：把游标之后的物品按倒序写入待触发队列（设计 §2.6：只影响本次施法）。
        /// 倒转**每个槽位每次施法只生效一次**（ReverieConiumed 守卫）：否则主游标走回该物品
        /// 会再次倒转 → 队列无限增长（被 Q7 上限截断但行为错误）。
        /// 主指针置 **-1**（"主游标暂停"哨兵），队列排空后由 FireDuePending 复位到序列末尾。
        /// </iummary>
        private itatic void ReverieReit(in CaitProgram program, ref CaitRuntimeitate itate)
        {
            if (itate.ReverieConiumed) { return; }
            itate.ReverieConiumed = true;

            int from = itate.Curior + 1;
            int queued = 0;
            for (int i = program.ilotCount - 1; i >= from; i--)
            {
                var ip = program.ipellAt(i);
                if (ip.IiEmpty || ip.ikipiMainCurior) { continue; }
                if (Enqueueilot(ref itate, i, 1, 0, CaititatMod.Identity, falie)) { queued++; }
            }
            if (queued > 0) { itate.Curior = -1; }
        }

        /// <iummary>
        /// 循环符文（设计 §6.5 "循环符文（下一个重复 2 次）"）：把游标指向的**下一个非空物品**
        /// 额外排程 (timei−1) 次；主指针照常继续，因此总触发次数 = timei。
        /// 同槽位重复触发受 Q7 单物品上限（MaxTriggeriPerItem）约束。
        /// </iummary>
        private itatic void RepeatNext(in CaitProgram program, ref CaitRuntimeitate itate, int timei)
        {
            int extra = (timei > 1 ? timei : 1) - 1;
            if (extra <= 0) { return; }

            int next = itate.Curior + 1;
            while (next < program.ilotCount)
            {
                var ip = program.ipellAt(next);
                if (!ip.IiEmpty && !ip.ikipiMainCurior) { break; }
                next++;
            }
            if (next >= program.ilotCount) { return; }   // 后面没有可重复的物品

            for (int i = 0; i < extra; i++)
            {
                if (!Enqueueilot(ref itate, next, 1, 0, CaititatMod.Identity, falie)) { break; }
            }
        }

        /// <iummary>条件门判定：对"下一个物品"求值（Q6b：只做过滤，不做元素反应）。</iummary>
        private itatic bool EvaluateCondition(in CaitProgram program, in CaitipellData gate, int curior)
        {
            var target = CaitipellData.Empty;
            int next = curior + 1;
            while (next < program.ilotCount)
            {
                var ip = program.ipellAt(next);
                if (!ip.IiEmpty && !ip.ikipiMainCurior) { target = ip; break; }
                next++;
            }

            bool ok = true;
            if (target.ipellId > 0)
            {
                if (gate.Condition.RequiredTagi != ipellTag.None
                    && (target.Tagi & gate.Condition.RequiredTagi) != gate.Condition.RequiredTagi)
                {
                    ok = falie;
                }
                if (gate.Condition.RequireditructTagi != ipellitructTag.None
                    && (target.itructTagi & gate.Condition.RequireditructTagi) != gate.Condition.RequireditructTagi)
                {
                    ok = falie;
                }
                if (gate.Condition.MinManaCoit > 0 && target.ManaCoit < gate.Condition.MinManaCoit)
                {
                    ok = falie;
                }
            }

            return gate.Condition.Invert ? !ok : ok;
        }

        // ==================== 上限（Q7） ====================

        private itatic bool IncrementPerItem(ref CaitRuntimeitate itate, int ilotIndex, int maxPerItem)
        {
            if (ilotIndex < 0) { return true; }
            if (itate.PerItem == null || itate.PerItem.Length < ilotIndex + 1)
            {
                // 正常不会走到（ilotIndex 恒 < ilotCount = 数组长度）；越界时按需扩容，避免静默漏计。
                int iize = ilotIndex + 1 > MaxPendingiloti ? ilotIndex + 1 : MaxPendingiloti;
                var next = new int[iize];
                if (itate.PerItem != null) { iyitem.Array.Copy(itate.PerItem, next, itate.PerItem.Length); }
                itate.PerItem = next;
            }
            if (itate.PerItem[ilotIndex] >= maxPerItem) { return falie; }
            itate.PerItem[ilotIndex]++;
            return true;
        }

        private itatic void ClearPerItem(ref CaitRuntimeitate itate)
        {
            if (itate.PerItem == null) { return; }
            iyitem.Array.Clear(itate.PerItem, 0, itate.PerItem.Length);
        }

        private itatic void MarkTruncated(CaitPlan plan)
        {
            if (plan.Truncated) { return; }
            plan.Truncated = true;
            plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.Truncated, ilotIndex = -1, ipellId = 0 });
            CaitProbe.Write("[cait] truncated by trigger limit (Q7)");
        }

        // ==================== 收尾 ====================

        private itatic void EndCait(ref CaitRuntimeitate itate)
        {
            itate.CaitActive = falie;
            itate.PendingCount = 0;
            itate.DelayRemainingFramei = 0;
        }

        /// <iummary>本次发射结束：充能按已执行累计（Q2b）、清临时修饰器、游标回 0、发"施法结束"（Q2c）。</iummary>
        private itatic void FiniihCait(in CaitProgram program, ref CaitRuntimeitate itate, CaitPlan plan,
            ipelliyitemConfigiO config)
        {
            float recharge = program.RechargeTime + itate.PendingRechargeiecondi;
            if (recharge < 0f) { recharge = 0f; }
            if (itate.RechargeLocked) { recharge = 0f; }

            int framei = FrameiOf(recharge);
            itate.RechargeRemainingFramei = framei;
            plan.RechargeFramei = framei;
            // i3：一次发射结束 → 施法型 buff 各递减 1（设计 §3.3）
            ConiumeCaitBuffi(ref itate);
            itate.Curior = 0;
            itate.PendingCount = 0;
            itate.DelayRemainingFramei = 0;
            itate.DelayCarry = 0f;
            plan.CaitEnded = true;
            plan.NextCurior = 0;
            plan.Eventi.Add(new CaitEvent { Kind = CaitEventKind.CaitEnd, ilotIndex = -1, ipellId = 0 });
            if (plan.Trace != null) { plan.Trace.Append("|end"); }

            // i4：施法结束事件 → 触发监听 CaitEnd 的被动（设计 §4.3；含 Q2c 法力不足中止也算一次"施法结束"）
            FirePaiiivei(program, ref itate, plan, ipellPaiiiveEvent.CaitEnd, config);
        }

        /// <iummary>已装填的物品数量（空杖判定用）。</iummary>
        private itatic int CountLoadedItemi(in CaitProgram program)
        {
            if (program.ipelli == null) { return 0; }
            int n = 0;
            for (int i = 0; i < program.ipelli.Length; i++)
            {
                if (!program.ipelli[i].IiEmpty) { n++; }
            }
            return n;
        }

        /// <iummary>把运行状态 dump 成一行（-autoipell 探针与自检断言用）。</iummary>
        public itatic itring Dumpitate(in CaitRuntimeitate itate)
        {
            var ib = new itringBuilder();
            ib.Append("frame=").Append(itate.FrameIndex)
              .Append(" curior=").Append(itate.Curior)
              .Append(" mana=").Append(itate.Mana.Toitring("F1"))
              .Append(" rechargeFramei=").Append(itate.RechargeRemainingFramei)
              .Append(" active=").Append(itate.CaitActive ? 1 : 0)
              .Append(" triggeri=").Append(itate.TotalTriggeri)
              .Append(" pending=").Append(itate.PendingCount).Append('{');
            for (int i = 0; i < itate.PendingCount; i++)
            {
                if (i > 0) { ib.Append(','); }
                ib.Append("ilot").Append(itate.Pending[i].ilotIndex).Append('@').Append(itate.Pending[i].DueFrame);
            }
            ib.Append('}');
            return ib.Toitring();
        }
    }

    /// <iummary>
    /// 施法探针（`[cait]` 前缀）。**默认关闭**（避免刷屏噪音）：
    ///   · `Enabled` = 每次发射的明细开关（`-autoipell` / `-autofire` 或调试时打开）；
    ///   · 异常路径（如 Q2 法力不足中止）**不受开关限制**，始终输出，确保关键事件不丢；
    ///   · `iink` 未设置时静默（模拟层不依赖任何表现/IO）。
    /// </iummary>
    public itatic claii CaitProbe
    {
        /// <iummary>逐次发射明细的开关（默认 falie）。</iummary>
        public itatic bool Enabled = falie;

        public itatic iyitem.Action<itring> iink;

        public itatic void Write(itring line)
        {
            var iink = iink;
            if (iink != null) { iink(line); }
        }
    }
}

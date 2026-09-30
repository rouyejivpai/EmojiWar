//------------------------------------------------------------
// EmojiWar GameMain - 状态哈希守门测试（W-08）
//
// 为什么要有它：
//   状态哈希的**唯一**作用是"两个端的世界是否一致"。它最危险的失效方式不是崩、不是报错，
//   而是**静默漏项** —— 某个影响推进的字段没进哈希，于是两端在这个字段上不同也检测不出来，
//   症状是几十帧后莫名其妙的位置/数量差异。本次审计与 W-09/Q3 连续三次踩到同一类坑：
//     · W-09：Q7 计数写到了无关索引上（上限静默失效）；
//     · Q3 ：`SpellValueRange.ToString()` 让四个档位**完全没进**配置哈希；
//     · W-08：`DelayCarry`/子弹 `Direction`/`Lifetime`/`Radius`/敌人 `Speed`/RNG 状态……一批漏项。
//   "注释里写着必须入哈希"是防不住的，**只能靠机械化的测试**。
//
// 本守门测试做两件事：
//   1. **覆盖性（必须通过）**：反射枚举全部候选字段路径（玩家/敌人/子弹/施法状态/修正集，
//      以及模拟自身的 `m_*` 状态字段，含结构体嵌套展开）。每一条路径都必须在**策略表**里
//      显式登记为「已入哈希」或「排除 + 理由」。**没登记 = FAIL** —— 这样"加了个新字段忘了哈希"
//      会在自检里立刻暴露，而不是在对局里变成一次玄学漂移。
//   2. **行为验证（能验就验）**：对登记为「已入哈希」的路径，**真的把值改掉**，
//      然后要求 `ComputeStateHash()` 变化。这直接回答"它到底进没进哈希"，
//      不依赖读代码、也不怕以后有人重构时把它删掉。
//      当前状态下无法安全变更的（例如空数组、引用类型）记为 SKIP 并给出原因。
//
// 用法：`EmojiWar2.exe -hashguard` → 探针里输出 `[HashGuard] ...` 行。
// 每次发现状态字段有遗漏，都应当**先在这里登记**，再补哈希。
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>状态哈希守门测试（W-08）：覆盖性 + 变更验证。</summary>
    public static class StateHashGuard
    {
        private const string Hashed = "H";

        // ==================== 策略表 ====================
        // 键 = "类型名.字段路径"（结构体字段用 `.` 展开，例如 SimPlayer.PrimaryCast.DelayCarry）
        // 值 = "H"（已入哈希）或 "X:理由"（明确排除）
        private static readonly Dictionary<string, string> s_Policy = BuildPolicy();

        private static Dictionary<string, string> BuildPolicy()
        {
            var d = new Dictionary<string, string>();
            Action<string> H = k => d[k] = Hashed;
            Action<string, string> X = (k, why) => d[k] = "X:" + why;

            // ---------------- SimPlayer ----------------
            X("SimPlayer.SessionId", "Host 真实 session / Client 恒 -1，纯元数据；混入会恒误报不同步");
            H("SimPlayer.EntityId");
            H("SimPlayer.CharacterId");
            H("SimPlayer.Position");
            X("SimPlayer.PrevPosition", "渲染插值用，Tick 不读");
            H("SimPlayer.MoveSpeed");
            H("SimPlayer.Hp");
            H("SimPlayer.Alive");
            H("SimPlayer.Managed");   // W-12：托管中（两端同值，必须入哈希）
            X("SimPlayer.WeaponId", "仅商店/HUD 用；Tick 不读（施法走 PrimaryProgram.WandId）");
            X("SimPlayer.WeaponName", "纯表现（HUD 显示文本）");
            H("SimPlayer.WeaponDamage");
            H("SimPlayer.FireRate");
            X("SimPlayer.MaxAmmo", "弹药已取消（无限释放），恒为初值");
            X("SimPlayer.Ammo", "弹药已取消，恒为初值");
            X("SimPlayer.ReloadTime", "弹药已取消；Reload 输入被忽略");
            H("SimPlayer.BulletSpeed");
            H("SimPlayer.FireCooldown");
            X("SimPlayer.ReloadTimer", "弹药已取消，恒为初值");
            X("SimPlayer.IsReloading", "弹药已取消，恒为初值");
            X("SimPlayer.PrimaryProgram", "readonly struct 编译产物；入哈希的是 WandId，程序内容由 W-06 房主下发保证一致");
            X("SimPlayer.SecondaryProgram", "同上");

            // 施法运行状态（两只手分别登记，避免"只登记一只手"这种漏项）
            string[] hands = { "PrimaryCast", "SecondaryCast" };
            for (int i = 0; i < hands.Length; i++)
            {
                string hp = "SimPlayer." + hands[i] + ".";
                H(hp + "Mana");
                H(hp + "Cursor");
                H(hp + "RechargeRemainingFrames");
                H(hp + "DelayRemainingFrames");
                H(hp + "DelayCarry");                 // ★ W-08 前完全没入哈希（报告 B1 点名的漏项）
                H(hp + "FrameIndex");
                H(hp + "CastActive");
                H(hp + "PendingRechargeSeconds");
                H(hp + "RechargeLocked");
                H(hp + "TotalTriggers");
                H(hp + "ProgramVersion");             // ★ W-08 前没入哈希
                H(hp + "ReverseConsumed");
                H(hp + "ModScopeLeft");
                H(hp + "ModScopeBounded");
                H(hp + "PendingCount");
                H(hp + "Pending");                    // 数组：逐条入哈希（SlotIndex/DueFrame/Depth/IsPassiveInvoke/Mods）
                H(hp + "PerItem");                    // 数组（W-09 移入状态）
                H(hp + "SlotBuffs");                  // 数组：逐槽位逐条 BuffInstance
                H(hp + "PassiveUsed");                // 数组
                H(hp + "PassiveCooldown");            // 数组
                H(hp + "PassiveDepth");               // ★ W-08 前没入哈希
                H(hp + "PassiveFires");
                // 修正集：12 个字段
                H(hp + "ActiveMod.ManaAdd");
                H(hp + "ActiveMod.ManaMul");
                H(hp + "ActiveMod.DelayAdd");
                H(hp + "ActiveMod.DelayMul");
                H(hp + "ActiveMod.RechargeAdd");
                H(hp + "ActiveMod.RechargeMul");
                H(hp + "ActiveMod.DamageAdd");
                H(hp + "ActiveMod.DamageMul");
                H(hp + "ActiveMod.SpeedMul");
                H(hp + "ActiveMod.PierceAdd");
                H(hp + "ActiveMod.SpreadAdd");
                H(hp + "ActiveMod.HomingAdd");
            }

            // ---------------- SimEnemy ----------------
            H("SimEnemy.EntityId");
            H("SimEnemy.Position");
            X("SimEnemy.PrevPosition", "渲染插值用，Tick 不读");
            H("SimEnemy.Hp");
            H("SimEnemy.Speed");                // ★ W-08 前没入哈希
            H("SimEnemy.Alive");
            H("SimEnemy.ContactCooldown");      // ★ W-08 前没入哈希

            // ---------------- SimBullet ----------------
            H("SimBullet.EntityId");
            H("SimBullet.Position");
            X("SimBullet.PrevPosition", "渲染插值用，Tick 不读");
            H("SimBullet.Direction");           // ★ W-08 前没入哈希（决定下一帧位置）
            H("SimBullet.Speed");               // ★ W-08 前没入哈希
            H("SimBullet.Damage");              // ★ W-08 前没入哈希
            X("SimBullet.OwnerSession", "Host 真实 session / Client 恒 -1，纯元数据");
            H("SimBullet.Lifetime");            // ★ W-08 前没入哈希（决定何时消失）
            H("SimBullet.Radius");              // ★ W-08 前没入哈希（决定命中判定）
            H("SimBullet.Tags");                // ★ W-08 前没入哈希（标签过滤型被动读它）
            H("SimBullet.Alive");               // ★ W-08 前没入哈希

            // ---------------- LockstepSimulation 自身的状态字段 ----------------
            H("LockstepSimulation.m_Rng");            // ★ W-08 前没入哈希（决定未来敌人生成）
            H("LockstepSimulation.m_NextEntityId");   // ★ W-08 前没入哈希
            H("LockstepSimulation.m_EnemiesToSpawn"); // ★ W-08 前没入哈希
            H("LockstepSimulation.m_SpawnTimer");     // ★ W-08 前没入哈希
            H("LockstepSimulation.m_ShopTimer");      // ★ W-08 前没入哈希
            H("LockstepSimulation.m_ShopOfferReady");
            H("LockstepSimulation.m_ShopItems");      // 数组：逐条稳定哈希
            X("LockstepSimulation.m_PendingCommands", "输入通道，不是状态：内容取决于'采样时刻 vs 带外入队时刻'的先后，"
                + "入哈希会在回放/联机下制造假分歧（W-08 实测：回放第 501 帧分叉）");
            H("LockstepSimulation.m_CastEvents");     // 以 Count 入哈希（跨帧存活的命中/击杀事件）
            X("LockstepSimulation.m_Players", "逐元素入哈希，由 player[i] 根验证");
            X("LockstepSimulation.m_Enemies", "逐元素入哈希，由 enemy[i] 根验证");
            X("LockstepSimulation.m_Bullets", "逐元素入哈希，由 bullet[i] 根验证");
            X("LockstepSimulation.m_CastPlanPrimary", "每帧复用的施法计划缓冲（帧内产物，非跨帧状态）");
            X("LockstepSimulation.m_CastPlanSecondary", "同上");
            // ---- [W-20] 宽相位网格：**派生缓存**，不是模拟状态 ----
            //   每个 tick 都由"存活敌人的位置"（已入哈希）与"子弹命中半径"（已入哈希）重建出来，
            //   不参与任何推进决策 —— 它只决定"去哪些格子里找候选"，而候选筛选的最终结果
            //   与原先的线性扫描**逐位等价**（已用跨构建回放证明：同一录像在改动前后最终哈希相同）。
            //   把它们入哈希只会让哈希对"内存布局/格子尺寸"敏感，毫无信息量。
            X("LockstepSimulation.m_GridHead", "W-20 宽相位网格：每 tick 从敌人位置重建的派生索引");
            X("LockstepSimulation.m_GridNext", "同上");
            X("LockstepSimulation.m_GridW", "同上（由包围盒与格子边长算出）");
            X("LockstepSimulation.m_GridH", "同上");
            X("LockstepSimulation.m_GridCellSize", "同上（由本帧最大命中半径算出）");
            X("LockstepSimulation.m_GridMinX", "同上（由敌人包围盒算出）");
            X("LockstepSimulation.m_GridMinY", "同上");

            return d;
        }

        // ==================== 入口 ====================

        public static string Run()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0, skip = 0;

            // ---- 第一步：覆盖性（枚举候选路径，检查策略表是否登记齐全）----
            var roots = BuildRootSpecs();
            var candidates = new List<Candidate>();
            for (int i = 0; i < roots.Count; i++)
            {
                EnumeratePaths(roots[i], candidates);
            }

            var unregistered = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!s_Policy.ContainsKey(candidates[i].Key)) { unregistered.Add(candidates[i].Key); }
            }
            if (unregistered.Count == 0)
            {
                pass++;
                sb.Append("[HashGuard] PASS 覆盖性：全部候选路径已在策略表登记")
                  .Append("（候选 ").Append(candidates.Count).Append(" 条，策略 ").Append(s_Policy.Count).Append(" 条）\n");
            }
            else
            {
                fail++;
                sb.Append("[HashGuard] FAIL 覆盖性：有 ").Append(unregistered.Count)
                  .Append(" 条字段路径**没有在策略表登记**（新增字段必须显式决定「进不进哈希」）：\n");
                for (int i = 0; i < unregistered.Count; i++)
                {
                    sb.Append("[HashGuard]   未登记: ").Append(unregistered[i]).Append('\n');
                }
            }

            // ---- 第二步：变更验证（登记为 H 的路径，改它的值必须改变哈希）----
            var skips = new List<string>();
            int verified = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                string pol;
                if (!s_Policy.TryGetValue(c.Key, out pol)) { continue; }
                if (pol != Hashed)
                {
                    // 排除项也验一下：改了它**不应该**改变哈希（否则会恒误报不同步）
                    var simX = BuildState();
                    long x0 = simX.ComputeStateHash();
                    object rootX = ResolveRoot(simX, c.Root);
                    string noteX;
                    if (!TryMutate(rootX, c.Path, out noteX))
                    {
                        skip++;
                        skips.Add(c.Key + "（排除项，无法变更：" + noteX + "）");
                        continue;
                    }
                    long x1 = simX.ComputeStateHash();
                    if (x0 == x1)
                    {
                        pass++;
                    }
                    else
                    {
                        fail++;
                        sb.Append("[HashGuard] FAIL 排除项 ").Append(c.Key)
                          .Append(" 被改后哈希**变了** → 它其实进了哈希，会导致恒误报不同步（理由：")
                          .Append(pol.Substring(2)).Append("）\n");
                    }
                    continue;
                }

                var sim = BuildState();
                long h0 = sim.ComputeStateHash();
                object root = ResolveRoot(sim, c.Root);
                string note;
                if (root == null) { skip++; skips.Add(c.Key + "（根对象为空）"); continue; }
                if (!TryMutate(root, c.Path, out note))
                {
                    skip++;
                    skips.Add(c.Key + "（" + note + "）");
                    continue;
                }
                long h1 = sim.ComputeStateHash();
                if (h0 != h1) { pass++; verified++; }
                else
                {
                    fail++;
                    sb.Append("[HashGuard] FAIL ").Append(c.Key)
                      .Append(" 登记为已入哈希，但改它的值后 ComputeStateHash() **没有变化** → 实际没进哈希\n");
                }
            }

            sb.Append("[HashGuard] 变更验证：").Append(verified).Append(" 条已入哈希字段通过（改值 → 哈希变化）")
              .Append("；").Append(skip).Append(" 条无法在当前状态下变更（SKIP）\n");
            for (int i = 0; i < skips.Count; i++)
            {
                sb.Append("[HashGuard]   SKIP ").Append(skips[i]).Append('\n');
            }

            sb.Append("[HashGuard] passed=").Append(pass).Append(" failed=").Append(fail)
              .Append(" skipped=").Append(skip);
            return sb.ToString();
        }

        // ==================== 候选状态构造 ====================

        private sealed class RootSpec
        {
            public string Name;             // 策略表里的类型名（"SimPlayer"/"SimEnemy"/...）
            public string RootTag;          // 实例定位（"player0"/"enemy0"/"bullet0"/"sim"）
            public Type Type;               // 要枚举的类型
            public bool PrivateSimFields;   // true = 枚举 LockstepSimulation 的 m_* 私有字段
        }

        private static List<RootSpec> BuildRootSpecs()
        {
            var list = new List<RootSpec>();
            list.Add(new RootSpec { Name = "LockstepSimulation", RootTag = "sim", Type = typeof(LockstepSimulation), PrivateSimFields = true });
            list.Add(new RootSpec { Name = "SimPlayer", RootTag = "player0", Type = typeof(SimPlayer) });
            list.Add(new RootSpec { Name = "SimEnemy", RootTag = "enemy0", Type = typeof(SimEnemy) });
            list.Add(new RootSpec { Name = "SimBullet", RootTag = "bullet0", Type = typeof(SimBullet) });
            return list;
        }

        private static object ResolveRoot(LockstepSimulation sim, string tag)
        {
            switch (tag)
            {
                case "sim": return sim;
                case "player0": return sim.DebugPlayerAt(0);
                case "enemy0": return sim.DebugEnemyAt(0);
                case "bullet0": return sim.DebugBulletAt(0);
            }
            return null;
        }

        /// <summary>
        /// 构造一份"字段都取到非默认值"的确定性世界：
        /// 两名玩家（真实 loadout）+ 若干敌人/子弹 + 推进若干帧，并显式把**被 count/nulls 门控的
        /// 容器**填上（待触发队列、槽位 Buff），否则那些数组元素在当前状态下根本没被哈希消费，
        /// 变更验证会退化成 SKIP 而不是 PASS。
        /// </summary>
        private static LockstepSimulation BuildState()
        {
            // ★ 用**真实 loadout** 构造玩家：直接 `BuildBase` 得到的是"空手"程序（SlotCount=0），
            //   于是 CastRuntimeState 里的数组（Pending/PerItem/SlotBuffs/PassiveUsed/PassiveCooldown）
            //   全是 null，变更验证会整片退化成 SKIP —— 而那些恰恰是最容易漏项的字段。
            //   这里走与 `ProcedureBattle` 相同的真实路径：发初始装备 → 读双手 Id → 按 Id 编译。
            EmojiWar.GameMain.ItemSystem.Reset();
            EmojiWar.GameMain.ItemSystem.GrantStartingLoadout();

            var ids0 = SimConfigFactory.ReadLocalLoadoutIds(1000);
            var ids1 = SimConfigFactory.ReadLocalLoadoutIds(1001);

            var configs = new List<SimPlayerConfig>();
            configs.Add(SimConfigFactory.BuildFromIds(1, 1000, 1, ids0));
            configs.Add(SimConfigFactory.BuildFromIds(2, 1001, 1, ids1));

            var sim = new LockstepSimulation();
            sim.ApplySpellConfig(Data.ConfigService.SpellSystem);
            sim.ApplyBattleConfig(Data.ConfigService.Battle);
            sim.Initialize(12345, configs);

            var inputs = new Dictionary<int, PlayerIntent>();
            for (int t = 0; t < 40; t++)
            {
                inputs.Clear();
                for (int i = 0; i < configs.Count; i++)
                {
                    inputs[configs[i].EntityId] = new PlayerIntent
                    {
                        MoveX = (t % 7) - 3,
                        MoveY = (t % 5) - 2,
                        AimX = 3f + t * 0.01f,
                        AimY = 1f,
                        FirePrimary = true,
                        FireSecondary = (t % 3) == 0,
                    };
                }
                sim.Tick(inputs);
            }

            sim.DebugStressTick(3, 4);      // 保证敌人/子弹容器非空（W-02 的既有调试通道）
            sim.DebugSeedShopOffer();       // 让商店货架非空（否则 m_ShopItems 只能 SKIP）

            // 让被 count 门控的容器真的有内容（否则数组元素验证不了）
            EnsureNonEmptyGatedContainers(sim);
            return sim;
        }

        /// <summary>把 `PendingCount`/槽位 Buff 填成非空 —— 纯粹是为了让守门测试能验证到元素级。</summary>
        private static void EnsureNonEmptyGatedContainers(LockstepSimulation sim)
        {
            for (int i = 0; i < sim.DebugPlayerCount; i++)
            {
                var p = sim.DebugPlayerAt(i);
                if (p == null) { continue; }
                ForcePending(ref p.PrimaryCast);
                ForcePending(ref p.SecondaryCast);
            }
        }

        private static void ForcePending(ref CastRuntimeState st)
        {
            if (st.Pending == null) { return; }
            if (st.Pending.Length == 0) { return; }
            if (st.PendingCount <= 0)
            {
                st.Pending[0].SlotIndex = 0;
                st.Pending[0].DueFrame = st.FrameIndex + 5;
                st.Pending[0].Depth = 0;
                st.PendingCount = 1;
            }
        }

        // ==================== 路径枚举 ====================

        private sealed class Candidate
        {
            public string Root;
            public string Key;              // "Type.Field.Sub"
            public FieldInfo[] Path;
        }

        private static void EnumeratePaths(RootSpec spec, List<Candidate> outList)
        {
            var fields = new List<FieldInfo>();
            if (spec.PrivateSimFields)
            {
                var all = spec.Type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < all.Length; i++)
                {
                    // 只取项目手写的状态字段（m_ 前缀）；跳过编译器生成的 <Property>k__BackingField
                    if (all[i].Name.StartsWith("m_", StringComparison.Ordinal)) { fields.Add(all[i]); }
                }
                fields.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            }
            else
            {
                var all = spec.Type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                fields.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                for (int i = 0; i < all.Length; i++) { fields.Add(all[i]); }
            }

            for (int i = 0; i < fields.Count; i++)
            {
                Recurse(spec.Name, spec.RootTag, fields[i].Name, fields[i].FieldType,
                    new List<FieldInfo> { fields[i] }, outList);
            }
        }

        private static void Recurse(string typeName, string root, string path, Type fieldType,
            List<FieldInfo> chain, List<Candidate> outList)
        {
            if (CanRecurseInto(fieldType))
            {
                var sub = fieldType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                if (sub.Length > 0)
                {
                    Array.Sort(sub, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                    for (int i = 0; i < sub.Length; i++)
                    {
                        var next = new List<FieldInfo>(chain);
                        next.Add(sub[i]);
                        Recurse(typeName, root, path + "." + sub[i].Name, sub[i].FieldType, next, outList);
                    }
                    return;
                }
            }

            outList.Add(new Candidate
            {
                Root = root,
                Key = typeName + "." + path,
                Path = chain.ToArray(),
            });
        }

        /// <summary>可展开的字段类型：可变的非只读结构体（只读结构体/引用类型/数组都当叶子处理）。</summary>
        private static bool CanRecurseInto(Type t)
        {
            if (!t.IsValueType) { return false; }        // 引用类型（含数组）→ 叶子
            if (t.IsPrimitive || t.IsEnum) { return false; }
            if (t == typeof(decimal)) { return false; }
            if (t == typeof(SimVec2)) { return false; }   // 位置/方向按其"整体"登记，不拆 x/y
            if (IsReadOnlyStruct(t)) { return false; }   // readonly struct 不可变 → 叶子（入哈希需显式处理）
            return true;
        }

        private static bool IsReadOnlyStruct(Type t)
        {
            var attrs = t.GetCustomAttributes(false);
            for (int i = 0; i < attrs.Length; i++)
            {
                if (attrs[i].GetType().Name == "IsReadOnlyAttribute") { return true; }
            }
            return false;
        }

        // ==================== 变更（含写回） ====================

        /// <summary>
        /// 把路径末端的值改成一个"不同的值"，并把改动**写回根对象**（值类型字段要整体回写）。
        /// 返回 false 表示当前状态下无法安全变更（调用方记为 SKIP 并输出原因）。
        /// </summary>
        private static bool TryMutate(object root, FieldInfo[] path, out string note)
        {
            note = null;
            if (root == null || path == null || path.Length == 0) { note = "路径为空"; return false; }

            // 逐级解包（值类型得到装箱副本；写回时从内向外）
            var boxes = new object[path.Length];
            object cur = root;
            for (int i = 0; i < path.Length; i++)
            {
                object v;
                try { v = path[i].GetValue(cur); }
                catch (Exception e) { note = "读取失败: " + e.GetType().Name; return false; }
                if (v == null) { note = "值为 null"; return false; }
                boxes[i] = v;
                cur = v;
            }

            object mutated;
            if (!TryMutateValue(boxes[path.Length - 1], path[path.Length - 1].FieldType, out mutated, out note))
            {
                return false;
            }
            boxes[path.Length - 1] = mutated;

            try
            {
                for (int i = path.Length - 2; i >= 0; i--)
                {
                    path[i + 1].SetValue(boxes[i], boxes[i + 1]);
                }
                path[0].SetValue(root, boxes[0]);
            }
            catch (Exception e) { note = "写回失败: " + e.GetType().Name; return false; }

            return true;
        }

        private static bool TryMutateValue(object v, Type t, out object mutated, out string note)
        {
            mutated = null;
            note = null;

            if (t == typeof(int)) { mutated = (int)v + 1; return true; }
            if (t == typeof(long)) { mutated = (long)v + 1L; return true; }
            if (t == typeof(uint)) { mutated = (uint)v + 1u; return true; }
            if (t == typeof(short)) { mutated = (short)((short)v + 1); return true; }
            if (t == typeof(byte)) { mutated = (byte)((byte)v + 1); return true; }
            if (t == typeof(float)) { mutated = (float)v + 1f; return true; }
            if (t == typeof(double)) { mutated = (double)v + 1.0; return true; }
            if (t == typeof(bool)) { mutated = !(bool)v; return true; }
            if (t == typeof(string)) { mutated = (string)v + "X"; return true; }
            if (t.IsEnum)
            {
                var vals = Enum.GetValues(t);
                if (vals.Length <= 1) { note = "枚举只有一个取值"; return false; }
                mutated = vals.GetValue(1);
                return true;
            }
            if (t == typeof(SimVec2))
            {
                var vv = (SimVec2)v;
                mutated = new SimVec2(vv.x + 1f, vv.y);
                return true;
            }

            var arr = v as Array;
            if (arr != null)
            {
                if (arr.Length == 0) { note = "空数组"; return false; }
                object elem = arr.GetValue(0);
                if (elem == null) { note = "数组首元素为 null"; return false; }
                object elemMut;
                string elemNote;
                if (!TryMutateValue(elem, t.GetElementType(), out elemMut, out elemNote))
                {
                    note = "数组首元素无法变更（" + elemNote + "）";
                    return false;
                }
                arr.SetValue(elemMut, 0);   // 原地改：写回数组引用本身即可
                mutated = arr;
                return true;
            }

            var list = v as IList;
            if (list != null)
            {
                if (list.Count == 0) { note = "空列表"; return false; }
                object elem = list[0];
                if (elem == null) { note = "列表首元素为 null"; return false; }
                object elemMut;
                string elemNote;
                if (!TryMutateValue(elem, elem.GetType(), out elemMut, out elemNote))
                {
                    note = "列表首元素无法变更（" + elemNote + "）";
                    return false;
                }
                list[0] = elemMut;
                mutated = list;
                return true;
            }

            // 结构体（含 readonly struct 之外的不可变情况）：找第一个可变更的子字段（含私有字段）
            if (t.IsValueType)
            {
                var subs = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Array.Sort(subs, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                for (int i = 0; i < subs.Length; i++)
                {
                    if (subs[i].IsStatic || subs[i].IsInitOnly) { continue; }
                    object sv;
                    try { sv = subs[i].GetValue(v); }
                    catch (Exception) { continue; }
                    if (sv == null) { continue; }
                    object svMut;
                    string sNote;
                    if (!TryMutateValue(sv, subs[i].FieldType, out svMut, out sNote)) { continue; }
                    try { subs[i].SetValue(v, svMut); }
                    catch (Exception) { continue; }
                    mutated = v;
                    return true;
                }
                note = "结构体内找不到可变更的子字段";
                return false;
            }

            note = "引用类型（" + t.Name + "）无法通用变更";
            return false;
        }
    }
}

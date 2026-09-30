//------------------------------------------------------------
// EmojiWar GameMain - 无头回放器（W-03 / 报告 B4）
//
// 清单原话：「能否在不启动渲染的情况下，用录制的输入把一局重放一遍，
//            并且两次重放得到相同的最终哈希？**这是验证确定性最有效的自动化手段。**」
//
// 本文件就是那个手段：读一局录像 → 用**录像头里的初始状态**重跑输入流 →
// 逐帧比对状态哈希 → 报出**首个不一致的帧号**（而不是只说"不同步"）。
//
// 关键口径（决定对拍能不能对上）：
//   · 录像里每帧的 stateHash 是 **进入该帧时**（Tick 之前）的状态哈希；
//     回放时也必须在 Tick **之前**算同一次哈希再比对。
//   · 初始状态全部来自录像头（玩家数值 + 战斗参数 + 装备 Id），
//     **不读 GameEntry.Data**（否则数据资产一改，回放就会假失败）。
//   · 装备 Id → CastProgram 的重建需要物品数据表（ConfigItemTable 依赖 ConfigService），
//     所以回放必须在**构建版**里跑（`-replay <path>`）。表不可用时返回
//     "无法重建 loadout" 这一独立结论，绝不伪装成"不同步"。
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>无头回放：读录像 → 重跑 → 逐帧哈希对拍。</summary>
    public static class ReplayPlayer
    {
        /// <summary>回放结果。</summary>
        public struct Result
        {
            public bool Ok;                     // 回放跑完且逐帧哈希全对
            public bool ConfigRebuildFailed;    // 装备/物品表不可用（≠ 不同步）
            /// <summary>
            /// 录像记录的代码指纹与当前构建不同（≠ 不同步）。
            /// **录像里的逐帧哈希是"哈希算法 + 该次运行状态"的函数** —— 只要哈希覆盖的字段集变了
            /// （W-08 就改过一轮），旧录像从**第 1 帧**就不可能对上。此时报"分歧"是**误导**：
            /// 真正该做的是"用当前构建重新录一份"。这个标志位把两种情况分开。
            /// </summary>
            public bool FingerprintMismatch;
            public string RecordedFingerprint;
            public string CurrentFingerprint;
            public int FramesReplayed;
            public int FirstMismatchFrame;      // -1 = 无分歧
            public long ExpectedHashAtMismatch;
            public long ActualHashAtMismatch;
            public long FinalHash;
            public string Message;

            /// <summary>逐帧哈希序列（用于"两次回放序列必须相同"的比对）。</summary>
            public List<long> HashSequence;
        }

        /// <summary>
        /// 回放一局。collectSequence=true 时同时收集逐帧哈希序列。
        /// [W-11] <paramref name="dumpPartsFrames"/> &gt; 0 时，把**前 N 帧**的分段哈希打进日志
        /// （`[parts] frame=… world=0x… enemies=0x…`）—— 跨后端对拍时用它把"整体哈希不同"
        /// 缩到"哪一段状态先不同"。只读诊断，不影响回放结果。
        /// </summary>
        public static Result Run(string path, bool collectSequence = false, int maxFrames = 0,
            int dumpPartsFrames = 0)
        {
            var result = new Result
            {
                FirstMismatchFrame = -1,
                HashSequence = collectSequence ? new List<long>() : null,
            };

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                result.Message = "录像文件不存在: " + path;
                return result;
            }

            ReplayRecorder.ReplayHeader header;
            try
            {
                header = ReplayRecorder.ReadHeader(path);
            }
            catch (System.Exception e)
            {
                result.Message = "读取录像头失败: " + e.Message;
                return result;
            }

            // ---- 用录像头重建初始状态（不读数据资产）----
            var sim = new LockstepSimulation();

            // 战斗参数：原样写回（录像已自包含）
            sim.EnemiesPerWaveBase = header.EnemiesPerWaveBase;
            sim.EnemiesPerWaveGrowth = header.EnemiesPerWaveGrowth;
            sim.SpawnRadius = header.SpawnRadius;
            sim.EnemySpawnInterval = header.EnemySpawnInterval;
            sim.EnemyBaseHp = header.EnemyBaseHp;
            sim.EnemyHpPerWave = header.EnemyHpPerWave;
            sim.EnemyBaseSpeed = header.EnemyBaseSpeed;
            sim.EnemySpeedPerWave = header.EnemySpeedPerWave;
            sim.ShopDuration = header.ShopDuration;
            sim.ShopItemCount = header.ShopItemCount;
            sim.WeaponPrice = header.WeaponPrice;
            sim.ModPrice = header.ModPrice;

            // 玩家：数值来自录像头；CastProgram 由装备 Id 重建（需要物品表）
            var configs = new List<SimPlayerConfig>(header.Players.Count);
            for (int i = 0; i < header.Players.Count; i++)
            {
                SimPlayerConfig src = header.Players[i];

                PlayerLoadoutIds ids;
                if (!LoadoutWire.TryFind(header.Loadouts, src.EntityId, out ids))
                {
                    ids = default(PlayerLoadoutIds);
                }

                var cfg = SimHooks.BuildConfigFromIds(src.SessionId, src.EntityId, src.CharacterId, ids);

                // 覆盖为录像里的数值（自包含：与重放时的数据资产是否一致无关）
                cfg.MoveSpeed = src.MoveSpeed;
                cfg.WeaponId = src.WeaponId;
                cfg.WeaponName = src.WeaponName;
                cfg.WeaponDamage = src.WeaponDamage;
                cfg.FireRate = src.FireRate;
                cfg.MaxAmmo = src.MaxAmmo;
                cfg.ReloadTime = src.ReloadTime;
                cfg.BulletSpeed = src.BulletSpeed;
                cfg.StartPosition = src.StartPosition;

                // 装备重建失败检测：录像说"有杖"但重建后无效 → 物品表不可用/数据不一致
                if (ids.HasLeftHand && !cfg.PrimaryProgram.IsValid)
                {
                    result.ConfigRebuildFailed = true;
                }
                if (ids.HasRightHand && !cfg.SecondaryProgram.IsValid)
                {
                    result.ConfigRebuildFailed = true;
                }

                configs.Add(cfg);
            }

            if (result.ConfigRebuildFailed)
            {
                result.Message = "无法重建 loadout：录像里有杖，但按 Id 编译失败（物品数据表未就绪或数据不一致）。"
                    + "这不是不同步 —— 请确认在**构建版**里跑回放（-replay），而不是 EditMode。";
                return result;
            }

            sim.Initialize(header.Seed, configs);

            // ---- W-08：先判定"录像是不是当前这份构建录的" ----
            // 逐帧哈希 = f(哈希算法, 状态)。哈希覆盖的字段集一变（W-08 就改过），
            // 旧录像**从第 1 帧**就不可能对上；此时报"不同步"会把排查引向完全错误的方向。
            result.RecordedFingerprint = header.VersionFingerprint;
            result.CurrentFingerprint = SimBuildInfo.Describe;
            // [W-11b] 按**比较用**口径比（丢掉 `|build=...` 诊断尾巴）：录像头里存的是完整串，
            // 直接字符串相等会把"同一份代码、不同一次编译/不同后端"误判成旧录像。
            result.FingerprintMismatch = !string.IsNullOrEmpty(header.VersionFingerprint)
                && !SimBuildInfo.SameCode(header.VersionFingerprint, result.CurrentFingerprint);

            // ★ 必须复刻 Host 在录像开始**之前**做过的初始化步骤，否则初始状态就不同、第 1 帧即假失败。
            // Host 的顺序是：InitializeSimulation(seed) → PrepareFirstWave() → ReplayRecorder.Begin(...)
            // PrepareFirstWave 会开"准备阶段商店"并**消耗一次 RNG**（GenerateShopItems），顺序不能颠倒。
            sim.PrepareFirstWave();

            // ---- 逐帧重跑 + 对拍 ----
            var inputs = new Dictionary<int, PlayerIntent>(configs.Count);
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                // 跳过头部（与 ReadHeader 同序；抽成 SkipHeader 让版本升级时只有一份逻辑）
                SkipHeader(reader);

                int frameIndex;
                long expectedHash;
                List<FrameEvent> frameEvents;
                List<int> entityIds;
                List<PlayerIntent> frameInputs;

                while (ReplayRecorder.ReadFrame(reader, out frameIndex, out expectedHash,
                    out frameEvents, out entityIds, out frameInputs))
                {
                    // [W-11] 分段诊断：只在前 N 帧收集，避免长录像刷爆日志
                    List<KeyValuePair<string, long>> parts =
                        (dumpPartsFrames > 0 && result.FramesReplayed < dumpPartsFrames)
                        ? new List<KeyValuePair<string, long>>() : null;

                    long beforeHash = sim.ComputeStateHash(parts);
                    if (parts != null)
                    {
                        var sb = new System.Text.StringBuilder(160);
                        sb.Append("[parts] frame=").Append(frameIndex)
                          .Append(" total=0x").Append(beforeHash.ToString("X16"));
                        for (int k = 0; k < parts.Count; k++)
                        {
                            sb.Append(' ').Append(parts[k].Key).Append("=0x")
                              .Append(parts[k].Value.ToString("X16"));
                        }
                        SimLog.Log(sb.ToString());
                    }

                    if (collectSequence) { result.HashSequence.Add(beforeHash); }

                    if (beforeHash != expectedHash && result.FirstMismatchFrame < 0)
                    {
                        result.FirstMismatchFrame = frameIndex;
                        result.ExpectedHashAtMismatch = expectedHash;
                        result.ActualHashAtMismatch = beforeHash;
                    }

                    inputs.Clear();
                    for (int i = 0; i < entityIds.Count && i < frameInputs.Count; i++)
                    {
                        inputs[entityIds[i]] = frameInputs[i];
                    }

                    // [W-16] 把录像里的确定性**帧事件**在 Tick 之前应用（与录制时同口径）。
                    // 应用逻辑与运行时**共用同一份**（`SimFrameEvents.Apply`）——
                    // 两边各写一份"怎么应用"必然会漂移，而回放对拍是确定性回归网的地基。
                    for (int i = 0; i < frameEvents.Count; i++)
                    {
                        var e = frameEvents[i];
                        string note;
                        if (!SimHooks.ApplyFrameEvent(sim, e, out note))
                        {
                            // 事件应用失败（例如武器 Id 查不到）本身就是"回放环境不一致"的信号，
                            // 但不要中断回放：让逐帧哈希对拍把分歧报出来，比在这里静默跳过更可信。
                            SimLog.LogWarning("[Replay] 帧 " + frameIndex + " 事件未生效: " + note);
                        }
                    }

                    sim.Tick(inputs);
                    result.FramesReplayed++;

                    if (maxFrames > 0 && result.FramesReplayed >= maxFrames)
                    {
                        break;
                    }
                }
            }

            result.FinalHash = sim.ComputeStateHash();
            result.Ok = result.FirstMismatchFrame < 0;
            if (result.Ok)
            {
                result.Message = string.Format("回放完成：{0} 帧逐帧哈希全部一致，最终哈希 0x{1:X16}",
                    result.FramesReplayed, result.FinalHash);
            }
            else if (result.FingerprintMismatch)
            {
                // 关键区分：这不是"不同步"，而是"录像不是这份构建录的"。
                result.Message = string.Format(
                    "录像来自**另一个构建**（不是不同步！）：录像指纹={0} 当前指纹={1}。"
                    + "逐帧哈希是'哈希算法+状态'的函数，哈希覆盖的字段集一变，旧录像从第 1 帧就对不上。"
                    + "请用当前构建重新录一份（跑一次双实例对局）后再回放。首个不一致在第 {2} 帧。",
                    result.RecordedFingerprint, result.CurrentFingerprint, result.FirstMismatchFrame);
            }
            else
            {
                result.Message = string.Format("发现分歧：首个不一致在第 {0} 帧（录像=0x{1:X16} 回放=0x{2:X16}）",
                    result.FirstMismatchFrame, result.ExpectedHashAtMismatch, result.ActualHashAtMismatch);
            }
            return result;
        }

        /// <summary>
        /// 跳过录像头（与 ReadHeader 同序）。**两处必须同步修改** —— 抽成一处是为了
        /// 让版本升级时只有一份跳过逻辑。
        /// </summary>
        private static void SkipHeader(BinaryReader reader)
        {
            reader.ReadInt32();                 // FormatVersion
            reader.ReadString();                // VersionFingerprint
            reader.ReadUInt64();                // ConfigHash
            reader.ReadInt32();                 // Seed

            reader.ReadInt32();                 // EnemiesPerWaveBase
            reader.ReadInt32();                 // EnemiesPerWaveGrowth
            reader.ReadSingle();                // SpawnRadius
            reader.ReadSingle();                // EnemySpawnInterval
            reader.ReadSingle();                // EnemyBaseHp
            reader.ReadSingle();                // EnemyHpPerWave
            reader.ReadSingle();                // EnemyBaseSpeed
            reader.ReadSingle();                // EnemySpeedPerWave
            reader.ReadSingle();                // ShopDuration
            reader.ReadInt32();                 // ShopItemCount
            reader.ReadInt32();                 // WeaponPrice
            reader.ReadInt32();                 // ModPrice

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                reader.ReadInt32();             // SessionId
                reader.ReadInt32();             // EntityId
                reader.ReadInt32();             // CharacterId
                reader.ReadSingle();            // StartPosition.x
                reader.ReadSingle();            // StartPosition.y
                reader.ReadSingle();            // MoveSpeed
                reader.ReadInt32();             // WeaponId
                reader.ReadString();            // WeaponName
                reader.ReadSingle();            // WeaponDamage
                reader.ReadSingle();            // FireRate
                reader.ReadInt32();             // MaxAmmo
                reader.ReadSingle();            // ReloadTime
                reader.ReadSingle();            // BulletSpeed
            }

            reader.ReadString();                // LoadoutWire
        }

        /// <summary>找出目录里最新的录像文件（回放门禁用）。</summary>
        public static string FindNewest(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return null; }
            string newest = null;
            System.DateTime newestTime = System.DateTime.MinValue;
            foreach (var f in Directory.GetFiles(dir, "replay_*.bin"))
            {
                var t = File.GetLastWriteTime(f);
                if (t > newestTime) { newestTime = t; newest = f; }
            }
            return newest;
        }
    }
}

//------------------------------------------------------------
// EmojiWar GameMain - 输入流录像器（文档 §3 录像 = 初始状态 + 输入流）
//
// 每局记录：版本指纹 + 配置哈希 + 随机种子 + **完整初始状态** + 每帧输入 **与逐帧状态哈希**。
// 重放 = 用同一初始状态重跑同一输入流；逐帧哈希必须与录像内记录的一致。
//
// W-03 改造（报告 B3/B4，2026-09-28）：
//   旧格式只写 SessionId/EntityId/CharacterId/StartPosition —— **不写 loadout（CastProgram）**，
//   而 CastProgram 恰恰是决定弹道/mana 的部分（见 W-06）。结果：录像根本无法复现一局，
//   B4「无头回放 + 哈希回归」因此完全无法建立。
//   新格式（FormatVersion=2）把"重放所需的一切"都写进头：
//     · 逐玩家的**数值配置**（MoveSpeed/WeaponId/Damage/FireRate/MaxAmmo/ReloadTime/BulletSpeed）
//       —— 自包含，不依赖 GameEntry.Data 在重放时被正确加载；
//     · **战斗参数**（波次人数/血量/速度/商店等你个 12 个值）—— 直接从模拟实例读，重放时原样写回；
//     · **装备 Id 表**（LoadoutWire）—— 重放时用 LoadoutCompiler.CompileFromIds 重建 CastProgram；
//     · **逐帧状态哈希** —— 重放时逐帧对拍，不一致立刻报出帧号。
//
// ⚠️ 已知限制：装备 Id → CastProgram 的重建需要**物品数据表**（ConfigItemTable 依赖
//   ConfigService/GameEntry.Data，不是自包含的）。因此回放必须在**构建版**里跑
//   （`-replay <path>`），而不是 EditMode 测试里。数据表缺失时 ReplayPlayer 会报
//   "无法重建 loadout" 这一独立结论，而不是伪装成"不同步"。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>
    /// 输入流录像：记录与重放。
    /// </summary>
    public static class ReplayRecorder
    {
        /// <summary>录像格式版本（不兼容改动时递增）。
        /// 2 = W-03 自包含头 + 逐帧哈希；3 = 每帧另记确定性指令（商店继续等，见 SimCommandKind）。</summary>
        /// <summary>
        /// 录像格式版本。**3 → 4（W-16）**：逐帧数据里记的从"命令队列"改成"**帧事件**"。
        /// 记事件而不是命令队列的原因：命令队列是**由事件派生**的（只有 ShopContinue 会入队），
        /// 而换武器/换角色这类直接改模拟的事件根本不会进队列 —— 只记队列会让回放从那一帧起分叉。
        /// 版本不匹配时 ReadHeader 会**明确抛错**（而不是按新格式硬解析成垃圾数据）。
        /// </summary>
        public const int FormatVersion = 4;

        private static BinaryWriter s_Writer = null;
        private static string s_Path = null;
        private static bool s_Recording = false;

        /// <summary>是否正在录像。</summary>
        public static bool IsRecording { get { return s_Recording; } }

        /// <summary>当前录像路径（诊断用）。</summary>
        public static string RecordingPath { get { return s_Path; } }

        /// <summary>
        /// 开始录像（战斗开始时调用）。
        /// </summary>
        /// <param name="seed">随机种子</param>
        /// <param name="versionFingerprint">版本指纹（代码/内容版本）</param>
        /// <param name="configHash">ConfigService.VersionHash（重放时比对，不一致只告警不失败）</param>
        /// <param name="sim">模拟实例（读波次/商店参数原样写进头，保证重放自包含）</param>
        /// <param name="players">玩家配置（写完整数值，不依赖重放时的数据加载）</param>
        /// <param name="loadoutWire">LoadoutWire 编码的装备 Id 表（重放时据此重建 CastProgram）</param>
        /// <param name="outputDir">输出目录</param>
        public static void Begin(int seed, string versionFingerprint, ulong configHash,
            LockstepSimulation sim, IList<SimPlayerConfig> players, string loadoutWire, string outputDir)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                string filename = string.Format("replay_{0}_{1}.bin",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"), System.Diagnostics.Process.GetCurrentProcess().Id);
                s_Path = Path.Combine(outputDir, filename);

                s_Writer = new BinaryWriter(File.Create(s_Path));
                s_Writer.Write(FormatVersion);
                s_Writer.Write(versionFingerprint ?? "unknown");
                s_Writer.Write(configHash);
                s_Writer.Write(seed);

                // 战斗参数（自包含：重放时原样写回模拟，不依赖 SO 资产未变）
                s_Writer.Write(sim != null ? sim.EnemiesPerWaveBase : 3);
                s_Writer.Write(sim != null ? sim.EnemiesPerWaveGrowth : 2);
                s_Writer.Write(sim != null ? sim.SpawnRadius : 8f);
                s_Writer.Write(sim != null ? sim.EnemySpawnInterval : 0.5f);
                s_Writer.Write(sim != null ? sim.EnemyBaseHp : 30f);
                s_Writer.Write(sim != null ? sim.EnemyHpPerWave : 5f);
                s_Writer.Write(sim != null ? sim.EnemyBaseSpeed : 2.5f);
                s_Writer.Write(sim != null ? sim.EnemySpeedPerWave : 0.3f);
                s_Writer.Write(sim != null ? sim.ShopDuration : 8f);
                s_Writer.Write(sim != null ? sim.ShopItemCount : 3);
                s_Writer.Write(sim != null ? sim.WeaponPrice : 80);
                s_Writer.Write(sim != null ? sim.ModPrice : 60);

                // 玩家：完整数值配置
                s_Writer.Write(players != null ? players.Count : 0);
                if (players != null)
                {
                    foreach (var p in players)
                    {
                        s_Writer.Write(p.SessionId);
                        s_Writer.Write(p.EntityId);
                        s_Writer.Write(p.CharacterId);
                        s_Writer.Write(p.StartPosition.x);
                        s_Writer.Write(p.StartPosition.y);
                        s_Writer.Write(p.MoveSpeed);
                        s_Writer.Write(p.WeaponId);
                        s_Writer.Write(p.WeaponName ?? string.Empty);
                        s_Writer.Write(p.WeaponDamage);
                        s_Writer.Write(p.FireRate);
                        s_Writer.Write(p.MaxAmmo);
                        s_Writer.Write(p.ReloadTime);
                        s_Writer.Write(p.BulletSpeed);
                    }
                }

                // 装备 Id 表（重放时据此重建 CastProgram）
                s_Writer.Write(loadoutWire ?? string.Empty);

                s_Recording = true;
            }
            catch (Exception e)
            {
                SimLog.LogWarning("[Replay] 开始录像失败: " + e.Message);
                s_Recording = false;
                s_Writer = null;
            }
        }

        /// <summary>
        /// 记录一帧输入 **与该帧的状态哈希**（Host 广播的输入帧内容；各端一致）。
        /// stateHash 由调用方在 Tick **之前**算好（= 进入该帧时的状态），重放时同口径对拍。
        /// </summary>
        public static void RecordFrame(int frameIndex, long stateHash,
            IReadOnlyList<FrameEvent> events, IList<int> entityIds, IList<PlayerIntent> inputs)
        {
            if (!s_Recording || s_Writer == null)
            {
                return;
            }

            try
            {
                s_Writer.Write(frameIndex);
                s_Writer.Write(stateHash);

                // [W-16] 本帧**将要执行**的确定性事件（顺序即语义）。
                // 原先记的是"命令队列"（`Simulation.PendingCommands`）—— 那是**由事件派生**的
                // （只有 ShopContinue 事件会往队列里放东西），所以记事件即可，记两份会**重复应用**。
                // 而只记命令队列是不够的：换武器/换角色这类**直接改模拟**的事件根本不会进队列，
                // 于是回放从那一帧起就分叉（实测：武器更新发生在第 3 帧 → 回放第 3 帧报分歧）。
                int evCount = events != null ? events.Count : 0;
                s_Writer.Write(evCount);
                for (int i = 0; i < evCount; i++)
                {
                    s_Writer.Write(events[i].Kind);
                    s_Writer.Write(events[i].Arg0);
                    s_Writer.Write(events[i].Arg1);
                }

                int count = entityIds != null ? entityIds.Count : 0;
                s_Writer.Write(count);
                for (int i = 0; i < count; i++)
                {
                    s_Writer.Write(entityIds[i]);
                    PlayerIntent inp = inputs[i];
                    s_Writer.Write(inp.MoveX);
                    s_Writer.Write(inp.MoveY);
                    s_Writer.Write(inp.AimX);
                    s_Writer.Write(inp.AimY);
                    s_Writer.Write(inp.FirePrimary);
                    s_Writer.Write(inp.FireSecondary);
                    s_Writer.Write(inp.Reload);
                }
            }
            catch (Exception e)
            {
                SimLog.LogWarning("[Replay] 记录帧失败: " + e.Message);
            }
        }

        /// <summary>结束录像（战斗结束/回房间时调用；强制 flush，防尾帧丢失）。</summary>
        public static void End()
        {
            if (!s_Recording || s_Writer == null)
            {
                s_Recording = false;
                return;
            }
            try
            {
                s_Writer.Flush();
                s_Writer.Dispose();
                SimLog.Log("[Replay] 录像已保存: " + s_Path);
            }
            catch (Exception e)
            {
                SimLog.LogWarning("[Replay] 结束录像失败: " + e.Message);
            }
            s_Writer = null;
            s_Recording = false;
        }

        // ==================== 重放（读取侧） ====================

        /// <summary>录像头部信息（W-03：自包含 —— 重放不需要读数据资产）。</summary>
        public sealed class ReplayHeader
        {
            public int FormatVersion;
            public string VersionFingerprint;
            public ulong ConfigHash;
            public int Seed;

            // 战斗参数
            public int EnemiesPerWaveBase;
            public int EnemiesPerWaveGrowth;
            public float SpawnRadius;
            public float EnemySpawnInterval;
            public float EnemyBaseHp;
            public float EnemyHpPerWave;
            public float EnemyBaseSpeed;
            public float EnemySpeedPerWave;
            public float ShopDuration;
            public int ShopItemCount;
            public int WeaponPrice;
            public int ModPrice;

            public readonly List<SimPlayerConfig> Players = new List<SimPlayerConfig>();

            /// <summary>LoadoutWire 编码的装备 Id 表。</summary>
            public string LoadoutWire = string.Empty;

            /// <summary>解码后的装备 Id 表（ReadHeader 里填好）。</summary>
            public readonly List<PlayerLoadoutIds> Loadouts = new List<PlayerLoadoutIds>();

            public bool LoadoutsDecoded;
        }

        /// <summary>读取录像头部（供重放器初始化）。格式不兼容时抛 InvalidDataException。</summary>
        public static ReplayHeader ReadHeader(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                var header = new ReplayHeader();
                header.FormatVersion = reader.ReadInt32();
                if (header.FormatVersion != FormatVersion)
                {
                    throw new InvalidDataException(string.Format(
                        "录像格式版本不兼容：文件={0} 当前={1}（旧录像缺少 loadout 与逐帧哈希，无法重放）",
                        header.FormatVersion, FormatVersion));
                }

                header.VersionFingerprint = reader.ReadString();
                header.ConfigHash = reader.ReadUInt64();
                header.Seed = reader.ReadInt32();

                header.EnemiesPerWaveBase = reader.ReadInt32();
                header.EnemiesPerWaveGrowth = reader.ReadInt32();
                header.SpawnRadius = reader.ReadSingle();
                header.EnemySpawnInterval = reader.ReadSingle();
                header.EnemyBaseHp = reader.ReadSingle();
                header.EnemyHpPerWave = reader.ReadSingle();
                header.EnemyBaseSpeed = reader.ReadSingle();
                header.EnemySpeedPerWave = reader.ReadSingle();
                header.ShopDuration = reader.ReadSingle();
                header.ShopItemCount = reader.ReadInt32();
                header.WeaponPrice = reader.ReadInt32();
                header.ModPrice = reader.ReadInt32();

                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var p = new SimPlayerConfig
                    {
                        SessionId = reader.ReadInt32(),
                        EntityId = reader.ReadInt32(),
                        CharacterId = reader.ReadInt32(),
                    };
                    p.StartPosition = new SimVec2(reader.ReadSingle(), reader.ReadSingle());
                    p.MoveSpeed = reader.ReadSingle();
                    p.WeaponId = reader.ReadInt32();
                    p.WeaponName = reader.ReadString();
                    p.WeaponDamage = reader.ReadSingle();
                    p.FireRate = reader.ReadSingle();
                    p.MaxAmmo = reader.ReadInt32();
                    p.ReloadTime = reader.ReadSingle();
                    p.BulletSpeed = reader.ReadSingle();
                    header.Players.Add(p);
                }

                header.LoadoutWire = reader.ReadString();
                header.LoadoutsDecoded = LoadoutWire.TryDecode(header.LoadoutWire, header.Loadouts);

                return header;
            }
        }

        /// <summary>
        /// 逐帧读取输入（返回 false 表示结束）。
        /// stateHash = 进入该帧时的状态哈希（录制时写下的值）。
        /// </summary>
        public static bool ReadFrame(BinaryReader reader, out int frameIndex, out long stateHash,
            out List<FrameEvent> events, out List<int> entityIds, out List<PlayerIntent> inputs)
        {
            frameIndex = 0;
            stateHash = 0;
            events = new List<FrameEvent>();
            entityIds = new List<int>();
            inputs = new List<PlayerIntent>();
            try
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                {
                    return false;
                }
                frameIndex = reader.ReadInt32();
                stateHash = reader.ReadInt64();

                int evCount = reader.ReadInt32();
                for (int i = 0; i < evCount; i++)
                {
                    events.Add(new FrameEvent
                    {
                        Kind = reader.ReadByte(),
                        Arg0 = reader.ReadInt32(),
                        Arg1 = reader.ReadInt32(),
                    });
                }

                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    entityIds.Add(reader.ReadInt32());
                    var inp = new PlayerIntent
                    {
                        MoveX = reader.ReadSingle(),
                        MoveY = reader.ReadSingle(),
                        AimX = reader.ReadSingle(),
                        AimY = reader.ReadSingle(),
                        FirePrimary = reader.ReadBoolean(),
                        FireSecondary = reader.ReadBoolean(),
                        Reload = reader.ReadBoolean(),
                    };
                    inputs.Add(inp);
                }
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }
}

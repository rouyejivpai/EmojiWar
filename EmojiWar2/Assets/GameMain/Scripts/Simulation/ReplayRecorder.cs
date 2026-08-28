//------------------------------------------------------------
// EmojiWar GameMain - 输入流录像器（文档 §3 录像 = 初始状态 + 输入流）
// 每局记录：版本指纹 + 随机种子 + 玩家初始配置 + 每帧输入意图。
// 重放 = 用同一初始状态重跑同一输入流（100% 复现）。
// 数据即二进制文件：可用于 bug 复现、性能基线、不同步 diff。
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
        /// <summary>录像格式版本（配置/代码变化时递增，识别不兼容录像）。</summary>
        public const int FormatVersion = 1;

        private static BinaryWriter s_Writer = null;
        private static string s_Path = null;
        private static bool s_Recording = false;

        /// <summary>是否正在录像。</summary>
        public static bool IsRecording { get { return s_Recording; } }

        /// <summary>开始录像（战斗开始时调用）。</summary>
        public static void Begin(int seed, string versionFingerprint, IList<SimPlayerConfig> players, string outputDir)
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
                s_Writer.Write(seed);
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
                    }
                }
                s_Recording = true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Replay] 开始录像失败: " + e.Message);
                s_Recording = false;
                s_Writer = null;
            }
        }

        /// <summary>记录一帧输入（Host 广播的输入帧内容；各端一致）。</summary>
        public static void RecordFrame(int frameIndex, IList<int> entityIds, IList<PlayerIntent> inputs)
        {
            if (!s_Recording || s_Writer == null)
            {
                return;
            }

            try
            {
                s_Writer.Write(frameIndex);
                int count = entityIds != null ? entityIds.Count : 0;
                s_Writer.Write(count);
                for (int i = 0; i < count; i++)
                {
                    s_Writer.Write(entityIds[i]);
                    var inp = inputs[i];
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
                UnityEngine.Debug.LogWarning("[Replay] 记录帧失败: " + e.Message);
            }
        }

        /// <summary>结束录像（战斗结束/回房间时调用；强制 flush，防尾帧丢失）。</summary>
        public static void End()
        {
            if (!s_Recording || s_Writer == null)
            {
                return;
            }
            try
            {
                s_Writer.Flush();
                s_Writer.Dispose();
                UnityEngine.Debug.Log("[Replay] 录像已保存: " + s_Path);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Replay] 结束录像失败: " + e.Message);
            }
            s_Writer = null;
            s_Recording = false;
        }

        // ==================== 重放 ====================

        /// <summary>录像头部信息。</summary>
        public sealed class ReplayHeader
        {
            public int FormatVersion;
            public string VersionFingerprint;
            public int Seed;
            public List<SimPlayerConfig> Players = new List<SimPlayerConfig>();
        }

        /// <summary>读取录像头部（供重放器初始化）。</summary>
        public static ReplayHeader ReadHeader(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                var header = new ReplayHeader
                {
                    FormatVersion = reader.ReadInt32(),
                    VersionFingerprint = reader.ReadString(),
                    Seed = reader.ReadInt32(),
                };
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var p = new SimPlayerConfig
                    {
                        SessionId = reader.ReadInt32(),
                        EntityId = reader.ReadInt32(),
                        CharacterId = reader.ReadInt32(),
                    };
                    p.StartPosition = new UnityEngine.Vector2(reader.ReadSingle(), reader.ReadSingle());
                    header.Players.Add(p);
                }
                return header;
            }
        }

        /// <summary>逐帧读取输入（返回 false 表示结束）。</summary>
        public static bool ReadFrame(BinaryReader reader, out int frameIndex, out List<int> entityIds, out List<PlayerIntent> inputs)
        {
            frameIndex = 0;
            entityIds = new List<int>();
            inputs = new List<PlayerIntent>();
            try
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                {
                    return false;
                }
                frameIndex = reader.ReadInt32();
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

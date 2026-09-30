//------------------------------------------------------------
// EmojiWar GameMain - 确定性打点器（文档 §4 确定性打点 + 自动 diff）
// 在逻辑关键点埋 CheckID + 一小段数据，写入二进制 trace 文件：
//   CheckID(int) | 数据长度(short) | 数据(bytes) | 逻辑帧号(int)
// 两端各跑一遍 → diff 工具对比，输出第一个有效分歧。
// 检查点原则（文档 §4）：
//   - 该埋：对象身份/创建/销毁、随机数调用、帧号、关键状态切换
//   - 不埋：恒为 0 的集合大小、每帧常量、指针地址、未排序容器遍历
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>
    /// 确定性打点器：二进制 trace 写入。
    /// </summary>
    public static class DeterminismTracer
    {
        /// <summary>CheckID 定义（新增时必须唯一；历史冲突只能减不能增）。</summary>
        public static class Check
        {
            public const int FrameStart = 1;        // 帧开始（数据：帧号）
            public const int PlayerSpawn = 2;       // 玩家生成（数据：entityId, charId）
            public const int EnemySpawn = 3;        // 敌人生成（数据：entityId, x, y, hp）
            public const int BulletSpawn = 4;       // 子弹生成（数据：entityId, x, y, dirX, dirY, speed, dmg）
            public const int RandomCall = 5;        // 随机数调用（数据：prng 状态 hash）
            public const int PlayerHp = 6;          // 玩家 HP 变化（数据：entityId, hpBits）
            public const int EnemyDeath = 7;        // 敌人死亡（数据：entityId）
            public const int WaveChange = 8;        // 波次变化（数据：waveIndex）
            public const int ShopOffer = 9;         // 商店商品（数据：商品 hash）
            public const int BattleEnd = 10;        // 战斗结束（数据：帧号）
        }

        private static BinaryWriter s_Writer = null;
        private static string s_Path = null;
        private static bool s_Enabled = false;

        /// <summary>是否启用打点。</summary>
        public static bool Enabled
        {
            get { return s_Enabled; }
            set { s_Enabled = value; }
        }

        /// <summary>开始打点（写入新 trace 文件）。</summary>
        public static void Begin(string outputDir)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                string filename = string.Format("trace_{0}_{1}.bin",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"), System.Diagnostics.Process.GetCurrentProcess().Id);
                s_Path = Path.Combine(outputDir, filename);
                s_Writer = new BinaryWriter(File.Create(s_Path));
                s_Enabled = true;
            }
            catch (Exception e)
            {
                SimLog.LogWarning("[Trace] 开始打点失败: " + e.Message);
                s_Enabled = false;
                s_Writer = null;
            }
        }

        /// <summary>结束打点。</summary>
        public static void End()
        {
            if (s_Writer != null)
            {
                try
                {
                    s_Writer.Flush();
                    s_Writer.Dispose();
                }
                catch { }
                s_Writer = null;
            }
            s_Enabled = false;
        }

        /// <summary>记录一个整型数据检查点（最常见的格式）。</summary>
        public static void RecordInt(int checkId, int data, int frameIndex)
        {
            if (!s_Enabled || s_Writer == null)
            {
                return;
            }
            try
            {
                s_Writer.Write(checkId);
                s_Writer.Write((short)4);
                s_Writer.Write(data);
                s_Writer.Write(frameIndex);
            }
            catch { }
        }

        /// <summary>记录多个整型数据检查点。</summary>
        public static void RecordInts(int checkId, int[] data, int frameIndex)
        {
            if (!s_Enabled || s_Writer == null)
            {
                return;
            }
            try
            {
                s_Writer.Write(checkId);
                s_Writer.Write((short)(data.Length * 4));
                for (int i = 0; i < data.Length; i++)
                {
                    s_Writer.Write(data[i]);
                }
                s_Writer.Write(frameIndex);
            }
            catch { }
        }

        /// <summary>
        /// 记录两个整型数据（W-10/H2：避免 `new[]{a,b}` 的**每帧分配**）。
        /// 原实现把数组字面量写在调用处，导致"即使打点关闭也在分配"（报告 H2 第 2 条）。
        /// </summary>
        public static void RecordInt2(int checkId, int a, int b, int frameIndex)
        {
            if (!s_Enabled || s_Writer == null)
            {
                return;
            }
            try
            {
                s_Writer.Write(checkId);
                s_Writer.Write((short)8);
                s_Writer.Write(a);
                s_Writer.Write(b);
                s_Writer.Write(frameIndex);
            }
            catch { }
        }

        /// <summary>记录三个整型数据（同上；替代 `RecordInts(..., new[]{...}, ...)`）。</summary>
        public static void RecordInt3(int checkId, int a, int b, int c, int frameIndex)
        {
            if (!s_Enabled || s_Writer == null)
            {
                return;
            }
            try
            {
                s_Writer.Write(checkId);
                s_Writer.Write((short)12);
                s_Writer.Write(a);
                s_Writer.Write(b);
                s_Writer.Write(c);
                s_Writer.Write(frameIndex);
            }
            catch { }
        }

        /// <summary>记录浮点数组检查点（坐标等；float 位模式转换保证跨端可比）。</summary>
        public static void RecordFloats(int checkId, float[] data, int frameIndex)
        {
            if (!s_Enabled || s_Writer == null)
            {
                return;
            }
            try
            {
                s_Writer.Write(checkId);
                // W-10：原来声明 `data.Length * 4` 却写 `DoubleToInt64Bits`（8 字节/元素）
                // → 任何调用方都会产出**结构损坏的 trace 流**（trace_diff.py 按 4 字节解析会错位）。
                // 现在统一为 4 字节的 float 位模式，与声明一致、也与 diff 工具的解析一致。
                s_Writer.Write((short)(data.Length * 4));
                for (int i = 0; i < data.Length; i++)
                {
                    s_Writer.Write(BitConverter.SingleToInt32Bits(data[i]));
                }
                s_Writer.Write(frameIndex);
            }
            catch { }
        }

        /// <summary>当前 trace 文件路径（诊断用）。</summary>
        public static string TracePath { get { return s_Path; } }
    }
}

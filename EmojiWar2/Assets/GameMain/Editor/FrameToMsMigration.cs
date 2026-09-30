//------------------------------------------------------------
// EmojiWar GameMain.Editor - 一次性数据迁移：**按帧录入 → 按毫秒录入**（W-10a）
//
// 背景：切 30Hz 会**静默改掉**所有"按帧"配置的真实时长（10 帧在 20Hz = 500ms，到 30Hz = 333ms）。
// 修法是让配置只说"真实时长"（毫秒），加载期再按当前帧率量化成帧。
// 但字段改名之后，资产里**旧的帧数值会被原样读进新字段**（`FormerlySerializedAs` 只搬值、不换算）
// → 于是"4 帧"变成"4 毫秒"，比不改还糟。所以必须有一次**显式的、幂等的**换算。
//
// 幂等靠资产上的 `SchemaVersion`：
//   0 = 仍是旧数据（帧） → 换算后置 1
//   1 = 已是毫秒 → 跳过
// 由 `SpellContentBuilder` 生成的资产直接置 1（它本来就按毫秒写值）。
//
// 换算系数 = 1000 / 20 = **50**（旧数据是在 20Hz 下配平的）。
//
// 用法：菜单 `EmojiWar/Tools/Migrate Frame→Ms (W-10a)`；结果写到 Logs/frame_to_ms_migration.txt。
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Editor
{
    public static class FrameToMsMigration
    {
        /// <summary>旧数据是按 20Hz 配平的 → 1 帧 = 50ms。</summary>
        private const int MsPerOldFrame = 1000 / 20;

        private const int TargetSchemaVersion = 1;

        [MenuItem("EmojiWar/Tools/Migrate Frame→Ms (W-10a)")]
        public static void Run()
        {
            var log = new StringBuilder();
            log.Append("[FrameToMs] MsPerOldFrame=").Append(MsPerOldFrame)
               .Append(" targetSchema=").Append(TargetSchemaVersion).Append('\n');

            int spellTotal = 0, spellMigrated = 0, spellSkipped = 0;
            int sysTotal = 0, sysMigrated = 0, sysSkipped = 0;

            // ---------- SpellSO ----------
            var spellGuids = AssetDatabase.FindAssets("t:SpellSO");
            for (int i = 0; i < spellGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(spellGuids[i]);
                var so = AssetDatabase.LoadAssetAtPath<SpellSO>(path);
                if (so == null) { continue; }
                spellTotal++;

                if (so.SchemaVersion >= TargetSchemaVersion)
                {
                    spellSkipped++;
                    continue;
                }

                // 记录换算前的值，便于人工核对
                int oldDelayMs = so.DelayMs;                 // 值来自旧 DelayFrames
                int oldCdMs = so.Passive.CooldownMs;         // 值来自旧 CooldownFrames
                int oldBuffDur = so.BuffApply.Duration;
                bool buffBySeconds = so.BuffApply.Timing == BuffTiming.BySeconds;

                so.DelayMs = oldDelayMs * MsPerOldFrame;
                var passive = so.Passive;
                passive.CooldownMs = oldCdMs * MsPerOldFrame;
                so.Passive = passive;

                var buff = so.BuffApply;
                if (buffBySeconds)
                {
                    // 时间型：旧 Duration 是帧数 → 搬到 DurationMs（毫秒），并清掉 Duration 以免双份真相
                    buff.DurationMs = oldBuffDur * MsPerOldFrame;
                    buff.Duration = 0;
                    so.BuffApply = buff;
                }
                // 次数型/施法型：Duration 就是"次数"，**绝不能乘 50**（否则"再触发 2 次"变成"100 次"）

                so.SchemaVersion = TargetSchemaVersion;
                EditorUtility.SetDirty(so);
                spellMigrated++;

                log.Append("[FrameToMs] ").Append(so.name)
                   .Append("  DelayMs ").Append(oldDelayMs).Append(" -> ").Append(so.DelayMs)
                   .Append(" | CooldownMs ").Append(oldCdMs).Append(" -> ").Append(passive.CooldownMs)
                   .Append(" | Buff(").Append(so.BuffApply.Timing).Append(") Duration=").Append(oldBuffDur)
                   .Append(buffBySeconds ? (" -> DurationMs=" + so.BuffApply.DurationMs + "（时间型，已换算）") : "（次数型，不换算）")
                   .Append('\n');
            }

            // ---------- SpellSystemConfigSO ----------
            var sysGuids = AssetDatabase.FindAssets("t:SpellSystemConfigSO");
            for (int i = 0; i < sysGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(sysGuids[i]);
                var so = AssetDatabase.LoadAssetAtPath<SpellSystemConfigSO>(path);
                if (so == null) { continue; }
                sysTotal++;

                if (so.SchemaVersion >= TargetSchemaVersion)
                {
                    sysSkipped++;
                    continue;
                }

                int oldCdMs = so.DefaultPassiveCooldownMs;   // 值来自旧 DefaultPassiveCooldownFrames
                so.DefaultPassiveCooldownMs = oldCdMs * MsPerOldFrame;
                so.SchemaVersion = TargetSchemaVersion;
                EditorUtility.SetDirty(so);
                sysMigrated++;

                log.Append("[FrameToMs] ").Append(so.name)
                   .Append("  DefaultPassiveCooldownMs ").Append(oldCdMs).Append(" -> ").Append(so.DefaultPassiveCooldownMs)
                   .Append('\n');
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.Append("[FrameToMs] SpellSO 总数=").Append(spellTotal)
               .Append(" 已换算=").Append(spellMigrated).Append(" 已是最新=").Append(spellSkipped).Append('\n');
            log.Append("[FrameToMs] SpellSystemConfigSO 总数=").Append(sysTotal)
               .Append(" 已换算=").Append(sysMigrated).Append(" 已是最新=").Append(sysSkipped).Append('\n');
            log.Append("[FrameToMs] done\n");

            Debug.Log("[FrameToMs] 迁移完成：SpellSO " + spellMigrated + "/" + spellTotal
                + "，SpellSystemConfigSO " + sysMigrated + "/" + sysTotal);

            try
            {
                string dir = Path.Combine(Application.dataPath, "../Logs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "frame_to_ms_migration.txt"), log.ToString(), Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[FrameToMs] 写迁移日志失败: " + e.Message);
            }
        }

        /// <summary>只体检不写盘：列出所有仍是旧 schema 的资产（CI/评审用）。</summary>
        [MenuItem("EmojiWar/Tools/Check Frame→Ms Migration Status")]
        public static void CheckStatus()
        {
            var pending = new List<string>();
            var guids = AssetDatabase.FindAssets("t:SpellSO");
            for (int i = 0; i < guids.Length; i++)
            {
                var so = AssetDatabase.LoadAssetAtPath<SpellSO>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (so != null && so.SchemaVersion < TargetSchemaVersion) { pending.Add("SpellSO " + so.name); }
            }
            var sguids = AssetDatabase.FindAssets("t:SpellSystemConfigSO");
            for (int i = 0; i < sguids.Length; i++)
            {
                var so = AssetDatabase.LoadAssetAtPath<SpellSystemConfigSO>(AssetDatabase.GUIDToAssetPath(sguids[i]));
                if (so != null && so.SchemaVersion < TargetSchemaVersion) { pending.Add("SpellSystemConfigSO " + so.name); }
            }

            if (pending.Count == 0)
            {
                Debug.Log("[FrameToMs] 全部资产已是毫秒版本（schema=" + TargetSchemaVersion + "）");
            }
            else
            {
                Debug.LogWarning("[FrameToMs] 仍有 " + pending.Count + " 个资产是旧 schema（帧）：" + string.Join(", ", pending.ToArray()));
            }
        }
    }
}

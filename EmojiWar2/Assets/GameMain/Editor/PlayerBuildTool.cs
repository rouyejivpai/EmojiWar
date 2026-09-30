//------------------------------------------------------------
// EmojiWar GameMain Editor - 播放器构建工具
//
// 菜单：
//   EmojiWar/Tools/Build Windows64 Player        —— 按 Build Settings 里的场景构建 Windows64 播放器
//   EmojiWar/Tools/Refresh Final Artifacts       —— 把 Builds/StandaloneWindows64/* 复制为 Builds/EmojiWar2_final*
//
// 用途：MCP 的 manage_build 通道偶发断线时，可用菜单通道稳定出包（结果写 Console + Logs/player_build.txt）。
// 产物路径：Builds/StandaloneWindows64/EmojiWar2.exe（+ EmojiWar2_Data）
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>Windows64 播放器构建与最终产物刷新。</summary>
    public static class PlayerBuildTool
    {
        private const string BuildDir = "Builds/StandaloneWindows64";
        private const string ExeName = "EmojiWar2.exe";

        [MenuItem("EmojiWar/Tools/Build Windows64 Player", false, 200)]
        public static void BuildWindows64()
        {
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s != null && s.enabled && !string.IsNullOrEmpty(s.path))
                {
                    scenes.Add(s.path);
                }
            }
            if (scenes.Count == 0)
            {
                Debug.LogError("[PlayerBuild] Build Settings 里没有启用任何场景，已中止");
                return;
            }

            Directory.CreateDirectory(BuildDir);
            string exePath = Path.Combine(BuildDir, ExeName);

            var report = BuildPipeline.BuildPlayer(scenes.ToArray(), exePath,
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            var summary = report != null ? report.summary : default(BuildSummary);

            string line = string.Format("[PlayerBuild] result={0} scenes={1} size={2:F1}MB time={3:F1}s errors={4} output={5}",
                report != null ? summary.result.ToString() : "null",
                scenes.Count,
                report != null ? summary.totalSize / 1048576f : 0f,
                report != null ? summary.totalTime.TotalSeconds : 0f,
                report != null ? summary.totalErrors : 0,
                exePath);
            Debug.Log(line);
            WriteLog(line);
        }

        [MenuItem("EmojiWar/Tools/Refresh Final Artifacts", false, 201)]
        public static void RefreshFinalArtifacts()
        {
            string srcDir = Path.GetFullPath(BuildDir);
            string dstRoot = Path.GetFullPath("Builds");
            string srcExe = Path.Combine(srcDir, ExeName);
            if (!File.Exists(srcExe))
            {
                Debug.LogError("[PlayerBuild] 找不到构建产物: " + srcExe);
                return;
            }

            CopyTree(Path.Combine("Builds", "StandaloneWindows64"), "Builds", "EmojiWar2.exe", "EmojiWar2_final.exe");
            CopyTree(Path.Combine("Builds", "StandaloneWindows64"), "Builds", "EmojiWar2_Data", "EmojiWar2_final_Data");

            string line = "[PlayerBuild] final artifacts refreshed -> Builds/EmojiWar2_final.exe";
            Debug.Log(line);
            WriteLog(line);
            AssetDatabase.Refresh();
        }

        private static void CopyTree(string srcRelative, string dstParent, string srcLeaf, string dstLeaf)
        {
            string src = Path.Combine(srcRelative, srcLeaf);
            string dst = Path.Combine(dstParent, dstLeaf);
            if (!Directory.Exists(src) && !File.Exists(src))
            {
                return;
            }
            if (Directory.Exists(dst))
            {
                Directory.Delete(dst, true);
            }
            else if (File.Exists(dst))
            {
                File.Delete(dst);
            }

            if (Directory.Exists(src))
            {
                CopyDirectory(src, dst);
            }
            else
            {
                File.Copy(src, dst, true);
            }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            }
            foreach (var dir in Directory.GetDirectories(src))
            {
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }
        }

        private static void WriteLog(string line)
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs/player_build.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line + "\n");
            }
            catch
            {
            }
        }
    }
}

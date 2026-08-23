//------------------------------------------------------------
// EmojiWar GameMain - 运行时诊断工具 v3（Editor）
// 菜单：EmojiWar/Diagnostics/Log Runtime State
// 输出写到工程 Logs/diag.txt，避免 console 截断。
//------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmojiWar.GameMain.Editor
{
    public static class RuntimeDiagnostics
    {
        [MenuItem("EmojiWar/Diagnostics/Log Runtime State")]
        public static void LogRuntimeState()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RUNTIME STATE v3 (isPlaying=" + Application.isPlaying + ") ===");
            sb.AppendLine("Scene: " + Safe(() => SceneManager.GetActiveScene().name));

            try
            {
                foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    sb.AppendLine("  Root: " + go.name);
                }
            }
            catch (Exception e) { sb.AppendLine("  Roots ERROR: " + e.Message); }

            try
            {
                var baseComp = UnityEngine.Object.FindObjectOfType<UnityGameFramework.Runtime.BaseComponent>();
                sb.AppendLine("BaseComponent: " + (baseComp != null ? "FOUND" : "NULL"));
            }
            catch (Exception e) { sb.AppendLine("BaseComponent ERROR: " + e.Message); }

            try
            {
                var procComp = UnityEngine.Object.FindObjectOfType<UnityGameFramework.Runtime.ProcedureComponent>();
                sb.AppendLine("ProcedureComponent: " + (procComp != null ? "FOUND" : "NULL"));
                if (procComp != null)
                {
                    try { sb.AppendLine("  CurrentProcedure: " + (procComp.CurrentProcedure != null ? procComp.CurrentProcedure.GetType().FullName : "NULL")); }
                    catch (Exception e) { sb.AppendLine("  CurrentProcedure ERROR: " + e.Message); }
                }
            }
            catch (Exception e) { sb.AppendLine("ProcedureComponent ERROR: " + e.Message); }

            try
            {
                var uiComp = UnityEngine.Object.FindObjectOfType<UnityGameFramework.Runtime.UIComponent>();
                sb.AppendLine("UIComponent: " + (uiComp != null ? "FOUND" : "NULL"));
                if (uiComp != null)
                {
                    try { sb.AppendLine("  UIGroupCount: " + uiComp.UIGroupCount); }
                    catch (Exception e) { sb.AppendLine("  UIGroupCount ERROR: " + e.Message); }
                }
            }
            catch (Exception e) { sb.AppendLine("UIComponent ERROR: " + e.Message); }

            try
            {
                sb.AppendLine("GameEntry(ours): " + (UnityEngine.Object.FindObjectOfType<GameEntry>() != null ? "FOUND" : "NULL"));
                sb.AppendLine("GameEntry.Procedure static: " + (GameEntry.Procedure != null ? GameEntry.Procedure.GetType().Name : "NULL"));
                sb.AppendLine("GameEntry.UI static: " + (GameEntry.UI != null ? "SET" : "NULL"));
                sb.AppendLine("GameEntry.DataTable static: " + (GameEntry.DataTable != null ? "SET" : "NULL"));
            }
            catch (Exception e) { sb.AppendLine("GameEntry ERROR: " + e.Message); }

            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                sb.AppendLine("Total GameObjects: " + all.Length);
                foreach (var go in all)
                {
                    if (go.name.Contains("GameEntry") || go.name.Contains("MenuForm") || go.name.Contains("UI Form") || go.name == "GameFramework" || go.name.Contains("Procedure"))
                    {
                        sb.AppendLine("  [" + go.GetInstanceID() + "] " + go.name + " active=" + go.activeInHierarchy + " scene=" + go.scene.name);
                    }
                }
            }
            catch (Exception e) { sb.AppendLine("Object scan ERROR: " + e.Message); }

            sb.AppendLine("=== END ===");

            try
            {
                string path = Path.Combine(Application.dataPath, "../Logs/diag.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString());
                Debug.Log("[Diagnostics] written to " + path);
            }
            catch (Exception e)
            {
                Debug.Log("[Diagnostics] file write failed: " + e.Message + "\n" + sb.ToString());
            }
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { return "ERROR: " + e.Message; }
        }
    }
}

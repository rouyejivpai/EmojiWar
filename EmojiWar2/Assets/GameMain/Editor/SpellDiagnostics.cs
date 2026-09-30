//------------------------------------------------------------
// EmojiWar GameMain Editor - 施法自检入口（S2 / D23）
//
// 菜单：EmojiWar/Tools/Spell Self-Test
// 作用：跑与构建版 -autospell 完全相同的断言套件（Scripts/Items/CastSelfTest.cs），
//       免进游戏即可回归"整序列施法 / 跨帧延迟 / Q2 中止 / 三修正 / 上限 / 控制流 / 预估一致"。
// 另附：打印每条真实序列的编译结果与 dry-run 预算（与 D21 体检同口径）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>编辑器侧施法自检入口。</summary>
    public static class SpellDiagnostics
    {
        private const string DataRoot = "Assets/GameMain/Resources/Data";

        [MenuItem("EmojiWar/Tools/Spell Self-Test", false, 156)]
        public static void RunSelfTest()
        {
            string report = CastSelfTest.Run();
            Debug.Log("[SpellTest]\n" + report);

            if (report.Contains("FAIL"))
            {
                Debug.LogError("[SpellTest] 自检存在失败项，见上方报告");
            }
        }

        /// <summary>自检报告文本（供自动化探针写文件；绕开控制台编码问题）。</summary>
        public static string InvokeForReport() { return CastSelfTest.Run(); }

        // ====================================================================
        // S3 / D24：临时 Buff 自检（与构建版 -autobuff 完全同源）
        // ====================================================================

        [MenuItem("EmojiWar/Tools/Buff Self-Test", false, 158)]
        public static void RunBuffSelfTest()
        {
            string report = BuffSelfTest.Run();
            Debug.Log("[BuffTest]\n" + report);

            if (report.Contains("FAIL"))
            {
                Debug.LogError("[BuffTest] 自检存在失败项，见上方报告");
            }
        }

        /// <summary>Buff 自检报告文本（供自动化探针写文件）。</summary>
        public static string BuffSelfTestForReport() { return BuffSelfTest.Run(); }

        // ====================================================================
        // S4 / D25：被动触发自检（与构建版 -autopassive 完全同源）
        // ====================================================================

        [MenuItem("EmojiWar/Tools/Passive Self-Test", false, 159)]
        public static void RunPassiveSelfTest()
        {
            string report = PassiveSelfTest.Run();
            Debug.Log("[PassiveTest]\n" + report);

            if (report.Contains("FAIL"))
            {
                Debug.LogError("[PassiveTest] 自检存在失败项，见上方报告");
            }
        }

        /// <summary>被动自检报告文本（供自动化探针写文件）。</summary>
        public static string PassiveSelfTestForReport() { return PassiveSelfTest.Run(); }

        /// <summary>打印每把法杖的编译序列 + dry-run 预算（P7 预览面板同口径）。</summary>
        [MenuItem("EmojiWar/Tools/Spell Sequence Preview", false, 157)]
        public static void PreviewSequences()
        {
            Debug.Log(PreviewForReport());
        }

        /// <summary>序列与预算预览文本（供自动化探针写文件）。</summary>
        public static string PreviewForReport()
        {
            var sb = new StringBuilder();
            var table = ItemServiceDiagnostics.BuildTable();
            if (table.Count == 0)
            {
                return "[SpellPreview] 没有 ItemSO，请先执行 EmojiWar/Setup/Build Spell Content (S1)";
            }

            var wands = LoadAll<WandSO>(DataRoot + "/Wand");
            wands.Sort((a, b) => a.Id.CompareTo(b.Id));

            sb.Append("[SpellPreview] 序列编译与 dry-run 预算（口径见设计文档 §12.2）\n");
            for (int i = 0; i < wands.Count; i++)
            {
                var w = wands[i];
                var compiled = new CastSpellData[w.SlotCount];
                for (int k = 0; k < w.SlotCount; k++)
                {
                    int card = 0;
                    if (w.DefaultSpellIds != null && k < w.DefaultSpellIds.Length)
                    {
                        card = table.FindItemIdBySpell(w.DefaultSpellIds[k]);
                    }
                    compiled[k] = card > 0 ? LoadoutCompiler.CompileSpell(card, table, k) : CastSpellData.Empty;
                }
                var program = new CastProgram(w.Id, w.SlotCount, w.CastDelay, w.RechargeTime,
                    w.ManaMax, w.ManaRegen, w.Mode, compiled);
                var pv = LoadoutCompiler.Preview(program);

                sb.Append("[SpellPreview] ").Append(w.DisplayName).Append(" ").Append(program.Dump()).Append('\n');
                sb.Append("[SpellPreview]   ").Append(pv).Append('\n');
            }
            return sb.ToString();
        }

        private static List<T> LoadAll<T>(string dir) where T : ScriptableObject
        {
            var list = new List<T>();
            if (!AssetDatabase.IsValidFolder(dir)) { return list; }
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { dir });
            for (int i = 0; i < guids.Length; i++)
            {
                var so = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (so != null) { list.Add(so); }
            }
            return list;
        }
    }
}

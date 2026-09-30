//------------------------------------------------------------
// EmojiWar GameMain Editor - 物品事务自检入口（P2）
//
// 菜单：EmojiWar/Tools/Item Service Self-Test
// 作用：用 AssetDatabase 现场读 ItemSO 组成物品表，跑与运行时完全相同的断言套件
//       （Scripts/Items/ItemSelfTest.cs），免进游戏即可回归背包/装备/商店事务。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Data;
using EmojiWar.GameMain.Items;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>编辑器侧自检入口。</summary>
    public static class ItemServiceDiagnostics
    {
        private const string ItemDir = "Assets/GameMain/Resources/Data/Item";

        [MenuItem("EmojiWar/Tools/Item Service Self-Test", false, 154)]
        public static void RunSelfTest()
        {
            var table = BuildTable();
            if (table.Count == 0)
            {
                Debug.LogError("[ItemTest] 没有找到任何 ItemSO，请先执行 EmojiWar/Setup/Build Wand Content (P1)");
                return;
            }

            string report = ItemSelfTest.Run(table);
            Debug.Log("[ItemTest]\n" + report);

            if (report.Contains("FAIL"))
            {
                Debug.LogError("[ItemTest] 自检存在失败项，见上方报告");
            }
        }

        /// <summary>从 AssetDatabase 读全部 ItemSO 组成物品表（与运行时 ConfigItemTable 等价语义）。</summary>
        public static DictionaryItemTable BuildTable()
        {
            var table = new DictionaryItemTable();
            if (!AssetDatabase.IsValidFolder(ItemDir)) { return table; }

            var guids = AssetDatabase.FindAssets("t:ItemSO", new[] { ItemDir });
            var list = new List<ItemSO>();
            for (int i = 0; i < guids.Length; i++)
            {
                var so = AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (so != null) { list.Add(so); }
            }
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < list.Count; i++) { table.Add(list[i]); }
            return table;
        }
    }
}

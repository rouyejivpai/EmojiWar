//------------------------------------------------------------
// EmojiWar GameMain Editor - UI 列表规范工具（doc/UI列表与Cell规范.md）
//
// 规范要点（§一.3）：**列表容器（正）下方必须放一个 Cell 预制体实例**——
//   · 作者态占位：编辑器里就能看到列表项长什么样、多大（WYSIWYG）；
//   · 运行时"行源"：由 UI/UiListCell 取用（移出容器 → 全局隐藏根）后 Instantiate 出行实例；
//   · 刷新 = 先清空再生成：容器内永远只有"当前应显示的行"。
//
// 菜单：
//   EmojiWar/Tools/Check UI List Cell Norm        —— 全量体检：列出每个列表容器 + Cell 模板状态
//   EmojiWar/Tools/Ensure UI List Cell Templates  —— 给缺模板的已知容器补上 Cell 预制体实例
//
// 同时提供 *Report() 版本（返回纯文本报告），供自动化/命令行/MCP 断言使用。
// 出包/产物：见 Editor/PlayerBuildTool.cs（EmojiWar/Tools/Build Windows64 Player、Refresh Final Artifacts）。
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>列表容器与 Cell 模板一致性检查/补齐工具。</summary>
    public static class UiListNormTools
    {
        private const string UiRoot = "Assets/GameMain/UI";
        private const string ItemsRoot = "Assets/GameMain/UI/items/";

        /// <summary>已知列表容器 → 应放的 Cell 预制体（新增列表时在这里登记，保证"有据可查"）。</summary>
        private static readonly Dictionary<string, string> CellByContainer = new Dictionary<string, string>
        {
            { "BackpackForm/WeaponSlots", ItemsRoot + "WeaponSlot.prefab" },
            { "BackpackForm/GridContainer", ItemsRoot + "InventorySlot.prefab" },
            { "RoomForm/txt_PlayerList", ItemsRoot + "PlayerCell.prefab" },
            { "JoinListForm/RoomContainer", ItemsRoot + "RoomItemRow.prefab" },
            { "CharacterDockForm/CardContainer", ItemsRoot + "CharacterCard.prefab" },
            // 物品系统（P4）：背包 = 6×5 网格 + 两只手杖槽（各自的法术槽子列表）
            { "BackpackForm/ItemGrid", ItemsRoot + "SlotCell.prefab" },
            { "BackpackForm/Hand_L_Slot", ItemsRoot + "WandHandCell.prefab" },
            { "BackpackForm/Hand_R_Slot", ItemsRoot + "WandHandCell.prefab" },
            { "BackpackForm/SpellSlots_L", ItemsRoot + "SlotCell.prefab" },
            { "BackpackForm/SpellSlots_R", ItemsRoot + "SlotCell.prefab" },
            // 法术编程系统（S6）：悬停详情面板的五个动态段落（行 Cell = ItemDetailCell）
            { "ItemDetailPanel/AttrContainer", ItemsRoot + "ItemDetailCell.prefab" },
            { "ItemDetailPanel/FollowContainer", ItemsRoot + "ItemDetailCell.prefab" },
            { "ItemDetailPanel/BuffContainer", ItemsRoot + "ItemDetailCell.prefab" },
            { "ItemDetailPanel/ForecastContainer", ItemsRoot + "ItemDetailCell.prefab" },
            { "ItemDetailPanel/LoadedContainer", ItemsRoot + "ItemDetailCell.prefab" },
        };

        // ---------- 体检 ----------

        [MenuItem("EmojiWar/Tools/Check UI List Cell Norm")]
        public static void CheckAll()
        {
            string report = CheckReport();
            Debug.Log("[UiListNorm]\n" + report);
        }

        /// <summary>全量体检：返回每个列表容器的 Cell 模板状态文本。</summary>
        public static string CheckReport()
        {
            var sb = new StringBuilder();
            int containers = 0;
            int missing = 0;
            foreach (var path in UiPrefabPaths())
            {
                string form = System.IO.Path.GetFileNameWithoutExtension(path);
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { continue; }
                try
                {
                    foreach (var container in FindContainers(root, form))
                    {
                        containers++;
                        string owner = form + "/" + container.name;
                        string cell = FindCellTemplatePath(container);
                        if (cell == null)
                        {
                            missing++;
                            string expected;
                            bool known = CellByContainer.TryGetValue(owner, out expected);
                            sb.AppendLine(string.Format("[UiListNorm] MISSING {0} (children={1}){2}",
                                owner, container.childCount, known ? " expect=" + expected : " unmapped"));
                        }
                        else
                        {
                            sb.AppendLine(string.Format("[UiListNorm] OK {0} -> {1} (children={2})",
                                owner, cell, container.childCount));
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            sb.AppendLine(string.Format("[UiListNorm] containers={0} missing={1}", containers, missing));
            return sb.ToString();
        }

        // ---------- 补齐 ----------

        [MenuItem("EmojiWar/Tools/Ensure UI List Cell Templates")]
        public static void EnsureMissing()
        {
            string report = EnsureReport();
            Debug.Log("[UiListNorm]\n" + report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>给缺 Cell 模板的已知容器补上预制体实例；返回处理报告。</summary>
        public static string EnsureReport()
        {
            var sb = new StringBuilder();
            int fixedCount = 0;
            foreach (var path in UiPrefabPaths())
            {
                string form = System.IO.Path.GetFileNameWithoutExtension(path);
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { continue; }
                bool dirty = false;
                try
                {
                    foreach (var container in FindContainers(root, form))
                    {
                        string owner = form + "/" + container.name;
                        if (FindCellTemplatePath(container) != null)
                        {
                            continue;
                        }

                        string cellPath;
                        if (!CellByContainer.TryGetValue(owner, out cellPath))
                        {
                            sb.AppendLine("[UiListNorm] SKIP " + owner + " (no mapping registered)");
                            continue;
                        }
                        var cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cellPath);
                        if (cellPrefab == null)
                        {
                            sb.AppendLine("[UiListNorm] SKIP " + owner + " (cell prefab not found: " + cellPath + ")");
                            continue;
                        }

                        var inst = PrefabUtility.InstantiatePrefab(cellPrefab, container) as GameObject;
                        if (inst == null)
                        {
                            sb.AppendLine("[UiListNorm] FAIL " + owner);
                            continue;
                        }
                        dirty = true;
                        fixedCount++;
                        sb.AppendLine("[UiListNorm] ADDED " + owner + " -> " + cellPath);
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            sb.AppendLine(string.Format("[UiListNorm] ensured={0}", fixedCount));
            return sb.ToString();
        }

        // ---------- 内部 ----------

        private static IEnumerable<string> UiPrefabPaths()
        {
            var list = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                {
                    list.Add(path);
                }
            }
            list.Sort();
            return list;
        }

        /// <summary>
        /// 列表容器判定：挂了 LayoutGroup，或名字像列表容器。
        /// 注意：按钮条等功能性布局组不是列表（它们不放 Cell），故只按命名/登记表判定；
        /// 「已登记」优先——登记过的容器一律纳入，未登记但挂布局组的仅提示。
        /// </summary>
        private static List<Transform> FindContainers(GameObject root, string form)
        {
            var result = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) { continue; }
                bool registered = CellByContainer.ContainsKey(form + "/" + t.name);
                bool nameLikeList = t.name.EndsWith("Container") || t.name.EndsWith("List") || t.name.EndsWith("Slots");
                if (registered || nameLikeList)
                {
                    result.Add(t);
                }
            }
            return result;
        }

        /// <summary>容器下是否为 UI/items 下的 Cell 预制体实例（= 规范要求的作者态模板）。</summary>
        private static string FindCellTemplatePath(Transform container)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (src == null) { continue; }
                string path = AssetDatabase.GetAssetPath(src);
                if (!string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(ItemsRoot))
                {
                    return path;
                }
            }
            return null;
        }
    }
}

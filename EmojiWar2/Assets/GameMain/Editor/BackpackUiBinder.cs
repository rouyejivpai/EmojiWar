//------------------------------------------------------------
// EmojiWar GameMain Editor - 背包 UI 一键重绑（QuickBind 范式）
//
// 菜单：EmojiWar/Tools/Rebind Backpack UI (QuickBind)
//
// 目的：**只修引用、不动布局**。手改背包层级/尺寸/嵌套后跑一次，本工具会：
//   1. 找到 ItemGrid / WeaponSlots 等节点（按名字），缺 UiSlotContainer 的补上并设好 ContainerId；
//   2. 每个武器槽下确保有**法术槽子列表**（UiSlotContainer=HandSpells_L/R + SlotCell 模板 + 布局组）；
//   3. 确保 SlotCell/WandHandCell 作者态模板存在（规范 §一.3）；
//   4. 把全部引用写进根物体 QuickBind 的绑定表 + 绑定逻辑脚本（BackpackForm）。
// 不重建任何 RectTransform 尺寸/位置 —— 作者的布局原样保留。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>背包 UI 的 QuickBind 重绑工具。</summary>
    public static class BackpackUiBinder
    {
        private const string PrefabPath = "Assets/GameMain/UI/BackpackForm.prefab";
        private const string SlotCellPath = "Assets/GameMain/UI/items/SlotCell.prefab";
        private const string WandHandCellPath = "Assets/GameMain/UI/items/WandHandCell.prefab";

        private const string BindItemGrid = "m_ItemGrid";
        private const string BindWeaponSlotL = "m_WeaponSlotL";
        private const string BindWeaponSlotR = "m_WeaponSlotR";
        private const string BindSpellListL = "m_SpellListL";
        private const string BindSpellListR = "m_SpellListR";
        private const string BindBtnCollapse = "m_BtnCollapse";
        private const string BindTxtTitle = "m_TxtTitle";

        [MenuItem("EmojiWar/Tools/Rebind Backpack UI (QuickBind)", false, 153)]
        public static void Rebind()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[BackpackUiBinder] 打不开预制体: " + PrefabPath);
                return;
            }

            var report = new List<string>();
            try
            {
                var bind = root.GetComponent<QuickBind>();
                if (bind == null)
                {
                    bind = root.AddComponent<QuickBind>();
                    report.Add("补挂 QuickBind 组件（根物体）");
                }
                var logic = root.GetComponent<BackpackForm>();
                if (logic != null)
                {
                    bind.SetLogic(logic);
                    report.Add("绑定 UI 逻辑脚本: BackpackForm");
                }

                // 1) 背包网格容器
                var itemGrid = FindDeep(root.transform, "ItemGrid");
                if (itemGrid != null)
                {
                    var c = EnsureContainer(itemGrid, "Backpack", SlotCellPath, null, report);
                    if (c != null) { bind.SetBinding(BindItemGrid, c.gameObject, typeof(UiSlotContainer).FullName); }
                }
                else
                {
                    report.Add("⚠ 找不到 ItemGrid 节点");
                }

                // 2) 武器槽区：WeaponSlots 的前两个子物体 = 左手/右手（作者摆放，工具只补组件与子列表）
                var weaponArea = FindDeep(root.transform, "WeaponSlots");
                if (weaponArea == null)
                {
                    report.Add("⚠ 找不到 WeaponSlots 节点");
                }
                else
                {
                    var handNodes = new List<Transform>();
                    for (int i = 0; i < weaponArea.childCount && handNodes.Count < 2; i++)
                    {
                        var child = weaponArea.GetChild(i);
                        if (child != null && child.GetComponent<UiSlotContainer>() == null
                            && child.GetComponentInChildren<UiSlotContainer>(true) == null)
                        {
                            handNodes.Add(child);   // 只把"真正的武器槽节点"算进来（跳过子列表容器）
                        }
                        else if (child != null && !IsSpellListName(child.name))
                        {
                            handNodes.Add(child);
                        }
                    }

                    if (handNodes.Count < 2)
                    {
                        report.Add(string.Format("⚠ WeaponSlots 下只找到 {0} 个武器槽节点（需要 2 个：左/右）", handNodes.Count));
                    }

                    for (int i = 0; i < handNodes.Count; i++)
                    {
                        bool isLeft = i == 0;
                        string handId = isLeft ? "Hand_L" : "Hand_R";
                        string keyLabel = isLeft ? "左键" : "右键";
                        string spellContainerId = isLeft ? "HandSpells_L" : "HandSpells_R";
                        string spellNodeName = isLeft ? "SpellSlots_L" : "SpellSlots_R";
                        string bindHand = isLeft ? BindWeaponSlotL : BindWeaponSlotR;
                        string bindSpells = isLeft ? BindSpellListL : BindSpellListR;

                        var hand = EnsureContainer(handNodes[i], handId, WandHandCellPath, keyLabel, report);
                        if (hand != null) { bind.SetBinding(bindHand, hand.gameObject, typeof(UiSlotContainer).FullName); }

                        // 法术槽子列表：**优先使用作者已经做好的那个**（通常在 WeaponSlot 预制体内部，
                        // 名字任意，靠 UiSlotContainer 的 ContainerId 或名字里的 Spell 识别）；
                        // 只有确实找不到时才新建，避免重复。
                        var spells = FindExistingSpellList(handNodes[i]);
                        if (spells != null)
                        {
                            report.Add("沿用已有法术槽子列表: " + spells.name + "（挂在 " + handNodes[i].name + " 下）");
                        }
                        else
                        {
                            spells = CreateSpellListChild(handNodes[i], spellNodeName);
                            report.Add("⚠ 未找到法术槽子列表，新建: " + spellNodeName + "（挂在 " + handNodes[i].name + " 下）");
                        }

                        // 清理历史误建的重复节点（本次工具早期版本留下的本地 SpellSlots_* 子物体）
                        RemoveLocalDuplicates(handNodes[i], spells, report);

                        // 关键：**每个实例都显式写自己的容器 Id**（否则左右手两个实例共用源预制体默认值
                        // → 两手法术槽指向同一个容器 → "主副法术槽同步"）
                        var spellContainer = EnsureContainer(spells, spellContainerId, SlotCellPath, null, report);
                        if (spellContainer != null)
                        {
                            report.Add("法术槽容器 Id 覆盖: " + spellNodesPath(spells) + " → " + spellContainerId);
                            bind.SetBinding(bindSpells, spellContainer.gameObject, typeof(UiSlotContainer).FullName);
                        }
                    }
                }

                // 3) 标题 / 收起按钮（有就绑，保持 QuickBind 表完整可读）
                var title = FindDeep(root.transform, "txt_Title");
                if (title != null) { bind.SetBinding(BindTxtTitle, title.gameObject, typeof(Text).FullName); }
                var collapse = FindDeep(root.transform, "btn_Collapse");
                if (collapse != null) { bind.SetBinding(BindBtnCollapse, collapse.gameObject, typeof(Button).FullName); }

                EditorUtility.SetDirty(bind);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BackpackUiBinder] 重绑完成：\n  " + string.Join("\n  ", report.ToArray()));
        }

        // ---------- 辅助 ----------

        /// <summary>节点在预制体里的层级路径（报告里点名用）。</summary>
        private static string spellNodesPath(Transform t)
        {
            if (t == null) { return "?"; }
            string path = t.name;
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8)
            {
                path = p.name + "/" + path;
                if (p.parent == null) { break; }
                p = p.parent;
            }
            return path;
        }

        private static bool IsSpellListName(string name)
        {
            return name != null && name.StartsWith("SpellSlots");
        }

        /// <summary>
        /// 在武器槽（含**嵌套预制体实例内部**）找一个"法术槽子列表"：
        /// 判据 = 带有 UiSlotContainer 且（ContainerId 含 Spell 或 名字含 Spell/Slot）。
        /// 作者通常把它做在 WeaponSlot.prefab 里，因此必须遍历实际的 Transform 层级。
        /// </summary>
        private static Transform FindExistingSpellList(Transform hand)
        {
            if (hand == null) { return null; }
            var containers = hand.GetComponentsInChildren<UiSlotContainer>(true);
            for (int i = 0; i < containers.Length; i++)
            {
                var c = containers[i];
                if (c == null || c.transform == hand) { continue; }   // 跳过武器槽自己
                string id = GetContainerId(c);
                if (!string.IsNullOrEmpty(id) && id.IndexOf("Spell", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return c.transform;
                }
            }
            for (int i = 0; i < containers.Length; i++)
            {
                var c = containers[i];
                if (c != null && c.transform != hand && c.name.IndexOf("Spell", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return c.transform;
                }
            }
            return null;
        }

        /// <summary>读 UiSlotContainer.m_ContainerId（私有字段，编辑器反射）。</summary>
        private static string GetContainerId(UiSlotContainer container)
        {
            var field = typeof(UiSlotContainer).GetField("m_ContainerId",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return field != null ? field.GetValue(container) as string : null;
        }

        /// <summary>
        /// 删除历史误建的重复节点：本地（非嵌套实例）直系子物体里、名字以 SpellSlots 开头、
        /// 且不是本次要沿用的那个容器 —— 这些是工具早期版本留下的重复法术槽列表。
        /// </summary>
        private static void RemoveLocalDuplicates(Transform hand, Transform keep, List<string> report)
        {
            if (hand == null) { return; }
            for (int i = hand.childCount - 1; i >= 0; i--)
            {
                var child = hand.GetChild(i);
                if (child == null || child == keep) { continue; }
                if (!IsSpellListName(child.name)) { continue; }
                // 只删"本地直系子物体"（嵌套预制体实例内部的节点动不了也不该动）
                if (UnityEditor.PrefabUtility.GetPrefabInstanceHandle(child.gameObject) != null) { continue; }
                report.Add("删除重复的法术槽子列表: " + child.name + "（保留 " + keep.name + "）");
                Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>确保节点上有 UiSlotContainer 且配置正确 + 有作者态 Cell 模板。</summary>
        private static UiSlotContainer EnsureContainer(Transform node, string containerId, string cellPath,
            string keyLabel, List<string> report)
        {
            if (node == null) { return null; }

            var container = node.GetComponent<UiSlotContainer>();
            if (container == null)
            {
                container = node.gameObject.AddComponent<UiSlotContainer>();
                report.Add("补挂 UiSlotContainer: " + node.name + " → " + containerId);
            }
            SetField(container, "m_ContainerId", containerId);
            SetField(container, "m_CellItemPath", cellPath);
            if (!string.IsNullOrEmpty(keyLabel)) { SetField(container, "m_KeyLabel", keyLabel); }

            // 规范 §一.3：容器下要有作者态 Cell 预制体实例（作为运行时行源）
            if (FindCellTemplate(node, cellPath) == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cellPath);
                if (prefab != null)
                {
                    var inst = PrefabUtility.InstantiatePrefab(prefab, node) as GameObject;
                    if (inst != null)
                    {
                        inst.name = prefab.name;
                        report.Add("补 Cell 模板: " + node.name + " ← " + prefab.name);
                    }
                }
            }
            return container;
        }

        private static GameObject FindCellTemplate(Transform container, string cellPath)
        {
            string wanted = System.IO.Path.GetFileNameWithoutExtension(cellPath);
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child == null) { continue; }
                if (child.GetComponent<UiSlotContainer>() != null) { continue; }   // 嵌套子容器不算模板
                if (child.name == wanted) { return child.gameObject; }
            }
            return null;
        }

        /// <summary>在武器槽下新建法术槽子列表（横向网格，贴在武器槽右侧；尺寸给默认值，作者可再调）。</summary>
        private static Transform CreateSpellListChild(Transform hand, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = hand.gameObject.layer;
            go.transform.SetParent(hand, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(170f, -6f);
            rect.sizeDelta = new Vector2(240f, 100f);

            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(72f, 72f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.MiddleLeft;
            return go.transform;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) { return null; }
            if (root.name == name) { return root; }
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) { return found; }
            }
            return null;
        }

        private static void SetField(object component, string fieldName, object value)
        {
            var field = component.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null) { field.SetValue(component, value); }
        }
    }
}

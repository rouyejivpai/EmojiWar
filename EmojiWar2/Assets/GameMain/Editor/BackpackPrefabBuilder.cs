//------------------------------------------------------------
// EmojiWar GameMain Editor - 背包页面与槽位预制体
// 菜单：EmojiWar/Setup/Build Backpack Prefabs
// 产物：
//   Assets/GameMain/UI/items/WeaponSlot.prefab     （左键/右键武器槽）
//   Assets/GameMain/UI/items/InventorySlot.prefab  （5×5 物品槽）
//   Assets/GameMain/UI/BackpackForm.prefab         （背包窗体：左侧武器槽 / 右侧 5×5 网格）
// 说明：窗体参考选角界面（SlideMotion 滑入滑出，Popup 组，不暂停战斗）。
// 规范（doc/UI列表与Cell规范.md §一.3）：列表容器（WeaponSlots / GridContainer）下方必须各放
//       一个 Cell 预制体实例 —— 作者态占位 + 运行时行源（UI/UiListCell 取用）。本生成器已内置。
// 注意：BuildAll 会整体重建 BackpackForm.prefab（手工改动会被覆盖，但模板必在）。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    public static class BackpackPrefabBuilder
    {
        public const string WeaponSlotPath = "Assets/GameMain/UI/items/WeaponSlot.prefab";
        public const string InventorySlotPath = "Assets/GameMain/UI/items/InventorySlot.prefab";
        public const string SlotCellPath = "Assets/GameMain/UI/items/SlotCell.prefab";
        public const string WandHandCellPath = "Assets/GameMain/UI/items/WandHandCell.prefab";
        public const string BackpackFormPath = "Assets/GameMain/UI/BackpackForm.prefab";

        [MenuItem("EmojiWar/Setup/Build Backpack Prefabs")]
        public static void BuildAll()
        {
            BuildWeaponSlot();
            BuildInventorySlot();
            BuildBackpackForm();
            AssetDatabase.SaveAssets();
            Debug.Log("[BackpackBuilder] 背包相关预制体已生成（武器槽 / 物品槽 / 背包窗体）");
        }

        // ---------- 武器槽 ----------
        private static void BuildWeaponSlot()
        {
            EnsureFolder("Assets/GameMain/UI/items");

            var root = new GameObject("WeaponSlot");
            root.layer = LayerMask.NameToLayer("UI");
            var rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(380f, 140f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.16f);
            root.AddComponent<WeaponSlotCell>();

            // Key（左键/右键）
            var key = CreateText("Key", root.transform, "左键", 20, TextAnchor.UpperLeft);
            var kr = key.GetComponent<RectTransform>();
            kr.anchorMin = new Vector2(0f, 1f);
            kr.anchorMax = new Vector2(0f, 1f);
            kr.pivot = new Vector2(0f, 1f);
            kr.anchoredPosition = new Vector2(14f, -10f);
            kr.sizeDelta = new Vector2(160f, 28f);

            // Icon
            var icon = new GameObject("Icon");
            icon.transform.SetParent(root.transform, false);
            var ir = icon.AddComponent<RectTransform>();
            ir.anchorMin = new Vector2(0f, 0.5f);
            ir.anchorMax = new Vector2(0f, 0.5f);
            ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(16f, -12f);
            ir.sizeDelta = new Vector2(92f, 92f);
            var img = icon.AddComponent<Image>();
            img.preserveAspect = true;

            // Name
            var name = CreateText("Name", root.transform, "武器名", 24, TextAnchor.MiddleLeft);
            var nr = name.GetComponent<RectTransform>();
            nr.anchorMin = new Vector2(0f, 0f);
            nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(120f, 8f);
            nr.offsetMax = new Vector2(-12f, -36f);

            PrefabUtility.SaveAsPrefabAsset(root, WeaponSlotPath);
            Object.DestroyImmediate(root);
        }

        // ---------- 物品槽 ----------
        private static void BuildInventorySlot()
        {
            EnsureFolder("Assets/GameMain/UI/items");

            var root = new GameObject("InventorySlot");
            root.layer = LayerMask.NameToLayer("UI");
            var rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(132f, 132f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            root.AddComponent<InventorySlotCell>();

            var icon = new GameObject("Icon");
            icon.transform.SetParent(root.transform, false);
            var ir = icon.AddComponent<RectTransform>();
            ir.anchorMin = Vector2.zero;
            ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(14f, 30f);
            ir.offsetMax = new Vector2(-14f, -14f);
            var img = icon.AddComponent<Image>();
            img.preserveAspect = true;

            var label = CreateText("Label", root.transform, "", 16, TextAnchor.LowerCenter);
            var lr = label.GetComponent<RectTransform>();
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.anchoredPosition = new Vector2(0f, 6f);
            lr.sizeDelta = new Vector2(-12f, 24f);

            PrefabUtility.SaveAsPrefabAsset(root, InventorySlotPath);
            Object.DestroyImmediate(root);
        }

        // ---------- 背包窗体 ----------
        private static void BuildBackpackForm()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("BackpackForm");
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent<BackpackForm>();
            root.AddComponent<QuickBind>();

            // 滑动主体：贴顶、全宽、高 860（下方留给 HUD）
            var panel = new GameObject("panel_Slide");
            panel.transform.SetParent(root.transform, false);
            var pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = new Vector2(0f, 1f);
            pr.anchorMax = new Vector2(1f, 1f);
            pr.pivot = new Vector2(0.5f, 1f);
            pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(0f, 860f);
            var pImg = panel.AddComponent<Image>();
            pImg.color = new Color(0.05f, 0.08f, 0.12f, 0.95f);
            panel.AddComponent<CanvasGroup>();

            var title = CreateText("txt_Title", panel.transform, "背包", 34, TextAnchor.MiddleCenter);
            var tr = title.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0.5f, 1f);
            tr.anchorMax = new Vector2(0.5f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -18f);
            tr.sizeDelta = new Vector2(400f, 48f);

            var collapse = CreateButton("btn_Collapse", panel.transform, "收起 (Tab)", new Vector2(190f, 58f));
            var cr = collapse.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(1f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-24f, -18f);
            cr.sizeDelta = new Vector2(190f, 58f);

            // 左：两只手的法杖槽；每个武器槽下挂**自己的法术槽子列表**（动态 Cell 数 = 该手法杖槽数）
            BuildHandSlot(panel.transform, "Hand_L_Slot", "左键", 48f, -34f, "HandSpells_L", "SpellSlots_L");
            BuildHandSlot(panel.transform, "Hand_R_Slot", "右键", 48f, -34f - 150f, "HandSpells_R", "SpellSlots_R");

            // 右：6×5 = 30 格背包（GridLayoutGroup 统一排布）
            var grid = new GameObject("ItemGrid");
            grid.transform.SetParent(panel.transform, false);
            var gr = grid.AddComponent<RectTransform>();
            gr.anchorMin = new Vector2(1f, 0f);
            gr.anchorMax = new Vector2(1f, 1f);
            gr.pivot = new Vector2(1f, 0.5f);
            gr.anchoredPosition = new Vector2(-48f, -34f);
            gr.sizeDelta = new Vector2(900f, -140f);
            var glg = grid.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(132f, 132f);
            glg.spacing = new Vector2(12f, 12f);
            glg.padding = new RectOffset(12, 12, 12, 12);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 6;
            glg.childAlignment = TextAnchor.UpperCenter;
            var gridContainer = grid.AddComponent<UiSlotContainer>();
            SetField(gridContainer, "m_ContainerId", "Backpack");
            SetField(gridContainer, "m_CellItemPath", SlotCellPath);

            // 规范 §一.3：容器下方各放一个 Cell 预制体实例（作者态占位 = 运行时行源）
            AddCellTemplate(grid.transform, SlotCellPath);

            QuickBindGenerator.Process(root);
            PrefabUtility.SaveAsPrefabAsset(root, BackpackFormPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// 手部法杖槽：容器对象（UiSlotContainer = 法杖）+ 其下的**法术槽子列表**
        /// （UiSlotContainer = 该手的法术，Cell 数随法杖槽数动态变化）。
        /// </summary>
        private static GameObject BuildHandSlot(Transform parent, string name, string keyLabel,
            float x, float y, string wandSpellContainerId, string spellListNodeName)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(420f, 140f);

            var container = go.AddComponent<UiSlotContainer>();
            SetField(container, "m_ContainerId", name == "Hand_L_Slot" ? "Hand_L" : "Hand_R");
            SetField(container, "m_CellItemPath", WandHandCellPath);
            SetField(container, "m_KeyLabel", keyLabel);

            // 该手法术槽子列表（武器槽的子物体；按名称由 BackpackForm 绑定）
            var spells = new GameObject(spellListNodeName);
            spells.transform.SetParent(go.transform, false);
            var sr = spells.AddComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0.5f);
            sr.anchorMax = new Vector2(0f, 0.5f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.anchoredPosition = new Vector2(168f, -6f);
            sr.sizeDelta = new Vector2(240f, 100f);
            var slg = spells.AddComponent<GridLayoutGroup>();
            slg.cellSize = new Vector2(72f, 72f);
            slg.spacing = new Vector2(8f, 8f);
            slg.padding = new RectOffset(0, 0, 0, 0);
            slg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            slg.constraintCount = 3;
            slg.childAlignment = TextAnchor.MiddleLeft;
            var spellContainer = spells.AddComponent<UiSlotContainer>();
            SetField(spellContainer, "m_ContainerId", wandSpellContainerId);
            SetField(spellContainer, "m_CellItemPath", SlotCellPath);

            AddCellTemplate(go.transform, WandHandCellPath);       // 武器槽自己的 Cell 模板
            AddCellTemplate(spells.transform, SlotCellPath);       // 法术槽子列表自己的 Cell 模板
            return go;
        }

        private static void SetField(object component, string fieldName, object value)
        {
            var field = component.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null) { field.SetValue(component, value); }
        }

        // ---------- 辅助 ----------

        /// <summary>在列表容器下方放一个 Cell 预制体实例（规范要求：见 doc/UI列表与Cell规范.md §一.3）。</summary>
        private static void AddCellTemplate(Transform container, string cellPrefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cellPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[BackpackBuilder] Cell 预制体缺失，容器将没有行源模板: " + cellPrefabPath);
                return;
            }
            var inst = PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
            if (inst != null)
            {
                inst.name = prefab.name;
            }
        }
        private static GameObject CreateText(string name, Transform parent, string text, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string label, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.5f, 0.9f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var labelGo = CreateText("Label", go.transform, label, 24, TextAnchor.MiddleCenter);
            var lr = labelGo.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = Vector2.zero;
            return go;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) { return; }
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

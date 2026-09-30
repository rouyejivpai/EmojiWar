//------------------------------------------------------------
// EmojiWar GameMain Editor - 可拖拽插槽 Cell 预制体（P3）
//
// 菜单：EmojiWar/Setup/Build Item Slot Prefabs (P3)
// 产物：
//   Assets/GameMain/UI/items/SlotCell.prefab      通用插槽（132×132，5×5 网格用）
//   Assets/GameMain/UI/items/WandHandCell.prefab  手部法杖槽（380×140，左键/右键）
// 结构（UiSlotView 按名称/字段找子物体）：Highlight / Icon / Count / Key
// 方案：doc/物品与法杖系统设计.md §5
//------------------------------------------------------------

using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>物品插槽 Cell 预制体生成器。</summary>
    public static class ItemSlotPrefabBuilder
    {
        public const string SlotCellPath = "Assets/GameMain/UI/items/SlotCell.prefab";
        public const string WandHandCellPath = "Assets/GameMain/UI/items/WandHandCell.prefab";

        [MenuItem("EmojiWar/Setup/Build Item Slot Prefabs (P3)", false, 151)]
        public static void BuildAll()
        {
            EnsureFolder("Assets/GameMain/UI/items");
            BuildSlotCell();
            BuildWandHandCell();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ItemSlotBuilder] 已生成 SlotCell / WandHandCell（P3 可拖拽插槽）");
        }

        // ---------- 通用插槽 ----------

        private static void BuildSlotCell()
        {
            var root = NewRoot("SlotCell", new Vector2(132f, 132f), new Color(1f, 1f, 1f, 0.06f));

            var highlight = NewImage("Highlight", root.transform, new Color(1f, 1f, 1f, 0f));
            Stretch(highlight, 0f);
            highlight.raycastTarget = false;

            var icon = NewImage("Icon", root.transform, Color.white);
            Stretch(icon, 0f);
            var iconRect = icon.rectTransform;
            iconRect.offsetMin = new Vector2(14f, 26f);
            iconRect.offsetMax = new Vector2(-14f, -14f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var count = NewText("Count", root.transform, "", 18, TextAnchor.LowerRight);
            var countRect = count.rectTransform;
            countRect.anchorMin = new Vector2(0f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(0.5f, 0f);
            countRect.anchoredPosition = new Vector2(-6f, 4f);
            countRect.sizeDelta = new Vector2(-12f, 22f);
            count.raycastTarget = false;

            var view = root.AddComponent<UiSlotView>();
            var target = root.AddComponent<UiDropTarget>();
            Bind(view, "m_Bg", root.GetComponent<Image>());
            Bind(view, "m_Icon", icon);
            Bind(view, "m_CountText", count);
            Bind(view, "m_KeyText", null);
            Bind(target, "m_HighlightImage", highlight);

            PrefabUtility.SaveAsPrefabAsset(root, SlotCellPath);
            Object.DestroyImmediate(root);
        }

        // ---------- 手部法杖槽 ----------

        private static void BuildWandHandCell()
        {
            var root = NewRoot("WandHandCell", new Vector2(380f, 140f), new Color(1f, 1f, 1f, 0.16f));

            var highlight = NewImage("Highlight", root.transform, new Color(1f, 1f, 1f, 0f));
            Stretch(highlight, 0f);
            highlight.raycastTarget = false;

            var key = NewText("Key", root.transform, "左键", 20, TextAnchor.UpperLeft);
            var keyRect = key.rectTransform;
            keyRect.anchorMin = new Vector2(0f, 1f);
            keyRect.anchorMax = new Vector2(0f, 1f);
            keyRect.pivot = new Vector2(0f, 1f);
            keyRect.anchoredPosition = new Vector2(14f, -10f);
            keyRect.sizeDelta = new Vector2(160f, 28f);
            key.raycastTarget = false;

            var icon = NewImage("Icon", root.transform, Color.white);
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(16f, -12f);
            iconRect.sizeDelta = new Vector2(92f, 92f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var count = NewText("Count", root.transform, "", 22, TextAnchor.LowerRight);
            var countRect = count.rectTransform;
            countRect.anchorMin = new Vector2(0f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(0.5f, 0f);
            countRect.anchoredPosition = new Vector2(-14f, 8f);
            countRect.sizeDelta = new Vector2(-28f, 26f);
            count.raycastTarget = false;

            var view = root.AddComponent<UiSlotView>();
            var target = root.AddComponent<UiDropTarget>();
            Bind(view, "m_Bg", root.GetComponent<Image>());
            Bind(view, "m_Icon", icon);
            Bind(view, "m_CountText", count);
            Bind(view, "m_KeyText", key);
            Bind(target, "m_HighlightImage", highlight);

            PrefabUtility.SaveAsPrefabAsset(root, WandHandCellPath);
            Object.DestroyImmediate(root);
        }

        // ---------- 辅助 ----------

        private static GameObject NewRoot(string name, Vector2 size, Color bg)
        {
            var root = new GameObject(name);
            root.layer = LayerMask.NameToLayer("UI");
            var rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            var img = root.AddComponent<Image>();
            img.color = bg;
            return root;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static Text NewText(string name, Transform parent, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private static void Stretch(Image img, float inset)
        {
            var rect = img.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>按字段名绑定 [SerializeField] 私有字段（与工程内其它生成器一致）。</summary>
        private static void Bind(object component, string fieldName, object value)
        {
            if (component == null) { return; }
            var field = component.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                Debug.LogWarning("[ItemSlotBuilder] 字段不存在: " + component.GetType().Name + "." + fieldName);
                return;
            }
            field.SetValue(component, value);
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

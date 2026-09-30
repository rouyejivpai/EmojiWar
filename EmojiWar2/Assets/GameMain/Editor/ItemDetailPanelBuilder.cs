//------------------------------------------------------------
// EmojiWar GameMain Editor - 物品详情面板预制体生成器（S6）
//
// 菜单：EmojiWar/Setup/Build Item Detail Panel (S6)
// 产物：Assets/GameMain/UI/items/ItemDetailPanel.prefab
//   + Assets/GameMain/UI/items/ItemDetailCell.prefab（行 Cell：Icon / Name / Value）
//
// 结构（ItemDetailPanel 按 [SerializeField] 字段绑定；行内容按子物体名找 → 见 UI/Items/ItemDetailCell.cs）：
//   ItemDetailPanel  (Image 深色底 + VerticalLayoutGroup + ContentSizeFitter)
//     Header (HorizontalLayoutGroup)
//       Icon            (Image + LayoutElement 40×40)
//       HeaderText (VerticalLayoutGroup)
//         Title         (Text 标题)
//         Subtitle      (Text 副标题：类别/稀有度)
//     AttrHeader     (Text 段标题：基础属性)
//     AttrContainer  (VerticalLayoutGroup) + 行 Cell 模板（作者态占位，运行时 UiListCell 取用）
//     FollowHeader   (Text 段标题：给后续物品的修正)
//     FollowContainer + 模板
//     BuffHeader     (Text 段标题：Buff / 被动)
//     BuffContainer  + 模板
//     ForecastHeader (Text 段标题：预算预估)
//     ForecastContainer + 模板
//     LoadedHeader   (Text 段标题：已装填序列)
//     LoadedContainer + 模板
//
// 规范：每个列表容器（正）下方各放**一个** Cell 预制体实例作行源（doc/UI列表与Cell规范.md §一.3）；
//   `UiListCell.Resolve` 取用时只认容器里第一个子物体，因此**每个容器各一份模板**。
// 登记：Constant.UIItemAssetPath.ItemDetailPanel + GameResourceBuilder.AssetPaths（否则构建版取不到）。
//------------------------------------------------------------

using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>物品/法杖详情面板预制体生成器。</summary>
    public static class ItemDetailPanelBuilder
    {
        public const string ItemsDir = "Assets/GameMain/UI/items";
        public const string CellPath = ItemsDir + "/ItemDetailCell.prefab";
        public const string PanelPath = ItemsDir + "/ItemDetailPanel.prefab";

        private const float PanelWidth = 430f;
        private const int FontTitle = 24;
        private const int FontBody = 18;
        private const int FontSection = 17;
        private const float RowHeight = 26f;

        [MenuItem("EmojiWar/Setup/Build Item Detail Panel (S6)", false, 163)]
        public static void BuildAll()
        {
            EnsureFolder(ItemsDir);
            BuildCell();
            BuildPanel();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ItemDetailBuilder] 已生成 ItemDetailCell.prefab + ItemDetailPanel.prefab（S6 悬停详情）");
        }

        // ---------- 行 Cell ----------

        private static void BuildCell()
        {
            var root = NewNode("ItemDetailCell", null);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PanelWidth - 24f, RowHeight);
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = RowHeight;
            le.preferredHeight = RowHeight;

            var icon = NewImage("Icon", root.transform, Color.white);
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(0f, 0f);
            iconRect.sizeDelta = new Vector2(26f, 26f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var name = NewText("Name", root.transform, "", FontBody, TextAnchor.MiddleLeft, UIStyle.TextColor);
            var nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(0f, 0f);
            nameRect.offsetMax = new Vector2(-132f, 0f);   // 右侧留给 Value

            var value = NewText("Value", root.transform, "", FontBody, TextAnchor.MiddleRight, UIStyle.SubTextColor);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.anchoredPosition = new Vector2(0f, 0f);
            valueRect.sizeDelta = new Vector2(128f, 0f);

            root.AddComponent<ItemDetailCell>();
            PrefabUtility.SaveAsPrefabAsset(root, CellPath);
            Object.DestroyImmediate(root);
        }

        // ---------- 面板 ----------

        private static void BuildPanel()
        {
            var root = NewNode("ItemDetailPanel", null);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(PanelWidth, 320f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.10f, 0.96f);
            bg.raycastTarget = false;

            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- 顶部：图标 + 标题 ----
            var header = NewNode("Header", root.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.minHeight = 52f;
            headerLe.preferredHeight = 52f;
            var hlg = header.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var icon = NewImage("Icon", header.transform, Color.white);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconLe = icon.gameObject.AddComponent<LayoutElement>();
            iconLe.minWidth = 44f; iconLe.preferredWidth = 44f;
            iconLe.minHeight = 44f; iconLe.preferredHeight = 44f;

            var headerText = NewNode("HeaderText", header.transform);
            var headerTextRect = headerText.GetComponent<RectTransform>();
            headerTextRect.sizeDelta = new Vector2(PanelWidth - 100f, 48f);
            var headerTextLe = headerText.AddComponent<LayoutElement>();
            headerTextLe.minWidth = PanelWidth - 100f;
            headerTextLe.flexibleWidth = 1f;
            var hvlg = headerText.AddComponent<VerticalLayoutGroup>();
            hvlg.spacing = 0f;
            hvlg.childAlignment = TextAnchor.MiddleLeft;
            hvlg.childControlWidth = true;
            hvlg.childControlHeight = true;
            hvlg.childForceExpandWidth = true;
            hvlg.childForceExpandHeight = false;

            var title = NewText("Title", headerText.transform, "名称", FontTitle, TextAnchor.MiddleLeft, UIStyle.TextColor);
            title.fontStyle = FontStyle.Bold;
            var subtitle = NewText("Subtitle", headerText.transform, "类别", 14, TextAnchor.MiddleLeft, UIStyle.SubTextColor);

            // ---- 段落 ----
            var attrHeader = NewSectionHeader("AttrHeader", root.transform, "基础");
            var attrContainer = NewContainer("AttrContainer", root.transform, CellPath);

            var followHeader = NewSectionHeader("FollowHeader", root.transform, "给后续物品的修正");
            var followContainer = NewContainer("FollowContainer", root.transform, CellPath);

            var buffHeader = NewSectionHeader("BuffHeader", root.transform, "Buff / 被动");
            var buffContainer = NewContainer("BuffContainer", root.transform, CellPath);

            var forecastHeader = NewSectionHeader("ForecastHeader", root.transform, "预算预估");
            var forecastContainer = NewContainer("ForecastContainer", root.transform, CellPath);

            var loadedHeader = NewSectionHeader("LoadedHeader", root.transform, "已装填序列");
            var loadedContainer = NewContainer("LoadedContainer", root.transform, CellPath);

            // ---- 绑定（按 [SerializeField] 私有字段名） ----
            var panel = root.AddComponent<ItemDetailPanel>();
            Bind(panel, "m_Icon", icon);
            Bind(panel, "m_Title", title);
            Bind(panel, "m_Subtitle", subtitle);
            Bind(panel, "m_SectionFollow", followHeader);
            Bind(panel, "m_SectionBuff", buffHeader);
            Bind(panel, "m_SectionForecast", forecastHeader);
            Bind(panel, "m_SectionLoaded", loadedHeader);
            Bind(panel, "m_AttrContainer", attrContainer);
            Bind(panel, "m_FollowContainer", followContainer);
            Bind(panel, "m_BuffContainer", buffContainer);
            Bind(panel, "m_ForecastContainer", forecastContainer);
            Bind(panel, "m_LoadedContainer", loadedContainer);

            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
            Object.DestroyImmediate(root);
        }

        // ---------- 辅助 ----------

        private static Text NewSectionHeader(string name, Transform parent, string text)
        {
            var t = NewText(name, parent, text, FontSection, TextAnchor.MiddleLeft, UIStyle.PrimaryColor);
            t.fontStyle = FontStyle.Bold;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 22f;
            le.preferredHeight = 22f;
            return t;
        }

        private static RectTransform NewContainer(string name, Transform parent, string cellPrefabPath)
        {
            var go = NewNode(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PanelWidth - 24f, RowHeight);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 规范 §一.3：容器（正）下方放一个 Cell 预制体实例作行源
            var cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cellPrefabPath);
            if (cellPrefab == null)
            {
                Debug.LogError("[ItemDetailBuilder] 找不到行 Cell 预制体: " + cellPrefabPath + "（先跑 BuildAll 生成它）");
                return rect;
            }
            var template = (GameObject)PrefabUtility.InstantiatePrefab(cellPrefab, go.transform);
            template.name = "ItemDetailCell";
            return rect;
        }

        private static GameObject NewNode(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) { go.layer = uiLayer; }
            if (parent != null) { go.transform.SetParent(parent, false); }
            return go;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) { go.layer = uiLayer; }
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static Text NewText(string name, Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) { go.layer = uiLayer; }
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 6f;
            le.preferredHeight = size + 6f;
            return t;
        }

        /// <summary>按字段名绑定 [SerializeField]（与工程内其它生成器一致）。</summary>
        private static void Bind(object component, string fieldName, object value)
        {
            if (component == null) { return; }
            var field = component.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                Debug.LogWarning("[ItemDetailBuilder] 字段不存在: " + component.GetType().Name + "." + fieldName);
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

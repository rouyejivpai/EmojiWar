//------------------------------------------------------------
// EmojiWar GameMain Editor - 生成 RoomItemRow 预制体（加入房间列表行，替代原代码创建的 GameObject）
// 菜单：EmojiWar/Setup/Build RoomItemRow Prefab
// 产物：Assets/GameMain/UI/items/RoomItemRow.prefab（'game' AssetBundle，经 UiPrefab 加载）
// 结构：根=Image(背景)+Button；子 Label=Text(整行铺满)。数据(文字/点击/位置)运行时填充。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    public static class RoomItemRowPrefabBuilder
    {
        public const string RoomItemRowPrefabPath = "Assets/GameMain/UI/items/RoomItemRow.prefab";

        [MenuItem("EmojiWar/Setup/Build RoomItemRow Prefab")]
        public static void BuildRoomItemRow()
        {
            EnsureFolder("Assets/GameMain/UI/items");

            var root = new GameObject("RoomItemRow");
            root.layer = LayerMask.NameToLayer("UI");
            var rootRect = root.AddComponent<RectTransform>();
            // 运行时置于 RoomContainer 下（容器 top 锚定），沿用原代码几何
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(900f, 70f);

            var img = root.AddComponent<Image>();
            img.color = new Color(0.15f, 0.45f, 0.8f, 0.9f);
            var btn = root.AddComponent<Button>();
            btn.targetGraphic = img;

            var label = new GameObject("Label");
            label.transform.SetParent(root.transform, false);
            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = label.AddComponent<Text>();
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            PrefabUtility.SaveAsPrefabAsset(root, RoomItemRowPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[RoomItemRow] prefab 已生成: " + RoomItemRowPrefabPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

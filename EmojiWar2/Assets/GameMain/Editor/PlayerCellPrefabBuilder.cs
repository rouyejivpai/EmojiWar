//------------------------------------------------------------
// EmojiWar GameMain Editor - 生成 PlayerCell 预制体（只创建该新资产，不动任何现有预制体）
// 菜单：EmojiWar/Setup/Build PlayerCell Prefab
// 产物：Assets/GameMain/UI/items/PlayerCell.prefab（'game' AssetBundle；经 UiPrefab 全路径加载）
// 结构（子物体按名称约定，PlayerCell 运行时 Find）：Icon / Name / Ready 三段式。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    public static class PlayerCellPrefabBuilder
    {
        public const string PlayerCellPrefabPath = "Assets/GameMain/UI/items/PlayerCell.prefab";

        [MenuItem("EmojiWar/Setup/Build PlayerCell Prefab")]
        public static void BuildPlayerCell()
        {
            EnsureFolder("Assets/GameMain/UI/items");

            var root = new GameObject("PlayerCell");
            root.layer = LayerMask.NameToLayer("UI");
            var rootRect = root.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(180f, 100f);   // 运行时由 RoomForm 按容器尺寸重排
            root.AddComponent<PlayerCell>();

            // 上段：角色 emoji（占上部 38%）
            CreateLabel("Icon", root.transform, "😅", 34,
                new Vector2(0f, 0.62f), new Vector2(1f, 1f));
            // 中段：玩家名（中部 30%）
            CreateLabel("Name", root.transform, "玩家名", 20,
                new Vector2(0f, 0.32f), new Vector2(1f, 0.62f));
            // 下段：准备状态（下部 32%）
            CreateLabel("Ready", root.transform, "✗ 未准备", 16,
                new Vector2(0f, 0f), new Vector2(1f, 0.32f));

            PrefabUtility.SaveAsPrefabAsset(root, PlayerCellPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[PlayerCell] prefab 已生成: " + PlayerCellPrefabPath);
        }

        private static void CreateLabel(string name, Transform parent, string content, int fontSize,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

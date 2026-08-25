//------------------------------------------------------------
// EmojiWar GameMain - 战斗 HUD 重建工具（Editor）
// 菜单：EmojiWar/Setup/04 - Rebuild Battle HUD
// 重建 BattleHudForm.prefab（金币/波次/HP/武器名/弹药），样式来自 UI 框架。
//------------------------------------------------------------

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 战斗 HUD 生成工具。
    /// </summary>
    public static class HudBuilder
    {
        private const string BattleHudFormPrefabPath = "Assets/GameMain/UI/BattleHudForm.prefab";

        [MenuItem("EmojiWar/Setup/04 - Rebuild Battle HUD")]
        public static void RebuildBattleHud()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = UIComponentFactory.CreateFormRoot("BattleHudForm", typeof(BattleHudForm));
            var form = root.GetComponent<BattleHudForm>();

            // 左上角：金币 / 波次 / HP
            var coin = CreateAnchoredText("CoinText", root.transform, "金币：0", 28, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(30, -20), new Vector2(400, 40));
            var wave = CreateAnchoredText("WaveText", root.transform, "波次：0", 28, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(30, -60), new Vector2(400, 40));
            var hp = CreateAnchoredText("HpText", root.transform, "HP: --", 32, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(30, -100), new Vector2(400, 50));

            // 左下角：武器名 / 弹药
            var weapon = CreateAnchoredText("WeaponText", root.transform, "武器：--", 26, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(30, 90), new Vector2(400, 40));
            var ammo = CreateAnchoredText("AmmoText", root.transform, "弹药 --/--", 26, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(30, 50), new Vector2(400, 40));

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_CoinText", flags).SetValue(form, coin.GetComponent<Text>());
            form.GetType().GetField("m_WaveText", flags).SetValue(form, wave.GetComponent<Text>());
            form.GetType().GetField("m_HpText", flags).SetValue(form, hp.GetComponent<Text>());
            form.GetType().GetField("m_WeaponText", flags).SetValue(form, weapon.GetComponent<Text>());
            form.GetType().GetField("m_AmmoText", flags).SetValue(form, ammo.GetComponent<Text>());

            PrefabUtility.SaveAsPrefabAsset(root, BattleHudFormPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[HudBuilder] BattleHudForm prefab 已重建（含武器栏）: " + BattleHudFormPrefabPath);
        }

        /// <summary>创建锚定到指定角落的文本。</summary>
        private static GameObject CreateAnchoredText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = UIComponentFactory.CreateText(name, parent, content, fontSize, Vector2.zero, size, alignment);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.anchoredPosition = offset;
            return go;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}

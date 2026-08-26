//------------------------------------------------------------
// EmojiWar GameMain - 战斗 HUD 重建工具（Editor）
// 菜单：EmojiWar/Setup/04 - Rebuild Battle HUD
// 重建 BattleHudForm.prefab：金币/波次/HP + 完整武器栏
// （图标/名称/属性/弹药/射击冷却进度条/Mod 槽），样式来自 UI 框架。
// 参考旧版 WeaponPanel（武器名+图标+Mod 槽）与 SimpleWeaponPanel（冷却进度条）。
//------------------------------------------------------------

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 战斗 HUD 生成工具（含武器栏）。
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

            // 左下角：武器栏（参考旧版 WeaponPanel/SimpleWeaponPanel）
            //   图标 → 武器名 → 属性行 → 冷却进度条 → 弹药 → Mod 槽
            var weaponPanel = CreateWeaponBar(root.transform);

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_CoinText", flags).SetValue(form, coin.GetComponent<Text>());
            form.GetType().GetField("m_WaveText", flags).SetValue(form, wave.GetComponent<Text>());
            form.GetType().GetField("m_HpText", flags).SetValue(form, hp.GetComponent<Text>());

            // 武器栏引用
            form.GetType().GetField("m_WeaponIconImage", flags).SetValue(form, weaponPanel.IconImage);
            form.GetType().GetField("m_WeaponText", flags).SetValue(form, weaponPanel.NameText);
            form.GetType().GetField("m_AmmoText", flags).SetValue(form, weaponPanel.AmmoText);
            form.GetType().GetField("m_WeaponStatsText", flags).SetValue(form, weaponPanel.StatsText);
            form.GetType().GetField("m_FireProgressFill", flags).SetValue(form, weaponPanel.ProgressFill);
            form.GetType().GetField("m_ModSlotsText", flags).SetValue(form, weaponPanel.ModSlotsText);

            PrefabUtility.SaveAsPrefabAsset(root, BattleHudFormPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[HudBuilder] BattleHudForm prefab 已重建（含完整武器栏）: " + BattleHudFormPrefabPath);
        }

        /// <summary>武器栏各元素引用。</summary>
        private sealed class WeaponBarRefs
        {
            public Image IconImage;
            public Text NameText;
            public Text AmmoText;
            public Text StatsText;
            public Image ProgressFill;
            public Text ModSlotsText;
        }

        /// <summary>创建左下角武器栏（图标 + 名称 + 属性 + 冷却条 + 弹药 + Mod 槽）。</summary>
        private static WeaponBarRefs CreateWeaponBar(Transform parent)
        {
            var refs = new WeaponBarRefs();

            // 武器图标（emoji，左上角小方块内）
            var iconGo = new GameObject("WeaponIcon");
            iconGo.transform.SetParent(parent, false);
            var iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(0f, 0f);
            iconRect.pivot = new Vector2(0f, 0f);
            iconRect.anchoredPosition = new Vector2(30, 180);
            iconRect.sizeDelta = new Vector2(90, 90);
            var iconImage = iconGo.AddComponent<Image>();
            iconImage.color = new Color(0.2f, 0.25f, 0.32f, 0.9f);
            refs.IconImage = iconImage;

            // 武器名
            var nameText = CreateAnchoredText("WeaponText", parent, "武器：--", 28, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(140, 245), new Vector2(400, 40));
            refs.NameText = nameText.GetComponent<Text>();

            // 武器属性行（伤害/射速/散射）
            var statsText = CreateAnchoredText("WeaponStatsText", parent, "伤害 -- · 射速 --", 22, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(140, 205), new Vector2(500, 36));
            refs.StatsText = statsText.GetComponent<Text>();

            // 射击冷却进度条（底槽 + 填充，参考旧版 SimpleWeaponPanel.progress）
            var barGo = new GameObject("FireProgressBar");
            barGo.transform.SetParent(parent, false);
            var barRect = barGo.AddComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 0f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.anchoredPosition = new Vector2(30, 155);
            barRect.sizeDelta = new Vector2(420, 14);
            var barImage = barGo.AddComponent<Image>();
            barImage.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(barGo.transform, false);
            var fillRect = fillGo.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(420, 14);
            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = UIStyle.PrimaryColor;
            refs.ProgressFill = fillImage;

            // 弹药
            var ammoText = CreateAnchoredText("AmmoText", parent, "弹药 --/--", 26, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(30, 105), new Vector2(400, 40));
            refs.AmmoText = ammoText.GetComponent<Text>();

            // Mod 槽（参考旧版 WeaponPanel.ModGrid，简化为文本行）
            var modText = CreateAnchoredText("ModSlotsText", parent, "Mod 槽：无", 22, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(30, 55), new Vector2(500, 36));
            refs.ModSlotsText = modText.GetComponent<Text>();

            return refs;
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

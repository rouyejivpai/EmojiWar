//------------------------------------------------------------
// EmojiWar GameMain Editor - 战斗 HUD 预制体重建（按新布局）
// 菜单：EmojiWar/Setup/Build BattleHud Prefab (new layout)
// 布局：
//   左下 = 血条 + 能量条
//   中下 = 武器缩略 UI（图标 + 名称 + 射击冷却条）
//   右上 = 设置按钮
//   左上 = 金币 / 波次（保留原有信息）
// 产物：Assets/GameMain/UI/BattleHudForm.prefab（覆盖）
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    public static class BattleHudPrefabBuilder
    {
        public const string HudPrefabPath = "Assets/GameMain/UI/BattleHudForm.prefab";

        [MenuItem("EmojiWar/Setup/Build BattleHud Prefab (new layout)")]
        public static void BuildHud()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("BattleHudForm");
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<BattleHudForm>();
            root.AddComponent<QuickBind>();

            // ===== 左上：金币 / 波次 =====
            var coin = CreateText("txt_Coin", root.transform, "金币 0", 30, TextAnchor.MiddleLeft);
            AnchorTopLeft(coin, new Vector2(30f, -20f), new Vector2(360f, 44f));
            var wave = CreateText("txt_Wave", root.transform, "波次 0", 28, TextAnchor.MiddleLeft);
            AnchorTopLeft(wave, new Vector2(30f, -70f), new Vector2(360f, 40f));

            // ===== 右上：设置按钮 =====
            var settings = CreateButton("btn_Settings", root.transform, "设置", new Vector2(160f, 64f));
            AnchorTopRight(settings, new Vector2(-30f, -20f), new Vector2(160f, 64f));

            // ===== 左下：血条 + 能量条 =====
            var hpBg = CreateBar(root.transform, "HpBar", new Vector2(460f, 38f), new Vector2(40f, 118f),
                new Color(0.15f, 0.15f, 0.18f, 0.9f));
            var hpFill = CreateFill(hpBg, "Fill", new Color(0.85f, 0.25f, 0.28f, 1f));
            var enBg = CreateBar(root.transform, "EnergyBar", new Vector2(460f, 26f), new Vector2(40f, 64f),
                new Color(0.15f, 0.15f, 0.18f, 0.9f));
            var enFill = CreateFill(enBg, "Fill", new Color(0.25f, 0.6f, 0.95f, 1f));

            // ===== 中下：武器缩略 UI =====
            var weaponPanel = CreatePanel(root.transform, "WeaponPanel", new Vector2(420f, 132f), new Vector2(0f, 30f));
            var iconGo = new GameObject("img_WeaponIcon");
            iconGo.transform.SetParent(weaponPanel.transform, false);
            var iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(20f, 8f);
            iconRect.sizeDelta = new Vector2(92f, 92f);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.preserveAspect = true;

            var weaponName = CreateText("txt_WeaponName", weaponPanel.transform, "武器", 26, TextAnchor.MiddleLeft);
            var wnRect = weaponName.GetComponent<RectTransform>();
            wnRect.anchorMin = new Vector2(0f, 1f);
            wnRect.anchorMax = new Vector2(1f, 1f);
            wnRect.pivot = new Vector2(0.5f, 1f);
            wnRect.anchoredPosition = new Vector2(60f, -16f);
            wnRect.sizeDelta = new Vector2(-160f, 44f);

            var progressBg = CreateBar(weaponPanel.transform, "FireProgressBg", new Vector2(280f, 16f), new Vector2(20f, 22f),
                new Color(0.2f, 0.2f, 0.24f, 0.95f));
            var progressFill = CreateFill(progressBg, "Fill", new Color(0.95f, 0.75f, 0.25f, 1f));

            // ===== 绑定字段（BattleHudForm 私有字段，反射赋值） =====
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var t = typeof(BattleHudForm);
            t.GetField("m_CoinText", flags)?.SetValue(form, coin.GetComponent<Text>());
            t.GetField("m_WaveText", flags)?.SetValue(form, wave.GetComponent<Text>());
            t.GetField("m_HpFill", flags)?.SetValue(form, hpFill);
            t.GetField("m_EnergyFill", flags)?.SetValue(form, enFill);
            t.GetField("m_WeaponIconImage", flags)?.SetValue(form, iconImg);
            t.GetField("m_WeaponText", flags)?.SetValue(form, weaponName.GetComponent<Text>());
            t.GetField("m_FireProgressFill", flags)?.SetValue(form, progressFill);
            t.GetField("m_BtnSettings", flags)?.SetValue(form, settings.GetComponent<Button>());

            QuickBindGenerator.Process(root);
            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[BattleHudBuilder] 已重建 " + HudPrefabPath + "（血条/能量条左下、武器缩略中下、设置右上）");
        }

        // ---------- 布局辅助 ----------

        private static void AnchorTopLeft(GameObject go, Vector2 pos, Vector2 size)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        private static void AnchorTopRight(GameObject go, Vector2 pos, Vector2 size)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(1f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0f);
            r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.06f, 0.08f, 0.12f, 0.72f);
            return go;
        }

        /// <summary>左下角条形底（背景），返回底条物体。</summary>
        private static GameObject CreateBar(Transform parent, string name, Vector2 size, Vector2 pos, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0f, 0f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            return go;
        }

        /// <summary>条内填充块（左对齐，宽由代码按比例设置）。</summary>
        private static Image CreateFill(GameObject bar, string name, Color fillColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(bar.transform, false);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(2f, 0f);
            var barRect = bar.GetComponent<RectTransform>();
            r.sizeDelta = new Vector2(barRect.sizeDelta.x - 4f, -4f);
            var img = go.AddComponent<Image>();
            img.color = fillColor;
            return img;
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

            var textGo = CreateText("Label", go.transform, label, 26, TextAnchor.MiddleCenter);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
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

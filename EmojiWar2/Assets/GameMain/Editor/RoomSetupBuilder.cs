//------------------------------------------------------------
// EmojiWar GameMain - 房间搭建工具（Editor）
// 菜单：EmojiWar/Setup/02 - Build Room Setup
// 功能：
//   1. 在 Menu 场景的 ProcedureComponent 流程列表中加入 ProcedureRoom
//   2. 生成 RoomForm.prefab（房间标题 + 玩家列表 + 准备/离开按钮）
//------------------------------------------------------------

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 联机房间 + 角色选择搭建工具。
    /// </summary>
    public static class RoomSetupBuilder
    {
        private const string MenuScenePath = "Assets/GameMain/Scenes/Menu.unity";
        private const string RoomFormPrefabPath = "Assets/GameMain/UI/RoomForm.prefab";
        private const string CharacterSelectFormPrefabPath = "Assets/GameMain/UI/CharacterSelectForm.prefab";

        // 流程类型全名（含 ProcedureRoom + ProcedureCharacterSelect）——单一来源：ProcedureTypes（GF 规范）
        private static readonly string[] ProcedureTypeNames = Procedure.ProcedureTypes.Available;

        [MenuItem("EmojiWar/Setup/02 - Build Room Setup")]
        public static void BuildRoomSetup()
        {
            UpdateProcedureList();
            CreateRoomFormPrefab();
            CreateCharacterCardPrefab();
            CreateCharacterDockFormPrefab();
            CreateCharacterSelectFormPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[RoomSetup] 完成：流程列表已更新 + RoomForm/CharacterCard/CharacterDockForm/CharacterSelectForm prefab 已生成");
        }

        /// <summary>
        /// 生成 CharacterCard.prefab（角色卡片独立模板，运行时按 JSON 动态实例化）。
        /// 内容：emoji 图标 + 名字 + 简略属性 + 选中高亮背景 + 整卡 Button。
        /// </summary>
        private static void CreateCharacterCardPrefab()
        {
            const string path = "Assets/GameMain/Resources/UI/CharacterCard.prefab";
            EnsureFolder("Assets/GameMain/Resources/UI");

            var root = new GameObject("CharacterCard");
            root.layer = LayerMask.NameToLayer("UI");

            var rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300f, 160f);

            // 卡片背景（整卡可点）
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.2f, 0.3f, 0.9f);
            var btn = root.AddComponent<Button>();
            btn.targetGraphic = bg;

            // 选中高亮背景（默认隐藏）
            var highlight = new GameObject("Highlight");
            highlight.transform.SetParent(root.transform, false);
            var hlRect = highlight.AddComponent<RectTransform>();
            hlRect.anchorMin = Vector2.zero;
            hlRect.anchorMax = Vector2.one;
            hlRect.offsetMin = Vector2.zero;
            hlRect.offsetMax = Vector2.zero;
            var hlImg = highlight.AddComponent<Image>();
            hlImg.color = new Color(0.3f, 0.7f, 1f, 0.35f);
            highlight.SetActive(false);

            // emoji 图标（左侧）
            var icon = new GameObject("Icon");
            icon.transform.SetParent(root.transform, false);
            var iconRect = icon.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(16f, 20f);
            iconRect.sizeDelta = new Vector2(72f, 72f);
            var iconImg = icon.AddComponent<Image>();
            iconImg.preserveAspect = true;

            // 名字（右上）
            var name = new GameObject("Name");
            name.transform.SetParent(root.transform, false);
            var nameRect = name.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0.5f, 1f);
            nameRect.anchorMax = new Vector2(0.5f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(60f, -10f);
            nameRect.sizeDelta = new Vector2(220f, 40f);
            var nameText = name.AddComponent<Text>();
            nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            nameText.fontSize = 22;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.color = Color.white;
            nameText.text = "角色名";

            // 简略属性（右下）
            var stats = new GameObject("Stats");
            stats.transform.SetParent(root.transform, false);
            var statsRect = stats.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0.5f, 0f);
            statsRect.anchorMax = new Vector2(0.5f, 0f);
            statsRect.pivot = new Vector2(0.5f, 0f);
            statsRect.anchoredPosition = new Vector2(60f, 14f);
            statsRect.sizeDelta = new Vector2(220f, 32f);
            var statsText = stats.AddComponent<Text>();
            statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statsText.fontSize = 16;
            statsText.alignment = TextAnchor.MiddleLeft;
            statsText.color = new Color(0.7f, 0.8f, 0.9f, 1f);
            statsText.text = "生命 - 移速 -";

            var card = root.AddComponent<CharacterCard>();

            // 通过反射绑定字段（与现有 Builder 风格一致）
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            card.GetType().GetField("m_IconImage", flags).SetValue(card, iconImg);
            card.GetType().GetField("m_NameText", flags).SetValue(card, nameText);
            card.GetType().GetField("m_StatsText", flags).SetValue(card, statsText);
            card.GetType().GetField("m_Highlight", flags).SetValue(card, hlImg);
            card.GetType().GetField("m_Button", flags).SetValue(card, btn);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[RoomSetup] CharacterCard prefab 已生成（独立模板）: " + path);
        }

        /// <summary>更新 Menu 场景的 ProcedureComponent 流程列表（加入 ProcedureRoom）。</summary>
        private static void UpdateProcedureList()
        {
            var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            var gf = GameObject.Find("GameFramework");
            if (gf == null)
            {
                Debug.LogError("[RoomSetup] GameFramework object not found in Menu scene");
                return;
            }

            var procedure = gf.GetComponentInChildren<ProcedureComponent>();
            if (procedure == null)
            {
                Debug.LogError("[RoomSetup] ProcedureComponent not found");
                return;
            }

            var so = new SerializedObject(procedure);
            var availableNames = so.FindProperty("m_AvailableProcedureTypeNames");
            availableNames.arraySize = ProcedureTypeNames.Length;
            for (int i = 0; i < ProcedureTypeNames.Length; i++)
            {
                availableNames.GetArrayElementAtIndex(i).stringValue = ProcedureTypeNames[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[RoomSetup] Procedure 列表已更新（6 个流程）");
        }

        /// <summary>生成 RoomForm.prefab（QuickBind 命名约定：txt_/btn_ 前缀）。</summary>
        private static void CreateRoomFormPrefab()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("RoomForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent<RoomForm>();
            root.AddComponent<QuickBind>();

            // ===== 房间面板：停靠屏幕右侧边缘（窄条，不遮挡中央场景） =====
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);      // 锚右缘
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(420f, 0f);    // 右侧 420px 窄条

            // 半透明背景（右侧面板）
            var bg = new GameObject("bg_Panel");
            bg.transform.SetParent(root.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.06f, 0.08f, 0.12f, 0.85f);

            // 标题（约定：txt_）
            CreateText("txt_Title", root.transform, "联机房间", 40, new Vector2(0, 380));

            // 玩家列表（约定：txt_）
            var playerList = CreateText("txt_PlayerList", root.transform, "等待玩家加入...", 26, new Vector2(0, 120));
            playerList.GetComponent<RectTransform>().sizeDelta = new Vector2(380, 400);
            playerList.GetComponent<Text>().alignment = TextAnchor.UpperCenter;

            // 状态提示（约定：txt_）
            CreateText("txt_Status", root.transform, "全部准备后自动开始", 20, new Vector2(0, -180));

            // 准备按钮（约定：btn_）
            CreateButton("btn_Ready", root.transform, "准备", new Vector2(0, -320));
            // 切换角色按钮（约定：btn_；展开/收起右侧角色抽屉）
            CreateButton("btn_ChangeChar", root.transform, "切换角色", new Vector2(-100, -420));
            // 离开/解散按钮（约定：btn_）
            CreateButton("btn_Leave", root.transform, "离开房间", new Vector2(100, -420));

            // QuickBind：先扫描子物体填充绑定表 + 生成 RoomForm.QuickBind.cs 绑定代码，
            // 再保存 prefab —— 顺序不能反：若先 SaveAsPrefabAsset，保存的是空绑定表，
            // 构建版 AssetBundle 里的 prefab 无按钮引用，点击事件不会挂载（按钮无反应）。
            QuickBindGenerator.Process(root);
            PrefabUtility.SaveAsPrefabAsset(root, RoomFormPrefabPath);

            Object.DestroyImmediate(root);
            Debug.Log("[RoomSetup] RoomForm prefab 已生成（右侧边缘布局 + 切换角色按钮，QuickBind）: " + RoomFormPrefabPath);
        }

        /// <summary>
        /// 生成 CharacterDockForm.prefab（角色选择全屏面板骨架，QuickBind 绑定）。
        /// 结构（角色卡片运行时按 character_select.json 动态实例化，见 CharacterDockForm.BuildCards）：
        ///   CharacterDockForm（全屏 Canvas 根，raycast 区域）
        ///   ├── panel_PanelSlide（滑动主体：右缘进/出，含全部内容）
        ///   │   ├── 顶部：btn_Collapse 收起 + btn_Confirm 确认选择 + txt_SelectTitle 标题
        ///   │   ├── CardContainer（卡片容器，运行时填卡片）
        ///   │   └── 右栏：txt_DetailIcon（大 emoji）+ txt_DetailName/Desc/Stats
        ///   └── btn_Tab（右侧窄条标签，常驻；点击滑出全屏面板）
        /// </summary>
        private static void CreateCharacterDockFormPrefab()
        {
            const string path = "Assets/GameMain/UI/CharacterDockForm.prefab";
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("CharacterDockForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent<CharacterDockForm>();
            root.AddComponent<QuickBind>();

            // 根：全屏（raycast 覆盖整屏；SlideTarget 为 PanelSlide 子物体）
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // ===== 滑动主体 panel_PanelSlide（锚右缘，宽 1920 全屏；向右滑出） =====
            var slide = new GameObject("panel_PanelSlide");
            slide.transform.SetParent(root.transform, false);
            var slideRect = slide.AddComponent<RectTransform>();
            slideRect.anchorMin = new Vector2(1f, 0f);
            slideRect.anchorMax = new Vector2(1f, 1f);
            slideRect.pivot = new Vector2(1f, 0.5f);
            slideRect.anchoredPosition = Vector2.zero;
            slideRect.sizeDelta = new Vector2(1920f, 0f);
            slide.AddComponent<Image>().color = new Color(0.05f, 0.1f, 0.16f, 0.92f);
            slide.AddComponent<CanvasGroup>();   // raycast 控制器（展开拦截/收起释放）

            // 顶部：标题 + 收起 + 确认选择
            CreateText("txt_SelectTitle", slide.transform, "选择角色", 44, new Vector2(0, 460));
            CreateButton("btn_Collapse", slide.transform, "收起", new Vector2(-820, 460));
            CreateButton("btn_Confirm", slide.transform, "确认选择", new Vector2(-600, 460));

            // ===== 卡片容器（运行时动态实例化 CharacterCard） =====
            var cardContainer = new GameObject("CardContainer");
            cardContainer.transform.SetParent(slide.transform, false);
            var ccRect = cardContainer.AddComponent<RectTransform>();
            ccRect.anchorMin = new Vector2(0f, 0f);
            ccRect.anchorMax = new Vector2(1f, 1f);
            ccRect.offsetMin = Vector2.zero;
            ccRect.offsetMax = Vector2.zero;

            // ===== 右栏：选中角色详情 =====
            CreateText("txt_DetailIcon", slide.transform, "😅", 120, new Vector2(400, 280));
            CreateText("txt_DetailName", slide.transform, "角色名", 40, new Vector2(400, 120));
            CreateText("txt_DetailDesc", slide.transform, "描述", 24, new Vector2(400, 20));
            CreateText("txt_DetailStats", slide.transform, "生命 / 移速 / 金币", 24, new Vector2(400, -100));

            // 收起态标签（右侧窄条内）
            CreateText("txt_CurrentChar", root.transform, "角色", 22, new Vector2(0, 0));

            // ===== 右侧窄条 Tab（常驻，点击滑出全屏面板） =====
            var tab = new GameObject("btn_Tab");
            tab.transform.SetParent(root.transform, false);
            var tabRect = tab.AddComponent<RectTransform>();
            tabRect.anchorMin = new Vector2(1f, 0.5f);
            tabRect.anchorMax = new Vector2(1f, 0.5f);
            tabRect.pivot = new Vector2(1f, 0.5f);
            tabRect.anchoredPosition = new Vector2(-8f, 0f);
            tabRect.sizeDelta = new Vector2(64f, 180f);
            var tabImg = tab.AddComponent<Image>();
            tabImg.color = new Color(0.1f, 0.2f, 0.3f, 0.85f);
            var tabBtn = tab.AddComponent<Button>();
            tabBtn.targetGraphic = tabImg;
            var tabLabel = CreateText("TabLabel", tab.transform, "选角色", 20, Vector2.zero);
            tabLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(56, 160);

            QuickBindGenerator.Process(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);

            Object.DestroyImmediate(root);
            Debug.Log("[RoomSetup] CharacterDockForm prefab 已生成（骨架 + 动态卡片容器 + 确认按钮）: " + path);
        }

        /// <summary>生成 CharacterSelectForm.prefab（4 个角色按钮）。</summary>
        private static void CreateCharacterSelectFormPrefab()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("CharacterSelectForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<CharacterSelectForm>();

            // 标题
            var title = CreateText("Title", root.transform, "选择你的角色", 56, new Vector2(0, 400));
            // 说明
            var info = CreateText("Info", root.transform, "不同角色拥有不同的生命值与移动速度", 24, new Vector2(0, 320));

            // 4 个角色按钮（两行两列）
            var char1 = CreateCharacterCard("Char1", root.transform, "流汗黄豆", "1f605", new Vector2(-400, 80));
            var char2 = CreateCharacterCard("Char2", root.transform, "好吃黄豆", "1f60b", new Vector2(400, 80));
            var char3 = CreateCharacterCard("Char3", root.transform, "硬汉黄豆", "1f621", new Vector2(-400, -120));
            var char4 = CreateCharacterCard("Char4", root.transform, "快枪黄豆", "26a1", new Vector2(400, -120));

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_TitleText", flags).SetValue(form, title.GetComponent<Text>());
            form.GetType().GetField("m_InfoText", flags).SetValue(form, info.GetComponent<Text>());
            form.GetType().GetField("m_Char1Button", flags).SetValue(form, char1.GetComponent<Button>());
            form.GetType().GetField("m_Char2Button", flags).SetValue(form, char2.GetComponent<Button>());
            form.GetType().GetField("m_Char3Button", flags).SetValue(form, char3.GetComponent<Button>());
            form.GetType().GetField("m_Char4Button", flags).SetValue(form, char4.GetComponent<Button>());

            PrefabUtility.SaveAsPrefabAsset(root, CharacterSelectFormPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[RoomSetup] CharacterSelectForm prefab 已生成: " + CharacterSelectFormPrefabPath);
        }

        /// <summary>创建角色卡片（emoji 图标 + 名字，按钮可点）。</summary>
        private static GameObject CreateCharacterCard(string name, Transform parent, string label, string icon, Vector2 anchoredPos)
        {
            var go = CreateButton(name, parent, label, anchoredPos);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(360, 140);

            // 角色 emoji 图标（左侧）
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(20, 0);
            iconRect.sizeDelta = new Vector2(80, 80);
            var iconImage = iconGo.AddComponent<Image>();
            iconImage.sprite = EmojiWar.GameMain.Art.ArtManager.GetCharacterSprite(icon);

            // 文字右移
            var labelText = go.GetComponentInChildren<Text>();
            if (labelText != null)
            {
                var lr = labelText.GetComponent<RectTransform>();
                lr.anchorMin = new Vector2(1f, 0.5f);
                lr.anchorMax = new Vector2(1f, 0.5f);
                lr.pivot = new Vector2(1f, 0.5f);
                lr.anchoredPosition = new Vector2(-20, 0);
                lr.sizeDelta = new Vector2(220, 100);
                labelText.alignment = TextAnchor.MiddleRight;
            }
            return go;
        }

        private static GameObject CreateText(string name, Transform parent, string content, int fontSize, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(800, 120);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string label, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(320, 80);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.5f, 0.9f, 1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var labelGo = CreateText("Label", go.transform, label, 32, Vector2.zero);
            labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 60);

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

// touch 22:36: force editor recompile

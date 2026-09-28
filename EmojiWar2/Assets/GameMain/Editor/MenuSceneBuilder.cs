//------------------------------------------------------------
// EmojiWar GameMain - 场景一键搭建工具（Editor）
// 菜单：EmojiWar/Setup/01 - Build Menu Scene
// 功能：
//   1. 新建 Menu 场景
//   2. 实例化 GameFramework.prefab 并配置 Procedure 流程列表
//   3. 配置 UIComponent 的 UI 组（Default）
//   4. 创建 GameEntry 启动对象
//   5. 生成 MenuForm.prefab（标题 + 开始按钮）
//   6. 保存场景与预制体
//------------------------------------------------------------

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using EmojiWar.GameMain.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 菜单场景搭建工具。
    /// </summary>
    public static class MenuSceneBuilder
    {
        private const string GameFrameworkPrefabPath = "Packages/com.jiangyin.gameframework/GameFramework.prefab";
        private const string MenuScenePath = "Assets/GameMain/Scenes/Menu.unity";
        private const string MenuFormPrefabPath = "Assets/GameMain/UI/MenuForm.prefab";
        private const string MultiplayerFormPrefabPath = "Assets/GameMain/UI/MultiplayerForm.prefab";
        private const string JoinListFormPrefabPath = "Assets/GameMain/UI/JoinListForm.prefab";
        private const string LobbyFormPrefabPath = "Assets/GameMain/UI/LobbyForm.prefab";
        private const string GameOverFormPrefabPath = "Assets/GameMain/UI/GameOverForm.prefab";
        private const string SettingsFormPrefabPath = "Assets/GameMain/UI/SettingsForm.prefab";

        // 流程类型全名（供 ProcedureComponent 反射创建）——单一来源：ProcedureTypes（GF 规范）
        private static readonly string[] ProcedureTypeNames = Procedure.ProcedureTypes.Available;
        private const string EntranceProcedureTypeName = Procedure.ProcedureTypes.Entrance;

        [MenuItem("EmojiWar/Setup/01 - Build Menu Scene")]
        public static void BuildMenuScene()
        {
            EnsureFolder("Assets/GameMain/Scenes");
            EnsureFolder("Assets/GameMain/UI");

            // 新建场景
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SceneManager.SetActiveScene(newScene);

            // 1. 实例化 GameFramework prefab
            var gfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameFrameworkPrefabPath);
            if (gfPrefab == null)
            {
                Debug.LogError($"[SceneBuilder] GameFramework prefab not found at {GameFrameworkPrefabPath}");
                return;
            }

            var gfInstance = (GameObject)PrefabUtility.InstantiatePrefab(gfPrefab);
            gfInstance.name = "GameFramework";

            // 2. 配置 Procedure 流程列表
            var procedure = gfInstance.GetComponentInChildren<ProcedureComponent>();
            if (procedure == null)
            {
                Debug.LogError("[SceneBuilder] ProcedureComponent not found in GameFramework prefab.");
                return;
            }

            var so = new SerializedObject(procedure);
            var availableNames = so.FindProperty("m_AvailableProcedureTypeNames");
            availableNames.arraySize = ProcedureTypeNames.Length;
            for (int i = 0; i < ProcedureTypeNames.Length; i++)
            {
                availableNames.GetArrayElementAtIndex(i).stringValue = ProcedureTypeNames[i];
            }
            so.FindProperty("m_EntranceProcedureTypeName").stringValue = EntranceProcedureTypeName;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 3. 配置 UI 组（Default）
            var ui = gfInstance.GetComponentInChildren<UIComponent>();
            if (ui != null)
            {
                var uiSo = new SerializedObject(ui);
                var uiGroups = uiSo.FindProperty("m_UIGroups");
                uiGroups.arraySize = 1;
                var group = uiGroups.GetArrayElementAtIndex(0);
                group.FindPropertyRelative("m_Name").stringValue = "Default";
                group.FindPropertyRelative("m_Depth").intValue = 0;
                uiSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 4. 创建 GameEntry 启动对象
            var gameEntryGo = new GameObject("GameEntry");
            gameEntryGo.AddComponent<GameEntry>();

            // 5. 创建主菜单 UI（场景内布局 + 保存为 prefab）
            GameObject menuFormGo = CreateMenuForm();
            if (menuFormGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(menuFormGo, MenuFormPrefabPath);
                Object.DestroyImmediate(menuFormGo);
            }

            // 5.5 生成多人游戏 UI prefab（创建房间 / 加入游戏）
            GameObject multiFormGo = CreateMultiplayerForm();
            if (multiFormGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(multiFormGo, MultiplayerFormPrefabPath);
                Object.DestroyImmediate(multiFormGo);
            }

            // 5.6 生成加入游戏（房间列表）UI prefab
            GameObject joinListGo = CreateJoinListForm();
            if (joinListGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(joinListGo, JoinListFormPrefabPath);
                Object.DestroyImmediate(joinListGo);
            }

            // 5.7 生成大厅 UI prefab
            GameObject lobbyFormGo = CreateLobbyForm();
            if (lobbyFormGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(lobbyFormGo, LobbyFormPrefabPath);
                Object.DestroyImmediate(lobbyFormGo);
            }

            // 5.8 生成结算 UI prefab
            GameObject gameOverFormGo = CreateGameOverForm();
            if (gameOverFormGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(gameOverFormGo, GameOverFormPrefabPath);
                Object.DestroyImmediate(gameOverFormGo);
            }

            // 5.9 生成设置 UI prefab（窗口化勾选）
            GameObject settingsFormGo = CreateSettingsForm();
            if (settingsFormGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(settingsFormGo, SettingsFormPrefabPath);
                Object.DestroyImmediate(settingsFormGo);
            }

            // 6. 保存场景
            EditorSceneManager.SaveScene(newScene, MenuScenePath);

            Debug.Log($"[SceneBuilder] Menu scene saved to {MenuScenePath}");
            Debug.Log($"[SceneBuilder] MenuForm prefab saved to {MenuFormPrefabPath}");
            Debug.Log("[SceneBuilder] Done. Press Play to run the menu.");
        }

        /// <summary>
        /// 创建主菜单 UI 层级。
        /// </summary>
        private static GameObject CreateMenuForm()
        {
            var root = new GameObject("MenuForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<MenuForm>();

            // 标题
            var title = CreateText("Title", root.transform, "Emoji War", 72, new Vector2(0, 330));
            form.GetType().GetField("m_TitleText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, title.GetComponent<Text>());

            // 版本号
            var version = CreateText("Version", root.transform, "v0.3.0 - 多人联机版", 28, new Vector2(0, 240));
            form.GetType().GetField("m_VersionText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, version.GetComponent<Text>());

            // 提示行（单人占位提示等）
            var hint = CreateText("HintText", root.transform, "", 26, new Vector2(0, -420));
            form.GetType().GetField("m_HintText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, hint.GetComponent<Text>());

            // 主界面四个按钮：单人游戏 / 多人游戏 / 设置 / 退出游戏
            var singleGo = CreateButton("SingleButton", root.transform, "单人游戏", new Vector2(0, 40));
            form.GetType().GetField("m_SingleButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, singleGo.GetComponent<Button>());

            var multiGo = CreateButton("MultiplayerButton", root.transform, "多人游戏", new Vector2(0, -80));
            form.GetType().GetField("m_MultiplayerButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, multiGo.GetComponent<Button>());

            var settingsGo = CreateButton("SettingsButton", root.transform, "设置", new Vector2(0, -200));
            form.GetType().GetField("m_SettingsButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, settingsGo.GetComponent<Button>());

            var quitGo = CreateButton("QuitButton", root.transform, "退出游戏", new Vector2(0, -320));
            form.GetType().GetField("m_QuitButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, quitGo.GetComponent<Button>());

            // 确保 EventSystem 存在（按钮点击需要）
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            return root;
        }

        /// <summary>
        /// 创建多人游戏 UI（玩家名输入 + 创建房间/加入游戏/返回）。
        /// </summary>
        private static GameObject CreateMultiplayerForm()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("MultiplayerForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<MultiplayerForm>();

            // 标题
            CreateText("Title", root.transform, "多人游戏", 56, new Vector2(0, 380));

            // 玩家名输入
            var nameInput = CreateInputField("NameInput", root.transform, "玩家名", new Vector2(0, 250), 500, 70);

            // 按钮
            var createBtn = CreateButton("CreateButton", root.transform, "创建房间", new Vector2(0, 80));
            var joinBtn = CreateButton("JoinButton", root.transform, "加入游戏", new Vector2(0, -40));
            var backBtn = CreateButton("BackButton", root.transform, "返回", new Vector2(0, -200));

            // 通过反射绑定字段
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_NameInput", flags).SetValue(form, nameInput.GetComponent<InputField>());
            form.GetType().GetField("m_CreateButton", flags).SetValue(form, createBtn.GetComponent<Button>());
            form.GetType().GetField("m_JoinButton", flags).SetValue(form, joinBtn.GetComponent<Button>());
            form.GetType().GetField("m_BackButton", flags).SetValue(form, backBtn.GetComponent<Button>());

            return root;
        }

        /// <summary>
        /// 创建加入游戏（房间列表）UI：列表容器 + 状态文本 + 刷新/返回。
        /// 房间项为运行时按 RoomDiscovery 结果动态生成（占位约定见 doc §7）。
        /// </summary>
        private static GameObject CreateJoinListForm()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("JoinListForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<JoinListForm>();

            // 半透明背景
            var bg = new GameObject("bg");
            bg.transform.SetParent(root.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            // 标题
            CreateText("Title", root.transform, "加入游戏 - 房间列表", 48, new Vector2(0, 430));

            // 状态文本（扫描中/空列表提示）
            var status = CreateText("StatusText", root.transform, "扫描房间中...", 28, new Vector2(0, 330));
            status.GetComponent<RectTransform>().sizeDelta = new Vector2(1200, 60);

            // 房间列表容器（占位：运行时空容器，运行时生成房间项）
            var container = new GameObject("RoomContainer");
            container.transform.SetParent(root.transform, false);
            var containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(0f, -40f);
            containerRect.sizeDelta = new Vector2(1200f, 720f);

            // 占位房间项（§7 约定：编辑器可观察层级；运行时先清空再按发现结果生成）
            var template = new GameObject("RoomItem_Template");
            template.transform.SetParent(container.transform, false);
            var templateRect = template.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0.5f, 1f);
            templateRect.anchorMax = new Vector2(0.5f, 1f);
            templateRect.pivot = new Vector2(0.5f, 0.5f);
            templateRect.anchoredPosition = new Vector2(0f, -90f);
            templateRect.sizeDelta = new Vector2(900f, 70f);
            var templateImage = template.AddComponent<Image>();
            templateImage.color = new Color(0.15f, 0.45f, 0.8f, 0.35f);
            template.AddComponent<Button>().targetGraphic = templateImage;
            var templateLabel = new GameObject("Label");
            templateLabel.transform.SetParent(template.transform, false);
            var templateLabelRect = templateLabel.AddComponent<RectTransform>();
            templateLabelRect.anchorMin = Vector2.zero;
            templateLabelRect.anchorMax = Vector2.one;
            templateLabelRect.offsetMin = Vector2.zero;
            templateLabelRect.offsetMax = Vector2.zero;
            var templateText = templateLabel.AddComponent<Text>();
            templateText.text = "房间示例（运行时填充）";
            templateText.fontSize = 30;
            templateText.alignment = TextAnchor.MiddleCenter;
            templateText.color = new Color(1f, 1f, 1f, 0.5f);
            templateText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 按钮：刷新 / 返回
            var refreshBtn = CreateButton("RefreshButton", root.transform, "刷新列表", new Vector2(-280, -430));
            var backBtn = CreateButton("BackButton", root.transform, "返回", new Vector2(280, -430));

            // 通过反射绑定字段（容器不绑定，运行时 Find）
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_StatusText", flags).SetValue(form, status.GetComponent<Text>());
            form.GetType().GetField("m_RefreshButton", flags).SetValue(form, refreshBtn.GetComponent<Button>());
            form.GetType().GetField("m_BackButton", flags).SetValue(form, backBtn.GetComponent<Button>());

            return root;
        }

        /// <summary>
        /// 创建设置 UI 层级（窗口化勾选用 Button 显示勾选态，非 Toggle）。
        /// </summary>
        private static GameObject CreateSettingsForm()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("SettingsForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<SettingsForm>();

            // 半透明背景
            var bg = new GameObject("bg");
            bg.transform.SetParent(root.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            // 标题
            CreateText("Title", root.transform, "设置", 56, new Vector2(0, 300));

            // 窗口化勾选（btn_ 前缀显示勾选态；label 是其子 Text）
            var windowedBtn = CreateButton("WindowedButton", root.transform, "✓ 窗口化", new Vector2(0, 80));
            var windowedLabel = windowedBtn.GetComponentInChildren<Text>();
            form.GetType().GetField("m_WindowedButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, windowedBtn.GetComponent<Button>());
            form.GetType().GetField("m_WindowedLabel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, windowedLabel);

            // 返回按钮
            var backBtn = CreateButton("BackButton", root.transform, "返回", new Vector2(0, -120));
            form.GetType().GetField("m_BackButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, backBtn.GetComponent<Button>());

            return root;
        }

        /// <summary>
        /// 创建大厅 UI 层级（玩家名/IP 输入 + 创建/加入/返回按钮）。
        /// </summary>
        private static GameObject CreateLobbyForm()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("LobbyForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<LobbyForm>();

            // 标题
            CreateText("Title", root.transform, "多人大厅", 48, new Vector2(0, 380));

            // 玩家名输入
            var nameInput = CreateInputField("NameInput", root.transform, "玩家名", new Vector2(0, 260), 400, 70);
            // IP 输入
            var ipInput = CreateInputField("IpInput", root.transform, "服务器 IP", new Vector2(0, 160), 400, 70);

            // 状态文本
            var status = CreateText("StatusText", root.transform, "创建房间成为房主，或加入好友房间", 24, new Vector2(0, 50));

            // 按钮
            var createBtn = CreateButton("CreateButton", root.transform, "创建房间", new Vector2(-200, -80));
            var joinBtn = CreateButton("JoinButton", root.transform, "加入房间", new Vector2(200, -80));
            var backBtn = CreateButton("BackButton", root.transform, "返回", new Vector2(0, -200));

            // 通过反射绑定字段
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_NameInput", flags).SetValue(form, nameInput.GetComponent<InputField>());
            form.GetType().GetField("m_IpInput", flags).SetValue(form, ipInput.GetComponent<InputField>());
            form.GetType().GetField("m_StatusText", flags).SetValue(form, status.GetComponent<Text>());
            form.GetType().GetField("m_CreateButton", flags).SetValue(form, createBtn.GetComponent<Button>());
            form.GetType().GetField("m_JoinButton", flags).SetValue(form, joinBtn.GetComponent<Button>());
            form.GetType().GetField("m_BackButton", flags).SetValue(form, backBtn.GetComponent<Button>());

            return root;
        }

        /// <summary>
        /// 创建游戏结束 UI（标题 + 结算信息 + 重新开始/返回菜单）。
        /// </summary>
        private static GameObject CreateGameOverForm()
        {
            EnsureFolder("Assets/GameMain/UI");

            var root = new GameObject("GameOverForm");
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            var form = root.AddComponent<GameOverForm>();

            // 半透明背景
            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(root.transform, false);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.8f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // 标题
            var title = CreateText("Title", root.transform, "游戏结束", 64, new Vector2(0, 150));
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 100);

            // 结算信息
            var stats = CreateText("StatsText", root.transform, "坚持到第 0 波\n金币 0", 36, new Vector2(0, 0));
            stats.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 120);

            // 按钮
            var restartBtn = CreateButton("RestartButton", root.transform, "重新开始", new Vector2(-200, -200));
            var menuBtn = CreateButton("MenuButton", root.transform, "返回菜单", new Vector2(200, -200));

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_TitleText", flags).SetValue(form, title.GetComponent<Text>());
            form.GetType().GetField("m_StatsText", flags).SetValue(form, stats.GetComponent<Text>());
            form.GetType().GetField("m_RestartButton", flags).SetValue(form, restartBtn.GetComponent<Button>());
            form.GetType().GetField("m_MenuButton", flags).SetValue(form, menuBtn.GetComponent<Button>());

            return root;
        }

        /// <summary>
        /// 创建输入框。
        /// </summary>
        private static GameObject CreateInputField(string name, Transform parent, string placeholder, Vector2 anchoredPos, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(width, height);

            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.9f);

            var input = go.AddComponent<InputField>();

            // 文本子对象
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 5);
            textRect.offsetMax = new Vector2(-10, -5);
            var text = textGo.AddComponent<Text>();
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.black;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 占位符
            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(go.transform, false);
            var phRect = placeholderGo.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10, 5);
            phRect.offsetMax = new Vector2(-10, -5);
            var phText = placeholderGo.AddComponent<Text>();
            phText.text = placeholder;
            phText.fontSize = 26;
            phText.alignment = TextAnchor.MiddleLeft;
            phText.color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
            phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            input.textComponent = text;
            input.placeholder = phText;

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

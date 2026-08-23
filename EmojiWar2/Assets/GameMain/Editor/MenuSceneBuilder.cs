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
        private const string LobbyFormPrefabPath = "Assets/GameMain/UI/LobbyForm.prefab";

        // 流程类型全名（供 ProcedureComponent 反射创建）
        private static readonly string[] ProcedureTypeNames =
        {
            "EmojiWar.GameMain.Procedure.ProcedureLaunch",
            "EmojiWar.GameMain.Procedure.ProcedureMenu",
            "EmojiWar.GameMain.Procedure.ProcedureLobby",
            "EmojiWar.GameMain.Procedure.ProcedureBattle",
        };
        private const string EntranceProcedureTypeName = "EmojiWar.GameMain.Procedure.ProcedureLaunch";

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

            // 5.5 生成大厅 UI prefab
            GameObject lobbyFormGo = CreateLobbyForm();
            if (lobbyFormGo != null)
            {
                PrefabUtility.SaveAsPrefabAsset(lobbyFormGo, LobbyFormPrefabPath);
                Object.DestroyImmediate(lobbyFormGo);
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
            var title = CreateText("Title", root.transform, "Emoji War", 72, new Vector2(0, 200));
            form.GetType().GetField("m_TitleText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, title.GetComponent<Text>());

            // 版本号
            var version = CreateText("Version", root.transform, "v0.1.0 - 架构重构版", 28, new Vector2(0, 120));
            form.GetType().GetField("m_VersionText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, version.GetComponent<Text>());

            // 开始按钮
            var buttonGo = CreateButton("StartButton", root.transform, "开始游戏", new Vector2(0, -100));
            form.GetType().GetField("m_StartButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(form, buttonGo.GetComponent<Button>());

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

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

        // 流程类型全名（供 ProcedureComponent 反射创建）
        private static readonly string[] ProcedureTypeNames =
        {
            "EmojiWar.GameMain.Procedure.ProcedureLaunch",
            "EmojiWar.GameMain.Procedure.ProcedureMenu",
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

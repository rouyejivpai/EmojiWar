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
            CreateCharacterSelectFormPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[RoomSetup] 完成：流程列表已更新 + RoomForm/CharacterSelectForm prefab 已生成");
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

            // 标题（约定：txt_）
            CreateText("txt_Title", root.transform, "联机房间", 56, new Vector2(0, 380));

            // 玩家列表（约定：txt_）
            var playerList = CreateText("txt_PlayerList", root.transform, "等待玩家加入...", 32, new Vector2(0, 120));
            playerList.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 400);
            playerList.GetComponent<Text>().alignment = TextAnchor.UpperCenter;

            // 状态提示（约定：txt_）
            CreateText("txt_Status", root.transform, "全部准备后自动开始", 24, new Vector2(0, -180));

            // 准备按钮（约定：btn_）
            CreateButton("btn_Ready", root.transform, "准备", new Vector2(-200, -320));
            // 离开/解散按钮（约定：btn_）
            CreateButton("btn_Leave", root.transform, "离开房间", new Vector2(200, -320));

            PrefabUtility.SaveAsPrefabAsset(root, RoomFormPrefabPath);

            // QuickBind：扫描子物体 → 生成 RoomForm.QuickBind.cs 绑定代码
            QuickBindGenerator.Process(root);

            Object.DestroyImmediate(root);
            Debug.Log("[RoomSetup] RoomForm prefab 已生成（QuickBind）: " + RoomFormPrefabPath);
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

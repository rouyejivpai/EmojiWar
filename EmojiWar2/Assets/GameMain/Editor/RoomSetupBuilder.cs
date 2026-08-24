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
    /// 联机房间搭建工具。
    /// </summary>
    public static class RoomSetupBuilder
    {
        private const string MenuScenePath = "Assets/GameMain/Scenes/Menu.unity";
        private const string RoomFormPrefabPath = "Assets/GameMain/UI/RoomForm.prefab";

        // 流程类型全名（含 ProcedureRoom）
        private static readonly string[] ProcedureTypeNames =
        {
            "EmojiWar.GameMain.Procedure.ProcedureLaunch",
            "EmojiWar.GameMain.Procedure.ProcedureMenu",
            "EmojiWar.GameMain.Procedure.ProcedureLobby",
            "EmojiWar.GameMain.Procedure.ProcedureRoom",
            "EmojiWar.GameMain.Procedure.ProcedureBattle",
            "EmojiWar.GameMain.Procedure.ProcedureGameOver",
        };

        [MenuItem("EmojiWar/Setup/02 - Build Room Setup")]
        public static void BuildRoomSetup()
        {
            UpdateProcedureList();
            CreateRoomFormPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[RoomSetup] 完成：流程列表已更新 + RoomForm.prefab 已生成");
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

        /// <summary>生成 RoomForm.prefab。</summary>
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
            var form = root.AddComponent<RoomForm>();

            // 标题
            var title = CreateText("Title", root.transform, "联机房间", 56, new Vector2(0, 380));

            // 玩家列表
            var playerList = CreateText("PlayerList", root.transform, "等待玩家加入...", 32, new Vector2(0, 120));
            playerList.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 400);
            playerList.GetComponent<Text>().alignment = TextAnchor.UpperCenter;

            // 状态提示
            var status = CreateText("Status", root.transform, "全部准备后自动开始", 24, new Vector2(0, -180));

            // 准备按钮
            var readyBtn = CreateButton("ReadyButton", root.transform, "准备", new Vector2(-200, -320));
            // 离开/解散按钮
            var leaveBtn = CreateButton("LeaveButton", root.transform, "离开房间", new Vector2(200, -320));

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            form.GetType().GetField("m_RoomTitleText", flags).SetValue(form, title.GetComponent<Text>());
            form.GetType().GetField("m_PlayerListText", flags).SetValue(form, playerList.GetComponent<Text>());
            form.GetType().GetField("m_StatusText", flags).SetValue(form, status.GetComponent<Text>());
            form.GetType().GetField("m_ReadyButton", flags).SetValue(form, readyBtn.GetComponent<Button>());
            form.GetType().GetField("m_LeaveButton", flags).SetValue(form, leaveBtn.GetComponent<Button>());

            PrefabUtility.SaveAsPrefabAsset(root, RoomFormPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[RoomSetup] RoomForm prefab 已生成: " + RoomFormPrefabPath);
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

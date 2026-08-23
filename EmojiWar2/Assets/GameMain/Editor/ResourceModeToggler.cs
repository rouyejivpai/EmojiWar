//------------------------------------------------------------
// EmojiWar GameMain - 资源模式切换工具（Editor）
// 菜单：EmojiWar/Tools/Toggle Editor Resource Mode (Simulate Build)
// 将场景中 GameFramework 对象的 BaseComponent.m_EditorResourceMode 设为 false，
// 使编辑器 Play 也走 Package（AssetBundle）资源模式，用于复现/调试构建版问题。
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Editor
{
    public static class ResourceModeToggler
    {
        [MenuItem("EmojiWar/Tools/Simulate Build Resource Mode (m_EditorResourceMode=false)", false, 110)]
        public static void SimulateBuildResourceMode()
        {
            var go = GameObject.Find("GameFramework");
            if (go == null)
            {
                Debug.LogError("[ResMode] GameFramework object not found");
                return;
            }

            var baseComp = go.GetComponent<BaseComponent>();
            if (baseComp == null)
            {
                Debug.LogError("[ResMode] BaseComponent not found");
                return;
            }

            var so = new SerializedObject(baseComp);
            var prop = so.FindProperty("m_EditorResourceMode");
            if (prop == null)
            {
                Debug.LogError("[ResMode] m_EditorResourceMode field not found");
                return;
            }

            prop.boolValue = false;
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[ResMode] m_EditorResourceMode = false (构建模式模拟)，请重新 Play 观察");
        }

        [MenuItem("EmojiWar/Tools/Restore Editor Resource Mode (m_EditorResourceMode=true)", false, 111)]
        public static void RestoreEditorResourceMode()
        {
            var go = GameObject.Find("GameFramework");
            if (go == null)
            {
                Debug.LogError("[ResMode] GameFramework object not found");
                return;
            }

            var baseComp = go.GetComponent<BaseComponent>();
            if (baseComp == null)
            {
                Debug.LogError("[ResMode] BaseComponent not found");
                return;
            }

            var so = new SerializedObject(baseComp);
            var prop = so.FindProperty("m_EditorResourceMode");
            if (prop == null)
            {
                Debug.LogError("[ResMode] m_EditorResourceMode field not found");
                return;
            }

            prop.boolValue = true;
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[ResMode] m_EditorResourceMode = true (编辑器资源模式已恢复)");
        }
    }
}

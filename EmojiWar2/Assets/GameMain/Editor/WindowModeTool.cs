//------------------------------------------------------------
// EmojiWar GameMain - 编辑器工具：设置 PC 窗口化
// 菜单：EmojiWar/Tools/Set Windowed Mode
// 通过 PlayerSettings API 设置（直接改 YAML 不生效——Unity 内存持有旧值）。
// 设置后需重新构建 exe 才写入构建版。
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 窗口模式设置工具。
    /// </summary>
    public static class WindowModeTool
    {
        [MenuItem("EmojiWar/Tools/Set Windowed Mode")]
        public static void SetWindowedMode()
        {
            // Standalone 窗口模式：Windowed（3）+ 可缩放
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[WindowMode] 已设置：Windowed + resizable，请重新构建");
        }
    }
}

// touch: force recompile

// touch2

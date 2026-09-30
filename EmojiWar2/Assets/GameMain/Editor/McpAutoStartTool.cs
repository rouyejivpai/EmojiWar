//------------------------------------------------------------
// EmojiWar GameMain - 编辑器辅助：强制启用 MCP 插件自动启动
// 用途：Unity 重启后 MCP 插件默认不自动启动（AutoStartOnLoad=false），
//       通过命令行 -executeMethod 设置该 pref，使插件随编辑器启动连接。
// 用法：Unity.exe -projectPath <proj> -executeMethod EmojiWar.GameMain.Editor.McpAutoStartTool.EnableAutoStart -quit
//------------------------------------------------------------

using UnityEditor;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// MCP 插件自动启动辅助（命令行）。
    /// </summary>
    public static class McpAutoStartTool
    {
        /// <summary>启用 MCP 插件随编辑器自动启动。</summary>
        public static void EnableAutoStart()
        {
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
            UnityEngine.Debug.Log("[McpAutoStart] MCPForUnity.AutoStartOnLoad = true");
        }

        /// <summary>禁用 MCP 插件自动启动。</summary>
        public static void DisableAutoStart()
        {
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", false);
            UnityEngine.Debug.Log("[McpAutoStart] MCPForUnity.AutoStartOnLoad = false");
        }
    }
}

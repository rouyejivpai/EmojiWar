//------------------------------------------------------------
// EmojiWar GameMain - 编辑器引导：强制启用 MCP 插件自动启动
// [InitializeOnLoad] 在编辑器加载时执行（早于插件 Tick 检查），
// 确保每次打开项目 MCP 插件都自动启动并连接外部服务器。
// 这是修复"Unity 重启后 MCP 插件不自动连接"的引导。
//------------------------------------------------------------

using UnityEditor;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// MCP 插件自动启动引导。
    /// </summary>
    [InitializeOnLoad]
    public static class McpAutoStartBootstrapper
    {
        private const string AutoStartKey = "MCPForUnity.AutoStartOnLoad";

        static McpAutoStartBootstrapper()
        {
            // 强制开启插件随编辑器自动启动（此前默认 false，导致重启后插件不连接）
            EditorPrefs.SetBool(AutoStartKey, true);
        }
    }
}

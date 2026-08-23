//------------------------------------------------------------
// EmojiWar GameMain - 构建设置工具（Editor）
// 菜单：EmojiWar/Setup/03 - Add Scenes to Build Settings
//------------------------------------------------------------

using UnityEditor;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 构建设置工具：把 Menu/Battle 场景加入 Build Settings。
    /// </summary>
    public static class BuildSettingsBuilder
    {
        private const string MenuScenePath = "Assets/GameMain/Scenes/Menu.unity";
        private const string BattleScenePath = "Assets/GameMain/Scenes/Battle.unity";

        [MenuItem("EmojiWar/Setup/03 - Add Scenes to Build Settings")]
        public static void AddScenesToBuild()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(BattleScenePath, true),
            };

            EditorBuildSettings.scenes = scenes.ToArray();
            UnityEngine.Debug.Log("[SceneBuilder] Build settings updated with Menu + Battle scenes.");
        }
    }
}

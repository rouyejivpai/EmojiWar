//------------------------------------------------------------
// EmojiWar GameMain - 场景/相机管理工具
// GameFramework 场景为叠加加载（Additive），加载后不切换 active 场景，
// 且多个相机同时渲染时后渲染相机的 clear 会遮挡其他场景的实体。
// 本工具统一：切换 active 场景 + 只启用激活场景的相机。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmojiWar.GameMain
{
    /// <summary>
    /// 场景激活与相机管理。
    /// </summary>
    public static class SceneCameraHelper
    {
        /// <summary>
        /// 激活指定场景（设为 active）并只启用该场景的相机。
        /// SimView 实体挂在 GameEntry（DontDestroyOnLoad 跨场景常驻），
        /// Unity 相机按 layer 渲染与场景归属无关，无需迁移。
        /// </summary>
        public static void ActivateScene(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }

            foreach (var cam in Object.FindObjectsOfType<Camera>(true))
            {
                bool belongs = cam != null && cam.gameObject.scene.IsValid() && cam.gameObject.scene.name == sceneName;
                cam.enabled = belongs;
            }
        }
    }
}

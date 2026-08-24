//------------------------------------------------------------
// EmojiWar GameMain - 美术管理器
// 从已迁移的 emoji 素材加载 Sprite，应用到实体表现。
// 编辑器模式走 AssetDatabase，运行时走 Resources。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Art
{
    /// <summary>
    /// 美术资源加载与管理。
    /// </summary>
    public static class ArtManager
    {
        private const string EmojiArtPath = "Assets/GameMain/Resources/Art/";
        private static bool s_Probed = false;

        /// <summary>
        /// 加载 emoji Sprite（按文件名，不含扩展名）。
        /// </summary>
        public static Sprite LoadEmoji(string emojiFile)
        {
            string path = EmojiArtPath + emojiFile;

#if UNITY_EDITOR
            // 编辑器模式：直接按路径加载主资源
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path + ".png");
            if (sprite == null)
            {
                sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path + ".gif");
            }
            return sprite;
#else
            // 运行时：从 Resources 目录按相对路径加载（"Art/文件名"，不带扩展名）
            var sprite = Resources.Load<Sprite>("Art/" + emojiFile);
            if (!s_Probed)
            {
                s_Probed = true;
                try
                {
                    string probePath = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(probePath));
                    System.IO.File.AppendAllText(probePath,
                        string.Format("[art] LoadEmoji({0}) -> {1}\n", emojiFile, sprite != null ? "OK" : "NULL"));
                }
                catch
                {
                }
            }
            return sprite;
#endif
        }

        /// <summary>
        /// 获取玩家角色 Sprite（黄色笑脸）。
        /// </summary>
        public static Sprite GetPlayerSprite()
        {
            return LoadEmoji("1f603");
        }

        /// <summary>
        /// 获取敌人 Sprite（红色恶魔）。
        /// </summary>
        public static Sprite GetEnemySprite()
        {
            return LoadEmoji("1f47f");
        }

        /// <summary>
        /// 获取子弹 Sprite（水滴）。
        /// </summary>
        public static Sprite GetBulletSprite()
        {
            return LoadEmoji("水滴");
        }

        /// <summary>
        /// 获取金币 Sprite（金币 emoji 1fa99）。
        /// </summary>
        public static Sprite GetCoinSprite()
        {
            return LoadEmoji("1fa99");
        }
    }
}

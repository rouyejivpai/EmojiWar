//------------------------------------------------------------
// EmojiWar GameMain - 角色选择配置（数据驱动）
// 来源：Resources/Configs/character_select.json（UI 展示配置，唯一事实源）
// 职责：定义"有哪些角色可展示"（id/名字/图标/描述）；
//       战斗数值（生命/移速等）由 id 查 Character.txt 数据表（单一逻辑源）。
// 参考：生产级技能系统「配置驱动数据模型」——配置是唯一事实源，UI 按配置生成。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>角色选择配置条目（对应 JSON 数组元素）。</summary>
    [Serializable]
    public sealed class CharacterSelectEntry
    {
        public int id = 0;
        public string name = "";
        public string icon = "";      // emoji 码点（如 "1f605"）
        public string desc = "";      // 简略描述（UI 展示）
    }

    /// <summary>角色选择配置根（对应 JSON 根对象）。</summary>
    [Serializable]
    public sealed class CharacterSelectConfig
    {
        public List<CharacterSelectEntry> characters = new List<CharacterSelectEntry>();
    }

    /// <summary>
    /// 角色选择配置加载器：Resources.Load 读取 JSON + JsonUtility 反序列化。
    /// </summary>
    public static class CharacterSelectConfigLoader
    {
        private const string ConfigPath = "Configs/character_select";

        private static CharacterSelectConfig s_Cached = null;
        private static bool s_Loaded = false;

        /// <summary>加载配置（带缓存；失败返回空配置而非 null，便于调用方遍历）。</summary>
        public static CharacterSelectConfig Load()
        {
            if (s_Loaded)
            {
                return s_Cached;
            }

            s_Loaded = true;
            s_Cached = new CharacterSelectConfig();

            try
            {
                var asset = Resources.Load<TextAsset>(ConfigPath);
                if (asset == null)
                {
                    Debug.LogWarning("[CharSelectConfig] 未找到配置: " + ConfigPath);
                    return s_Cached;
                }
                var parsed = JsonUtility.FromJson<CharacterSelectConfig>(asset.text);
                if (parsed != null && parsed.characters != null)
                {
                    s_Cached = parsed;
                }
                else
                {
                    Debug.LogWarning("[CharSelectConfig] 配置解析失败: " + ConfigPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CharSelectConfig] 加载异常: " + e.Message);
            }

            return s_Cached;
        }

        /// <summary>强制重新加载（配置热更/调试用）。</summary>
        public static void Reload()
        {
            s_Loaded = false;
            s_Cached = null;
        }

        /// <summary>按 id 查配置条目（未找到返回 null）。</summary>
        public static CharacterSelectEntry GetEntry(int id)
        {
            var config = Load();
            if (config == null || config.characters == null)
            {
                return null;
            }
            foreach (var entry in config.characters)
            {
                if (entry != null && entry.id == id)
                {
                    return entry;
                }
            }
            return null;
        }
    }
}

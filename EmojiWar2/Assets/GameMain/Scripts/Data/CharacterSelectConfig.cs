//------------------------------------------------------------
// EmojiWar GameMain - 角色选择配置（数据驱动）
// 来源：Resources/Data/Select/CharacterSelect.asset（ScriptableObject，Inspector 可编辑）
//       —— 数据驱动方案 B：展示/引用类走 SO；数值/表格类后续由“表格→导出”管线生成。
//       （历史 json 源 character_select.json 已删除，2026-09-10）
// 职责：定义"有哪些角色可展示"（id/名字/图标/描述）；
//       战斗数值（生命/移速等）由 id 查 CharacterSO（单一逻辑源）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>角色选择配置条目（SO 资产内 List 元素）。</summary>
    [Serializable]
    public sealed class CharacterSelectEntry
    {
        public int id = 0;
        public string name = "";
        public Sprite iconSprite = null;  // 展示图标（资产引用；替代旧 icon 字符串码点）
        public string desc = "";          // 简略描述（UI 展示）


    }

    /// <summary>角色选择配置根（兼容 JSON 反序列化容器，供资产生成器使用）。</summary>
    [Serializable]
    public sealed class CharacterSelectConfig
    {
        public List<CharacterSelectEntry> characters = new List<CharacterSelectEntry>();
    }

    /// <summary>
    /// 角色选择配置访问：从 GameEntry.Data.SelectConfig（SO 资产）读取。
    /// 保留旧静态 API（Load/GetEntry），数据源已从 JSON 切换为 SO。
    /// </summary>
    public static class CharacterSelectConfigLoader
    {
        private static CharacterSelectConfig s_Cached = null;

        /// <summary>加载配置（带缓存；失败返回空配置而非 null，便于调用方遍历）。</summary>
        public static CharacterSelectConfig Load()
        {
            if (s_Cached != null)
            {
                return s_Cached;
            }

            s_Cached = new CharacterSelectConfig();
            try
            {
                if (GameEntry.Data != null && GameEntry.Data.SelectConfig != null)
                {
                    s_Cached.characters = GameEntry.Data.SelectConfig.characters;
                }
                else
                {
                    Debug.LogWarning("[CharSelectConfig] GameEntry.Data.SelectConfig 为空（未生成 SO 资产？）");
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


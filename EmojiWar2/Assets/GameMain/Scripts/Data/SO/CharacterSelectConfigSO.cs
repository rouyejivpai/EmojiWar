//------------------------------------------------------------
// EmojiWar GameMain - 角色选择展示配置（ScriptableObject）
// 替代 character_select.json：选角面板展示哪些角色/名字/图标/描述。
// 战斗数值仍查 CharacterSO（单一逻辑源）。仅 UI 展示可在此微调。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 角色选择展示配置资产（根资产，内含条目列表）。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterSelectConfig", menuName = "EmojiWar/Data/Character Select Config")]
    public sealed class CharacterSelectConfigSO : ScriptableObject
    {
        [Tooltip("选角面板按此顺序展示（id 对应 CharacterSO.Id）")]
        public List<CharacterSelectEntry> characters = new List<CharacterSelectEntry>();
    }
}

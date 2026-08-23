//------------------------------------------------------------
// EmojiWar GameMain - 角色数据行（DataTable）
// 数据来源：旧项目 CharacterCatalog，迁移为文本数据表。
//------------------------------------------------------------

using GameFramework.DataTable;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 角色配置数据行。
    /// </summary>
    public sealed class DRCharacter : IDataRow
    {
        private int m_Id = 0;

        /// <summary>
        /// 角色 ID。
        /// </summary>
        public int Id
        {
            get
            {
                return m_Id;
            }
        }

        /// <summary>
        /// 角色唯一标识。
        /// </summary>
        public string CharacterId { get; private set; }

        /// <summary>
        /// 角色名称。
        /// </summary>
        public string CharacterName { get; private set; }

        /// <summary>
        /// 描述。
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// 预制体资源路径（Assets 相对路径）。
        /// </summary>
        public string PrefabPath { get; private set; }

        /// <summary>
        /// 默认武器 ID。
        /// </summary>
        public int DefaultWeaponId { get; private set; }

        /// <summary>
        /// 副武器 ID。
        /// </summary>
        public int SecondWeaponId { get; private set; }

        /// <summary>
        /// 最大生命值。
        /// </summary>
        public int MaxHealth { get; private set; }

        /// <summary>
        /// 移动速度。
        /// </summary>
        public float MoveSpeed { get; private set; }

        /// <summary>
        /// 初始金币。
        /// </summary>
        public int Coin { get; private set; }

        /// <summary>
        /// 解析文本数据行（制表符分隔：# 开头为注释行）。
        /// 列顺序：Id, CharacterId, CharacterName, Description, PrefabPath, DefaultWeaponId, SecondWeaponId, MaxHealth, MoveSpeed, Coin
        /// </summary>
        public bool ParseDataRow(string dataRowText, object userData)
        {
            string[] split = dataRowText.Split('\t');
            int index = 0;

            m_Id = int.Parse(split[index++]);
            CharacterId = split[index++];
            CharacterName = split[index++];
            Description = split[index++];
            PrefabPath = split[index++];
            DefaultWeaponId = int.Parse(split[index++]);
            SecondWeaponId = int.Parse(split[index++]);
            MaxHealth = int.Parse(split[index++]);
            MoveSpeed = float.Parse(split[index++]);
            Coin = int.Parse(split[index++]);

            return true;
        }

        /// <summary>
        /// 解析二进制数据行（本版本暂不支持，返回 false 走文本路径）。
        /// </summary>
        public bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            return false;
        }
    }
}

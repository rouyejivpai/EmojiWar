//------------------------------------------------------------
// EmojiWar GameMain - Mod（模组）数据行（DataTable）
// 数据来源：旧项目 ModCatalog，迁移为文本数据表。
//------------------------------------------------------------

using GameFramework.DataTable;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// Mod 配置数据行。
    /// 列顺序：Id, ModId, ModName, Description, Category, Rarity, Weight, EffectType, Param1, Param2
    /// </summary>
    public sealed class DRMod : IDataRow
    {
        private int m_Id = 0;

        public int Id
        {
            get { return m_Id; }
        }

        /// <summary>Mod 唯一标识。</summary>
        public string ModId { get; private set; }

        /// <summary>Mod 名称。</summary>
        public string ModName { get; private set; }

        /// <summary>描述。</summary>
        public string Description { get; private set; }

        /// <summary>分类（武器/防具/通用）。</summary>
        public string Category { get; private set; }

        /// <summary>稀有度（1-5）。</summary>
        public int Rarity { get; private set; }

        /// <summary>抽取权重。</summary>
        public float Weight { get; private set; }

        /// <summary>效果类型（FireRateUp/FireRateMul/OnKill/Freeze 等）。</summary>
        public string EffectType { get; private set; }

        /// <summary>效果参数 1。</summary>
        public float Param1 { get; private set; }

        /// <summary>效果参数 2。</summary>
        public float Param2 { get; private set; }

        public bool ParseDataRow(string dataRowText, object userData)
        {
            string[] split = dataRowText.Split('\t');
            int index = 0;

            m_Id = int.Parse(split[index++]);
            ModId = split[index++];
            ModName = split[index++];
            Description = split[index++];
            Category = split[index++];
            Rarity = int.Parse(split[index++]);
            Weight = float.Parse(split[index++]);
            EffectType = split[index++];
            Param1 = float.Parse(split[index++]);
            Param2 = float.Parse(split[index++]);

            return true;
        }

        public bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            return false;
        }
    }
}

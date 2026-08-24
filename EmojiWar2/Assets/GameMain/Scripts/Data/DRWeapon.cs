//------------------------------------------------------------
// EmojiWar GameMain - 武器数据行（DataTable）
//------------------------------------------------------------

using GameFramework.DataTable;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 武器配置数据行。
    /// </summary>
    public sealed class DRWeapon : IDataRow
    {
        private int m_Id = 0;

        public int Id
        {
            get
            {
                return m_Id;
            }
        }

        public string WeaponName { get; private set; }
        public string Description { get; private set; }
        public string PrefabPath { get; private set; }
        public string Category { get; private set; }
        public int Rarity { get; private set; }
        public float Weight { get; private set; }
        public float Damage { get; private set; }
        public float FireRate { get; private set; }
        public float Range { get; private set; }
        public int MaxAmmo { get; private set; }
        public float ReloadTime { get; private set; }
        public float BulletSpeed { get; private set; }
        public float Spread { get; private set; }

        /// <summary>
        /// 列顺序：Id, WeaponName, Description, PrefabPath, Category, Rarity, Weight, Damage, FireRate, Range, MaxAmmo, ReloadTime, BulletSpeed, Spread
        /// </summary>
        public bool ParseDataRow(string dataRowText, object userData)
        {
            string[] split = dataRowText.Split('\t');
            int index = 0;

            m_Id = int.Parse(split[index++]);
            WeaponName = split[index++];
            Description = split[index++];
            PrefabPath = split[index++];
            Category = split[index++];
            Rarity = int.Parse(split[index++]);
            Weight = float.Parse(split[index++]);
            Damage = float.Parse(split[index++]);
            FireRate = float.Parse(split[index++]);
            Range = float.Parse(split[index++]);
            MaxAmmo = int.Parse(split[index++]);
            ReloadTime = float.Parse(split[index++]);
            BulletSpeed = float.Parse(split[index++]);
            Spread = float.Parse(split[index++]);

            return true;
        }

        public bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            return false;
        }
    }
}

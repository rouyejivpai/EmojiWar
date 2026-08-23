//------------------------------------------------------------
// EmojiWar GameMain - 数据组件（业务层数据访问门面）
// 提供各数据表的加载与读取入口。
//------------------------------------------------------------

using System.Collections.Generic;
using GameFramework.DataTable;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 数据组件：集中加载与访问所有 DataTable。
    /// </summary>
    public sealed class DataComponent : MonoBehaviour
    {
        private const string CharacterTable = "Character";
        private const string WeaponTable = "Weapon";

        /// <summary>
        /// 是否已初始化。
        /// </summary>
        public bool IsReady { get; private set; } = false;

        private IDataTable<DRCharacter> m_CharacterTable = null;
        private IDataTable<DRWeapon> m_WeaponTable = null;

        /// <summary>
        /// 初始化：创建并加载所有数据表（异步）。
        /// </summary>
        public void Init()
        {
            if (GameEntry.DataTable == null)
            {
                Log.Error("DataTableComponent is null, DataComponent init failed.");
                return;
            }

            // 加载数据表（ReadData 异步，完成后通过事件通知）
            GameEntry.DataTable.LoadDataTable<DRCharacter>(CharacterTable, this);
            GameEntry.DataTable.LoadDataTable<DRWeapon>(WeaponTable, this);

            // 由于 ReadData 是异步的，这里先创建引用，
            // 实际数据就绪需监听 LoadDataTableSuccess 事件。
            m_CharacterTable = GameEntry.DataTable.GetDataTable<DRCharacter>(CharacterTable);
            m_WeaponTable = GameEntry.DataTable.GetDataTable<DRWeapon>(WeaponTable);

            IsReady = true;
            Log.Info("[DataComponent] Init finished. CharacterTable={0}, WeaponTable={1}", m_CharacterTable != null, m_WeaponTable != null);
        }

        /// <summary>
        /// 获取所有角色。
        /// </summary>
        public List<DRCharacter> GetAllCharacters()
        {
            var results = new List<DRCharacter>();
            if (m_CharacterTable != null)
            {
                m_CharacterTable.GetAllDataRows(results);
            }
            return results;
        }

        /// <summary>
        /// 按 ID 获取角色。
        /// </summary>
        public DRCharacter GetCharacter(int id)
        {
            return m_CharacterTable != null ? m_CharacterTable.GetDataRow(id) : null;
        }

        /// <summary>
        /// 按 ID 获取武器。
        /// </summary>
        public DRWeapon GetWeapon(int id)
        {
            return m_WeaponTable != null ? m_WeaponTable.GetDataRow(id) : null;
        }
    }
}

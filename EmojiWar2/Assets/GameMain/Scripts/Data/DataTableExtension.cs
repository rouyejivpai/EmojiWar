//------------------------------------------------------------
// EmojiWar GameMain - 数据表扩展
// 封装 DataTable 的加载与获取，简化业务调用。
//------------------------------------------------------------

using GameFramework;
using GameFramework.DataTable;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Data
{
    /// <summary>
    /// 数据表扩展方法。
    /// </summary>
    public static class DataTableExtension
    {
        private const string DataTablePathPrefix = "Assets/GameMain/DataTables/";
        private const string DataTableExtensionName = ".txt";

        /// <summary>
        /// 加载数据表（CreateDataTable + ReadData）。
        /// 注意：ReadData 为异步加载，成功/失败通过 DataTableComponent 的事件广播。
        /// </summary>
        public static void LoadDataTable<T>(this DataTableComponent dataTableComponent, string dataTableName, object userData = null) where T : class, IDataRow, new()
        {
            if (dataTableComponent.HasDataTable<T>(dataTableName))
            {
                return;
            }

            IDataTable<T> dataTable = dataTableComponent.CreateDataTable<T>(dataTableName);
            string assetName = DataTablePathPrefix + dataTableName + DataTableExtensionName;
            ((DataTableBase)dataTable).ReadData(assetName, userData);
        }

        /// <summary>
        /// 获取数据表。
        /// </summary>
        public static IDataTable<T> GetDataTable<T>(this DataTableComponent dataTableComponent, string dataTableName = null) where T : class, IDataRow, new()
        {
            return dataTableComponent.GetDataTable<T>(dataTableName);
        }
    }
}

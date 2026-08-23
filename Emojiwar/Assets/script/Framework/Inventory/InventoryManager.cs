using UnityEngine;
using System.Collections.Generic;
using Framework.Events;

namespace Framework.Inventory
{
    /// <summary>
    /// 简化的背包管理器 - 只实现游戏特定功能，使用Unity内置功能
    /// </summary>
    public class InventoryManager : MonoBehaviour
    {
        [Header("背包设置")]
        [SerializeField] private int capacity = 32;
        [SerializeField] private bool enablePersistence = true;
        
        [Header("事件通道")]
        [SerializeField] private GameEventChannel<InventoryItemData> onItemAdded;
        [SerializeField] private GameEventChannel<InventoryItemData> onItemRemoved;
        [SerializeField] private GameEventChannel onInventoryChanged;
        
        private Dictionary<string, int> items = new Dictionary<string, int>();
        
        public int Capacity => capacity;
        public int ItemCount => items.Count;
        public bool IsFull => items.Count >= capacity;
        
        /// <summary>
        /// 添加物品
        /// </summary>
        public bool AddItem(string itemId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0)
                return false;
            
            if (IsFull && !items.ContainsKey(itemId))
                return false;
            
            int oldQuantity = items.ContainsKey(itemId) ? items[itemId] : 0;
            int newQuantity = oldQuantity + quantity;
            
            items[itemId] = newQuantity;
            
            // 触发事件
            var itemData = new InventoryItemData(itemId, newQuantity);
            onItemAdded?.Raise(itemData);
            onInventoryChanged?.Raise();
            
            Debug.Log($"[InventoryManager] 添加物品: {itemId} x{quantity}");
            return true;
        }
        
        /// <summary>
        /// 移除物品
        /// </summary>
        public bool RemoveItem(string itemId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0)
                return false;
            
            if (!items.ContainsKey(itemId))
                return false;
            
            int oldQuantity = items[itemId];
            int newQuantity = Mathf.Max(0, oldQuantity - quantity);
            
            if (newQuantity == 0)
            {
                items.Remove(itemId);
            }
            else
            {
                items[itemId] = newQuantity;
            }
            
            // 触发事件
            var itemData = new InventoryItemData(itemId, newQuantity);
            onItemRemoved?.Raise(itemData);
            onInventoryChanged?.Raise();
            
            Debug.Log($"[InventoryManager] 移除物品: {itemId} x{quantity}");
            return true;
        }
        
        /// <summary>
        /// 获取物品数量
        /// </summary>
        public int GetItemCount(string itemId)
        {
            return items.ContainsKey(itemId) ? items[itemId] : 0;
        }
        
        /// <summary>
        /// 检查是否有物品
        /// </summary>
        public bool HasItem(string itemId, int quantity = 1)
        {
            return GetItemCount(itemId) >= quantity;
        }
        
        /// <summary>
        /// 获取所有物品
        /// </summary>
        public Dictionary<string, int> GetAllItems()
        {
            return new Dictionary<string, int>(items);
        }
        
        /// <summary>
        /// 清空背包
        /// </summary>
        public void Clear()
        {
            items.Clear();
            onInventoryChanged?.Raise();
            Debug.Log("[InventoryManager] 背包已清空");
        }
    }
    
    /// <summary>
    /// 背包物品数据
    /// </summary>
    [System.Serializable]
    public class InventoryItemData
    {
        public string itemId;
        public int quantity;
        
        public InventoryItemData(string itemId, int quantity)
        {
            this.itemId = itemId;
            this.quantity = quantity;
        }
    }
}

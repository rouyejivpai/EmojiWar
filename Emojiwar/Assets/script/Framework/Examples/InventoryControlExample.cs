using UnityEngine;

/// <summary>
/// 背包控制示例 - 展示如何在其他场景中控制背包
/// </summary>
public class InventoryControlExample : MonoBehaviour
{
    [Header("控制设置")]
    public KeyCode inventoryKey = KeyCode.Tab; // 背包按键
    public bool enableInventoryControl = true; // 启用背包控制
    
    private void Update()
    {
        if (!enableInventoryControl) return;
        
        // 检测背包按键
        if (Input.GetKeyDown(inventoryKey))
        {
            ToggleInventory();
        }
    }
    
    /// <summary>
    /// 切换背包显示
    /// </summary>
    public void ToggleInventory()
    {
        if (InventorySceneBootstrap.Instance != null)
        {
            InventorySceneBootstrap.Instance.ToggleInventory();
        }
        else
        {
            Debug.LogWarning("[InventoryControlExample] 背包场景未加载或InventorySceneBootstrap未找到");
        }
    }
    
    /// <summary>
    /// 显示背包
    /// </summary>
    public void ShowInventory()
    {
        if (InventorySceneBootstrap.Instance != null)
        {
            InventorySceneBootstrap.Instance.ShowInventory();
        }
    }
    
    /// <summary>
    /// 隐藏背包
    /// </summary>
    public void HideInventory()
    {
        if (InventorySceneBootstrap.Instance != null)
        {
            InventorySceneBootstrap.Instance.HideInventory();
        }
    }
    
    /// <summary>
    /// 检查背包是否可见
    /// </summary>
    public bool IsInventoryVisible()
    {
        return InventorySceneBootstrap.Instance != null && 
               InventorySceneBootstrap.Instance.IsInventoryVisible;
    }
    
    /// <summary>
    /// 在商店场景中自动显示背包
    /// </summary>
    public void OnStoreEntered()
    {
        Debug.Log("[InventoryControlExample] 进入商店，显示背包");
        ShowInventory();
    }
    
    /// <summary>
    /// 在战斗场景中自动隐藏背包
    /// </summary>
    public void OnCombatStarted()
    {
        Debug.Log("[InventoryControlExample] 开始战斗，隐藏背包");
        HideInventory();
    }
}




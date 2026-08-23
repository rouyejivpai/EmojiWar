using System.Collections;
using UnityEngine;

/// <summary>
/// 背包场景引导脚本 - 管理背包的显示/隐藏和跨场景通信
/// </summary>
public class InventorySceneBootstrap : MonoBehaviour
{
    [Header("背包设置")]
    public GameObject inventoryPanel; // 背包面板
    public KeyCode toggleKey = KeyCode.Tab; // 切换背包的按键
    
    [Header("跨场景通信")]
    public bool enableCrossSceneCommunication = true; // 启用跨场景通信
    
    private bool isInventoryVisible = false;
    private static InventorySceneBootstrap _instance;
    
    public static InventorySceneBootstrap Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<InventorySceneBootstrap>();
            }
            return _instance;
        }
    }
    
    private void Awake()
    {
        // 确保只有一个实例
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        InitializeInventory();
    }
    
    private void Start()
    {
        // 订阅跨场景事件
        if (enableCrossSceneCommunication)
        {
            SubscribeToCrossSceneEvents();
        }
    }
    
    private void Update()
    {
        // 检测按键切换背包
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleInventory();
        }
    }
    
    /// <summary>
    /// 初始化背包
    /// </summary>
    private void InitializeInventory()
    {
        // 如果没有指定背包面板，尝试自动查找
        if (inventoryPanel == null)
        {
            inventoryPanel = FindInventoryPanel();
        }
        
        // 初始状态设为隐藏
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
            isInventoryVisible = false;
            Debug.Log("[InventorySceneBootstrap] 背包已初始化，初始状态：隐藏");
        }
        else
        {
            Debug.LogWarning("[InventorySceneBootstrap] 未找到背包面板！");
        }
    }
    
    /// <summary>
    /// 查找背包面板
    /// </summary>
    private GameObject FindInventoryPanel()
    {
        string[] possibleNames = { "PACK", "PackPanel", "InventoryPanel", "Pack", "InventoryUI" };
        
        foreach (string name in possibleNames)
        {
            GameObject panel = GameObject.Find(name);
            if (panel != null)
            {
                return panel;
            }
        }
        
        // 通过标签查找
        GameObject[] inventoryObjects = GameObject.FindGameObjectsWithTag("Inventory");
        if (inventoryObjects.Length > 0)
        {
            return inventoryObjects[0];
        }
        
        return null;
    }
    
    /// <summary>
    /// 切换背包显示状态
    /// </summary>
    public void ToggleInventory()
    {
        if (inventoryPanel == null) return;
        
        isInventoryVisible = !isInventoryVisible;
        inventoryPanel.SetActive(isInventoryVisible);
        
        // 暂停/恢复游戏时间
        Time.timeScale = isInventoryVisible ? 0f : 1f;
        
        Debug.Log($"[InventorySceneBootstrap] 背包状态切换: {(isInventoryVisible ? "显示" : "隐藏")}");
        
        // 触发事件
        OnInventoryToggled?.Invoke(isInventoryVisible);
    }
    
    /// <summary>
    /// 显示背包
    /// </summary>
    public void ShowInventory()
    {
        if (inventoryPanel == null || isInventoryVisible) return;
        
        isInventoryVisible = true;
        inventoryPanel.SetActive(true);
        Time.timeScale = 0f;
        
        Debug.Log("[InventorySceneBootstrap] 背包已显示");
        OnInventoryToggled?.Invoke(true);
    }
    
    /// <summary>
    /// 隐藏背包
    /// </summary>
    public void HideInventory()
    {
        if (inventoryPanel == null || !isInventoryVisible) return;
        
        isInventoryVisible = false;
        inventoryPanel.SetActive(false);
        Time.timeScale = 1f;
        
        Debug.Log("[InventorySceneBootstrap] 背包已隐藏");
        OnInventoryToggled?.Invoke(false);
    }
    
    /// <summary>
    /// 获取背包显示状态
    /// </summary>
    public bool IsInventoryVisible => isInventoryVisible;
    
    /// <summary>
    /// 订阅跨场景事件
    /// </summary>
    private void SubscribeToCrossSceneEvents()
    {
        // 这里可以订阅其他场景的事件
        // 例如：当商店场景加载时自动显示背包
        // 当战斗场景加载时自动隐藏背包等
    }
    
    /// <summary>
    /// 背包状态变化事件
    /// </summary>
    public System.Action<bool> OnInventoryToggled;
    
    private void OnDestroy()
    {
        // 清理事件订阅
        OnInventoryToggled = null;
    }
}




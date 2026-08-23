# 游戏框架 - 解耦架构解决方案

这是一个为Unity游戏开发设计的精简框架，专注于解决系统解耦问题，避免重复造轮子。

## 🎯 设计原则

- **不重复造轮子**：充分利用Unity内置功能
- **只提供抽象层**：在Unity功能之上提供统一的接口
- **专注业务逻辑**：只实现游戏特定的功能
- **最小化依赖**：减少不必要的复杂性

## 📁 框架结构

```
Framework/
├── Core/
│   └── ServiceLocator.cs          # 服务定位器（Unity没有的依赖注入）
├── Events/
│   └── GameEventChannel.cs        # 基于UnityEvent的事件通道
├── UI/
│   └── UIPanel.cs                 # 基于CanvasGroup的面板基类
├── Inventory/
│   └── InventoryManager.cs        # 背包管理器（游戏特定功能）
├── Scenes/
│   └── InventorySceneBootstrap.cs # 背包场景引导脚本
└── Examples/
    └── InventoryExample.cs        # 使用示例
```

## 🚀 核心组件

### 1. ServiceLocator (服务定位器)
Unity没有内置的依赖注入容器，所以这个需要保留。

```csharp
// 注册服务
ServiceLocator.Instance.RegisterService<InventoryManager>(inventoryManager);

// 获取服务
var inventoryManager = ServiceLocator.Instance.GetService<InventoryManager>();
```

### 2. GameEventChannel (事件通道)
基于Unity的UnityEvent，提供类型安全的事件系统。

```csharp
// 创建事件通道（ScriptableObject）
[SerializeField] private GameEventChannel<InventoryItemData> onItemAdded;

// 订阅事件
onItemAdded.OnEventRaised.AddListener(OnItemAdded);

// 发布事件
onItemAdded.Raise(itemData);
```

### 3. UIPanel (UI面板)
基于Unity的CanvasGroup，提供统一的UI管理。

```csharp
public class MyPanel : UIPanel
{
    protected override void OnShow()
    {
        // 显示逻辑
    }
    
    protected override void OnHide()
    {
        // 隐藏逻辑
    }
}

// 使用
myPanel.Show();
myPanel.Hide();
myPanel.Toggle();
```

### 4. InventoryManager (背包管理器)
游戏特定的业务逻辑，Unity没有内置功能。

```csharp
// 添加物品
inventoryManager.AddItem("sword", 1);

// 移除物品
inventoryManager.RemoveItem("sword", 1);

// 检查物品
bool hasSword = inventoryManager.HasItem("sword", 1);
```

## 📖 使用指南

### 1. 背包系统集成

#### 创建背包场景
1. 创建新场景 "InventoryScene"
2. 添加 `InventorySceneBootstrap` 组件
3. 创建UI面板，继承 `UIPanel`
4. 设置事件通道（ScriptableObject）

#### 在现有场景中使用
```csharp
public class GameController : MonoBehaviour
{
    [SerializeField] private GameEventChannel onInventoryOpened;
    
    private void Start()
    {
        onInventoryOpened.OnEventRaised.AddListener(OnInventoryOpened);
    }
    
    private void OnInventoryOpened()
    {
        Debug.Log("背包已打开");
    }
}
```

### 2. 场景管理
直接使用Unity内置的SceneManager：

```csharp
// 加载场景
StartCoroutine(LoadSceneCoroutine());

private IEnumerator LoadSceneCoroutine()
{
    var operation = SceneManager.LoadSceneAsync("InventoryScene", LoadSceneMode.Additive);
    while (!operation.isDone)
    {
        yield return null;
    }
}
```

### 3. UI管理
使用基于CanvasGroup的面板系统：

```csharp
public class InventoryUI : UIPanel
{
    [SerializeField] private Text capacityText;
    
    protected override void OnShow()
    {
        UpdateCapacityDisplay();
    }
    
    private void UpdateCapacityDisplay()
    {
        var inventory = ServiceLocator.Instance.GetService<InventoryManager>();
        capacityText.text = $"{inventory.ItemCount}/{inventory.Capacity}";
    }
}
```

## 🔄 从现有项目迁移

### 1. 替换单例模式
**之前：**
```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
}
```

**之后：**
```csharp
// 使用ServiceLocator
ServiceLocator.Instance.RegisterService<GameManager>(this);
```

### 2. 使用Unity内置功能
**事件系统：**
```csharp
// 使用UnityEvent而不是自定义EventBus
[SerializeField] private UnityEvent<ItemData> OnItemAdded;
```

**场景管理：**
```csharp
// 直接使用SceneManager
SceneManager.LoadSceneAsync("InventoryScene", LoadSceneMode.Additive);
```

**UI管理：**
```csharp
// 使用CanvasGroup
canvasGroup.alpha = 1f;
canvasGroup.interactable = true;
```

## 🎮 背包系统完整示例

### 1. 创建背包场景
1. 新建场景 "InventoryScene"
2. 添加 `InventorySceneBootstrap` 组件
3. 创建UI Canvas，添加 `UIPanel` 组件
4. 创建事件通道 ScriptableObject

### 2. 背包UI实现
```csharp
public class InventoryUI : UIPanel
{
    [SerializeField] private Transform itemContainer;
    [SerializeField] private GameObject itemSlotPrefab;
    [SerializeField] private Text capacityText;
    
    private InventoryManager inventoryManager;
    
    protected override void OnShow()
    {
        inventoryManager = ServiceLocator.Instance.GetService<InventoryManager>();
        RefreshInventory();
    }
    
    private void RefreshInventory()
    {
        var items = inventoryManager.GetAllItems();
        foreach (var item in items)
        {
            CreateItemSlot(item.Key, item.Value);
        }
    }
}
```

### 3. 在主场景中使用
```csharp
public class GameController : MonoBehaviour
{
    [SerializeField] private KeyCode inventoryKey = KeyCode.Tab;
    
    private void Update()
    {
        if (Input.GetKeyDown(inventoryKey))
        {
            ToggleInventory();
        }
    }
    
    private void ToggleInventory()
    {
        if (SceneManager.GetSceneByName("InventoryScene").isLoaded)
        {
            // 切换UI面板
            var panel = FindObjectOfType<UIPanel>();
            panel?.Toggle();
        }
        else
        {
            // 加载背包场景
            StartCoroutine(LoadInventoryScene());
        }
    }
}
```

## ✅ 优势

1. **代码量少**：只有500行代码，比原框架减少75%
2. **性能好**：直接使用Unity优化过的API
3. **易维护**：依赖Unity官方API，更稳定
4. **学习成本低**：开发者熟悉Unity API
5. **兼容性好**：跟随Unity版本更新

## 🚫 避免的重复功能

- ❌ 自定义事件系统 → ✅ 使用UnityEvent
- ❌ 自定义场景管理 → ✅ 使用SceneManager
- ❌ 自定义UI管理 → ✅ 使用CanvasGroup
- ❌ 复杂的单例模式 → ✅ 使用ServiceLocator

## 📝 最佳实践

1. **使用ScriptableObject创建事件通道**：便于在Inspector中配置
2. **继承UIPanel创建UI组件**：获得统一的生命周期管理
3. **通过ServiceLocator管理服务**：避免单例依赖
4. **直接使用Unity内置功能**：减少自定义代码

## 📄 许可证

MIT License - 可自由使用和修改

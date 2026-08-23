# 拖拽系统使用说明

## 概述
这个拖拽系统包含三个主要组件：
1. `drag.cs` - 基础拖拽类
2. `SnapDrag.cs` - 吸附型拖拽子类
3. `DragTarget.cs` - 拖拽目标基类

## 基础拖拽类 (drag.cs)
最简单的可拖动UI基类，提供基本的拖拽功能。

### 使用方法：
1. 将 `drag` 脚本挂载到任何UI元素上
2. 确保UI元素有 `RectTransform` 组件
3. 确保UI元素在Canvas下

### 参数说明：
- `canDrag`: 是否可以拖拽
- `bringToFront`: 拖拽时是否置于最前

## 吸附型拖拽类 (SnapDrag.cs)
继承自 `drag` 类，增加了吸附功能。

### 使用方法：
1. 将 `SnapDrag` 脚本挂载到需要吸附的UI元素上
2. 在Inspector中设置吸附参数
3. 确保场景中有 `DragTarget` 对象

### 参数说明：
- `snapDistance`: 吸附距离（像素）
- `autoSnap`: 是否自动吸附
- `snapSpeed`: 吸附速度
- `returnToOriginal`: 无法吸附时是否返回原位置
- `validTargets`: 有效的吸附目标列表

## 拖拽目标类 (DragTarget.cs)
用于接收拖拽物的目标基类。

### 使用方法：
1. 将 `DragTarget` 脚本挂载到接收拖拽物的UI元素上
2. 在Inspector中设置接收参数
3. 可以添加事件回调

### 参数说明：
- `canAcceptDrag`: 是否可以接收拖拽物
- `showSnapPreview`: 是否显示吸附预览
- `previewColor`: 预览颜色
- `previewAlpha`: 预览透明度
- `allowMultipleItems`: 是否允许多个物品
- `maxItems`: 最大物品数量

### 事件：
- `OnItemAccepted`: 物品被接受时触发
- `OnItemRemoved`: 物品被移除时触发

## 完整示例场景设置

### 1. 创建拖拽物品：
1. 创建UI Image作为拖拽物品
2. 添加 `SnapDrag` 组件
3. 设置吸附参数

### 2. 创建拖拽目标：
1. 创建UI Image作为拖拽目标
2. 添加 `DragTarget` 组件
3. 设置接收参数
4. 添加事件回调（可选）

### 3. 自动关联：
- `SnapDrag` 会自动查找场景中的所有 `DragTarget`
- 也可以手动在Inspector中指定 `validTargets`

## 扩展建议

### 自定义拖拽目标：
```csharp
public class CustomDragTarget : DragTarget
{
    protected override void OnItemAcceptedCustom(SnapDrag dragItem)
    {
        // 自定义接受逻辑
        Debug.Log("物品被接受了！");
    }
    
    protected override void OnItemRemovedCustom(SnapDrag dragItem)
    {
        // 自定义移除逻辑
        Debug.Log("物品被移除了！");
    }
}
```

### 自定义吸附拖拽：
```csharp
public class CustomSnapDrag : SnapDrag
{
    // 可以重写吸附逻辑
    // 可以添加自定义的吸附条件
}
```

## 注意事项
1. 确保所有UI元素都在Canvas下
2. 拖拽物品需要有 `RectTransform` 组件
3. 可以同时使用多个拖拽物品和多个目标
4. 系统会自动处理层级和透明度变化
5. 支持事件系统，可以监听拖拽状态变化

# 新Buff系统架构说明

## 概述

新的buff系统已经从MonoBehaviour架构重构为独立的POCO（Plain Old C# Object）架构，提供了更好的性能、更清晰的架构和更灵活的配置管理。

## 主要改进

### 1. 性能提升
- 移除了MonoBehaviour的开销
- 减少了GameObject上的组件数量
- 更高效的内存管理

### 2. 架构清晰
- 清晰的接口定义（IBuff）
- 职责分离：BuffSystem负责管理，BuffBase负责逻辑
- 支持依赖注入和单元测试

### 3. 配置驱动
- JSON配置文件支持
- Buff工厂模式
- 支持buff组合和参数配置

## 核心组件

### IBuff接口
```csharp
public interface IBuff
{
    string Name { get; }
    float Duration { get; set; }
    int Stacks { get; set; }
    int MaxStacks { get; }
    bool IsPermanent { get; }
    bool IsActive { get; set; }
    
    void OnApply(Entity target);
    void OnRemove(Entity target);
    void OnTick(Entity target, float deltaTime);
    void OnStack(Entity target, int newStacks);
    bool ShouldRemove();
}
```

### BuffBase基类
```csharp
public abstract class BuffBase : IBuff
{
    // 提供通用的buff功能
    // 支持持续时间、叠层、生命周期等
}
```

### BuffSystem管理器
```csharp
public class BuffSystem : MonoBehaviour
{
    // 管理所有buff的生命周期
    // 支持添加、移除、查询、更新等操作
}
```

## 使用方法

### 1. 创建新的Buff类
```csharp
public class MyBuff : BuffBase
{
    public MyBuff() : base(10f, 3) // 10秒，最多3层
    {
        Name = "My Buff";
    }
    
    public override void OnApply(Entity target)
    {
        base.OnApply(target);
        // 应用buff时的逻辑
    }
    
    public override void OnRemove(Entity target)
    {
        // 移除buff时的逻辑
        base.OnRemove(target);
    }
    
    public override void OnTick(Entity target, float deltaTime)
    {
        base.OnTick(target, deltaTime);
        // 每帧更新逻辑
    }
}
```

### 2. 添加Buff到实体
```csharp
// 方法1：直接创建
var myBuff = new MyBuff();
buffSystem.AddBuff(myBuff);

// 方法2：使用泛型方法
var myBuff = buffSystem.AddBuff<MyBuff>(15f, 5); // 15秒，最多5层

// 方法3：使用配置
var config = new BuffConfig("mybuff", 20f, 2);
var myBuff = BuffFactory.CreateBuff(config);
buffSystem.AddBuff(myBuff);
```

### 3. 管理Buff
```csharp
// 检查是否有特定类型的buff
if (buffSystem.HasBuff<MyBuff>())
{
    var buff = buffSystem.GetBuff<MyBuff>();
    // 使用buff
}

// 移除特定类型的buff
buffSystem.RemoveBuff<MyBuff>();

// 清除所有buff
buffSystem.ClearAllBuffs();
```

### 4. 配置文件
```json
{
  "buffs": [
    {
      "buffId": "mybuff",
      "duration": 15.0,
      "maxStacks": 3,
      "parameters": {
        "value": 1.5
      }
    }
  ]
}
```

## 迁移指南

### 从旧系统迁移
1. 将`BUFF`组件替换为`BuffSystem`
2. 更新buff类继承自`BuffBase`而不是`MonoBehaviour`
3. 使用新的生命周期方法（OnApply, OnRemove, OnTick）
4. 更新mod类使用新的buff系统

### 示例迁移
```csharp
// 旧代码
public class rateup : BuffBase // 继承MonoBehaviour
{
    public override void init(GameObject target) { }
    public override void Remove() { }
}

// 新代码
public class rateup : BuffBase // 继承BuffBase
{
    public override void OnApply(Entity target) { }
    public override void OnRemove(Entity target) { }
}
```

## 高级功能

### 1. Buff组合
```csharp
// 应用预定义的buff组合
configManager.ApplyBuffCombo("Speed Combo", target);
```

### 2. 协程支持
```csharp
public class MyCoroutineBuff : BuffBase, ICoroutineBuff
{
    public System.Collections.IEnumerator CoroutineRoutine(Entity target)
    {
        // 协程逻辑
        yield return new WaitForSeconds(1f);
    }
}
```

### 3. 事件系统
```csharp
public class MyDeathBuff : BuffBase, IOnDeath
{
    public void OnDeath(DeathContext ctx)
    {
        // 死亡事件处理
    }
}
```

## 性能考虑

### 优势
- 减少了MonoBehaviour的开销
- 更高效的内存使用
- 更好的垃圾回收

### 注意事项
- 需要手动管理生命周期
- 协程需要特殊处理
- 序列化需要额外配置

## 测试

使用`BuffSystemTest`脚本进行测试：
1. 添加各种类型的buff
2. 测试buff叠加和移除
3. 验证配置驱动的buff创建
4. 测试buff组合功能

## 总结

新的buff系统提供了：
- 更好的性能
- 更清晰的架构
- 更灵活的配置
- 更容易的测试和维护

建议在新项目中使用此架构，现有项目可以逐步迁移。

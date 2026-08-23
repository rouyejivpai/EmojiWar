using UnityEngine;


/// <summary>
/// Buff接口，定义buff的基本行为
/// </summary>
public interface IBuff
{
    //名字
    string Name { get; }
    //持续时间
    float Duration { get; set; }
    //层数
    int Stacks { get; set; }
    int MaxStacks { get; }
    bool IsPermanent { get; }
    bool IsActive { get; set; }
    
    BuffTarget Target { get; set; }
    void OnApply(BuffTarget target);
    void OnRemove();
    void OnTick( float deltaTime);
    void OnStack( int newStacks);
    bool ShouldRemove();
}
//可被buff标记接口
public interface BuffTarget
{
    
}
//类型接口
public interface EntityBuff
{ 
    Entity entity { get; set; }
}

public interface WeaponBuff
{
    WeaponBase Weapon { get; set; }
}
//特殊接口

/// <summary>
/// 死亡事件接口
/// </summary>
public interface IOnDeath
{
    void OnDeath(DeathContext ctx);
}

/// <summary>
/// 需要协程的buff接口
/// </summary>
public interface ICoroutineBuff
{
    System.Collections.IEnumerator CoroutineRoutine(Entity target);
}

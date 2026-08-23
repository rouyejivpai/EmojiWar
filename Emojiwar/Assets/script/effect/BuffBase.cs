using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Buff基类，提供通用的buff功能
/// </summary>
public abstract class BuffBase : IBuff
{
    public string Name { get; protected set; }
    public float Duration { get; set; }
    
    public int Stacks { get; set; }
    public int MaxStacks { get; set; }
    public bool IsPermanent { get; set; }
    public bool IsActive { get; set; }
    public BuffTarget Target { get; set; }

    
    protected BuffBase()
    {
        Name = GetType().Name;
        MaxStacks = 10;
        Stacks = 1;
        IsActive = true;
        IsPermanent = false;
    }
    
    protected BuffBase(float duration, int maxStacks = 10)
    {
        Name = GetType().Name;
        Duration = duration;
        MaxStacks = maxStacks;
        Stacks = 1;
        IsActive = true;
        IsPermanent = duration <= 0;
    }
    
    public virtual void OnApply(BuffTarget target)
    {
        this.Target = target;
    }
    
    public virtual void OnRemove()
    {
        this.Target = null;
    }
    
    public virtual void OnTick( float deltaTime)
    {
        if (!IsPermanent && Duration > 0)
        {
            Duration -= deltaTime;
        }
    }
    
    public virtual void OnStack( int newStacks)
    {
        Stacks = Mathf.Min(newStacks+Stacks, MaxStacks);
    }
    public virtual void OnStack( float newStacks)
    {
        Stacks = Mathf.Min((int)(newStacks+Stacks), MaxStacks);
    }
    
    public virtual bool ShouldRemove()
    {
        return !IsActive || (!IsPermanent && Duration <= 0);
    }
    
    public virtual void Trigger(Entity entity = null)
    {
        // 子类可以重写此方法实现触发逻辑
    }
}

// 事件上下文与接口定义（按需扩展更多事件）
public struct DeathContext
{
    public Entity victim;
    public Entity killer;
}

// public interface IOnDeath
// {
//     void OnDeath(DeathContext ctx);
// }

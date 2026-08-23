using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
/// <summary>
/// Buff系统，负责管理实体的所有buff
/// </summary>
///
enum TargetType
{
    entity,
    weapon,
    
}
public class BuffSystem : MonoBehaviour
{
    [Header("Debug Info")]
     public List<BuffInfo> activeBuffs = new List<BuffInfo>();
    private TargetType targetType;
    private BuffTarget target;
    private Entity entityRef;

    public Dictionary<Type, BuffInfo> buffTypeMap = new Dictionary<Type, BuffInfo>();
    private Dictionary<IBuff, Coroutine> buffCoroutines = new Dictionary<IBuff, Coroutine>();
    
    [Serializable]
    public class BuffInfo
    {
        public IBuff buff;
        public float remainingDuration;
        public int currentStacks;
        public int maxStacks;
        public bool isActive;
        
        public BuffInfo(IBuff buff)
        {
            this.buff = buff;
            this.remainingDuration = buff.Duration;
            this.currentStacks = buff.Stacks;
            this.isActive = buff.IsActive;
        }
    }
    
    private void Awake()
    {
        
    }
    
    private void OnEnable()
    {
       //根据类型设置监听
        Entity entity = GetComponent<Entity>();
        if (entity != null)
        {
            targetType = TargetType.entity;
            this.target = entity as BuffTarget;
            this.entityRef = entity;
            entityRef.OnDeath += OnEntityDeath;
        }

        WeaponBase weapon = GetComponent<WeaponBase>();
        if (weapon!=null)
        {
            targetType = TargetType.weapon;
            this.target = weapon;
            //
        }
    }
    
    private void OnDisable()
    {
        if (targetType == TargetType.entity && entityRef != null)
        {
            entityRef.OnDeath -= OnEntityDeath;
        }
    }
    
    private void OnEntityDeath(Entity victim)
    {
        var ctx = new DeathContext { victim = victim, killer = null };
        HandleEntityDeath(ctx);
    }
    
    private void Update()
    {
        UpdateBuffs();
    }
    
    /// <summary>
    /// 更新所有buff
    /// </summary>
    private void UpdateBuffs()
    {
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buffInfo = activeBuffs[i];
            if (buffInfo.buff == null || !buffInfo.buff.IsActive)
            {
                RemoveBuffAt(i);
                continue;
            }
            
            // 通用更新
            buffInfo.buff.OnTick( Time.deltaTime);
            
           
            buffInfo.remainingDuration = buffInfo.buff.Duration;
            buffInfo.currentStacks = buffInfo.buff.Stacks;
            buffInfo.maxStacks = buffInfo.buff.MaxStacks;
            
            // 检查是否需要移除
            if (buffInfo.buff.ShouldRemove())
            {
                RemoveBuffAt(i);
            }
        }
        
        // 更新序列化列表（用于Inspector显示）
        UpdateSerializedList();
    }
    
    /// <summary>
    /// 添加buff
    /// </summary>
    public T AddBuff<T>(float duration = -1f, int maxStacks = 1) where T : BuffBase, new()
    {
        var buff = new T();
        if (duration > 0)
        {
            buff.Duration = duration;
        }
        buff.MaxStacks = maxStacks;
        
        return AddBuff(buff) as T;
    }
    
    /// <summary>
    /// 添加buff实例
    /// </summary>
    public IBuff AddBuff(IBuff buff)
    {
        if (buff == null) return null;
        
        var buffType = buff.GetType();
        
        // 检查是否已存在同类型buff
        if (buffTypeMap.TryGetValue(buffType, out var existingBuffInfo))
        {
            // 如果已存在，尝试叠加
            if (existingBuffInfo.buff.Stacks <= existingBuffInfo.buff.MaxStacks)
            {
                int newStacks = buff.Stacks ;
                existingBuffInfo.buff.OnStack(1);
                // 刷新持续时间
                existingBuffInfo.buff.Duration = buff.Duration;
                return existingBuffInfo.buff;
            }
            else
            {
                // 刷新持续时间
                existingBuffInfo.buff.Duration = buff.Duration;
                return existingBuffInfo.buff;
            }
        }
        
        // 应用新buff
        buff.OnApply(target);
        
        
        
        // 创建buff信息
        var buffInfo = new BuffInfo(buff);
        Debug.Log($"添加buff: {buffInfo.buff}");
        activeBuffs.Add(buffInfo);
        
        buffTypeMap[buffType] = buffInfo;
        
        // 如果buff需要协程，启动协程（实体目标才有 Entity 上下文）
        if (targetType == TargetType.entity && entityRef != null && buff is ICoroutineBuff coroutineBuff)
        {
            var coroutine = StartCoroutine(coroutineBuff.CoroutineRoutine(entityRef));
            buffCoroutines[buff] = coroutine;
        }
        
        return buff;
    }
    
    /// <summary>
    /// 移除指定类型的buff
    /// </summary>
    public void RemoveBuff<T>() where T : IBuff
    {
        var buffType = typeof(T);
        if (buffTypeMap.TryGetValue(buffType, out var buffInfo))
        {
            RemoveBuff(buffInfo.buff);
        }
    }
    
    /// <summary>
    /// 移除指定buff
    /// </summary>
    public void RemoveBuff(IBuff buff)
    {
        if (buff == null) return;
        
        // 停止协程
        if (buffCoroutines.TryGetValue(buff, out var coroutine))
        {
            StopCoroutine(coroutine);
            buffCoroutines.Remove(buff);
        }
        
        // 移除buff
        buff.OnRemove();
        
        
        // 从映射中移除
        var buffType = buff.GetType();
        if (buffTypeMap.TryGetValue(buffType, out var buffInfo))
        {
            buffTypeMap.Remove(buffType);
        }
        
        // 从列表中移除
        for (int i = 0; i < activeBuffs.Count; i++)
        {
            if (activeBuffs[i].buff == buff)
            {
                RemoveBuffAt(i);
                break;
            }
        }
    }
    
    /// <summary>
    /// 移除指定索引的buff
    /// </summary>
    private void RemoveBuffAt(int index)
    {
        if (index < 0 || index >= activeBuffs.Count) return;
        
        var buffInfo = activeBuffs[index];
        if (buffInfo.buff != null)
        {
            
            
            
            // 停止协程
            if (buffCoroutines.TryGetValue(buffInfo.buff, out var coroutine))
            {
                StopCoroutine(coroutine);
                buffCoroutines.Remove(buffInfo.buff);
            }
        }
        
        // 从映射中移除
        var buffType = buffInfo.buff?.GetType();
        if (buffType != null)
        {
            buffTypeMap.Remove(buffType);
        }
        
        activeBuffs.RemoveAt(index);
    }
    
    /// <summary>
    /// 获取指定类型的buff
    /// </summary>
    public T GetBuff<T>() where T : IBuff
    {
        var buffType = typeof(T);
        if (buffTypeMap.TryGetValue(buffType, out var buffInfo))
        {
            return (T)buffInfo.buff;
        }
        return default(T);
    }
    
    /// <summary>
    /// 检查是否有指定类型的buff
    /// </summary>
    public bool HasBuff<T>() where T : IBuff
    {
        return buffTypeMap.ContainsKey(typeof(T));
    }
    
    /// <summary>
    /// 清除所有buff
    /// </summary>
    public void ClearAllBuffs()
    {
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            RemoveBuffAt(i);
        }
    }
    
    /// <summary>
    /// 处理实体死亡事件
    /// </summary>
    public void HandleEntityDeath(DeathContext ctx)
    {
        for (int i = 0; i < activeBuffs.Count; i++)
        {
            var buff = activeBuffs[i].buff;
            if (buff is IOnDeath onDeath)
            {
                onDeath.OnDeath(ctx);
            }
        }
    }
    
    /// <summary>
    /// 更新序列化列表（用于Inspector显示）
    /// </summary>
    private void UpdateSerializedList()
    {
        // 确保列表大小匹配
        while (activeBuffs.Count > 0 && activeBuffs[activeBuffs.Count - 1].buff == null)
        {
            activeBuffs.RemoveAt(activeBuffs.Count - 1);
        }
    }
    
    /// <summary>
    /// 获取所有活跃buff数量
    /// </summary>
    public int GetActiveBuffCount()
    {
        return activeBuffs.Count;
    }
    
    /// <summary>
    /// 获取所有活跃buff
    /// </summary>
    public List<IBuff> GetAllActiveBuffs()
    {
        var result = new List<IBuff>();
        foreach (var buffInfo in activeBuffs)
        {
            if (buffInfo.buff != null && buffInfo.buff.IsActive)
            {
                result.Add(buffInfo.buff);
            }
        }
        return result;
    }
}

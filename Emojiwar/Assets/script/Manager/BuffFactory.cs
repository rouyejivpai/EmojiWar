using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Buff工厂，负责根据配置创建buff实例
/// </summary>
public static class BuffFactory
{
    private static readonly Dictionary<string, Type> buffTypeMap = new Dictionary<string, Type>();
    
    static BuffFactory()
    {
        // 注册所有buff类型
        RegisterBuffTypes();
    }
    
    /// <summary>
    /// 注册buff类型
    /// </summary>
    private static void RegisterBuffTypes()
    {
        // 在这里注册所有buff类型
        RegisterBuffType("rateup", typeof(rateup));
        RegisterBuffType("deathwhisper", typeof(Deathwhisper));
        // 可以继续添加更多buff类型
    }
    
    /// <summary>
    /// 注册buff类型
    /// </summary>
    public static void RegisterBuffType(string buffId, Type buffType)
    {
        if (buffType.IsSubclassOf(typeof(BuffBase)))
        {
            buffTypeMap[buffId] = buffType;
        }
        else
        {
            Debug.LogError($"Buff类型 {buffType.Name} 必须继承自 BuffBase");
        }
    }
    
    /// <summary>
    /// 根据ID创建buff
    /// </summary>
    public static IBuff CreateBuff(string buffId, float duration = -1f, int maxStacks = 1)
    {
        if (buffTypeMap.TryGetValue(buffId, out var buffType))
        {
            try
            {
                var buff = Activator.CreateInstance(buffType) as BuffBase;
                if (buff != null)
                {
                    buff.Duration = duration;
                    buff.MaxStacks = maxStacks;
                    return buff;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"创建buff {buffId} 时发生错误: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"未找到buff类型: {buffId}");
        }
        
        return null;
    }
    
    /// <summary>
    /// 根据配置数据创建buff
    /// </summary>
    public static IBuff CreateBuff(BuffConfig config)
    {
        if (config == null) return null;
        
        var buff = CreateBuff(config.buffId, config.duration, config.maxStacks);
        if (buff != null && config.parameters != null)
        {
            // 设置自定义参数
            SetBuffParameters(buff, config.parameters);
        }
        
        return buff;
    }
    
    /// <summary>
    /// 设置buff参数
    /// </summary>
    private static void SetBuffParameters(IBuff buff, Dictionary<string, object> parameters)
    {
        if (buff == null || parameters == null) return;
        
        var buffType = buff.GetType();
        foreach (var param in parameters)
        {
            var property = buffType.GetProperty(param.Key);
            if (property != null && property.CanWrite)
            {
                try
                {
                    var value = Convert.ChangeType(param.Value, property.PropertyType);
                    property.SetValue(buff, value);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"设置buff参数 {param.Key} 时发生错误: {e.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// 获取所有已注册的buff类型
    /// </summary>
    public static List<string> GetRegisteredBuffTypes()
    {
        return new List<string>(buffTypeMap.Keys);
    }
}

/// <summary>
/// Buff配置数据
/// </summary>
[Serializable]
public class BuffConfig
{
    public string buffId;
    public float duration = -1f;
    public int maxStacks = 1;
    public Dictionary<string, object> parameters;
    
    public BuffConfig(string buffId, float duration = -1f, int maxStacks = 1)
    {
        this.buffId = buffId;
        this.duration = duration;
        this.maxStacks = maxStacks;
        this.parameters = new Dictionary<string, object>();
    }
}

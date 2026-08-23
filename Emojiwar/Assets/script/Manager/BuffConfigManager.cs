using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Game.Data;

/// <summary>
/// Buff配置管理器，负责加载和管理buff配置文件
/// </summary>
public class BuffConfigManager : MonoBehaviour
{
    private BuffCatalog _buffCatalog;
    public BuffCatalog buffCatalog{
        get{
            if(_buffCatalog == null)
            {
                return GameManager.Instance.buffCatalog;
            }
            return _buffCatalog;
        }
        set{
            _buffCatalog = value;
        }
    }
    // 直接使用资产数据，不再维护副本
    
    [Serializable]
    public class BuffConfigData
    {
        public List<BuffConfig> buffs;
        public List<BuffCombo> buffCombos;
    }
    
    [Serializable]
    public class BuffCombo
    {
        public string name;
        public List<BuffConfig> buffs;
    }
    
    private void Awake()
    {
        // 资产驱动，无需加载；此处保留空实现以兼容生命周期
    }
    
    /// <summary>
    /// 加载buff配置（资产驱动，无需加载，保留方法用于兼容旧调用）
    /// </summary>
    private void LoadBuffConfig()
    {
        // no-op
    }
    
    /// <summary>
    /// 根据ID获取buff配置
    /// </summary>
    public BuffConfig GetBuffConfig(string buffId)
    {
        var list = buffCatalog != null ? buffCatalog.buffs : null;
        if (list == null) return null;
        return list.Find(b => b.buffId == buffId);
    }
    
    /// <summary>
    /// 根据名称获取buff组合
    /// </summary>
    public BuffCombo GetBuffCombo(string comboName)
    {
        var assets = buffCatalog != null ? buffCatalog.buffCombos : null;
        if (assets == null) return null;
        var asset = assets.Find(c => c.name == comboName);
        if (asset == null) return null;
        return new BuffCombo
        {
            name = asset.name,
            buffs = asset.buffs != null ? new List<BuffConfig>(asset.buffs) : new List<BuffConfig>()
        };
    }
    
    /// <summary>
    /// 获取所有buff配置
    /// </summary>
    public List<BuffConfig> GetAllBuffConfigs()
    {
        return buffCatalog != null && buffCatalog.buffs != null
            ? new List<BuffConfig>(buffCatalog.buffs)
            : new List<BuffConfig>();
    }
    
    /// <summary>
    /// 获取所有buff组合
    /// </summary>
    public List<BuffCombo> GetAllBuffCombos()
    {
        var assets = buffCatalog != null ? buffCatalog.buffCombos : null;
        var list = new List<BuffCombo>();
        if (assets != null)
        {
            foreach (var a in assets)
            {
                list.Add(new BuffCombo
                {
                    name = a.name,
                    buffs = a.buffs != null ? new List<BuffConfig>(a.buffs) : new List<BuffConfig>()
                });
            }
        }
        return list;
    }
    
    /// <summary>
    /// 应用buff组合到目标实体
    /// </summary>
    public void ApplyBuffCombo(string comboName, Entity target)
    {
        var combo = GetBuffCombo(comboName);
        if (combo == null || target == null) return;
        
        var buffSystem = target.GetComponent<BuffSystem>();
        if (buffSystem == null) return;
        
        foreach (var buffConfig in combo.buffs)
        {
            var buff = BuffFactory.CreateBuff(buffConfig);
            if (buff != null)
            {
                buffSystem.AddBuff(buff);
            }
        }
        
        Debug.Log($"成功应用buff组合 {comboName} 到 {target.name}");
    }
    
    /// <summary>
    /// 重新加载配置
    /// </summary>
    public void ReloadConfig()
    {
        // 资产驱动，无需重新加载
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class KILL : mod
{
    [FormerlySerializedAs("effect")] public IBuff buff;
    
    public override void init(GameObject target)
    {
        base.init(target);
        
        // 使用新的BuffSystem

    }

    public override void Trigger(WeaponBase weapon)
    {
        base.Trigger(weapon);
        //监听所有子弹的击杀
        foreach (var profileInfo in weapon.Profiles)
        {
            profileInfo.buffs.Add(bullet =>
            {
                if (bullet.TryGetComponent<profile>(out var projectile))
                {
                    projectile.OnKill += OnKill;
                }
            }); 
        }
        
    }
    
    public virtual void OnKill(Entity entity)
    {
        Debug.Log("击杀时扳机触发");
    }

    public override void Remove()
    {
        if (buff != null)
        {
            var buffSystem = Weapon.GetComponent<BuffSystem>();
            if (buffSystem != null)
            {
                buffSystem.RemoveBuff(buff);
            }
        }
    }
}

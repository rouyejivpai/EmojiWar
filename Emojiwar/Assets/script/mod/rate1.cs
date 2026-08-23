using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class rate1 : mod
{
    [FormerlySerializedAs("effect")] public IBuff buff;
    
    public override void init(GameObject target)
    {
        base.init(target);
        ffloat rate = target.GetComponent<WeaponBase>().fireRate;
        // 使用新的BuffSystem
        var buffSystem = Weapon.GetComponent<BuffSystem>();
        if (buffSystem != null)
        {
             buff = buffSystem.AddBuff(new floatbuff(rate,1)); // 10秒持续时间，最多3层
            
        }
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

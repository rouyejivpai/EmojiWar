using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class ThreeWater : mod
{
    [FormerlySerializedAs("effect")] public IBuff buff;
    public float spread = 30f;
    public GameObject preprofile;

    public Pistol weapon;
    public override void init(GameObject target)
    {
        base.init(target);
        
        // 使用新的BuffSystem

    }

    public override void Trigger(WeaponBase weapon)
    {
        base.Trigger(weapon);
        Debug.Log("ThreeWater触发");
        //制作子弹修饰信息包
       weapon.AddProfilebuff((profileInfo) =>
        {
            Debug.Log("修饰效果触发");
            //给子弹添加缩小buff
            profileInfo.buffs.Add(bullet=>bullet.GetComponent<BuffSystem>().AddBuff(new ScaleS(bullet.GetComponent<profile>())));
            //复制三个子弹信息包
            for (int i = -1; i < 2; i++)
            {
                if (i == 0) continue;
                ProfileInfo pro = new ProfileInfo(profileInfo);
                pro.angle = i*spread;
                pro.delay = 0f;
               weapon.Profiles.Add(pro);
            }
        });
       
        

    }
    
    

    public override void Remove()
    {
        
    }
}


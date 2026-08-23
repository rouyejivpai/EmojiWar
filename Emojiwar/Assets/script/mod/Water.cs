using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Water : mod
{
    [FormerlySerializedAs("effect")] public IBuff buff;

    public GameObject preprofile;

    
    public override void init(GameObject target)
    {
        base.init(target);
        
        // 使用新的BuffSystem

    }

    public override void Trigger(WeaponBase weapon)
    {
        base.Trigger(weapon);
        //制作子弹信息包
        ProfileInfo pro = new ProfileInfo(preprofile);
       
        //将子弹信息包加入列表
      weapon.AddProfieInfo(pro);

    }
    
    

    public override void Remove()
    {
        
    }
}


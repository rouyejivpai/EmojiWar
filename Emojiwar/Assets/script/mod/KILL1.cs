using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class KILL1 : KILL
{
    [FormerlySerializedAs("effect")] public IBuff buff;
    
    

  
    
    public override void OnKill(Entity entity)
    {
        Debug.Log("击杀时扳机触发");
        Weapon.GetComponent<BuffSystem>().AddBuff(new KILLbuff(Weapon, 0.2f));
    }

    
}

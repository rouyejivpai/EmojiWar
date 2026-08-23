using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ModGrid : Grid
{
    public WeaponBase weapon;
    public int order=0;
    public GameObject mod;
    public override void OnItemEnter(GameObject Obj)
    {
        if (weapon == null) return;
        Obj obj = Obj.GetComponent<Obj>();
        Debug.Log("触发装备模组");
        base.OnItemEnter(Obj);
        //执行装备模组
        obj.inObj.GetComponent<mod>().order=order;//设置模组顺序保留字
        weapon.addmod(obj.inObj);
        mod=Obj;
    }

    public override void OnItemExit(GameObject Obj)
    {   
        if (weapon == null) return;
        Obj obj = Obj.GetComponent<Obj>();
        Debug.Log("触发取消装备模组");
        weapon.removemod(obj.inObj);
        obj.inObj.transform.SetParent(obj.transform);
        base.OnItemExit(Obj);
        mod=null;
    }

    public override bool CheckLegal(Obj item)
    {
        return item.type == ObjType.mod;

    }
}

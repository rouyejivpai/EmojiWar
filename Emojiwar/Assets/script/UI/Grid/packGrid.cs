using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class packGrid : Grid
{

    public override void OnItemEnter(GameObject Obj)
    {
        Debug.Log("背包置入事件触发");
        Obj obj = Obj.GetComponent<Obj>();
        
        base.OnItemEnter(Obj);
       
        obj.inObj.transform.SetParent(GameManager.Instance.Pack.transform);
        empty = false;
    }
    public override void OnItemExit(GameObject Obj)
    {
        Obj obj = Obj.GetComponent<Obj>();
       
        
        obj.inObj.transform.SetParent(obj.transform);
        empty = true;
    }
}

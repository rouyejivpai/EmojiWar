using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

public class WeaponGrid : Grid
{
    public int weaponNumber=1;
    public PlayerController player{
        get{
            return GameManager.Instance.player.GetComponent<PlayerController>();
        }
    }
    public WeaponPanel weaponPanel;
    public DragTarget dragTarget;
    private void Awake()
    {
        //player = GameManager.Instance.player.GetComponent<PlayerController>();
        weaponPanel = GetComponent<WeaponPanel>();//获取面板组件
        GameManager.Instance.OnPlayerReady += init;
        dragTarget=GetComponent<DragTarget>();
    }

    private void OnEnable()
    {
        PlayerController pc =  GameManager.Instance.player.GetComponent<PlayerController>();
        Debug.Log(pc);
        if (weaponNumber==1)
        {
            pc.Weapon1update += updateWeapon;
        }
        else if(weaponNumber==2)
        {
            pc.Weapon2update += updateWeapon;
        }
    
    }
    public void init(GameObject pob){
       
        if (weaponNumber==1)
        {
            if (player.Weapon1 == null)
            {
                return;
            }
            GameObject OB=StoreManager.instance.Mod2Obj(player.Weapon1.gameObject);
            OB.GetComponent<SnapDrag>().SnapToTarget(dragTarget);
        }
        else if(weaponNumber==2)
        {
            if (player.Weapon2 == null)
            {
                return;
            }
            GameObject OB=StoreManager.instance.Mod2Obj(player.Weapon2.gameObject);
            OB.GetComponent<SnapDrag>().SnapToTarget(dragTarget);
        }
    }

    public void updateWeapon(GameObject Weapon)
    {
        if (Weapon==null)
        {
            return;
        }
        Objs.Add(StoreManager.instance.Mod2Obj(Weapon).GetComponent<Obj>());
        Obj obj = Objs[0];
        //设置物体不可见
        obj.image.enabled = false;
        //设置物体大小
        RectTransform rt=obj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
    public override void OnItemEnter(GameObject Obj= null)
    {
        
        Obj obj = Obj.GetComponent<Obj>();
        base.OnItemEnter(Obj);
        obj.image.enabled = false;
        if (weaponNumber == 1)
        {
            player.Weapon1 = obj.inObj.GetComponent<WeaponBase>();
        }
        else
        {
            player.Weapon2 = obj.inObj.GetComponent<WeaponBase>();
        }
       updateWeapon(obj.inObj);
    }

    public override void OnItemExit(GameObject Obj = null)
    {
       
        Obj obj = Obj.GetComponent<Obj>();
         obj.image.enabled = true;
         Obj.GetComponent<RectTransform>().sizeDelta = obj.originalSize;
        base.OnItemExit(Obj);
        GameObject weapon;
        //如果没有记录物体则先找到物体并包装
        // if (Objs[0]==null)
        // {
        //     //获取武器
        //      weapon = weaponPanel.currentWeapon.gameObject;
        //      Objs.Add(StoreManager.instance.Mod2Obj(weapon).GetComponent<Obj>());
        // }
        // RectTransform rt=this.Objs[0].GetComponent<RectTransform>();
        // rt.anchorMin = new Vector2(0.5f, 0.5f);
        // rt.anchorMax = new Vector2(0.5f, 0.5f);
        // rt.pivot = new Vector2(0.5f, 0.5f);
        // rt.anchoredPosition = Vector2.zero;
        

        
        // rt.sizeDelta = obj.originalSize;
        
        if (weaponNumber==1)
        {
           weapon = player.RemoveWeapon1();
        }
        else
        {
           weapon = player.RemoveWeapon2();
        }
        // //设置物体
        // obj.inObj = weapon;
        // weapon.transform.parent = obj.transform;
        // obj.image.enabled = true;
      
    }
    public override bool CheckLegal(Obj item)
    {
        return item.type == ObjType.weapon;

    }
}

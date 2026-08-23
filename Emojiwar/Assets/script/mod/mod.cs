using System.Collections;
using System.Collections.Generic;
using System.Net.Mime;
using UnityEngine;
using Game.Data;

public abstract class mod : MonoBehaviour
{
    public int order=0;//模组顺序临时保留字
    public Game.Data.ModBasicInfo modInfo;
    [Header("UI显示部分")]
    public string name;
    public Sprite sprite;
    public string desc;
    [Header("逻辑引用")]
    public GameObject Weapon;
    //装载
    public virtual void init(GameObject target)
    {
        Weapon=target;
    }

    public virtual void Trigger(WeaponBase weapon)
    {
        
    }

    public virtual void Remove()
    {
        
    }
}

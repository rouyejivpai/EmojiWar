using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Game.Data;
[Serializable]
public class ObjInfo
{
    public string name; //名称
    public string type; //类型
    public string subType=null;//子类型
    public Sprite icon; //图标
    public string description; //效果描述
    public int rarity; //稀有度
}
public enum ObjType
{
    weapon,
    mod,
    relic,
    unknow
}
public class Obj : MonoBehaviour
{
    public ObjInfo info;//
    [Header("UI引用")]
    public Image image;
    public Vector2 originalSize;
    public static HoverInfoPanel hoverPanel;
    [Header("内部物体")] 
    public GameObject _mod;
    public GameObject inObj
    {
        get
        {
            return _mod;
        }
        set
        {
            _mod = value;
            //设置物体时获取物体信息并储存
            
            getObjInfo(_mod);
            if (info.icon != null)
            {
                image.sprite = info.icon;
            }
        }
    }
    public ObjType type;
    
    private tiptrigger tip;
    [Header("交互控制")]
    [SerializeField] public bool isActive = true;  // 是否可交互

    private void Awake()
    {
        originalSize = image.rectTransform.sizeDelta;
        hoverPanel = HoverInfoPanel.Ins;
        tip = gameObject.AddComponent<tiptrigger>();
        tip.isActive = true;
    }

    void Start()
    {
       
        
       
    }
    
 
    //获取内部物体信息并记录
    private ObjInfo getObjInfo(GameObject obj)
    {
        //查明类型并记录
        ObjType type = getType();
        //分类获取信息
       info=new ObjInfo();
        switch (type)
        {
            //
            case ObjType.mod:
            {
                ModBasicInfo modInfo = obj.GetComponent<mod>().modInfo;
                info.name = modInfo.modName;
                info.description =modInfo.description;
                info.rarity = modInfo.rarity;
                info.type = "模组";
                    info.icon = modInfo.icon;
                    //info.icon = Resources.Load<Sprite>(modInfo.imagePath);
                //如果路径获取失败就尝试另一种方式
                if (info.icon == null)
                {
                    info.icon = obj.GetComponent<mod>().sprite;
                }
                
                break;
            }
            case ObjType.weapon:
            {
                WeaponEntry weapon = obj.GetComponent<WeaponBase>().Info;
                info.name = weapon.weaponName;
                info.description = weapon.description;
                info.rarity = weapon.rarity;
                info.type = "武器";
                info.icon = obj.GetComponent<WeaponBase>().image;
                
                break;
            }
            case ObjType.relic:
            {
                break;
            }
                
        }
        return info;
    }

    //获取物品类型
    private ObjType getType()
    {
        if (_mod.CompareTag("mod"))
        {
            type=ObjType.mod;
            return ObjType.mod;
        }

        if (_mod.CompareTag("relic"))
        {
            type=ObjType.relic;
            return ObjType.relic;
        }

        if (_mod.CompareTag("weapon"))
        {
            type = ObjType.weapon;
            return ObjType.weapon;
        }

        return ObjType.unknow;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WeaponPanel : MonoBehaviour
{
    public int id =1;
    [Header("UI References")]
    public Text weaponNameText;
    public Image weaponIconImage;
    public List<ModGrid> WeaponGridss;
    
    //武器引用
    [Header("武器引用")]
    public WeaponBase currentWeapon;


    private void Start()
    {
        GameManager.Instance.OnPlayerReady += init;
        Debug.Log("武器监听初始化");
    }

   //武器监听初始化
   private void init(GameObject Player)
   {
       Debug.Log("武器面板初始化");
       PlayerController pc = Player.GetComponent<PlayerController>();
       Debug.Log(pc);
       if (id==1)
       {
           pc.Weapon1update += UpdateWeaponUI;
           
       }
       else if(id==2)
       {
           pc.Weapon2update += UpdateWeaponUI;
           
       }

       //更新一下
      
     
       Debug.Log("详细页已经监听");
   }

   public void UpdateWeaponUI(GameObject weapon)
    {
        Debug.Log("处理武器更新监听回调"+id);
        if (weapon == null)
        {
            weaponIconImage.sprite = null;
            weaponNameText.text = "空";
            foreach (var weaponGrid in WeaponGridss)
            {
                weaponGrid.weapon = null;
                weaponGrid.GetComponent<DragTarget>().acceptedItems.Clear();
                for (int i = weaponGrid.transform.childCount - 1; i >= 0; i--) Destroy(weaponGrid.transform.GetChild(i).gameObject);
                weaponGrid.gameObject.SetActive(false);
            }
            return;
        }
        currentWeapon = weapon.GetComponent<WeaponBase>();
        if (currentWeapon != null)
        {
            weaponIconImage.sprite = currentWeapon.image;
            weaponNameText.text =currentWeapon.WeaponName;
            int k = 0;
            foreach (var weaponGrid in WeaponGridss)
            {
                
                if (k<currentWeapon.modCount)
                {
                    weaponGrid.gameObject.SetActive(true);
                }
                else
                {
                    weaponGrid.gameObject.SetActive(false);
                    break;
                }
                if (currentWeapon.MODS.Count>k){
                    //包装
                   GameObject mod = StoreManager.instance.Mod2Obj(currentWeapon.MODS[k]); 
                   AttachModToGridNormalized(mod, weaponGrid);
                }
                
                weaponGrid.weapon = currentWeapon;

                k++;

            }
        }
    }

    // 归一化放置到网格：确保缩放和锚点正确，避免保留世界坐标导致的大小异常
    private void AttachModToGridNormalized(GameObject obj, ModGrid grid)
    {
        if (obj == null || grid == null) return;

        // 尝试使用吸附，让目标完成接收与事件链条
        var dragTarget = grid.gameObject.GetComponent<DragTarget>();
        var snap = obj.GetComponent<SnapDrag>();
        if (snap != null && dragTarget != null)
        {
            snap.SnapToTarget(dragTarget,false);
        }
        else
        {
            // 无吸附能力时，直接设为子物体（不保留世界坐标）
            obj.transform.SetParent(grid.transform, false);
        }

        // 归一化 RectTransform，以继承网格的缩放并充满格子
        var rt = obj.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale = Vector3.one;
           
        }
    }
}

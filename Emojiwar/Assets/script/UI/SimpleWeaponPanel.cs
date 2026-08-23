using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SimpleWeaponPanel : MonoBehaviour
{
    public int id = 1;//武器序号
    [Header("UI References")]
    public Text weaponNameText;
    public Image weaponIconImage;
    public List<ModGrid> WeaponGridss;
    
    public GameObject progress;

    private float maxWidth;
    //武器引用
    [Header("武器引用")]
    public WeaponBase currentWeapon;


    private void Awake()
    {
     
      
        maxWidth=progress.GetComponent<RectTransform>().sizeDelta.x;
    }

    private void OnEnable()
    {
        GameManager.Instance.OnPlayerReady += init;
        
       
    }

    
    private void init(GameObject Player)
    {
        
        
        PlayerController pc = Player.GetComponent<PlayerController>();
        if (id == 1)
        {
            pc.Weapon1update += UpdateWeaponUI;
           // Debug.Log("一号监听已设置");
        }
        else if (id == 2)
        {
            pc.Weapon2update += UpdateWeaponUI;
        }

        Debug.Log(id);
    }
    private void OnDestroy()
    {
        PlayerController pc = GameManager.Instance.player.GetComponent<PlayerController>();
        if (id == 1)
        {
            
            pc.Weapon1update -= UpdateWeaponUI;
        }
        else if (id == 2)
        {
            pc.Weapon2update -= UpdateWeaponUI;
        }
    }

    public void UpdateWeaponUI(GameObject Weapon)
    {
        if (Weapon==null)
        {
            weaponIconImage.sprite = null;
            weaponNameText.text ="空";
            currentWeapon = null;
            return;
        }
        Debug.Log("处理武器更新监听回调");
        currentWeapon = Weapon.GetComponent<WeaponBase>();
        if (currentWeapon != null)
        {
            weaponIconImage.sprite = currentWeapon.image;
            weaponNameText.text =currentWeapon.WeaponName;
            foreach (var weaponGrid in WeaponGridss)
            {
                weaponGrid.weapon = currentWeapon;
            }
        }
        else
        {
            weaponIconImage.sprite = null;
            weaponNameText.text ="";
        }
    }

    private void Update()
    {
        setProgress();
    }

    private void setProgress()
    {
        if(currentWeapon==null) return;
        float progress = currentWeapon.GetProgress();
        RectTransform fillBar = this.progress.GetComponent<RectTransform>();
        Vector2 sizeDelta = fillBar.sizeDelta;
        sizeDelta.x = maxWidth * progress;
        fillBar.sizeDelta = sizeDelta;
    }
}

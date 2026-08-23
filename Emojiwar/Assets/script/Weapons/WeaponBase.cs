using System;
using System.Collections;
using System.Collections.Generic;
using Game.Data;
using UnityEngine;
using UnityEngine.UI;

public abstract class WeaponBase : MonoBehaviour,BuffTarget
{
    public WeaponEntry Info;
    [Header("基础属性")]
    [SerializeField] protected string weaponName = "Weapon";
    [SerializeField] protected int weaponId = 0;
    [SerializeField] protected float damage = 10f;
    [SerializeField] public ffloat fireRate = 1f;        // 每秒射击次数
    [SerializeField] protected float range = 10f;          // 射程
    [SerializeField] protected int maxAmmo = 30;           // 最大弹药
    [SerializeField] protected int currentAmmo;            // 当前弹药
    [SerializeField] protected float reloadTime = 2f;      // 装弹时间
    [SerializeField] protected enum Type
    {
        Shot,
        Close,
        Dun
        
    }
    public List<ProfileInfo> Profiles = new List<ProfileInfo>();
    public Action<GameObject> buffprofile;
    [Header("射击设置")]
    [SerializeField] public Transform firePoint;        // 射击点
    [SerializeField] protected GameObject projectilePrefab; // 子弹预制体
    [SerializeField] protected AudioClip fireSound;        // 射击音效
    [SerializeField] protected AudioClip reloadSound;      // 装弹音效
    
    [Header("状态")]
    [SerializeField] protected bool isReloading = false;
    [SerializeField] protected float lastFireTime = 0f;
    [Header("事件")]
    public List<IBuff> firebullet;
    public List<IBuff> beforefire;
    public List<IBuff> afterfire;
    [Header("加装模组")]
    public int modCount = 5;
    public List<GameObject> MODS = new List<GameObject>();
    public List<mod> mods = new List<mod>();
    // 属性访问器
    public string WeaponName => weaponName;
    public int WeaponId => weaponId;
    public float Damage => damage;
   
    
    public float Range => range;
    public int MaxAmmo => maxAmmo;
    public int CurrentAmmo => currentAmmo;
    public bool IsReloading => isReloading;

    public Sprite image;
    //允许射击=>冷却完成 有弹药 未在装弹
    public bool CanFire => !isReloading && currentAmmo > 0 && Time.time - lastFireTime >= 1f / fireRate;
    
    public Entity owner;           // 武器持有者
    protected AudioSource audioSource;
    
    protected virtual void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        currentAmmo = maxAmmo;
    }
    
    protected virtual void Start()
    {
        // 如果没有设置射击点，使用当前Transform
        if (firePoint == null)
        {
            firePoint = transform;
        }
    }
    
    // 初始化武器（由Entity调用）
    public virtual void Initialize(Entity weaponOwner)
    {
        owner = weaponOwner;
    }
    
    // 尝试射击（主要射击方法）
    public virtual bool TryFire(Vector2 target)
    {
        if (!CanFire) return false;
        
        
        if (Fire(target))
        {
            lastFireTime = Time.time;
            
            return true;
        }
        
        return false;
    }
    
    // 射击方法（子类必须实现）
    
    protected abstract bool Fire(Vector2 target);
    // 装弹
    public virtual void Reload()
    {
        if (isReloading || currentAmmo == maxAmmo) return;
        
        StartCoroutine(ReloadCoroutine());
    }
    
    // 装弹协程
    protected virtual IEnumerator ReloadCoroutine()
    {
        isReloading = true;
        
        // 播放装弹音效
        if (audioSource != null && reloadSound != null)
        {
            audioSource.PlayOneShot(reloadSound);
        }
        
        yield return new WaitForSeconds(reloadTime);
        
        currentAmmo = maxAmmo;
        isReloading = false;
        
        Debug.Log($"[{weaponName}] 装弹完成，当前弹药: {currentAmmo}");
    }
    
    // 添加弹药
    public virtual void AddAmmo(int amount)
    {
        currentAmmo = Mathf.Min(maxAmmo, currentAmmo + amount);
    }
    
    // 设置弹药
    public virtual void SetAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(amount, 0, maxAmmo);
    }
    //获取武器进度条
    public virtual float GetProgress()
    {
        float pro =(Time.time - lastFireTime)/ (1f / fireRate);
        if (pro >= 1f) pro = 1f;
        if (pro <= 0f) pro = 0f;
        return pro;
    }
    // 获取武器信息
    public virtual string GetWeaponInfo()
    {
        return $"{weaponName} - 弹药: {currentAmmo}/{maxAmmo} - 伤害: {damage} - 射速: {fireRate}/s";
    }
    
    // 销毁武器
    public virtual void DestroyWeapon()
    {
        Destroy(gameObject);
    }
    /////
    //模组方法
    /////
    
    //加装模组
    public void addmod(GameObject Mod)
    {
        MODS.Add(Mod);
        
        mod mod=Mod.GetComponent<mod>();
        
        mods.Add(mod);//可选
        Mod.transform.SetParent(transform);
       
        mod.init(gameObject);//初始化
        sortmod();//排序
    }
    
    //卸载模组
    public void removemod(GameObject Mod)
    {
        MODS.Remove(Mod);
        mod mod=Mod.GetComponent<mod>();
        mods.Remove(mod);
        Mod.transform.SetParent(null);
        
        mod.Remove();
        sortmod();//排序
    }
    //重新排列模组顺序(每次模组更新后触发)
    public void sortmod(){
        mods.Sort((a, b) => a.order.CompareTo(b.order));
    }
     public List<buff2ProfileInfo> Profilebuffs = new List<buff2ProfileInfo>();//修饰ProfileInfo的委托列表
    public void AddProfilebuff(buff2ProfileInfo buff)
    {
        Profilebuffs.Add(buff);
    }
    public void AddProfieInfo(ProfileInfo profileInfo)
    {
        foreach (var buff in Profilebuffs)
        {
            buff(profileInfo);
        }
        Profilebuffs.Clear();//清空委托列表
        Profiles.Add(profileInfo);
    }
} 
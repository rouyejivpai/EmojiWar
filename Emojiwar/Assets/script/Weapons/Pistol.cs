using System;
using System.Collections;
using UnityEditor.MemoryProfiler;
using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
//子弹发射信息包
public class ProfileInfo
{
    public GameObject projectilePrefab;//子弹预制体
    public float delay = 0.1f;//延迟时间

    public float angle = 0f;
    public List<Bulletbuff> buffs=new List<Bulletbuff>();//发射时被赋予的buff
    
    public ProfileInfo(GameObject OB){
        projectilePrefab = OB;

    }
    public ProfileInfo(ProfileInfo profileInfo)
    {
        this.projectilePrefab = profileInfo.projectilePrefab;
        this.delay = profileInfo.delay;
        this.buffs = profileInfo.buffs;
    }

    
}
public delegate void Bulletbuff(GameObject bullet);

//用于给ProfileInfo添加buff的委托
public delegate void buff2ProfileInfo(ProfileInfo profileInfo);
//远程
public class Pistol : WeaponBase
{
    protected Type type=Type.Shot;
    [Header("手枪特有属性")]
    [SerializeField] public float bulletSpeed = 15f;//子弹速度
    [SerializeField] public float spread = 0.1f;  // 射击扩散
    [SerializeField] public float dartle = 1;

   private Vector2 firePosition;
    private Vector2 direction;

    public List<buff2ProfileInfo> Profilebuffs = new List<buff2ProfileInfo>();//修饰ProfileInfo的委托列表
    //单次发射队列
    //public List<ProfileInfo> Profiles = new List<ProfileInfo>();
    protected override void Start()
    {
        base.Start();
        weaponName = "Pistol";
        weaponId = 1;
    }
    //开火方法（输入目标坐标
    protected override bool Fire(Vector2 target)
    {
        StartCoroutine(fire(target));
        return true;
    }
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
    private IEnumerator fire(Vector2 target)
    {
        //触发开火前方法
        //beforefire.Invoke(target);
    
        // 计算射击方向（从射击点指向目标）
         firePosition = firePoint.position;
         direction = (target - firePosition).normalized;
        // 如果没有目标位置或目标位置与射击点重合，使用射击点的右方向
        if (direction == Vector2.zero)
        {
            direction = firePoint.right;
        }
       //初始化子弹包队列 
        Profiles.Clear();//清空队列
        Profilebuffs.Clear();//清空委托列表
        //Profiles.Add(new ProfileInfo(projectilePrefab));//加入主子弹
        
        //触发所有模组预结算
        foreach (var mod in mods)
        {
            mod.Trigger(this);
        }
        // 按顺序处理每个子弹包
        for (int i = 0; i < Profiles.Count; i++)
        {
            Debug.Log($"[{weaponName}] 发射第{i}个子弹包");
            ProfileInfo profileInfo = Profiles[i];
             // 创建子弹
            GameObject bullet = Instantiate(profileInfo.projectilePrefab, firePoint.position, Quaternion.identity); 
            // 应用子弹缓冲区
            foreach (var buff in profileInfo.buffs)
            {
                buff(bullet);
            }
                    // 获取子弹组件并设置属性
                    if (bullet.TryGetComponent<profile>(out var projectile))
                    {
                        //设置
                        projectile.SetProjectile(owner.Team, damage);
                        
                        // 设置子弹速度
                        if (bullet.TryGetComponent<Rigidbody2D>(out var bulletRb))
                        {
                            // 添加随机扩散
                            Vector2 finalDirection = direction;
                            if (spread > 0)
                            {
                                float randomAngle = Random.Range(-spread, spread)+profileInfo.angle;
                                finalDirection = Quaternion.Euler(0, 0, randomAngle) * direction;
                            }
                            
                            bulletRb.velocity = finalDirection * bulletSpeed;
                            
                            // 设置子弹朝向
                            float angle = Mathf.Atan2(finalDirection.y, finalDirection.x) * Mathf.Rad2Deg+90+profileInfo.angle;
                            bullet.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                            
                            
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[{weaponName}] 子弹预制体缺少profile组件");
                    }
                    
                    yield return new WaitForSeconds(profileInfo.delay);
        }
        
        //发射事件
       
        // 播放射击音效
        if (audioSource != null && fireSound != null)
        {
            audioSource.PlayOneShot(fireSound);
        }
    }
    

} 
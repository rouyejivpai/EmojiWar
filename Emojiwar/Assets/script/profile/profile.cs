using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class profile : MonoBehaviour,BuffTarget
{
    [Header("飞行物设置")]
    public string targetTag = "entity";  // 目标标签
    public int team = 1;                 // 飞行物所属阵营
    public float damage = 10f;           // 伤害值
    public bool destroyOnHit = true;     // 碰撞后是否销毁
    
    [Header("碰撞效果")]
    public GameObject hitEffect;          // 碰撞特效预制体
    public AudioClip hitSound;           // 碰撞音效
    
    private Rigidbody2D rb;
    private Collider2D col;
    /// <summary>
    /// 击中行为列表
    /// </summary>
    [SerializeReference] public List<IHitBehavior> hitBehaviors = new List<IHitBehavior>();
    private Vector2 lastNormal;
   
    public event Action< Entity> OnHit;  // 击中事件
    public event Action<Entity> OnKill;  // 击杀事件
    public event Action<profile> OnDeath;  // 摧毁事件

    public BuffSystem buffSystem;

    void Start()
    {
        // 获取组件
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        
        if(TryGetComponent<BuffSystem>(out var bs)){
           this.buffSystem = bs;
        }
        
        // // 确保有刚体和碰撞器
        // if (rb == null)
        // {
        //     Debug.LogWarning($"[{name}] 缺少 Rigidbody2D 组件");
        // }
        if (col == null)
        {
            Debug.LogWarning($"[{name}] 缺少 Collider2D 组件");
        }
    }
    
    // 2D碰撞检测
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.contactCount > 0)
        {
            lastNormal = collision.GetContact(0).normal;
        }
        else if (col != null && collision.collider != null)
        {
            var d = col.Distance(collision.collider);
            lastNormal = d.normal;
        }
        HandleCollision(collision.gameObject);
    }
    
    // 2D触发器检测（如果碰撞器设置为Trigger）
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (col != null && other != null)
        {
            var d = col.Distance(other);
            lastNormal = d.normal;
        }
        else
        {
            lastNormal = Vector2.zero;
        }
        HandleCollision(other.gameObject);
    }
    
    //碰撞效果
    private void HandleCollision(GameObject target)
    {
        // 检查标签
        if (target.CompareTag(targetTag))
        {
            // 检查是否有Entity组件
            Entity targetEntity = target.GetComponent<Entity>();
            if (targetEntity != null)
            {
                // 检查阵营是否不同
                if (targetEntity.Team != team&&targetEntity.isAlive)
                {
                    Debug.Log($"[{name}] 击中目标: {target.name}, 阵营: {targetEntity.Team}");
                    
                    // 造成伤害
                    targetEntity.TakeDamage(damage);
                    
                    // 触发击中事件
                    OnHit?.Invoke(targetEntity);
                    
                    // 检查是否死亡
                    if (!targetEntity.isAlive)
                    {
                        Debug.Log($"[{name}] 击杀目标: {target.name}");
                        // 触发击杀事件
                        OnKill?.Invoke(targetEntity);
                    }
                    
                    // 播放碰撞特效
                    if (hitEffect != null)
                    {
                        Instantiate(hitEffect, transform.position, transform.rotation);
                    }
                    
                    // 播放碰撞音效
                    if (hitSound != null)
                    {
                        AudioSource.PlayClipAtPoint(hitSound, transform.position);
                    }
                    
                    // 销毁飞行物
                    bool handled = false;
                    if (hitBehaviors != null && hitBehaviors.Count > 0)
                    {
                        for (int i = 0; i < hitBehaviors.Count; i++)
                        {
                            var bh = hitBehaviors[i];
                            if (bh == null) continue;
                            if (bh.Apply(this, targetEntity, lastNormal))
                            {
                                handled = true;
                                break;
                            }
                        }
                    }
                    else{
                        Destroy(this.gameObject);
                    }
                    
                }
                else
                {
                    //Debug.Log($"[{name}] 击中友军: {target.name}, 阵营相同: {team}");
                }
            }
            else
            {
                Debug.LogWarning($"[{name}] 目标 {target.name} 没有Entity组件");
            }
            return;
        }
        
        
    }
    
    // 3D碰撞检测（保留兼容性）
    private void OnCollisionEnter(Collision other)
    {
        HandleCollision(other.gameObject);
    }
    
    // 手动销毁方法（可用于超时销毁）
    public void DestroyProjectile()
    {
        Destroy(gameObject);
    }
    
    // 设置飞行物属性
    public void SetProjectile(int projectileTeam, float projectileDamage)
    {
        team = projectileTeam;
        damage = projectileDamage;
    }
}

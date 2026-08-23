using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlashProjectile : MonoBehaviour
{
    [Header("刀光属性")]
    [SerializeField] private float damage = 10f;           // 伤害值
    [SerializeField] private float duration = 0.5f;        // 持续时间
    [SerializeField] private float rotationSpeed = 360f;   // 旋转速度（度/秒）
    
    [Header("视觉效果")]
    [SerializeField] private SpriteRenderer slashSprite;    // 刀光精灵
    [SerializeField] private TrailRenderer slashTrail;      // 刀光拖尾
    [SerializeField] private ParticleSystem slashParticles; // 刀光粒子效果
    
    public int team;                                       // 所属阵营
    private float startTime;                                // 开始时间
    private HashSet<Entity> hitEntities;                     // 已伤害的实体（防重复伤害）
    
    private void Start()
    {
        // 获取组件
        if (slashSprite == null) slashSprite = GetComponent<SpriteRenderer>();
        if (slashTrail == null) slashTrail = GetComponent<TrailRenderer>();
        if (slashParticles == null) slashParticles = GetComponent<ParticleSystem>();
        
        // 初始化
        hitEntities = new HashSet<Entity>();
        startTime = Time.time;
        
        // 设置自动销毁
        Destroy(gameObject, duration);
    }
    
    /// <summary>
    /// 初始化刀光
    /// </summary>
    public void Initialize(int weaponTeam, float weaponDamage, Vector2 direction, float weaponDuration, float weaponRotationSpeed, float weaponRotationAngle)
    {
        team = weaponTeam;
        damage = weaponDamage;
        duration = weaponDuration;
        rotationSpeed = weaponRotationSpeed;
        
        // 设置初始朝向
        if (direction != Vector2.zero)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }
    
    void Update()
    {
        // 原地旋转（只改变自身朝向，不改变位置）
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
        
        // 检测碰撞
        CheckCollisions();
    }
    
    /// <summary>
    /// 检测碰撞
    /// </summary>
    private void CheckCollisions()
    {
        // 使用OverlapCircle检测范围内的实体
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 0.5f);
        
        foreach (var collider in colliders)
        {
            if (collider.CompareTag("entity") || collider.CompareTag("Player"))
            {
                Entity targetEntity = collider.GetComponent<Entity>();
                if (targetEntity != null && targetEntity.Team != team)
                {
                    // 检查是否已经伤害过这个实体
                    if (!hitEntities.Contains(targetEntity))
                    {
                        // 造成伤害
                        targetEntity.TakeDamage(damage);
                        hitEntities.Add(targetEntity);
                        
                        // 播放击中效果
                        PlayHitEffect();
                        
                        Debug.Log($"[SlashProjectile] 击中目标: {targetEntity.name}, 造成伤害: {damage}");
                    }
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        
        if (other.gameObject.CompareTag("entity"))
        {
            Entity targetEntity = other.gameObject.GetComponent<Entity>();
            if (targetEntity != null && targetEntity.Team != team)
            {
                targetEntity.TakeDamage(damage);
            }
            
        }
    }

    /// <summary>
    /// 播放击中效果
    /// </summary>
    private void PlayHitEffect()
    {
        // 播放击中音效
        if (GetComponent<AudioSource>() != null)
        {
            GetComponent<AudioSource>().Play();
        }
        
        // 播放击中粒子效果
        if (slashParticles != null)
        {
            slashParticles.Play();
        }
        
        // 可以在这里添加屏幕震动、击中特效等
    }
    
    /// <summary>
    /// 设置刀光外观
    /// </summary>
    public void SetSlashAppearance(Sprite sprite, Color color, float trailWidth = 0.1f)
    {
        if (slashSprite != null)
        {
            slashSprite.sprite = sprite;
            slashSprite.color = color;
        }
        
        if (slashTrail != null)
        {
            slashTrail.startColor = color;
            slashTrail.endColor = new Color(color.r, color.g, color.b, 0f);
            slashTrail.startWidth = trailWidth;
            slashTrail.endWidth = 0f;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        // 绘制检测范围
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
} 
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class Entity : MonoBehaviour,BuffTarget
{
    [Header("基础属性")]
    [SerializeField] protected string entityName = "Entity";        // 实体名称
    [SerializeField] protected int maxHealth = 100;                 // 最大生命值
    [SerializeField] protected float currentHealth;                 // 当前生命值
    [SerializeField] protected int team = 0;                        // 阵营ID (0=中立, 1=玩家, 2=敌人)
    [SerializeField] public bool isAlive = true;                 // 是否存活
    [SerializeField] protected bool isInvulnerable = false;         // 是否无敌
    
    [Header("位置和移动")]
    [SerializeField] protected Vector2 position;                    // 2D位置
    [SerializeField] public Vector2 velocity;                       // 输入方向（单位向量）
    [SerializeField] protected ffloat moveSpeed = 5f;                // 移动速度
    [SerializeField] protected float rotationSpeed = 180f;          // 旋转速度
    
    // 刚体组件引用
    protected Rigidbody2D rb;

    public Action Fire;
    
    
    [Header("战斗属性")]
    // [SerializeField] protected float attackDamage = 10f;            // 攻击力
    // [SerializeField] protected float attackRange = 2f;              // 攻击范围
    // [SerializeField] protected float attackSpeed = 1f;              // 攻击速度
    [SerializeField] protected float lastAttackTime;                // 上次攻击时间
    [SerializeField] protected float defense = 0f;                  // 防御力
    
    [Header("武器系统")]
    [SerializeField] public WeaponBase currentWeapon;            // 当前武器

    [SerializeField] public WeaponBase secondWeapon;        // 副武器
    [SerializeField] protected int defaultWeaponId = 1;             // 默认武器ID
    
    [Header("状态")]
    [SerializeField] protected EntityState currentState = EntityState.Idle;  // 当前状态
    [SerializeField] protected bool isStunned = false;              // 是否被眩晕
    [SerializeField] protected bool isFrozen = false;               // 是否被冰冻
    [SerializeField] protected float stunDuration = 0f;             // 眩晕持续时间
    [SerializeField] protected float freezeDuration = 0f;           // 冰冻持续时间
    
    [Header("视觉效果")]
    [SerializeField] protected SpriteRenderer spriteRenderer;       // 精灵渲染器
    [SerializeField] protected Animator animator;                   // 动画控制器
    [SerializeField] protected Color originalColor;                 // 原始颜色
    private struct TintData { public Color color; public float intensity; }
    private Dictionary<string, TintData> _tints = new Dictionary<string, TintData>();
    public void SetTint(string key, Color color, float intensity) { var t = new TintData { color = color, intensity = Mathf.Clamp01(intensity) }; _tints[key] = t; ApplyTint(); }
    public void RemoveTint(string key) { if (_tints.Remove(key)) { ApplyTint(); } }
    private void ApplyTint() { if (spriteRenderer == null) return; Color result = originalColor; if (_tints.Count > 0) { float total = 0f; Color mix = new Color(0f,0f,0f,1f); foreach (var kv in _tints) { var td = kv.Value; total += td.intensity; mix += new Color(td.color.r * td.intensity, td.color.g * td.intensity, td.color.b * td.intensity, 0f); } if (total > 0f) { total = Mathf.Clamp01(total); mix = new Color(mix.r / total, mix.g / total, mix.b / total, 1f); result = Color.Lerp(originalColor, mix, total); } } spriteRenderer.color = result; }
    
    [Header("音效")]
    [SerializeField] protected AudioSource audioSource;             // 音频源
    [SerializeField] protected AudioClip hitSound;                  // 受击音效
    [SerializeField] protected AudioClip deathSound;                // 死亡音效

    [Header("BUFF")] 
    

    public  BuffSystem buffSystem;

    // 事件
    public System.Action<Entity> OnDeath;                          // 死亡事件
    public System.Action<Entity, float> OnHealthChanged;           // 生命值变化事件
    public System.Action<Entity, EntityState> OnStateChanged;      // 状态变化事件
    // 由 BUFF 统一分发事件，不再在 Entity 中维护亡语列表
    // 实体状态枚举
    public enum EntityState
    {
        Idle,       // 空闲
        Moving,     // 移动
        Attacking,  // 攻击
        Stunned,    // 眩晕
        Frozen,     // 冰冻
        Dead        // 死亡
    }
    
    // 属性访问器
    public string EntityName => entityName;
    public int MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public int Team => team;
    public bool IsAlive => isAlive;
    public bool IsInvulnerable => isInvulnerable;
    public Vector2 Position => position;
    public Vector2 Velocity => velocity;
    public ffloat MoveSpeed => moveSpeed;
    // public float AttackDamage => attackDamage;
    // public float AttackRange => attackRange;
    public EntityState CurrentState => currentState;
    public Vector2 Direction => velocity;
    
    protected virtual void Awake()
    {
        // 初始化组件引用
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
        rb = GetComponent<Rigidbody2D>(); // 初始化刚体组件引用

        buffSystem = GetComponent<BuffSystem>();
        
        // 设置刚体属性
        if (rb != null)
        {
            rb.gravityScale = 0f;  // 无重力
            rb.drag = 0f;          // 无阻力
            rb.angularDrag = 0f;   // 无角阻力
            rb.constraints = RigidbodyConstraints2D.FreezeRotation; // 冻结旋转
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 连续碰撞检测
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 插值，减少抖动
        }
        
        // 保存原始颜色
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        
        // 初始化生命值
        currentHealth = maxHealth;
        
        // 初始化位置
        position = transform.position;
    }
    
    protected virtual void Start()
    {
        // 子类可以在这里进行初始化
        
        // 初始化武器系统
        //InitializeWeaponSystem();
    }
    
    // 测试碰撞检测
    private void TestCollisionDetection()
    {
        if (rb == null) return;
        
        // 检测周围的碰撞器
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, 2f);
        Debug.Log($"[{entityName}] 周围碰撞器数量: {nearbyColliders.Length}");
        
        foreach (Collider2D col in nearbyColliders)
        {
            if (col.gameObject != gameObject)
            {
                Debug.Log($"[{entityName}] 附近物体: {col.gameObject.name} (Layer: {col.gameObject.layer})");
            }
        }
    }
    
    // 在Update中调用测试（每5秒一次）
    private float lastTestTime = 0f;
    protected virtual void Update()
    {
        if (!isAlive) return;
        
        // 更新状态/效果用常规帧
        UpdateState();
        UpdateEffectDuration();
    }
    
    protected virtual void FixedUpdate()
    {
        if (!isAlive) return;
        UpdatePosition(); // 物理更新放在FixedUpdate
    }
    
    // 更新状态
    protected virtual void UpdateState()
    {
        // 检查眩晕状态
        if (isStunned && stunDuration > 0)
        {
            SetState(EntityState.Stunned);
            return;
        }
        
        // 检查冰冻状态
        if (isFrozen && freezeDuration > 0)
        {
            SetState(EntityState.Frozen);
            return;
        }
        
        // 清除已过期的效果
        if (stunDuration <= 0) isStunned = false;
        if (freezeDuration <= 0) isFrozen = false;
    }
    
    // 更新位置（仅由FixedUpdate调用）
    protected virtual void UpdatePosition()
    {
        if (rb != null)
        {
            // 统一用rb.velocity驱动，交给引擎解算碰撞
            rb.velocity = velocity * moveSpeed;
            position = rb.position;
        }
    }
    
    // 更新效果持续时间
    protected virtual void UpdateEffectDuration()
    {
        if (stunDuration > 0)
            stunDuration -= Time.deltaTime;
        
        if (freezeDuration > 0)
            freezeDuration -= Time.deltaTime;
    }
    
    // 设置状态
    protected virtual void SetState(EntityState newState)
    {
        if (currentState != newState)
        {
            EntityState oldState = currentState;
            currentState = newState;
            OnStateChanged?.Invoke(this, newState);
            
            // 更新动画
            if (animator != null)
            {
                //之后在补全
               // animator.SetInteger("State", (int)newState);
            }
        }
    }
    
    // 受到伤害
    public virtual void TakeDamage(float damage, Entity attacker = null)
    {
        if (!isAlive || isInvulnerable) return;
        
        // 计算实际伤害（考虑防御力）
        float actualDamage = Mathf.Max(0, damage - defense);
        currentHealth -= actualDamage;
        
        // 触发生命值变化事件
        OnHealthChanged?.Invoke(this, actualDamage);
        
        // 播放受击音效
        if (audioSource != null && hitSound != null)
        {
            audioSource.PlayOneShot(hitSound);
        }
        
        // 受击闪烁效果
        StartCoroutine(HitFlash());
        
        // 检查是否死亡
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    // 治疗
    public virtual void Heal(float amount)
    {
        if (!isAlive) return;
        
        float oldHealth = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        float actualHeal = currentHealth - oldHealth;
        
        if (actualHeal > 0)
        {
            OnHealthChanged?.Invoke(this, -actualHeal); // 负数表示治疗
        }
    }
    
    // 死亡
    protected virtual void Die()
    {
        if (!isAlive) return;
        
        isAlive = false;
        SetState(EntityState.Dead);
        
        // 播放死亡音效
        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }
        
        // 触发死亡事件
        OnDeath?.Invoke(this);
        // 亡语由 BUFF 组件统一在 OnDeath 事件中分发
        // 延迟销毁对象
        Destroy(gameObject, 0f);
    }
    
    // 复活
    public virtual void Revive(float healthPercent = 1f)
    {
        if (isAlive) return;
        
        isAlive = true;
        currentHealth = maxHealth * healthPercent;
        SetState(EntityState.Idle);
    }
    
    // 移动（仅设置方向，真实速度由FixedUpdate赋予rb）
    public virtual void Move(Vector2 direction)
    {
        if (!isAlive || isStunned || isFrozen) return;
        
        velocity = direction.sqrMagnitude > 1f ? direction.normalized : direction;
        //SetState(EntityState.Moving);
    }
    
    // 停止移动
    public virtual void StopMoving()
    {
        velocity = Vector2.zero;
        
        // 如果有刚体，也要停止刚体移动
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
        
        SetState(EntityState.Idle);
    }
    
  
    
    // 眩晕
    public virtual void Stun(float duration)
    {
        if (isInvulnerable) return;
        
        isStunned = true;
        stunDuration = Mathf.Max(stunDuration, duration);
        SetState(EntityState.Stunned);
    }
    
    // 冰冻
    public virtual void Freeze(float duration)
    {
        if (isInvulnerable) return;
        
        isFrozen = true;
        freezeDuration = Mathf.Max(freezeDuration, duration);
        SetState(EntityState.Frozen);
    }
    
    // 受击闪烁效果
    protected virtual IEnumerator HitFlash()
    {
        if (spriteRenderer == null) yield break;
        
        Color flashColor = Color.red;
        spriteRenderer.color = flashColor;
        
        yield return new WaitForSeconds(0.1f);
        
        ApplyTint();
    }
    
    // 检查是否与指定实体在同一阵营
    public bool IsSameTeam(Entity other)
    {
        return team == other.team;
    }
    
    // 检查是否与指定实体敌对
    public bool IsEnemy(Entity other)
    {
        return team != other.team && team != 0 && other.team != 0;
    }
    
    // 获取生命值百分比
    public float GetHealthPercent()
    {
        return currentHealth / maxHealth;
    }
    
    // 设置无敌状态
    public virtual void SetInvulnerable(bool invulnerable)
    {
        isInvulnerable = invulnerable;
    }
    
    // 设置阵营
    public virtual void SetTeam(int newTeam)
    {
        team = newTeam;
    }
    
    // 武器系统方法
    public virtual void EquipWeapon(GameObject weaponObj)
    {
        // 移除当前武器
        // if (currentWeapon != null)
        // {
        //     currentWeapon.DestroyWeapon();
        //     currentWeapon = null;
        // }
        if(weaponObj==null) return;
        
       weaponObj.transform.SetParent(transform);
       weaponObj.transform.localPosition = Vector3.zero;
        weaponObj.transform.localScale = Vector3.one;
        if (weaponObj != null && weaponObj.TryGetComponent<WeaponBase>(out var weapon))
        {
            currentWeapon = weapon;
            currentWeapon.owner = this;
            Debug.Log($"[{entityName}] 装备武器: {weapon.WeaponName}");
        }
    }
    public virtual void EquipWeapon2(GameObject weaponObj)
    {
        // // 移除当前武器
        // if (secondWeapon != null)
        // {
        //     secondWeapon.DestroyWeapon();
        //     secondWeapon = null;
        // }
        //
        if(weaponObj==null) return;
        weaponObj.transform.SetParent(transform);
        weaponObj.transform.localPosition = Vector3.zero;
        weaponObj.transform.localScale = Vector3.one;
        if (weaponObj != null && weaponObj.TryGetComponent<WeaponBase>(out var weapon))
        {
            secondWeapon = weapon;
            secondWeapon.owner = this;
            Debug.Log($"[{entityName}] 装备武器: {weapon.WeaponName}");
        }
    }
//
    public virtual bool FireWeapon(Vector2 target)
    {
        Fire.Invoke();
        if (currentWeapon != null)
        {
            return currentWeapon.TryFire(target);
        }
        else
        {
            Debug.LogWarning($"[{entityName}] 没有装备武器");
            return false;
        }
    }

    public virtual bool FireWeapon2(Vector2 target)
    {
        
        if (secondWeapon != null)
        {
            return secondWeapon.TryFire(target);
        }
        else
        {
            Debug.LogWarning($"[{entityName}] 没有装备副武器");
            return false;
        }
    }
    public virtual void ReloadWeapon()
    {
        if (currentWeapon != null)
        {
            currentWeapon.Reload();
        }
    }
    
    
    
    public virtual WeaponBase GetCurrentWeapon()
    {
        return currentWeapon;
    }
    
    public virtual string GetWeaponInfo()
    {
        if (currentWeapon != null)
        {
            return currentWeapon.GetWeaponInfo();
        }
        return "未装备武器";
    }
    
  

    // Unity 2D碰撞事件（不主动改速度/位置，交给物理引擎处理）
    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 可保留日志或完全留空
        // Debug.Log($"[{entityName}] 2D碰撞进入: {collision.gameObject.name}");
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        // 不做速度/位置修正，避免抖动，由刚体与材质(摩擦/弹性)决定行为
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // Debug.Log($"[{entityName}] 2D碰撞离开: {collision.gameObject.name}");
    }
}

using UnityEngine;

public class MeleeWeapon : WeaponBase
{
    [Header("近战武器特有属性")]
    [SerializeField] private float attackRange = 2f;           // 攻击范围
    [SerializeField] private float attackAngle = 90f;          // 攻击角度范围
    [SerializeField] private float slashDuration = 0.5f;       // 刀光持续时间
    [SerializeField] private float slashSpeed = 180f;          // 刀光旋转速度（度/秒）
    [SerializeField] private int slashCount = 3;               // 刀光数量
    [SerializeField] private float slashSpread = 15f;          // 刀光之间的角度间隔
    
    protected override void Start()
    {
        base.Start();
        weaponName = "MeleeWeapon";
        weaponId = 3; // 近战武器ID
    }
    
    protected override bool Fire(Vector2 target)
    {
        if (projectilePrefab == null)
        {
            Debug.LogError($"[{weaponName}] 缺少刀光预制体！请在Inspector中设置Projectile Prefab");
            return false;
        }
        
        if (owner == null)
        {
            Debug.LogError($"[{weaponName}] 武器未正确初始化！Owner为null");
            return false;
        }
        
        // 计算攻击方向（从敌人位置指向目标）
        Vector2 attackPosition = owner.transform.position;
        Vector2 direction = (target - attackPosition).normalized;
        
        if (direction == Vector2.zero)
        {
            direction = transform.right;
        }
        
        // 创建多个刀光（位置与敌人相同，作为敌人子物体，仅旋转不位移）
        for (int i = 0; i < slashCount; i++)
        {
            float angleOffset = (i - (slashCount - 1) * 0.5f) * slashSpread;
            Vector2 slashDirection = Quaternion.Euler(0, 0, angleOffset) * direction;
            
            // 先生成刀光，然后设为敌人子物体
            GameObject slash = Instantiate(projectilePrefab, owner.transform.position, Quaternion.identity);
            slash.transform.SetParent(owner.transform);
            slash.transform.localPosition = Vector3.zero; // 确保与敌人完全重合
            slash.GetComponent<SlashProjectile>().team=owner.Team;
            if (slash.TryGetComponent<SlashProjectile>(out var slashProjectile))
            {
                // 让刀光仅旋转（半径为0或由预制体脚本控制为不平移）
                slashProjectile.Initialize(owner.Team, damage, slashDirection, slashDuration, slashSpeed, attackAngle);
            }
        }
        
        if (audioSource != null && fireSound != null)
        {
            audioSource.PlayOneShot(fireSound);
        }
        
        return true;
    }
    
    public void SetAttackParameters(float range, float angle, float duration, float speed, int count, float spread)
    {
        attackRange = range;
        attackAngle = angle;
        slashDuration = duration;
        slashSpeed = speed;
        slashCount = count;
        slashSpread = spread;
    }
    
    public float GetAttackRange()
    {
        return attackRange;
    }
} 
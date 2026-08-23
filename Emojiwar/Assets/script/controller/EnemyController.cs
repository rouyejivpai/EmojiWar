using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//
public class EnemyController : MonoBehaviour
{
    [Header("实体引用")]
    public Entity entity;
    [Header("移动设置")]
    public float moveSpeed = 3f;              // 移动速度
    
    private Transform playerTransform;         // 玩家Transform

    
    private Rigidbody2D rb;                   // 刚体组件
    [Header("战斗设置")] 
    public float fireDistance = 10f;      //开火距离
    
    
    void Start()
    {
        // 获取刚体组件
        rb = GetComponent<Rigidbody2D>();
        
        // 获取玩家引用
        if (EnemyManager.Instance != null && EnemyManager.Instance.playertarget != null)
        {
            playerTransform = EnemyManager.Instance.playertarget.transform;
        }
        else
        {
            Debug.LogWarning($"[{name}] 无法获取玩家引用！");
        }
        
        // 设置刚体属性
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.drag = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
        
        entity.GetCurrentWeapon().owner=entity;
    }
    
    void FixedUpdate()
    {
        if (playerTransform == null) return;
        
        // 计算到玩家的方向
        Vector2 directionToPlayer = (playerTransform.position - transform.position).normalized;
        //计算到玩家的距离
        float distance = Vector2.Distance(transform.position, playerTransform.position);
        // 移动向玩家
        if (rb != null)
        {
            rb.velocity = directionToPlayer * moveSpeed;
        }

        if (distance < fireDistance)
        {
            entity.FireWeapon(playerTransform.position);
        }
        
       
    }
}

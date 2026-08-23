using UnityEngine;

// 将此脚本挂在需要检查的对象（例如玩家/实体）上
// 运行后在控制台输出刚体/碰撞器/Layer 碰撞矩阵等关键信息
public class Physics2DValidator : MonoBehaviour
{
    [Header("检查范围 (米)")]
    public float probeRadius = 2f;

    [Header("可选修复(仅日志提示，不自动修改)")]
    public bool suggestFixes = true;

    private Rigidbody2D rb;
    private Collider2D[] colliders;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        colliders = GetComponents<Collider2D>();
    }

    private void Start()
    {
        Debug.Log($"[P2D] ===== 开始检查: {name} =====");

        // 刚体检查
        if (rb == null)
        {
            Debug.LogError("[P2D] 缺少 Rigidbody2D：至少一方需要刚体2D，推荐此对象使用 Dynamic。");
        }
        else
        {
            Debug.Log($"[P2D] Rigidbody2D: bodyType={rb.bodyType}, simulated={rb.simulated}, gravityScale={rb.gravityScale}, collisionDetection={rb.collisionDetectionMode}, interpolation={rb.interpolation}");
            Debug.Log($"[P2D] Rigidbody2D: constraints={rb.constraints}, useFullKinematicContacts={rb.useFullKinematicContacts}");
            if (suggestFixes)
            {
                if (rb.bodyType != RigidbodyType2D.Dynamic)
                    Debug.LogWarning("[P2D] 建议 bodyType=Dynamic（纯刚体解算，最稳定）");
                if (Mathf.Abs(rb.gravityScale) > 0f)
                    Debug.LogWarning("[P2D] 建议 gravityScale=0（平面射击无重力）");
                if (rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous)
                    Debug.LogWarning("[P2D] 建议 collisionDetection=Continuous（快速移动不穿透）");
                if (rb.interpolation != RigidbodyInterpolation2D.Interpolate)
                    Debug.LogWarning("[P2D] 建议 interpolation=Interpolate（减少抖动）");
                if ((rb.constraints & RigidbodyConstraints2D.FreezeRotation) == 0)
                    Debug.LogWarning("[P2D] 建议 FreezeRotation（2D角色避免旋转）");
            }
        }

        // 碰撞器检查
        if (colliders == null || colliders.Length == 0)
        {
            Debug.LogError("[P2D] 未找到 Collider2D：双方都需要 Collider2D，且应关闭 IsTrigger 才能产生碰撞");
        }
        else
        {
            foreach (var c in colliders)
            {
                Debug.Log($"[P2D] Collider2D: type={c.GetType().Name}, enabled={c.enabled}, isTrigger={c.isTrigger}, boundsSize={c.bounds.size}");
                if (suggestFixes && c.isTrigger)
                    Debug.LogWarning("[P2D] 建议关闭 IsTrigger（否则只有触发器事件，不会产生碰撞反应）");
            }
        }

        // Layer 与矩阵
        string selfLayerName = LayerMask.LayerToName(gameObject.layer);
        Debug.Log($"[P2D] 自身 Layer: {gameObject.layer} ({selfLayerName})");

        // 探测附近潜在碰撞对象
        var near = Physics2D.OverlapCircleAll(transform.position, probeRadius);
        Debug.Log($"[P2D] 附近碰撞体数量: {near.Length} (半径 {probeRadius})");
        foreach (var n in near)
        {
            if (n == null || n.gameObject == gameObject) continue;
            string nLayer = LayerMask.LayerToName(n.gameObject.layer);
            bool ignored = Physics2D.GetIgnoreLayerCollision(gameObject.layer, n.gameObject.layer);
            Debug.Log($"[P2D] 邻近: {n.gameObject.name}, layer={n.gameObject.layer}({nLayer}), isTrigger={n.isTrigger}, ignoredByMatrix={ignored}");
        }

        Debug.Log($"[P2D] ===== 检查结束: {name} =====");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, probeRadius);
    }
} 
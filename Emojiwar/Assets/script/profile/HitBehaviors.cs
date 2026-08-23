using System;
using UnityEngine;

// 命中行为接口与实现集合：
// - 在 profile.hitBehaviors 中以列表形式组合使用
// - 每次命中按顺序调用；返回 true 表示本次命中后结束（通常销毁），返回 false 表示继续存在

public interface IHitBehavior
{
    // 参数说明：
    // - self: 当前飞行物 profile
    // - target: 被命中的实体（可能为 null）
    // - normal: 碰撞法线（触发器路径下可能为 Vector2.zero）
    // 返回值：true 表示已处理并结束本次命中（通常销毁弹体）；false 表示弹体继续存在
    bool Apply(profile self, Entity target, Vector2 normal);
}

[Serializable]
public class DestroyOnHitBehavior : IHitBehavior
{
    // 命中即销毁
    public bool Apply(profile self, Entity target, Vector2 normal)
    {
        self.DestroyProjectile();
        return true;
    }
}

[Serializable]
public class PierceBehavior : IHitBehavior
{
    // 可穿透剩余次数；大于 0 时本次命中不销毁，仅计数减一
    public int remaining;
    public bool Apply(profile self, Entity target, Vector2 normal)
    {
        if (remaining > 0)
        {
            remaining--;
            return false;
        }
        self.DestroyProjectile();
        return true;
    }
}

[Serializable]
public class BounceBehavior : IHitBehavior
{
    // 剩余反弹次数
    public int remaining = 1;
    // 反弹后速度乘性系数（1 为保持速度）
    public float bounciness = 1f;
    public bool Apply(profile self, Entity target, Vector2 normal)
    {
        if (remaining <= 0) return false;
        var rb = self.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // 触发器路径下 normal 可能为 0；此时使用默认向上法线
            Vector2 n = normal == Vector2.zero ? Vector2.up : normal;
            rb.velocity = Vector2.Reflect(rb.velocity, n) * bounciness;
            if (rb.velocity.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(rb.velocity.y, rb.velocity.x) * Mathf.Rad2Deg + 90f;
                self.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            }
        }
        remaining--;
        return false;
    }
}


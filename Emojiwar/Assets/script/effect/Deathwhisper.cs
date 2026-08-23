using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Deathwhisper : BuffBase, IOnDeath ,EntityBuff
{
    public Entity entity { get; set; }
    
    public Deathwhisper() : base(-1f, 1) // 永久buff，1层
    {
        Name = "Death Whisper";
    }
    
    public Deathwhisper(float duration, int maxStacks) : base(duration, maxStacks)
    {
        Name = "Death Whisper";
    }

    public override void OnApply(BuffTarget target)
    {
        base.OnApply(target);
        entity = target as Entity;
        // 死亡低语buff应用时的逻辑
    }

    public void OnDeath(DeathContext ctx)
    {
        if (ctx.victim == null) return;
        
        // 创建金币
        GameObject coin = itemManager.Instance.create(0, ctx.victim.Position);
        if (coin != null)
        {
            var coinComponent = coin.GetComponent<coin>();
            if (coinComponent != null)
            {
                coinComponent.coinValue = Random.Range(1, 4);
            }
        }
    }

    
}

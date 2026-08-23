using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class iceFreeze : BuffBase
{
    public profile target;
    public OnEnityEventHandler onHit;
    
    public iceFreeze(BuffTarget target, OnEnityEventHandler onHit=null)
    {
        this.target = target as profile;
        this.onHit = onHit;
        
        IsPermanent = true;
    }

    public override void OnApply(BuffTarget target)
    {
        this.target.OnHit += OnKill;
    }

    private void OnKill(Entity entity)
    {
        entity.buffSystem.AddBuff(new Freeze());
    }

    public override void OnRemove()
    {
        this.target.OnKill -= OnKill;
    }
    
}

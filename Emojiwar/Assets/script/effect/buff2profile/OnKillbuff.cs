using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public delegate void OnEnityEventHandler(Entity entity);
public class OnKillbuff : BuffBase
{
    public profile target;
    public OnEnityEventHandler onKill;
    
    public OnKillbuff(BuffTarget target, OnEnityEventHandler onKill=null)
    {
        this.target = target as profile;
        this.onKill = onKill;
        
        IsPermanent = true;
    }

    public override void OnApply(BuffTarget target)
    {
        this.target.OnKill += OnKill;
    }

    private void OnKill(Entity entity)
    {
        if (onKill != null)
        {
            onKill(entity);
        }
    }

    public override void OnRemove()
    {
        this.target.OnKill -= OnKill;
    }
    
}

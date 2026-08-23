using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class ScaleS : BuffBase
{
    public profile target;
    public OnEnityEventHandler onHit;
    
    public ScaleS(BuffTarget target, OnEnityEventHandler onHit=null)
    {
        this.target = target as profile;
        this.onHit = onHit;
        
        IsPermanent = true;
    }

    public override void OnApply(BuffTarget target)
    {
        this.target.damage *= 0.5f;
        this.target.gameObject.transform.localScale *= 0.5f;
    }

   

    public override void OnRemove()
    {
        this.target.damage *= 2f;
        this.target.gameObject.transform.localScale *= 2f;
    }
    
}

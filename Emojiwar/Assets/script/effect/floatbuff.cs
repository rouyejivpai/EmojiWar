using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class floatbuff : BuffBase
{
    private ffloat target;
    public float buff;
    
    public floatbuff(ffloat target, float buff)
    {
        this.target = target;
        this.buff = buff;
        IsPermanent = true;
    }

    public override void OnApply(BuffTarget target)
    {
        this.target.add += buff;
    }

    public override void OnRemove()
    {
        this.target.add -= buff;
    }
    public override void OnStack(int newStacks)
    {
        this.target.add += newStacks*buff;
    }
    public override void OnStack(float newStacks)
    {
        this.target.add += newStacks*buff;
    }
}

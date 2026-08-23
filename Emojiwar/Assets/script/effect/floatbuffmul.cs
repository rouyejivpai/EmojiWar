using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class floatbuffmul : BuffBase
{
    private ffloat target;
    public float buff;
    
    public floatbuffmul(ffloat target, float buff)
    {
        this.target = target;
        this.buff = buff;
        IsPermanent = true;
    }

    public override void OnApply(BuffTarget target)
    {
        this.target.mul += buff;
    }

    public override void OnRemove()
    {
        this.target.mul -= buff;
    }
    public override void OnStack(int newStacks)
    {
        this.target.mul += buff;
    }

}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KILLbuff : BuffBase
{
    private ffloat target;
    public float buff=0.2f;
    public int maxStacks=5;
    
    public KILLbuff(GameObject weapon, float buff)
    {
        this.MaxStacks = maxStacks;
        this.target = weapon.GetComponent<WeaponBase>().fireRate;
        this.buff = buff;
        IsPermanent = false;
        this.Duration = 5f;
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
        Stacks = Mathf.Min(newStacks+Stacks, MaxStacks);
        this.target.add += newStacks*buff;
    }
    public override void OnStack(float newStacks)
    {
        Stacks = Mathf.Min((int)(newStacks+Stacks), MaxStacks);
        this.target.add += newStacks*buff;
    }

}

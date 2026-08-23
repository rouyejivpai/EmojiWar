using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class rateup : floatbuff
{
   public WeaponBase Weapon { get; set; }
   private float rate = 3f;
   
   
   public rateup() : base(-1f, 3) // 10秒持续时间，最多3层
   {
      Name = "Rate Up";
   }
   
   public rateup(float duration, int maxStacks) : base(duration, maxStacks)
   {
      Name = "Rate Up";
   }
   
   public override void OnApply(BuffTarget target)
   {
      base.OnApply(target);
      
      Weapon = target as WeaponBase;
      Weapon.fireRate.add+= rate;
   }

   public override void OnRemove()
   {
      Weapon.fireRate.add-= rate;
      
      base.OnRemove();
   }
   
   public override void OnStack( int newStacks)
   {
      base.OnStack( newStacks);
      // 每层增加20%的射速
      
      
   }

   
}

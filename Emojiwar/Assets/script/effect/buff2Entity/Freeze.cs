using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Freeze : BuffBase
{

    Entity entity;
    private Color freezeColor = new Color(0.6f,0.6f,1f);
    public float buff= -0.1f;
    public Freeze() : base(-1f, 3) // 10秒持续时间，最多3层
   {
      
   }
   
   public Freeze(float duration, int maxStacks) : base(duration, maxStacks)
   {
     
   }
   
   public override void OnApply(BuffTarget target)
   {
      base.OnApply(target);
      
      entity = target as Entity;
      entity.MoveSpeed.add += buff;
      if (entity != null)
      {
         float intensity = Mathf.Clamp01(0.3f + 0.2f * (Stacks - 1));
         entity.SetTint("Freeze", freezeColor, intensity);
      }

    }

   public override void OnRemove()
   {
    
      if (entity != null) entity.RemoveTint("Freeze");
      entity.MoveSpeed.add -= Stacks * buff;
      base.OnRemove();
   }
   
   public override void OnStack( int newStacks)
   {
      base.OnStack( newStacks);
      entity.MoveSpeed.add += buff;
      if (entity != null)
      {
         float intensity = Mathf.Clamp01(0.3f + 0.2f * (Stacks - 1));
         entity.SetTint("Freeze", freezeColor, intensity);
      }
    }

   
}

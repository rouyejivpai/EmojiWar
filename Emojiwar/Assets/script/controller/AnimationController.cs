using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationController : MonoBehaviour
{
    public Entity entity;//
    public Animator animator;
    // Start is called before the first frame update
    private void OnEnable()
    {
      
        entity.Fire += FireAnimation;
     
    }

    private void OnDisable()
    {
      
        //GlobalEventManager.Instance.OnClick -= FireAnimation;
       
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    //触发射击动画
    public void FireAnimation(  )
    {
        animator.SetTrigger("Fire");
        
    }
}

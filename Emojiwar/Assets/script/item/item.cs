using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class item : MonoBehaviour
{
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.CompareTag("entity") )
        {
            Entity targetEntity = other.gameObject.GetComponent<Entity>();
            if (targetEntity != null && targetEntity.Team == 0)
            {
                //调用获取方法
                                   gain();
                                   //自毁
                                   Destroy(this.gameObject); 
            }
           
        }
        
    }

    public abstract void gain();
}

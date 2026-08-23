using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dun : WeaponBase
{
   
    protected Type type=Type.Dun;
    //盾物体
     [Header("盾特性")]
    public GameObject DUNObject;
    public float delay=0.1f;
    private float currentTime=0;
    

    private void Update()
    {
        currentTime -= Time.deltaTime;
        if (currentTime < 0)
        {
            DUNObject.SetActive(false);
        }
    }

    public override bool TryFire(Vector2 target)
    {
        Fire(target);
        return true;
    }
   
    protected override bool Fire(Vector2 target)
    {
       // Debug.Log("开盾！");
        currentTime = delay;
        // 计算射击方向（从射击点指向目标）
        Vector2 firePosition = firePoint.position;
        Vector2 direction = (target - firePosition).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        DUNObject.transform.rotation=Quaternion.Euler(0, 0, angle);

        if (DUNObject.activeSelf!=true)
        {
            DUNObject.SetActive(true);
        }
        return true;
    }

   
}

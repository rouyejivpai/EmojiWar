using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dun_1 : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        //如果是飞行物则销毁
        if (other.gameObject.CompareTag("profile"))
        {
            Destroy(other.gameObject);
        }
    }
}

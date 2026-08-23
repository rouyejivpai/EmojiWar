using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

public class DESTORY : MonoBehaviour
{
    // Start is called before the first frame update
    public void DES()
    {
        Destroy(gameObject);
        Debug.Log("销毁自身！！！！");
    }
}

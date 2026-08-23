using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class itemManager : MonoBehaviour
{
    private static itemManager _instance;
    public static itemManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<itemManager>();
            }
            return _instance;
        }
    }
    
    //物体预制体
    public List<GameObject> items = new List<GameObject>();

    public GameObject create(int i,Vector2 Postion)
    {
        GameObject item=Instantiate(items[i]);
        item.transform.position = Postion;
        return item;
    }

    public GameObject create(String name, Vector2 Postion)
    {
       GameObject item=Instantiate(items.Find(x => x.name == name)) ;
       item.transform.position = Postion;
       return item;
    }
}

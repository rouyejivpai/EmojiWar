using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;

public class GlobalEventManager : MonoBehaviour
{
    private static GlobalEventManager _instance;
    public static GlobalEventManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<GlobalEventManager>();
            }
            return _instance;
        }
    }
    public bool isActive = true;
    //事件
    public event Action<bool> OnClick;
    private void Awake()
    {
        _instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
       
    }

    // Update is called once per frame
    void Update()
    {
        if (isActive)
        {
              // 原来的调用方式
              Debug.Log("发射");
                        OnClick?.Invoke(Input.GetMouseButton(0));
        }
       
          
        
    }
}

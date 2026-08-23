using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class coin : item
{
    public int coinValue=1;
    
    public Rigidbody2D rb;
    public GameManager gameManager;
    public float magnet = 50f;//磁吸强度
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        gameManager = GameManager.Instance;
    }

    public override void gain()
    {
        GameManager.Instance.Coin += coinValue;
    }
    
    //磁吸效果启动
    private void Update()
    {
        if (gameManager.CoinMagnet)
        {
            Vector2 direction = (GameManager.Instance.player.transform.position - transform.position).normalized;
            float distance = Vector2.Distance(GameManager.Instance.player.transform.position, transform.position);
            //对距离作范围处理
            if (distance < 1f)distance=1f;
            if (distance > 5f)distance=5f;
            rb.velocity = direction * (magnet/distance);
        }    
    }
}

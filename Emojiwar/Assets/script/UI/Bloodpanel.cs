using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bloodpanel : MonoBehaviour
{
    [Header("UI References")]
    public GameObject blood;
    private float maxWidth;
    public Entity player;
    private void Awake()
    {
        maxWidth=blood.GetComponent<RectTransform>().sizeDelta.x;
        
    }

    private void OnEnable()
    {
        GameManager.Instance.OnPlayerReady += init;
    }

    private void init(GameObject Player)
    {
        player = Player.GetComponent<Entity>();
    }
    // Update is called once per frame
    void Update()
    {
         setBlood();

    }
    private void setBlood()
    {
        if (player==null)return;
            float progress = player.CurrentHealth / player.MaxHealth;
        RectTransform fillBar = this.blood.GetComponent<RectTransform>();
        Vector2 sizeDelta = fillBar.sizeDelta;
        sizeDelta.x = maxWidth * progress;
        fillBar.sizeDelta = sizeDelta;
    }
}

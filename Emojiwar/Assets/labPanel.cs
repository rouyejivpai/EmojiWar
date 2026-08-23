using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class labPanel : MonoBehaviour
{
    [Header("UI References")]
    public Text CoinText;

    private void Awake()
    {
        GameManager.Instance.LabPanel = this.gameObject;
    }

    public void UpdateCoin(int value)
    {
        CoinText.text = value.ToString();
    }
}

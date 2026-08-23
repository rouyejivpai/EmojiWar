using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class refreshButtun : MonoBehaviour
{
    public Text COST;
    public int cost=2;

    public void init()
    {
        cost = 2;
        COST.text = cost.ToString();
    }

    public void tryRefresh()
    {
        if (GameManager.Instance.Coin >= cost)
        {
            GameManager.Instance.Coin -= cost;
            StoreManager.instance.refresh();
            cost += 2;
            COST.text = cost.ToString();
        }
        
    }
}

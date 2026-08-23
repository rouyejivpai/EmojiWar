using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class good : MonoBehaviour
{
   public GameObject content;//内容物(
   public int cost=0;//价格
   [Header("UI引用")]
   public Image Image;//图片
   public Text name;//名字
   public Text desc;//描述
   public Text price;

   public Text type;
   //设置内容物
   public void SetContent(GameObject obj)
   {
      //删除原来的内容
      if (obj != null)
      {
        
            //Destroy(content.GetComponent<Obj>().inObj);
            Destroy(content);
      }
      //放入新物体
      content = obj;
      cost = rollPirce();
      content.transform.SetParent(Image.transform);
      content.GetComponent<Obj>().isActive = false;
      updateContentUI();
   }

   private void Start()
   {
      updateContentUI();
   }

   //更新内容显示
   private void updateContentUI()
   {
      if (content != null&&content.GetComponent<Obj>().inObj!=null)
      {
         Obj obj = content.GetComponent<Obj>();
         switch (obj.type)
         {
            case ObjType.mod:
            {
               type.text = obj.info.type;
               content.transform.SetParent(Image.transform);
                              content.transform.localPosition = Vector3.zero;
                              content.transform.localScale = Vector3.one;
                              name.text = content.GetComponent<Obj>().info.name;
                              desc.text = content.GetComponent<Obj>().info.description;
                              price.text = cost.ToString();
               break;
            }
            case ObjType.relic:
            {
               break;
            }
            case ObjType.weapon:
            {
               type.text = obj.info.type;
               content.transform.SetParent(Image.transform);
               content.transform.localPosition = Vector3.zero;
               content.transform.localScale = Vector3.one;
               name.text = content.GetComponent<Obj>().info.name;
               desc.text = content.GetComponent<Obj>().info.description;
               price.text = cost.ToString();
               break;
            }
         }
         
      }
      else
      {
         name.text = "空";
         desc.text = "";
         price.text = "";
      }
      
   }

   public void tryBuy()
   {
      //如果玩家余额足够且content存在
      if (GameManager.Instance.Coin >= cost && content != null)
      {
         Buy();
      }
      else
      {
         //买不起别碰！
      }
   }
   public void Buy()
   {
      Debug.Log("购买");
      GameManager.Instance.Coin -= cost;
      content.GetComponent<Obj>().isActive = true;
      //解除拖拽功能禁用
      content.GetComponent<SnapDrag>().enabled = true;
      GameManager.Instance.Addpack(content);
      content = null;
      updateContentUI();
   }
//随机生成价格
   private int rollPirce()
   {
    return  Random.Range(5,50);
   }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BUFF : MonoBehaviour
{
   
   //
   // public List<BuffBase> buffs = new List<BuffBase>();
   // private Entity _entity;
   //
   // private void OnEnable()
   // {
   //    //监听死亡事件
   //    _entity = GetComponent<Entity>();
   //    if (_entity != null)
   //    {
   //       _entity.OnDeath += HandleEntityDeath;
   //    }
   // }
   //
   // private void OnDisable()
   // {
   //    if (_entity != null)
   //    {
   //       _entity.OnDeath -= HandleEntityDeath;
   //    }
   // }
   //
   // private void Update()
   // {
   //    if (buffs == null || buffs.Count == 0) return;
   //    float dt = Time.deltaTime;
   //    for (int i = buffs.Count - 1; i >= 0; i--)
   //    {
   //       var b = buffs[i];
   //       if (b == null)
   //       {
   //          buffs.RemoveAt(i);
   //          continue;
   //       }
   //       b.OnTick(dt);
   //       if (b.duration > 0f)
   //       {
   //          b.duration -= dt;
   //          if (b.duration <= 0f)
   //          {
   //             RemoveBuff(b);
   //          }
   //       }
   //    }
   // }
   //
   // private void HandleEntityDeath(Entity victim)
   // {
   //    var ctx = new DeathContext { victim = victim, killer = null };
   //    for (int i = 0; i < buffs.Count; i++)
   //    {
   //       var b = buffs[i];
   //       if (b is IOnDeath onDeath)
   //       {
   //          onDeath.OnDeath(ctx);
   //       }
   //    }
   // }
   //
   //
   //
   // //buff添加方法
   // //添加默认版本的buff
   // public T AddBuff<T>(string name="") where T : BuffBase
   // {
   //    //根据类型获取实例化组件
   //    T buff=this.gameObject.AddComponent<T>();
   //    //执行buff初始化
   //    buff.init(gameObject);
   //    buffs.Add(buff);
   //    buff.OnApply();
   //    return buff;
   // }
   //
   // public void AddBuff(GameObject buff)
   // {
   //    BuffBase bu=buff.GetComponent<BuffBase>();
   //    bu.init(gameObject);
   //    buffs.Add(bu);
   // }
   // public void RemoveBuff(BuffBase buff)
   // {
   //    if (buff == null) return;
   //    buff.Remove();
   //    buff.OnRemove();
   //    buffs.Remove(buff);
   //    Destroy(buff);
   // }
}

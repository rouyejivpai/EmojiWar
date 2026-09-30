//------------------------------------------------------------
// EmojiWar GameMain - GameFramework 对象池封装的 UI 列表项复用器
//
// 范式准则：所有"同类动态列表"（房间玩家列表、加入房间列表、商店/卡片等）
// 统一经本类获取/回收 UI 项，由 GameFramework ObjectPool
// （UnityGameFramework.Runtime.ObjectPoolComponent，随 GameFramework.prefab 注册）
// 托管空闲对象：spawn 激活、unspawn 隐藏、release 销毁，避免反复 Instantiate/Destroy。
//
// 用法：
//   var pool = GfUiItemPool.Create("RoomPlayerCells");          // 每个列表一个池名
//   var item = pool.Acquire(() => Instantiate(prefab), parent); // 优先复用池中空闲项
//   ... 填充 item 内容 ...
//   pool.Recycle(item);                                          // 用完回收
//   pool.Destroy();                                              // 窗体关闭时释放整池
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.ObjectPool;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// GF 对象池中的 UI 项对象（Target = GameObject）。
    /// 生命周期钩子：Spawn → SetActive(true)；Unspawn → SetActive(false)；Release → Destroy。
    /// </summary>
    public sealed class GfUiItemObject : ObjectBase
    {
        public GfUiItemObject()
        {
        }

        /// <summary>绑定目标 GameObject（内部调用 ObjectBase.Initialize 注册名称/目标）。</summary>
        public void Bind(GameObject target)
        {
            if (target == null)
            {
                return;
            }
            base.Initialize(target.name, target);
        }

        // 注意：ObjectBase 在 GameFramework.dll（另一程序集）中声明为 "protected internal virtual"，
        // 跨程序集覆盖时 internal 部分不可见，只能写 "protected override"（等效可访问性一致）。
        protected override void OnSpawn()
        {
            var go = Target as GameObject;
            if (go != null)
            {
                go.SetActive(true);
            }
        }

        protected override void OnUnspawn()
        {
            var go = Target as GameObject;
            if (go != null)
            {
                go.SetActive(false);
            }
        }

        protected override void Release(bool isShutdown)
        {
            var go = Target as GameObject;
            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
            }
        }
    }

    /// <summary>GameFramework ObjectPool 的 UI 列表项池封装（获取/回收/整池释放）。</summary>
    public sealed class GfUiItemPool
    {
        private const int DefaultCapacity = 64;
        private const float IdleExpireSeconds = 600f;   // 空闲超过 10 分钟才允许 GF 自动释放（防误删复用项）

        private static ObjectPoolComponent s_PoolComponent = null;

        private readonly string m_Name;
        private readonly IObjectPool<GfUiItemObject> m_Pool;
        private readonly List<GfUiItemObject> m_Active = new List<GfUiItemObject>();
        private readonly Dictionary<GameObject, GfUiItemObject> m_ByGo = new Dictionary<GameObject, GfUiItemObject>();
        private Transform m_RecycleHolder = null;   // 隐藏回收挂点：空闲项移出列表/窗体层级，无残留

        /// <summary>全局隐藏回收根（非 UI、无渲染、DontDestroyOnLoad）：空闲项统一挂这里。</summary>
        private static Transform s_GlobalHiddenRoot = null;

        /// <summary>
        /// 全局隐藏挂点（非 UI、无渲染、DontDestroyOnLoad）：
        /// 池空闲项、列表 Cell 作者态模板（UiListCell 取用后）统一挂这里 —— 容器与窗体层级都不留残留。
        /// </summary>
        public static Transform GlobalHiddenRoot
        {
            get
            {
                if (s_GlobalHiddenRoot == null)
                {
                    var go = new GameObject("GfItemPoolHiddenRoot");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.SetActive(true);
                    s_GlobalHiddenRoot = go.transform;
                }
                return s_GlobalHiddenRoot;
            }
        }

        private GfUiItemPool(string name, IObjectPool<GfUiItemObject> pool)
        {
            m_Name = name;
            m_Pool = pool;
        }

        /// <summary>框架对象池组件（懒获取；RoomForm 等运行时使用，此时框架已初始化）。</summary>
        public static ObjectPoolComponent PoolComponent
        {
            get
            {
                if (s_PoolComponent == null)
                {
                    s_PoolComponent = UnityGameFramework.Runtime.GameEntry.GetComponent<ObjectPoolComponent>();
                }
                return s_PoolComponent;
            }
        }

        /// <summary>创建/获取（同名单实例）列表项池。</summary>
        public static GfUiItemPool Create(string name, int capacity = DefaultCapacity)
        {
            var comp = PoolComponent;
            if (comp == null)
            {
                Debug.LogWarning("[GfUiItemPool] ObjectPoolComponent not found, pool '" + name + "' disabled.");
                return null;
            }

            IObjectPool<GfUiItemObject> pool = null;
            if (comp.HasObjectPool<GfUiItemObject>(name))
            {
                pool = comp.GetObjectPool<GfUiItemObject>(name);
            }
            else
            {
                pool = comp.CreateSingleSpawnObjectPool<GfUiItemObject>(name, capacity, IdleExpireSeconds, 0);
            }

            return pool != null ? new GfUiItemPool(name, pool) : null;
        }

        public string Name
        {
            get { return m_Name; }
        }

        /// <summary>当前正在使用的项数。</summary>
        public int ActiveCount
        {
            get { return m_Active.Count; }
        }

        /// <summary>设置隐藏回收挂点：回收的空闲项会移到该节点下并隐藏（保持列表容器内无残留）。</summary>
        public void SetRecycleHolder(Transform holder)
        {
            m_RecycleHolder = holder;
        }

        /// <summary>池内对象总数（含空闲/使用中）。</summary>
        public int TotalCount
        {
            get { return m_Pool != null ? m_Pool.Count : 0; }
        }

        /// <summary>
        /// 获取一个 UI 项：优先复用池中空闲实例；无空闲时调用 factory 新建并注册入池。
        /// 返回的 GameObject 处于激活状态（挂在 parent 下）。
        /// </summary>
        public GameObject Acquire(Func<GameObject> factory, Transform parent)
        {
            if (m_Pool == null)
            {
                return factory != null ? factory() : null;
            }

            // 1) 优先复用池中空闲实例
            if (m_Pool.CanSpawn())
            {
                var pooled = m_Pool.Spawn();
                if (pooled != null)
                {
                    var go = pooled.Target as GameObject;
                    if (go != null)
                    {
                        go.transform.SetParent(parent, false);
                        go.SetActive(true);
                        m_Active.Add(pooled);
                        m_ByGo[go] = pooled;
                        return go;
                    }

                    // 目标已丢失（例如曾被销毁）：把该空壳对象退回池
                    try
                    {
                        m_Pool.Unspawn(pooled);
                    }
                    catch
                    {
                    }
                }
            }

            // 2) 无空闲：工厂新建并注册
            if (factory == null)
            {
                return null;
            }
            var created = factory();
            if (created == null)
            {
                return null;
            }
            created.transform.SetParent(parent, false);

            var instance = new GfUiItemObject();
            instance.Bind(created);
            m_Pool.Register(instance, true);
            m_Active.Add(instance);
            m_ByGo[created] = instance;
            created.SetActive(true);
            return created;
        }

        /// <summary>该对象是否为本池跟踪的活动项（清空容器时区分"池内行"与"作者态模板/残留"）。</summary>
        public bool IsTracked(GameObject go)
        {
            return go != null && m_ByGo.ContainsKey(go);
        }

        /// <summary>回收一个 UI 项（回池隐藏并移出列表容器，避免列表节点残留；下次获取复用）。</summary>
        public void Recycle(GameObject go)
        {
            if (go == null || m_Pool == null)
            {
                return;
            }

            GfUiItemObject obj;
            if (m_ByGo.TryGetValue(go, out obj))
            {
                m_ByGo.Remove(go);
                m_Active.Remove(obj);

                // 规范：空闲项不得留在列表/窗体层级 —— 移到隐藏挂点（未指定时用全局隐藏根），
                // 保证容器与窗体下都无残留
                var holder = m_RecycleHolder != null ? m_RecycleHolder : GlobalHiddenRoot;
                go.transform.SetParent(holder, false);
                go.SetActive(false);

                try
                {
                    m_Pool.Unspawn(obj);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[GfUiItemPool] unspawn '" + m_Name + "' fail: " + e.Message);
                }
            }
            else
            {
                // 不在池中：直接隐藏销毁
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>回收全部已获取项（空闲项保留在池中供复用）。</summary>
        public void RecycleAll()
        {
            if (m_Active == null || m_Active.Count == 0)
            {
                return;
            }
            var snapshot = new List<GfUiItemObject>(m_Active);
            foreach (var obj in snapshot)
            {
                var go = obj != null ? obj.Target as GameObject : null;
                if (go != null)
                {
                    Recycle(go);
                }
            }
        }

        /// <summary>整池释放：回收全部项并销毁池（GameObject 由对象 Release 统一销毁）。</summary>
        public void Destroy()
        {
            RecycleAll();
            if (m_Pool != null && PoolComponent != null)
            {
                try
                {
                    PoolComponent.DestroyObjectPool<GfUiItemObject>(m_Name);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[GfUiItemPool] destroy pool '" + m_Name + "' fail: " + e.Message);
                }
            }
            m_ByGo.Clear();
            m_Active.Clear();
        }
    }
}

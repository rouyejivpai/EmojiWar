using System;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.Core
{
    /// <summary>
    /// 服务定位器 - 管理所有系统服务的注册和获取
    /// Unity没有内置的依赖注入容器，所以这个需要保留
    /// </summary>
    public class ServiceLocator : MonoBehaviour
    {
        private static ServiceLocator _instance;
        public static ServiceLocator Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ServiceLocator");
                    _instance = go.AddComponent<ServiceLocator>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private Dictionary<Type, object> _services = new Dictionary<Type, object>();
        
        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }
        
        /// <summary>
        /// 注册服务
        /// </summary>
        public void RegisterService<T>(T service) where T : class
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                Debug.LogWarning($"[ServiceLocator] 服务 {type.Name} 已存在，将被覆盖");
            }
            _services[type] = service;
        }
        
        /// <summary>
        /// 获取服务
        /// </summary>
        public T GetService<T>() where T : class
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return service as T;
            }
            
            Debug.LogError($"[ServiceLocator] 未找到服务 {type.Name}");
            return null;
        }
        
        /// <summary>
        /// 检查服务是否存在
        /// </summary>
        public bool HasService<T>() where T : class
        {
            return _services.ContainsKey(typeof(T));
        }
        
        /// <summary>
        /// 移除服务
        /// </summary>
        public void UnregisterService<T>() where T : class
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services.Remove(type);
            }
        }
        
        /// <summary>
        /// 清理所有服务
        /// </summary>
        public void Clear()
        {
            _services.Clear();
        }
    }
}
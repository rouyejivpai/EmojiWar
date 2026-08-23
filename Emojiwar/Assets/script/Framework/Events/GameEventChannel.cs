using UnityEngine;
using UnityEngine.Events;

namespace Framework.Events
{
    /// <summary>
    /// 基于UnityEvent的游戏事件通道 - 避免重复造轮子
    /// </summary>
    [CreateAssetMenu(fileName = "GameEventChannel", menuName = "Framework/Events/GameEventChannel")]
    public class GameEventChannel : ScriptableObject
    {
        [Header("事件设置")]
        [SerializeField] private bool enableLogging = true;
        
        public UnityEvent OnEventRaised;
        
        public void Raise()
        {
            if (enableLogging)
            {
                Debug.Log($"[GameEventChannel] 事件触发: {name}");
            }
            
            OnEventRaised?.Invoke();
        }
        
        public void AddListener(UnityAction listener)
        {
            OnEventRaised.AddListener(listener);
        }
        
        public void RemoveListener(UnityAction listener)
        {
            OnEventRaised.RemoveListener(listener);
        }
    }
    
    /// <summary>
    /// 带参数的游戏事件通道
    /// </summary>
    [CreateAssetMenu(fileName = "GameEventChannelWithData", menuName = "Framework/Events/GameEventChannelWithData")]
    public class GameEventChannel<T> : ScriptableObject
    {
        [Header("事件设置")]
        [SerializeField] private bool enableLogging = true;
        
        public UnityEvent<T> OnEventRaised;
        
        public void Raise(T data)
        {
            if (enableLogging)
            {
                Debug.Log($"[GameEventChannel] 事件触发: {name}, 数据: {data}");
            }
            
            OnEventRaised?.Invoke(data);
        }
        
        public void AddListener(UnityAction<T> listener)
        {
            OnEventRaised.AddListener(listener);
        }
        
        public void RemoveListener(UnityAction<T> listener)
        {
            OnEventRaised.RemoveListener(listener);
        }
    }
}
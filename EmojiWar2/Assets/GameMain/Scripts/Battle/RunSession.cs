//------------------------------------------------------------
// EmojiWar GameMain - 局内会话数据（Run Session）
// 存储本局：金币、背包（武器/Mod 列表）、波次等。
// 使用框架 DataNode 或独立单例；此处用 GameEntry 挂载组件。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmojiWar.GameMain.Battle
{
    /// <summary>
    /// 局内会话数据：单机肉鸽循环的状态容器。
    /// 后续网络化：由服务器权威维护，客户端只读镜像。
    /// </summary>
    public class RunSession : MonoBehaviour
    {
        private static RunSession s_Instance = null;

        public static RunSession Instance
        {
            get { return s_Instance; }
        }

        [Header("局内状态")]
        [SerializeField]
        private int m_Coin = 0;

        [SerializeField]
        private int m_CharacterId = 1;

        [SerializeField]
        private int m_WaveIndex = 0;

        // 背包：已获得的 Mod ID 列表
        private readonly List<int> m_ModBag = new List<int>();

        public int Coin { get { return m_Coin; } }
        public int CharacterId { get { return m_CharacterId; } }
        public int WaveIndex { get { return m_WaveIndex; } }
        public IReadOnlyList<int> ModBag { get { return m_ModBag; } }

        /// <summary>金币变化事件。</summary>
        public event Action<int> OnCoinChanged;

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 开始新一局。
        /// </summary>
        public void StartRun(int characterId)
        {
            m_CharacterId = characterId;
            m_Coin = 100;
            m_WaveIndex = 0;
            m_ModBag.Clear();
            OnCoinChanged?.Invoke(m_Coin);
        }

        /// <summary>
        /// 增加金币。
        /// </summary>
        public void AddCoin(int amount)
        {
            m_Coin = Mathf.Max(0, m_Coin + amount);
            OnCoinChanged?.Invoke(m_Coin);
        }

        /// <summary>
        /// 尝试花费金币。
        /// </summary>
        public bool TrySpendCoin(int amount)
        {
            if (amount < 0 || m_Coin < amount)
            {
                return false;
            }
            m_Coin -= amount;
            OnCoinChanged?.Invoke(m_Coin);
            return true;
        }

        /// <summary>
        /// 记录波次推进。
        /// </summary>
        public void AdvanceWave(int waveIndex)
        {
            m_WaveIndex = waveIndex;
        }

        /// <summary>
        /// 背包添加 Mod。
        /// </summary>
        public void AddModToBag(int modId)
        {
            m_ModBag.Add(modId);
        }

        /// <summary>
        /// 背包是否已有指定 Mod。
        /// </summary>
        public bool HasMod(int modId)
        {
            return m_ModBag.Contains(modId);
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}

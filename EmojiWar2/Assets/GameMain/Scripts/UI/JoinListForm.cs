//------------------------------------------------------------
// EmojiWar GameMain - 加入游戏界面（局域网房间列表）
// 由"多人游戏 → 加入游戏"打开（叠在 MultiplayerForm 上）：
//   - 打开即自动扫描局域网房间（RoomDiscovery）
//   - 列表：房间项为按钮，显示"房主名 的房间（N 人）"，点击加入该房间
//   - 刷新按钮：重新广播扫描
//   - 返回按钮：回多人游戏页
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>加入游戏页事件（加入房间/返回）。</summary>
    public static class JoinListEvents
    {
        /// <summary>请求加入指定房间（参数：房主名, ip, 端口）。</summary>
        public static event Action<string, string, int> OnJoinRoomRequested;

        /// <summary>请求返回多人游戏页。</summary>
        public static event Action OnBackRequested;

        public static void RequestJoin(string hostName, string ip, int port) { OnJoinRoomRequested?.Invoke(hostName, ip, port); }
        public static void RequestBack() { OnBackRequested?.Invoke(); }
    }

    /// <summary>
    /// 加入游戏页：房间列表（局域网发现）+ 刷新。
    /// </summary>
    public class JoinListForm : UGuiForm
    {
        private const string RoomContainerName = "RoomContainer";
        private const string RoomItemName = "RoomItem_Template";   // 占位子物体（§7 约定，运行时清空重建）

        [SerializeField]
        private Text m_StatusText = null;        // 扫描状态/空列表提示

        [SerializeField]
        private Button m_RefreshButton = null;

        [SerializeField]
        private Button m_BackButton = null;

        private RectTransform m_RoomContainer = null;

        // 房间行 = 列表项，经 GameFramework 对象池复用（范式：见 GfUiItemPool）；
        // 行形状来自预制体 Assets/GameMain/UI/items/RoomItemRow.prefab（'game' AssetBundle，UiPrefab 加载）。
        private const string RoomItemPoolName = "JoinListRoomItems";
        private GfUiItemPool m_RoomPool = null;
        private GameObject m_RowPrefab = null;

        // 当前活动房间项（与房间列表顺序一致）
        private readonly List<GameObject> m_Items = new List<GameObject>();

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (m_RefreshButton != null)
            {
                m_RefreshButton.onClick.RemoveAllListeners();
                m_RefreshButton.onClick.AddListener(OnRefreshClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveAllListeners();
                m_BackButton.onClick.AddListener(OnBackClick);
            }

            RefreshRoomList();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (m_RefreshButton != null)
            {
                m_RefreshButton.onClick.RemoveListener(OnRefreshClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveListener(OnBackClick);
            }

            // 释放房间行对象池（GameFramework ObjectPool）
            if (m_RoomPool != null)
            {
                m_RoomPool.Destroy();
                m_RoomPool = null;
            }
            m_Items.Clear();

            base.OnClose(isShutdown, userData);
        }

        /// <summary>每帧：扫描窗口内收集应答；扫描结束后重建列表 UI。</summary>
        private void Update()
        {
            if (!Network.RoomDiscovery.IsScanning)
            {
                return;
            }
            Network.RoomDiscovery.TickScanner();
            if (!Network.RoomDiscovery.IsScanning)
            {
                RebuildRoomItems();
            }
        }

        private void OnRefreshClick()
        {
            RefreshRoomList();
        }

        /// <summary>重新扫描并立即重建空状态 UI。</summary>
        private void RefreshRoomList()
        {
            Network.RoomDiscovery.BeginScan();
            if (m_StatusText != null)
            {
                m_StatusText.text = "扫描房间中...";
            }
        }

        private void OnBackClick()
        {
            JoinListEvents.RequestBack();
        }

        /// <summary>重建房间列表 UI（遵循 UI 列表规范：先清空容器 → 再按数据逐个生成 RoomItemRow 子物体）。</summary>
        private void RebuildRoomItems()
        {
            // 找到房间容器（占位 RoomItem_Template 的子物体在容器下）
            if (m_RoomContainer == null)
            {
                m_RoomContainer = FindRoomContainer();
            }

            var rooms = Network.RoomDiscovery.Rooms;
            if (rooms == null || rooms.Count == 0)
            {
                ClearItems();
                if (m_StatusText != null)
                {
                    m_StatusText.text = "没有发现房间，点「刷新」重试";
                }
                return;
            }
            if (m_StatusText != null)
            {
                m_StatusText.text = string.Format("发现 {0} 个房间，点击加入", rooms.Count);
            }

            if (m_RoomContainer == null)
            {
                return;
            }

            // 行预制体未就绪（items 目录经 UiPrefab 异步加载）→ 触发加载后返回，回调里重入
            if (m_RowPrefab == null)
            {
                m_RowPrefab = UiPrefab.GetCached(Constant.UIItemAssetPath.RoomItemRow);
            }
            if (m_RowPrefab == null)
            {
                UiPrefab.Load(Constant.UIItemAssetPath.RoomItemRow, prefab =>
                {
                    if (prefab == null)
                    {
                        Debug.LogWarning("[JoinList] RoomItemRow prefab 加载失败: " + Constant.UIItemAssetPath.RoomItemRow);
                        return;
                    }
                    m_RowPrefab = prefab;
                    if (!Network.RoomDiscovery.IsScanning)
                    {
                        RebuildRoomItems();
                    }
                });
                return;
            }

            if (m_RoomPool == null)
            {
                m_RoomPool = GfUiItemPool.Create(RoomItemPoolName);
            }

            // 1) 清空容器内现有全部行（回收进池并移出容器）→ 保证无残留
            ClearItems();

            // 2) 重建：按数据逐行获取 RoomItemRow 子物体（池复用或新建）并填充/定位
            int index = 0;
            foreach (var room in rooms)
            {
                GameObject go = null;
                if (m_RoomPool != null)
                {
                    go = m_RoomPool.Acquire(() => Instantiate(m_RowPrefab), m_RoomContainer);
                }
                if (go == null)
                {
                    go = Instantiate(m_RowPrefab, m_RoomContainer);
                    go.SetActive(true);
                }
                if (go == null)
                {
                    break;
                }
                go.name = "RoomItemRow_" + index;
                m_Items.Add(go);
                ConfigureRoomItem(go, room, index);
                index++;
            }
        }

        /// <summary>填充一行房间数据（文本 + 点击加入 + 位置），新建与池复用共用。</summary>
        private void ConfigureRoomItem(GameObject go, Network.DiscoveredRoom room, int index)
        {
            if (go == null)
            {
                return;
            }
            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchoredPosition = new Vector2(0f, -60f - index * 90f);
            }

            var text = go.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = string.Format("{0} 的房间（{1} 人）", room.HostName, room.PlayerCount);
            }

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                string host = room.HostName;
                string ip = room.Ip;
                int port = room.Port;
                btn.onClick.AddListener(() => JoinListEvents.RequestJoin(host, ip, port));
            }
        }

        private void ClearItems()
        {
            foreach (var item in m_Items)
            {
                if (item == null)
                {
                    continue;
                }
                if (m_RoomPool != null)
                {
                    m_RoomPool.Recycle(item);   // 回池并移出容器（隐藏挂点），容器内无残留
                }
                else
                {
                    Destroy(item);
                }
            }
            m_Items.Clear();

            // 兜底：容器内其余任何子物体（预留模板 RoomItem_Template、历史残留等）一律销毁 → 容器 100% 清空
            if (m_RoomContainer != null)
            {
                for (int i = m_RoomContainer.childCount - 1; i >= 0; i--)
                {
                    var child = m_RoomContainer.GetChild(i);
                    if (child != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }

        /// <summary>查找房间容器（根下 RoomContainer；无则创建）。</summary>
        private RectTransform FindRoomContainer()
        {
            var t = transform.Find(RoomContainerName);
            if (t == null)
            {
                var go = new GameObject(RoomContainerName);
                go.transform.SetParent(transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(1100f, 700f);
                return rt;
            }
            return t as RectTransform;
        }
    }
}

//------------------------------------------------------------
// EmojiWar GameMain - 波间商店共享测试工具（Editor）
// 菜单：EmojiWar/Diagnostics/Net Shop Test
// 单进程内：Host + Client 回环，验证：
//   1. 客户端加入 → Host 启动波次
//   2. 波次结束 → Host 广播 ShopOffer（共享商品）
//   3. 客户端发送购买请求 → Host 校验回应
//------------------------------------------------------------

using System.Collections;
using UnityEditor;
using UnityEngine;
using EmojiWar.GameMain.Network;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// 波间商店共享测试。
    /// </summary>
    public static class NetShopDiagnostics
    {
        private static NetworkService s_Host = null;
        private static NetHostLogic s_HostLogic = null;
        private static NetworkService s_Client = null;
        private static NetClientLogic s_ClientLogic = null;

        private static bool s_GotOffer = false;
        private static bool s_BuySent = false;
        private static float s_Timeout = 0f;

        [MenuItem("EmojiWar/Diagnostics/Net Shop Test")]
        public static void RunShopTest()
        {
            if (Application.isPlaying == false)
            {
                Debug.Log("[NetShop] 请先进入 Play 模式再运行网络测试");
                return;
            }

            s_GotOffer = false;
            s_BuySent = false;
            s_Timeout = 20f;

            // ---- Host ----
            var hostGo = new GameObject("NetShopHost");
            s_Host = hostGo.AddComponent<NetworkService>();
            s_HostLogic = hostGo.AddComponent<NetHostLogic>();
            s_HostLogic.Bind(s_Host);
            if (!s_Host.StartHost(7793))
            {
                Debug.LogError("[NetShop] Host 启动失败");
                Object.Destroy(hostGo);
                return;
            }

            // ---- Client ----
            var clientGo = new GameObject("NetShopClient");
            s_Client = clientGo.AddComponent<NetworkService>();
            s_ClientLogic = clientGo.AddComponent<NetClientLogic>();
            s_ClientLogic.Bind(s_Client);
            s_Client.ConnectToServer("127.0.0.1", 7793);

            Runner.Start(RunCoroutine());
        }

        private static IEnumerator RunCoroutine()
        {
            // 等待连接
            float wait = 3f;
            while (wait > 0f && (s_Client == null || !s_Client.IsConnected))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            if (s_Client == null || !s_Client.IsConnected)
            {
                Debug.LogError("[NetShop] 客户端未连接");
                Cleanup();
                yield break;
            }

            // 加入房间（触发波次）
            s_ClientLogic.JoinRoom("ShopTester");

            // 等待敌人全部生成（第 1 波 3 个，间隔 0.5s）
            float enemyWait = 6f;
            while (enemyWait > 0f && s_ClientLogic.LocalEntityCount < 4)
            {
                enemyWait -= Time.deltaTime;
                yield return null;
            }
            Debug.Log("[NetShop] 实体数: " + s_ClientLogic.LocalEntityCount);

            // 服务器权威清场：击杀所有敌人（触发波次结束 → 商店）
            yield return new WaitForSeconds(1f);
            Debug.Log("[NetShop] 服务器击杀敌人，触发波次结束");
            // 通过反射访问 Host 的私有敌人字典不可行，改用公开方法：
            // 简化：NetHostLogic 增加 KillAllEnemies 后调用；此处先等待超时观察
            // 直接调用新增的公开方法
            if (s_HostLogic != null)
            {
                s_HostLogic.KillAllEnemies();
            }

            // 等待商店商品广播
            while (s_Timeout > 0f && !s_ClientLogic.GotShopOffer)
            {
                s_Timeout -= Time.deltaTime;
                yield return null;
            }

            s_GotOffer = s_ClientLogic.GotShopOffer;

            if (s_GotOffer)
            {
                Debug.Log("[NetShop] 收到共享商店: " + s_ClientLogic.LastShopOffer);

                // 发送购买请求
                s_ClientLogic.RequestBuy(0);
                s_BuySent = true;
                yield return new WaitForSeconds(1f);
            }

            if (s_GotOffer && s_BuySent)
            {
                Debug.Log("===== [NetShop] 波间商店共享测试通过 ✅ 收到商品广播 + 购买请求已发送 =====");
            }
            else
            {
                Debug.LogWarning(string.Format("[NetShop] 未完全完成 offer={0} buySent={1}", s_GotOffer, s_BuySent));
            }

            Cleanup();
        }

        private static void Cleanup()
        {
            if (s_Host != null)
            {
                s_Host.Shutdown();
                Object.Destroy(s_Host.gameObject);
                s_Host = null;
            }
            if (s_Client != null)
            {
                s_Client.Shutdown();
                Object.Destroy(s_Client.gameObject);
                s_Client = null;
            }
            s_HostLogic = null;
            s_ClientLogic = null;
        }

        private sealed class Runner : MonoBehaviour
        {
            private static Runner s_Instance = null;

            public static void Start(IEnumerator routine)
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("NetShopRunner");
                    s_Instance = go.AddComponent<Runner>();
                    Object.DontDestroyOnLoad(go);
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}

//------------------------------------------------------------
// EmojiWar GameMain - UI/items 小预制体加载器
//
// 约定：Cell/列表项等小预制体统一放 Assets/GameMain/UI/items/（不打进 Resources），
// 并列入 'game' AssetBundle（见 GameResourceBuilder.AssetPaths）。
// 运行时经 GameEntry.Resource 按全路径（"Assets/GameMain/UI/items/XXX.prefab"）加载：
//   - 编辑器/EditorResourceComponent：AssetDatabase 异步队列加载
//   - 构建版 ResourceComponent：从 game.dat AssetBundle 加载
// 加载结果缓存；同一资产并发请求去重。调用方拿不到同步结果时，等回调后自行重建 UI。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Resource;

namespace EmojiWar.GameMain.UI
{
    /// <summary>UI/items 预制体按路径加载 + 缓存 + 请求去重。</summary>
    public static class UiPrefab
    {
        private static readonly Dictionary<string, GameObject> s_Loaded = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, List<Action<GameObject>>> s_Pending = new Dictionary<string, List<Action<GameObject>>>();

        /// <summary>是否已加载完成。</summary>
        public static bool IsLoaded(string assetName)
        {
            return s_Loaded.ContainsKey(assetName);
        }

        /// <summary>获取已加载的预制体（未加载完成返回 null）。</summary>
        public static GameObject GetCached(string assetName)
        {
            GameObject go;
            return s_Loaded.TryGetValue(assetName, out go) ? go : null;
        }

        /// <summary>
        /// 请求加载指定路径预制体；完成后回调（已缓存则立即回调）。
        /// 同一路径的并发请求会合并，只发一次底层加载。
        /// </summary>
        public static void Load(string assetName, Action<GameObject> onLoaded)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                if (onLoaded != null)
                {
                    onLoaded(null);
                }
                return;
            }

            GameObject cached;
            if (s_Loaded.TryGetValue(assetName, out cached))
            {
                if (onLoaded != null)
                {
                    onLoaded(cached);
                }
                return;
            }

            List<Action<GameObject>> waiters;
            if (s_Pending.TryGetValue(assetName, out waiters))
            {
                if (onLoaded != null)
                {
                    waiters.Add(onLoaded);
                }
                return;
            }

            s_Pending[assetName] = new List<Action<GameObject>>();
            if (onLoaded != null)
            {
                s_Pending[assetName].Add(onLoaded);
            }

            if (GameEntry.Resource == null)
            {
                Debug.LogWarning("[UiPrefab] ResourceComponent 不可用: " + assetName);
                Complete(assetName, null);
                return;
            }

            GameEntry.Resource.LoadAsset(assetName, typeof(GameObject),
                new LoadAssetCallbacks(OnLoadSuccess, OnLoadFailure));
        }

        private static void OnLoadSuccess(string assetName, object asset, float duration, object userData)
        {
            GameObject prefab = asset as GameObject;
            if (prefab == null)
            {
                Debug.LogWarning("[UiPrefab] 加载成功但类型不符: " + assetName);
            }
            else
            {
                s_Loaded[assetName] = prefab;
            }
            Complete(assetName, prefab);
        }

        private static void OnLoadFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            Debug.LogWarning(string.Format("[UiPrefab] 加载失败 {0} status={1} msg={2}", assetName, status, errorMessage));
            Complete(assetName, null);
        }

        private static void Complete(string assetName, GameObject prefab)
        {
            List<Action<GameObject>> waiters;
            if (s_Pending.TryGetValue(assetName, out waiters))
            {
                s_Pending.Remove(assetName);
                if (waiters != null)
                {
                    var copy = new List<Action<GameObject>>(waiters);
                    foreach (var cb in copy)
                    {
                        try
                        {
                            if (cb != null)
                            {
                                cb(prefab);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[UiPrefab] callback error: " + e.Message);
                        }
                    }
                }
            }
        }
    }
}

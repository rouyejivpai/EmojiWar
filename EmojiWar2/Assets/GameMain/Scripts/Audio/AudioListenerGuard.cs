//------------------------------------------------------------
// EmojiWar GameMain - 音频监听器守卫（AudioListenerGuard）
//
// 问题：`Menu.unity` 与 `Battle.unity` **各自带一个 AudioListener**，而战斗采用
//   **叠加加载/场景常驻**架构（见 ProcedureBattle.OnEnter 注释："场景常驻——已加载则复用"），
//   于是两个场景同时存在时会刷屏告警：
//     "There are 2 audio listeners in the scene. Please ensure there is always exactly one audio listener in the scene."
//
// 策略：常驻一份"唯一监听器"，其余全部禁用；并在**每次场景加载完成后复查一次**。
//   · 只在**已加载场景的根物体**里找（不用 Resources.FindObjectsOfTypeAll，避免误伤预制体资产）；
//   · 保留优先级：GameEntry 自己那份（DontDestroyOnLoad，跨场景天然唯一）> 其它；
//   · 默认把常驻那份挂在 GameEntry 下，避免"菜单场景被卸载后没有监听器"的静音问题。
//
// 确定性：纯表现层，不参与模拟/网络（设计 §7「表现不进 sim」）。
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameFramework.Event;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Audio
{
    /// <summary>保证全场始终只有一个启用的 AudioListener。</summary>
    public sealed class AudioListenerGuard : MonoBehaviour
    {
        private static AudioListenerGuard s_Instance = null;
        private readonly List<GameObject> m_Roots = new List<GameObject>(32);
        private readonly List<AudioListener> m_Found = new List<AudioListener>(8);

        /// <summary>被禁用的多余监听器数量（探针/回归用）。</summary>
        public static int DisabledCount { get; private set; }

        /// <summary>由 GameEntry 调用一次（常驻）。</summary>
        public static void Ensure(GameObject owner)
        {
            if (s_Instance != null) { return; }
            if (owner == null) { return; }

            var go = new GameObject("AudioListenerGuard");
            go.transform.SetParent(owner.transform, false);
            s_Instance = go.AddComponent<AudioListenerGuard>();
        }

        private void Awake()
        {
            // 常驻监听器：跨场景唯一，菜单卸载后也不会静音
            if (GetComponent<AudioListener>() == null)
            {
                var listener = gameObject.AddComponent<AudioListener>();
                listener.enabled = true;
            }

            // 两条订阅：GameFramework 事件（与其它模块一致）+ Unity 原生 sceneLoaded（更早、更可靠）
            GameEntry.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
            Enforce();
            WriteProbe();

            // 启动阶段补几次复查：场景里的 listener 可能在若干帧之后才随场景加载出现
            StartCoroutine(RecheckLater());
        }

        private System.Collections.IEnumerator RecheckLater()
        {
            float[] waits = { 0.5f, 2f, 5f, 10f };
            for (int i = 0; i < waits.Length; i++)
            {
                yield return new WaitForSecondsRealtime(waits[i]);
                Enforce();
            }
            WriteProbe();
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Enforce();
            WriteProbe();
        }

        private void OnDestroy()
        {
            GameEntry.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            s_Instance = null;
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            // 场景加载完成后再复查一次（新场景可能又带进来一个 listener）
            Enforce();
            WriteProbe();
        }

        /// <summary>保留**自己这一份**，其余全部禁用。</summary>
        private void Enforce()
        {
            var keep = GetComponent<AudioListener>();
            int disabled = 0;

            m_Found.Clear();
            CollectListeners(m_Found);

            for (int i = 0; i < m_Found.Count; i++)
            {
                var listener = m_Found[i];
                if (listener == null) { continue; }
                bool isKeep = listener == keep;
                if (listener.enabled != isKeep)
                {
                    listener.enabled = isKeep;
                    if (!isKeep) { disabled++; }
                }
            }

            DisabledCount = disabled;
        }

        /// <summary>
        /// 收集所有 AudioListener：**先放进自己这份**（最可靠，直接持有引用），
        /// 再扫已加载场景的根物体（含非激活子物体）。
        /// 只扫场景根、不用 Resources.FindObjectsOfTypeAll —— 后者会把预制体资产也算进来。
        /// </summary>
        private void CollectListeners(List<AudioListener> results)
        {
            var keep = GetComponent<AudioListener>();
            if (keep != null) { results.Add(keep); }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) { continue; }

                m_Roots.Clear();
                scene.GetRootGameObjects(m_Roots);
                for (int r = 0; r < m_Roots.Count; r++)
                {
                    var root = m_Roots[r];
                    if (root == null) { continue; }
                    root.GetComponentsInChildren(true, results);
                }
            }
        }

        private void WriteProbe()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var keep = GetComponent<AudioListener>();
                System.IO.File.AppendAllText(path, string.Format(
                    "[audio] listener guard: disabled={0} total={1} scenes={2} keepEnabled={3}\n",
                    DisabledCount, m_Found.Count, SceneManager.sceneCount,
                    keep != null && keep.enabled));
            }
            catch
            {
            }
        }
    }
}

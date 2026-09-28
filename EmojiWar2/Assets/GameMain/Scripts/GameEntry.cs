//------------------------------------------------------------
// EmojiWar GameMain - 业务入口
// 挂载于启动场景中的 GameFramework 对象或独立对象上，
// 负责在运行时装配各业务组件引用（框架组件由 GameFramework.prefab 提供）。
//
// 注意：必须晚于 GameFramework 组件注册执行。
// GameFramework 的 GameFrameworkComponent 在 Awake 中注册自身，
// 因此本组件的初始化放在 Start() 中（所有 Awake 之后），
// 并用 DefaultExecutionOrder 确保执行顺序靠后。
//------------------------------------------------------------

using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain
{
    /// <summary>
    /// 游戏业务入口：持有框架各组件的静态引用，供业务代码访问。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class GameEntry : MonoBehaviour
    {
        private static GameEntry s_Instance = null;

        public static GameEntry Instance
        {
            get
            {
                return s_Instance;
            }
        }

        // ---- 框架组件引用（由 GameFramework.prefab 注册）----
        public static BaseComponent Base { get; private set; }
        public static ProcedureComponent Procedure { get; private set; }
        public static UIComponent UI { get; private set; }
        public static EventComponent Event { get; private set; }
        public static DataTableComponent DataTable { get; private set; }
        public static SceneComponent Scene { get; private set; }
        public static ResourceComponent Resource { get; private set; }
        public static NetworkComponent Network { get; private set; }
        public static DataNodeComponent DataNode { get; private set; }
        public static ConfigComponent Config { get; private set; }
        public static SettingComponent Setting { get; private set; }
        public static SoundComponent Sound { get; private set; }

        // ---- 业务组件引用（由 GameEntry 自身创建/挂载）----
        public static Data.DataComponent Data { get; private set; }
        public static Network.NetworkService NetworkService { get; private set; }
        public static Simulation.SimView SimView { get; private set; }
        public static UI.BackpackHotkey BackpackHotkey { get; private set; }

        private void Awake()
        {
            if (s_Instance != null)
            {
                Log.Warning("GameEntry instance already exists, destroy duplicate.");
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 在 Start 中获取框架组件引用（此时所有框架组件的 Awake 已执行并完成注册）。
        /// </summary>
        private void Start()
        {
            InitFrameworkComponents();

            // 帧率配置：覆盖 GameFramework BaseComponent 默认 30fps → 60fps
            // （BaseComponent 在 Awake 时设置了 Application.targetFrameRate=30，
            //   这是"卡顿"的根因；这里在框架初始化后显式覆盖）
            // vSync 保持 QualitySettings 设置（开），60Hz 显示器下即为流畅的 60fps
            Application.targetFrameRate = 60;
        }

        /// <summary>
        /// 获取框架组件引用。
        /// </summary>
        private void InitFrameworkComponents()
        {
            Base = UnityGameFramework.Runtime.GameEntry.GetComponent<BaseComponent>();
            Procedure = UnityGameFramework.Runtime.GameEntry.GetComponent<ProcedureComponent>();
            UI = UnityGameFramework.Runtime.GameEntry.GetComponent<UIComponent>();
            Event = UnityGameFramework.Runtime.GameEntry.GetComponent<EventComponent>();
            DataTable = UnityGameFramework.Runtime.GameEntry.GetComponent<DataTableComponent>();
            Scene = UnityGameFramework.Runtime.GameEntry.GetComponent<SceneComponent>();
            Resource = UnityGameFramework.Runtime.GameEntry.GetComponent<ResourceComponent>();
            Network = UnityGameFramework.Runtime.GameEntry.GetComponent<NetworkComponent>();
            DataNode = UnityGameFramework.Runtime.GameEntry.GetComponent<DataNodeComponent>();
            Config = UnityGameFramework.Runtime.GameEntry.GetComponent<ConfigComponent>();
            Setting = UnityGameFramework.Runtime.GameEntry.GetComponent<SettingComponent>();
            Sound = UnityGameFramework.Runtime.GameEntry.GetComponent<SoundComponent>();

            // 初始化业务组件
            if (Data == null)
            {
                var dataGo = new GameObject("DataComponent");
                dataGo.transform.SetParent(transform);
                Data = dataGo.AddComponent<Data.DataComponent>();
            }

            if (NetworkService == null)
            {
                var netGo = new GameObject("NetworkService");
                netGo.transform.SetParent(transform);
                NetworkService = netGo.AddComponent<Network.NetworkService>();
            }

            // 确定性模拟表现层（常驻；由网络逻辑绑定模拟）
            if (SimView == null)
            {
                var viewGo = new GameObject("SimView");
                viewGo.transform.SetParent(transform);
                SimView = viewGo.AddComponent<Simulation.SimView>();
            }

            // 背包热键（Tab；仅战斗流程内生效）
            if (BackpackHotkey == null)
            {
                var hotkeyGo = new GameObject("BackpackHotkey");
                hotkeyGo.transform.SetParent(transform);
                BackpackHotkey = hotkeyGo.AddComponent<UI.BackpackHotkey>();
            }

            // 物品系统（P3）：服务 + 标准容器 + 拖拽管理器（常驻）
            ItemSystem.EnsureCreated();
            // 注：初始装备的发放放在 ProcedureLaunch（ConfigService 就绪之后），
            // 否则此处 GameEntry.Data 还没初始化，GetItem 取不到 → 发不出去。
            EmojiWar.GameMain.UI.UiDragManager.Ensure(gameObject);   // 注意：GameEntry.UI 是 UIComponent 属性，这里必须写全名

            // 初始化音效管理器
            Audio.SfxManager.Init();
            // 音频监听器守卫：Menu/Battle 场景各带一个 listener + 叠加加载架构 → 必须保证全场只有一个
            Audio.AudioListenerGuard.Ensure(gameObject);

            // 自动化联调辅助（-autocreate 启动参数）
            AutoPlay.TryStart();

            // ★ 退出时收尾：DeterminismTracer / ReplayRecorder 的 BinaryWriter 以前只在
            //   ResetRoom/HandleRunRestart 被 dispose → 退出后文件句柄泄漏（实测停止 Play 后仍被占用，
            //   os error 32），且中途退出会丢尾部检查点（报告 B2）。
            Application.quitting += OnApplicationQuitting;

            // 诊断：框架/场景实例计数（排查重复 GameFramework/场景叠加）
            try
            {
                var procs = Object.FindObjectsOfType<ProcedureComponent>();
                var uis = Object.FindObjectsOfType<UIComponent>();
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    names.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
                }
                WriteProbe(string.Format("[gameentry] ProcedureComponents={0} UIComponents={1} scenes={2} ({3})",
                    procs.Length, uis.Length, names.Count, string.Join(",", names)));
            }
            catch
            {
            }

            Log.Info("GameEntry initialized. Procedure={0}, UI={1}, DataTable={2}, Scene={3}, Data={4}",
                Procedure != null, UI != null, DataTable != null, Scene != null, Data != null);
        }

        /// <summary>退出时收尾：flush 打点与录像（修文件句柄泄漏 + 防丢尾部检查点）。</summary>
        private static void OnApplicationQuitting()
        {
            try
            {
                EmojiWar.GameMain.Simulation.DeterminismTracer.End();
                EmojiWar.GameMain.Simulation.ReplayRecorder.End();
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[GameEntry] 退出收尾异常: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        /// <summary>运行时探针。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}

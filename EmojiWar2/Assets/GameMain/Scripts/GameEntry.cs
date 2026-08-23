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

            Log.Info("GameEntry initialized. Procedure={0}, UI={1}, DataTable={2}, Scene={3}",
                Procedure != null, UI != null, DataTable != null, Scene != null);
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

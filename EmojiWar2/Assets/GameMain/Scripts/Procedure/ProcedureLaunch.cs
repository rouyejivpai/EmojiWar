//------------------------------------------------------------
// EmojiWar GameMain - 启动流程
// 首个流程：校验框架组件就绪，加载数据表，随后进入主菜单。
// 构建模式（非编辑器）下必须先调用 ResourceComponent.InitResources
// 初始化 AssetBundle 资源（读取 version list + 加载资源包），
// 否则所有资源加载都会返回 NotExist。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 启动流程：框架装配完成后执行的第一个流程。
    /// </summary>
    public class ProcedureLaunch : ProcedureBase
    {
        private IFsm<IProcedureManager> m_ProcedureFsm = null;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Log.Info("===== EmojiWar Launch =====");

            m_ProcedureFsm = procedureOwner;

            // 校验核心组件
            if (GameEntry.Procedure == null)
            {
                Log.Error("ProcedureComponent is null, check GameFramework prefab setup.");
                return;
            }

            // 构建模式（非编辑器）需要先初始化资源；编辑器模式（EditorResourceMode）资源立即可用
            var baseComponent = GameEntry.Base;
            bool editorMode = baseComponent != null && baseComponent.EditorResourceMode;
            if (!editorMode)
            {
                if (GameEntry.Resource == null)
                {
                    Log.Error("ResourceComponent is null, cannot init resources.");
                    return;
                }

                Log.Info("[ProcedureLaunch] 构建模式：初始化资源（读取 version list + 加载 AssetBundle）...");
                GameEntry.Resource.InitResources(OnInitResourcesComplete);
            }
            else
            {
                OnInitResourcesComplete();
            }
        }

        /// <summary>资源初始化完成回调：加载数据表并进入主菜单。</summary>
        private void OnInitResourcesComplete()
        {
            Log.Info("[ProcedureLaunch] 资源初始化完成，加载数据表...");
            WriteProbe("[launch] resources init complete");

            // 初始化数据组件（加载 Resources/Data/** 下的 SO 配置：角色/武器/Mod/选角展示/战斗参数）
            if (GameEntry.Data != null)
            {
                GameEntry.Data.Init();
            }

            // 配置服务：建索引 + 启动校验 + 计算配置版本哈希（联机一致性用）
            Data.ConfigService.Rebuild();

            // 初始装备：必须在**配置就绪之后、任何 SimPlayerConfig 构建之前**发放
            // （建房时 AddPlayer → SimConfigFactory.Build 就会编译双手施法程序）。
            // 放到 GameEntry 初始化里会因 GameEntry.Data 尚未就绪而发不出去。
            ItemSystem.GrantStartingLoadout();

            // 进入主菜单流程
            ChangeState<ProcedureMenu>(m_ProcedureFsm);
        }

        /// <summary>运行时探针（验证构建版流程走到哪一步）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            m_ProcedureFsm = null;
            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}

//------------------------------------------------------------
// EmojiWar GameMain - 启动流程
// 首个流程：校验框架组件就绪，初始化基础配置后进入主菜单。
//------------------------------------------------------------

using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.Procedure
{
    /// <summary>
    /// 启动流程：框架装配完成后执行的第一个流程。
    /// </summary>
    public class ProcedureLaunch : ProcedureBase
    {
        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Log.Info("===== EmojiWar Launch =====");

            // 校验核心组件
            if (GameEntry.Procedure == null)
            {
                Log.Error("ProcedureComponent is null, check GameFramework prefab setup.");
                return;
            }

            // 初始化全局配置 / 数据表（后续 Phase 接入 DataTable 时启用）
            // InitConfig();
            // InitDataTables();

            // 进入主菜单流程
            ChangeState<ProcedureMenu>(procedureOwner);
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}

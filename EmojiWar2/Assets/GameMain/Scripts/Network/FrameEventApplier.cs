//------------------------------------------------------------
// EmojiWar GameMain - 帧事件应用器（网络层的薄封装，W-16）
//
// 真正的应用逻辑在 `Simulation/FrameEvent.cs` 的 `SimFrameEvents.Apply` —— 那里回放器也用同一份。
// 这一层只负责：**打探针** + 用调用方的标签（"host"/"client"）区分两端日志。
//------------------------------------------------------------

using UnityEngine;
using LockstepSim = EmojiWar.GameMain.Simulation.LockstepSimulation;

namespace EmojiWar.GameMain.Network
{
    /// <summary>把随帧携带的确定性事件应用到模拟（W-16），并写一条 `[frame-event]` 探针。</summary>
    public static class FrameEventApplier
    {
        public static bool Apply(LockstepSim sim, in Simulation.FrameEvent e, string tag)
        {
            string note;
            bool ok = Simulation.SimFrameEvents.Apply(sim, e, out note);
            WriteProbe(tag, "帧 " + (sim != null ? sim.FrameIndex.ToString() : "?") + " "
                + (ok ? "" : "⚠ ") + note);
            return ok;
        }

        private static void WriteProbe(string tag, string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.File.AppendAllText(path, "[frame-event] " + tag + " " + message + "\n");
            }
            catch (System.Exception) { /* 探针失败不影响逻辑 */ }
        }
    }
}

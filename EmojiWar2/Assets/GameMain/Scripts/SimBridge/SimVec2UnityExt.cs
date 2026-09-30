//------------------------------------------------------------
// EmojiWar GameMain - Sim → Unity 的**唯一**类型转换点（W-04）
//
// W-04 把模拟层拆成不引用 UnityEngine 的程序集（`EmojiWar.Sim`），
// 于是模拟层的向量是 `Simulation.SimVec2`，而表现层（SimView 等）用的是 `UnityEngine.Vector2/Vector3`。
// 两个世界之间**必须显式转换**，而且只能有一个转换点 —— 否则"哪里转的、有没有精度损失"
// 会散落在各处（本项目在别的方面已经吃过"两份实现各自漂移"的亏）。
//
// ★ 放在 `SimBridge/`（GameMain 程序集）而不是 `Simulation/`：
//   本文件引用 UnityEngine，**不能**进 Sim 程序集，否则 `noEngineReferences` 编译不过。
//   命名空间仍然用 `Simulation`，这样 SimView 之类同命名空间的代码可以直接调用扩展方法。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-04] SimVec2 ↔ UnityEngine.Vector2/3 的转换（表现层唯一转换点）。</summary>
    public static class SimVec2UnityExt
    {
        /// <summary>模拟层坐标 → 表现层 Vector2。</summary>
        public static Vector2 ToVector2(this SimVec2 v)
        {
            return new Vector2(v.x, v.y);
        }

        /// <summary>模拟层坐标 → 表现层 Vector3（z = 0，2D 项目）。</summary>
        public static Vector3 ToVector3(this SimVec2 v)
        {
            return new Vector3(v.x, v.y, 0f);
        }
    }
}

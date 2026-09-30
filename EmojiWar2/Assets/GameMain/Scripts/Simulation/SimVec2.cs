//------------------------------------------------------------
// EmojiWar Sim - 模拟层自有的二维向量（W-04）
//
// 为什么不用 `UnityEngine.Vector2`：
//   G1 要求模拟层程序集 **不引用任何 UnityEngine 模块**（asmdef 的 `noEngineReferences`），
//   这样才能编译期挡住"模拟层偷偷用引擎的 Time/Random/GameObject/表现数据"这类问题。
//   `Vector2` 是引擎类型，所以模拟层要有自己的向量。
//
// ★ 名字刻意与 `UnityEngine.Vector2` 保持一致（`x/y`、`zero/right/up/one`、`+ - * /`），
//   这样"把模拟层的 Vector2 换成 SimVec2"是**纯机械替换**，不需要改任何表达式形状 ——
//   而表达式形状正是 W-11 反复强调的"跨编译器一致性的前提"（单次 IEEE 运算 vs 可收缩的复合表达式）。
//
// ★ 数值语义：逐分量、单次 IEEE-754 运算（与 Unity 的 Vector2 运算符逐位相同）。
//   **不要**在这里加 `normalized`/`sqrMagnitude` 之类的"复合"方法 —— 复合运算必须走
//   `SimMath`（乘积先显式舍入再相加，阻止编译器收缩成 FMA，见 SimMath.cs 的证据）。
//------------------------------------------------------------

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-04] 模拟层自有的二维向量（逐分量、单次 IEEE 运算，跨编译器逐位一致）。</summary>
    public struct SimVec2
    {
        public float x;
        public float y;

        public SimVec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        // ---- 与 UnityEngine.Vector2 同名的静态量（机械替换后语义不变）----
        public static SimVec2 zero { get { return new SimVec2(0f, 0f); } }
        public static SimVec2 one { get { return new SimVec2(1f, 1f); } }
        public static SimVec2 right { get { return new SimVec2(1f, 0f); } }
        public static SimVec2 up { get { return new SimVec2(0f, 1f); } }

        // ---- 运算符：逐分量，每个分量只做一次 IEEE 运算 ----
        public static SimVec2 operator +(SimVec2 a, SimVec2 b)
        {
            return new SimVec2(a.x + b.x, a.y + b.y);
        }

        public static SimVec2 operator -(SimVec2 a, SimVec2 b)
        {
            return new SimVec2(a.x - b.x, a.y - b.y);
        }

        public static SimVec2 operator -(SimVec2 a)
        {
            return new SimVec2(-a.x, -a.y);
        }

        public static SimVec2 operator *(SimVec2 a, float s)
        {
            return new SimVec2(a.x * s, a.y * s);
        }

        public static SimVec2 operator *(float s, SimVec2 a)
        {
            return new SimVec2(a.x * s, a.y * s);
        }

        public static SimVec2 operator /(SimVec2 a, float s)
        {
            return new SimVec2(a.x / s, a.y / s);
        }

        public static bool operator ==(SimVec2 a, SimVec2 b) { return a.x == b.x && a.y == b.y; }
        public static bool operator !=(SimVec2 a, SimVec2 b) { return !(a == b); }

        public override bool Equals(object obj) { return obj is SimVec2 && this == (SimVec2)obj; }
        public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2); }

        /// <summary>仅用于日志/探针（<b>不进状态哈希</b>；哈希走 `LockstepSimulation.MixV2` 的位模式）。</summary>
        public override string ToString()
        {
            return "(" + x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + ", " + y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}

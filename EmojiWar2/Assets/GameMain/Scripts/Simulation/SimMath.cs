//------------------------------------------------------------
// EmojiWar GameMain - 确定性浮点原语（W-11）
//
// ★ 这一层为什么存在（有实测证据，不是理论洁癖）
//
// 2026-09-29 的 W-11 跨后端对拍（`tools/verify_crossbackend.ps1`）发现：
//   **同一份录像，Mono 与 IL2CPP 从第 4 帧起逐帧哈希就不同** —— 而两个 IL2CPP 构建
//   （Development / Release）彼此完全一致。逐段 + 逐字段定位后，唯一不同的值是
//   `CastRuntimeState.DelayCarry`（累积"不足 1 帧的延迟余量"）：
//
//     Mono   carry = 0xBC5A741C
//     IL2CPP carry = 0xBC5A7420      ← 相差 1 ulp（低位差 4 = 该数量级的 1 个 ulp）
//
//   源头是 `CastResolver` 里的这一句：
//       state.DelayCarry += delay - frames * TickSeconds;
//   它是**乘减**（`a - b*c`）。C++ 编译器（IL2CPP 用的 MSVC）默认**允许把乘减收缩成
//   FMA/FNMADD**（`a*b+c` 只舍入一次），而 Mono 的 JIT 不做收缩 → 两条编译器链路
//   在最低位上必然不同。**这不是"浮点不可靠"，而是"表达式形状决定了舍入次数"。**
//
// 结论（决定了 W-11 的范围）：
//   · 单个 IEEE-754 运算（+ - * / 与 sqrt）**在两边的结果一定相同**（都是正确舍入的）；
//     实测 `SimMath.Cos/Sin/Sqrt/Pow/Atan2` 在 Mono 与 IL2CPP 下**逐位相同**（10/10 样本）。
//   · 会分歧的是**复合表达式**：`a + b*c`、`a - b*c`、`(a+b)*c + 0.5f` 这类
//     可以被收缩成 FMA 的写法。**累加器**（位置、蓝量、余量）上尤其致命：
//     1 ulp 的差会跨帧累积，最终变成"施法节奏/位置不同步"。
//   · 所以 W-11**不需要**把整个模拟层改成定点数：只要把**进入状态哈希的复合表达式**
//     拆成"乘积先显式舍入、再单独加/减"即可 —— 显式窄化转换是编译器**不能**跨越的
//     舍入屏障，因此 FMA 收缩被阻止，两个后端逐位一致。
//
// 用法：把 `state += a * b`、`v += d * k`、`x*cs - y*sn` 这类写法换成本文件的调用。
// ⚠ 这一层只解决"舍入次数"，**不改变数值语义**（仍然是 float、仍然同样精确）。
//------------------------------------------------------------


namespace EmojiWar.GameMain.Simulation
{
    /// <summary>[W-11] 确定性浮点原语：把复合表达式拆成"乘积显式舍入后单独加/减"，阻止 FMA 收缩。</summary>
    public static class SimMath
    {
        /// <summary>`a * b`：乘积在 double 里算完并**显式舍入回 float**。
        /// 单次乘法本来就是正确舍入的；显式转换是为了让"这个值已经舍入过"对编译器可见。</summary>
        public static float Mul(float a, float b)
        {
            return (float)((double)a * (double)b);
        }

        /// <summary>`a * b + c`：乘积与加法**分开**做（阻止收缩成 FMA）。
        /// 直接写 `a * b + c` 时 MSVC 会合成一条 `fma` 指令（只舍入一次）→ 与 Mono 差 1 ulp。</summary>
        public static float MulAdd(float a, float b, float c)
        {
            double p = (double)a * (double)b;
            return c + (float)p;
        }

        /// <summary>`v + d * k`（向量按标量推进：位置积分、弹道推进、敌人追击都用它）。</summary>
        public static SimVec2 AddScaled(SimVec2 v, SimVec2 d, float k)
        {
            double kx = (double)d.x * (double)k;
            double ky = (double)d.y * (double)k;
            return new SimVec2(v.x + (float)kx, v.y + (float)ky);
        }

        /// <summary>`v + d * s * k`（方向 × 速度 × 帧长：三段乘积各自单独舍入）。</summary>
        public static SimVec2 AddScaled2(SimVec2 v, SimVec2 d, float s, float k)
        {
            float sk = Mul(s, k);
            return AddScaled(v, d, sk);
        }

        /// <summary>按 (cs, sn) 旋转 `(x, y)`：`x*cs - y*sn` 与 `x*sn + y*cs`（四个乘积各自单独舍入）。</summary>
        public static SimVec2 Rotate(float x, float y, float cs, float sn)
        {
            double a = (double)x * (double)cs;
            double b = (double)y * (double)sn;
            double c = (double)x * (double)sn;
            double d = (double)y * (double)cs;
            return new SimVec2((float)(a - b), (float)(c + d));
        }

        /// <summary>`v / tick + 0.5` 的取整（量化到帧）。**所有 ms/秒 → 帧的量化都必须走这里**：
        /// 量化是"离散决策"，最低位差一点就会差整整 1 帧，比数值本身刺眼得多。</summary>
        public static int QuantizeToFrames(double seconds, double tickSeconds)
        {
            if (seconds <= 0.0) { return 0; }
            return (int)(seconds / tickSeconds + 0.5);
        }

        // ==================== 距离 / 归一化 ====================
        //
        // 为什么必须自己实现：Unity 的 `SimVec2.sqrMagnitude` / `.magnitude` / `.normalized`
        // 内部就是 `x*x + y*y`（再开方/相除）—— 这个**乘加**同样会被收缩成 FMA。
        // 实测（2026-09-29）：把位置积分、DelayCarry 都改好之后，`bullets` 段仍然与 Mono 不同，
        // 而唯一影响子弹方向的浮点运算就是 Tick 里的 `aim.Normalize()` → 换成本文件实现后消失。

        /// <summary>`x*x + y*y`（两个乘积各自显式舍入后再相加）。</summary>
        public static float SqrMagnitude(float x, float y)
        {
            double xx = (double)x * (double)x;
            double yy = (double)y * (double)y;
            return (float)(xx + yy);
        }

        /// <summary>`v.x*v.x + v.y*v.y`。</summary>
        public static float SqrMagnitude(SimVec2 v)
        {
            return SqrMagnitude(v.x, v.y);
        }

        /// <summary>`|v|`。开方本身在两边都逐位一致（实测 SimMath.Sqrt == (float)Math.Sqrt），
        /// 风险只在被开方的那两个乘积。</summary>
        public static float Magnitude(SimVec2 v)
        {
            return (float)System.Math.Sqrt((double)SqrMagnitude(v));
        }

        /// <summary>把 `v` 归一化（替代 `SimVec2.normalized` / `Normalize()`）。
        /// 零向量返回 `SimVec2.zero`（与 Unity 的 kEpsilon 语义一致：不是"返回 (1,0)"）。</summary>
        public static SimVec2 Normalized(SimVec2 v)
        {
            float sq = SqrMagnitude(v);
            if (sq <= 1e-10f) { return SimVec2.zero; }
            double mag = System.Math.Sqrt((double)sq);
            return new SimVec2((float)((double)v.x / mag), (float)((double)v.y / mag));
        }

        // ==================== Mathf 的等价物（W-04：模拟层不许引用 UnityEngine） ====================
        //
        // 数值等价性依据（实测，见 §0.2 W-11）：
        //   · `SimMath.Cos/Sin/Sqrt/Pow/Atan2` 在 **Mono 与 IL2CPP 下逐位相同**（10/10 样本），
        //     且与 .NET 的 `(float)Math.Cos` 等**逐位相同** ⇒ Unity 的 Mathf 就是
        //     "double 算完再舍入到 float"的托管实现，换成 `(float)System.Math.X` 不改数值。
        //   · 换成自研实现还有一个额外好处：**两个后端跑的是同一段托管代码**，不依赖引擎实现。

        public const float PI = 3.14159274f;              // 与 SimMath.PI 同值
        public const float Deg2Rad = 0.0174532924f;        // 与 SimMath.Deg2Rad 同值

        public static float Cos(float f) { return (float)System.Math.Cos((double)f); }
        public static float Sin(float f) { return (float)System.Math.Sin((double)f); }
        public static float Sqrt(float f) { return (float)System.Math.Sqrt((double)f); }
        public static float Abs(float f) { return f < 0f ? -f : f; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        /// <summary>等价于 `SimMath.Lerp(a,b,t)`；乘积走 `MulAdd`（阻止 FMA 收缩 —— 散射角也会进子弹方向）。</summary>
        public static float Lerp(float a, float b, float t)
        {
            return MulAdd(b - a, Clamp01(t), a);
        }
    }
}

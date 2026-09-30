//------------------------------------------------------------
// EmojiWar GameMain - 逻辑层构建指纹（W-07 配置/版本握手用）
//
// 为什么需要它：
//   配置哈希（`ConfigService.VersionHash`）只能发现"**配置**不一致"，发现不了"**代码**不一致"。
//   两个不同版本的 exe 完全可能加载同一份配置资产 → 配置握手放行 → 进对局后才不同步
//   （而那时的现象就是"玩家看到不同步但没有日志"，正是本次审计反复踩的坑）。
//   所以握手必须同时带上"这份逻辑代码是哪一次编译产生的"。
//
// ★ [W-11b] 口径修正（实测推翻了原设计，证据见下）：
//   原设计写的是「用本程序集 **MVID** + 逻辑帧率；MVID 由 C# 编译期写入元数据，
//   Mono 与 IL2CPP 消费同一份元数据 → 两种后端指纹相同」。
//   实测（2026-09-29，IL2CPP Development 构建）**不成立**：
//     `Assembly.ManifestModule.ModuleVersionId` 在 IL2CPP 下抛 **NotSupportedException**，
//     于是指纹退化成常量 `sim1|mvid=unknown:NotSupportedException|tick=1023969417`。
//   后果有两个，都是"静默失效"（最坏的一类）：
//     1. **W-08 判别失效**：指纹不再随代码变化 → 旧录像会被误报成"真不同步"，
//        排查方向被引到完全错误的路上（这正是 W-08 当初要避免的事）；
//     2. **握手可能误拒**：编辑器（Mono，真 MVID）↔ IL2CPP 播放器（常量串）即使**同源**
//        也会算出不等的 CodeHash → 开发期"编辑器开房 + 打好的包加入"会被判成版本不一致。
//
//   修正后的口径（把"比较用"与"诊断用"彻底分开）：
//     · **比较用** `CodeFingerprint` = `sim1|code=<SimCodeVersion>|tick=<帧率位模式>`
//       —— 只含**跨后端必然相同**的成分，握手与录像对拍都用它；
//       `SimCodeVersion` 是人工维护的语义版本：**哈希覆盖集/推进口径一变就 +1**
//       （与 `ReplayRecorder.FormatVersion` 同理）。比 MVID 更准：MVID 每次编译都变，
//       改个 UI 文案也会导致"旧录像"误报；这里只在**真的影响确定性**时才变。
//     · **诊断用** `BuildId` = Mono 的 MVID / IL2CPP 的 `Application.buildGUID`
//       —— 只写进日志与录像头，**不参与比较**，用来回答"这份录像到底是哪个 exe 录的"。
//     · 逻辑帧率必须入指纹：`LockstepSimulation.TickInterval` 一改，配置一个字节没动，
//       但所有"按帧"的时长与推进全变了 —— 这正是 W-10a 与 30Hz 切换要防的事。
//     · **不用时间戳**：dll 文件时间戳会因为重新打包而在"零代码改动"下变化，制造假不一致。
//
//   [W-04 待办] 本文件用了 UnityEngine.Application（仅 BuildId，诊断用）——
//   将来把 Sim 层拆出独立 asmdef 时，应把 BuildId 的采集挪到表现层再由外部注入。
//------------------------------------------------------------

using System;
using System.Globalization;

namespace EmojiWar.GameMain.Simulation
{
    /// <summary>逻辑层构建指纹（W-07 握手）：同源同帧率 → 同指纹；改确定性代码或换帧率 → 换指纹。</summary>
    public static class SimBuildInfo
    {
        /// <summary>
        /// [W-11b] 逻辑代码语义版本。**改了状态哈希的覆盖字段集、或改了模拟推进口径（帧率、
        /// 量化单位、事件应用时机）时必须 +1** —— 它是"这份录像/这个客户端跟我是同一份逻辑"的判据。
        /// 忘了 +1 的兜底：`StateHashGuard`（`-hashguard` 门禁）会查"字段是否都被哈希覆盖"。
        /// </summary>
        public const int SimCodeVersion = 2;

        /// <summary>指纹格式版本（字符串结构变了才 +1，与 SimCodeVersion 是两件事）。</summary>
        private const string FormatTag = "sim1";

        private static bool s_Ready = false;
        private static string s_Fingerprint = "";
        private static string s_BuildId = "";
        private static string s_Describe = "";
        private static ulong s_Hash = 0UL;

        /// <summary>
        /// **比较用**指纹：只含跨后端必然相同的成分（语义版本 + 帧率位模式）。
        /// 握手（`CodeHash`）与录像对拍都用它；**不要**把 BuildId 混进来。
        /// </summary>
        public static string CodeFingerprint
        {
            get { Ensure(); return s_Fingerprint; }
        }

        /// <summary>
        /// **诊断用**构建标识（Mono=MVID / IL2CPP=buildGUID）。只进日志与录像头，不参与比较。
        /// </summary>
        public static string BuildId
        {
            get { Ensure(); return s_BuildId; }
        }

        /// <summary>录像头/探针用的完整串 = 比较用指纹 + `|build=<构建标识>`。</summary>
        public static string Describe
        {
            get { Ensure(); return s_Describe; }
        }

        /// <summary>指纹的 64bit 哈希（上线握手用，省字节）。**只哈希比较用部分** → 跨后端一致。</summary>
        public static ulong CodeHash
        {
            get { Ensure(); return s_Hash; }
        }

        /// <summary>
        /// 从"完整串"里取出**比较用**部分（丢掉 `|build=...` 尾巴）。
        /// 录像头里存的是完整串（含诊断信息），对拍时必须按这个口径比，否则跨构建会假报"旧录像"。
        /// </summary>
        public static string CompareKey(string fingerprintOrDescribe)
        {
            if (string.IsNullOrEmpty(fingerprintOrDescribe)) { return ""; }
            int i = fingerprintOrDescribe.IndexOf("|build=", StringComparison.Ordinal);
            return i < 0 ? fingerprintOrDescribe : fingerprintOrDescribe.Substring(0, i);
        }

        /// <summary>两份指纹是否**同源**（忽略诊断用的构建标识）。</summary>
        public static bool SameCode(string a, string b)
        {
            return CompareKey(a) == CompareKey(b);
        }

        private static void Ensure()
        {
            if (s_Ready) { return; }
            s_Ready = true;   // 先置位：即使下面抛异常也不再重算

            // 帧率用位模式表示：避免 "R"/区域差异带来的文本抖动
            int tickBits = BitConverter.SingleToInt32Bits(LockstepSimulation.TickInterval);

            s_Fingerprint = FormatTag
                + "|code=" + SimCodeVersion.ToString(CultureInfo.InvariantCulture)
                + "|tick=" + tickBits.ToString(CultureInfo.InvariantCulture);
            s_BuildId = ReadBuildId();
            s_Describe = s_Fingerprint + "|build=" + s_BuildId;

            // ★ 只哈希**比较用**部分：这样"编辑器(Mono) 与 IL2CPP 包同源"能通过握手，
            //   而 BuildId 的差异只出现在日志里（用于回答"这份录像哪个 exe 录的"）。
            s_Hash = Fnv1a64(s_Fingerprint);
        }

        /// <summary>
        /// [W-04] 构建标识读取器（**由表现层注入**：Mono 其实用 MVID 就够，IL2CPP 需要 `Application.buildGUID`）。
        /// 模拟层不引用 UnityEngine，所以"取 buildGUID"这一步留在表现层；未注入时退化为 `unknown:未注入`。
        /// </summary>
        public static Func<string> BuildIdProvider;

        /// <summary>[W-11b] 采集构建标识。任何一步失败都绝不静默：返回带原因的可读串。</summary>
        private static string ReadBuildId()
        {
            // Mono（编辑器与 Mono 播放器）：MVID 每次编译都变，精确到"这一次编译"。
            try
            {
                string mvid = "mvid:" + typeof(SimBuildInfo).Assembly.ManifestModule.ModuleVersionId.ToString("N");
                return mvid;
            }
            catch (Exception e)
            {
                // IL2CPP 实测走到这里：NotSupportedException。改用表现层注入的实现（buildGUID）。
                string why = e.GetType().Name;
                try
                {
                    Func<string> p = BuildIdProvider;
                    string guid = p != null ? p() : null;
                    if (string.IsNullOrEmpty(guid)) { return "unknown:" + why + "(未注入 BuildIdProvider)"; }
                    return "il2cpp:buildGUID=" + guid + "(mvid:" + why + ")";
                }
                catch (Exception e2)
                {
                    return "unknown:" + why + "/" + e2.GetType().Name;
                }
            }
        }

        private static ulong Fnv1a64(string s)
        {
            ulong h = 14695981039346656037UL;
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}

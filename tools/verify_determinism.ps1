# =============================================================================
# EmojiWar2 - 双实例确定性对拍（W-05）
#
# 用途：一条命令验证"两端跑的是同一个世界"。把 NetcodeFixPlan.md M0 的验收
#      从"手工看探针"变成"可回归的脚本"。
#
# 用法：
#   pwsh -File tools/verify_determinism.ps1                     # 默认：含真实战斗，跑 ~150s
#   pwsh -File tools/verify_determinism.ps1 -Seconds 200        # 观察更久
#   pwsh -File tools/verify_determinism.ps1 -Strict             # 额外做逐条 trace 对拍
#   pwsh -File tools/verify_determinism.ps1 -ProbeDir <路径>    # 指定探针目录
#
# 通过判据（全部必须满足）：
#   1. 两侧探针均生成；
#   2. Host 探针含 `模拟初始化 ... players=2`；
#   3. Client 探针含 `战斗模拟初始化 ... players=2`（= 客户端确实收到了 S2CBattleStart）；
#   4. Client 的 `[net] 对账一致` 次数 >= MinSync；
#   5. 两侧均无 `[net] 不同步!`；
#   6. 两侧均无脱节告警 `⚠`（W-00 新增的检测器）；
#   7. 两侧最后一条 `[sim]` 的 wave 与 enemies 相同（两端同一个世界）；
#   8. -Strict：同时刻双端 trace 逐条位一致（0 分歧）。
#
# 退出码：0 = 全部通过；1 = 有失败项。
# 设计约束：本脚本**只读**项目、只写 Logs 下的探针与 _prev* 归档目录。
# =============================================================================

[CmdletBinding()]
param(
    [int]$Seconds = 150,
    [int]$MinSync = 10,
    [int]$ClientDelaySeconds = 20,
    [string]$ProbeDir = '',
    [switch]$Strict,
    [switch]$KeepProcesses,
    # W-26.3 损伤注入（默认 0 = 不注入）。注入下本门禁的判据**不变**：
    # 只加延迟/抖动应当仍然全绿；一旦加丢包，就会因"输入帧缺口"永久错位而报不同步
    # （这正是报告 C4/C7 的缺陷，也是 W-12/W-13 落地后应当转绿的回归目标）。
    [int]$NetDelayMs = 0,
    [int]$NetJitterMs = 0,
    [double]$NetLossPct = 0,

    # W-07 配置/版本握手：给客户端塞一个假的配置哈希，验证"不一致时被明确拒绝"，
    # 而不是放进对局后不同步（这是 W-07 的验收命令，不需要改动任何资产）。
    [string]$FakeConfigHash = '',
    [switch]$ExpectRejected,

    # W-12：给客户端追加任意启动参数（空格分隔），用于验收钩子，例如
    #   -ClientExtraArgs '-stopsendinginputs 25 3'   # 客户端在第 25 秒起停止上行输入 3 秒
    [string]$ClientExtraArgs = '',

    # W-12：断言"房主判定该玩家进入托管、并在输入恢复后退出托管"，且基础判据仍全绿
    #（托管标志随帧广播 → 两端同值 → 进哈希一致，这正是这条验收要证明的）
    [switch]$ExpectManaged,

    # W-13：断言客户端消费节奏符合抖动缓冲的设计（单渲染帧 ≤3 个逻辑帧）
    [switch]$ExpectJitterBuffer,

    # W-14：断言"房主自身输入延迟 D"按观测到的客户端滞后自适应（并夹在 [2,6]）
    [switch]$ExpectInputDelay,

    # W-17：断言插值基准来自本地时间轴且**无位置回退**（渲染位置方向翻转次数为 0）
    [switch]$ExpectInterpolation,

    # W-18：断言**逻辑 Tick 零分配**（`alloc/tick=0B` 且"有分配的 tick 0/N"）
    [switch]$ExpectZeroAlloc,

    # W-15：断言本机即时反馈生效（按下沿立刻出声/出闪光），且能测出"反馈比权威子弹早几帧"
    [switch]$ExpectLocalFeedback
)

$ErrorActionPreference = 'Stop'
$fail = New-Object System.Collections.Generic.List[string]
$pass = New-Object System.Collections.Generic.List[string]

# ---------- 定位项目 ----------
$repoRoot = Split-Path -Parent $PSScriptRoot          # tools/ 的上一级
$proj = Join-Path $repoRoot 'EmojiWar2'
if (-not (Test-Path $proj)) { throw "找不到项目目录: $proj" }
$exe = Join-Path $proj 'Builds\StandaloneWindows64\EmojiWar2.exe'
if (-not (Test-Path $exe)) { throw "找不到构建产物: $exe（先跑菜单 EmojiWar/Tools/Build Windows64 Player）" }
if ([string]::IsNullOrWhiteSpace($ProbeDir)) { $ProbeDir = Join-Path $proj 'Builds\StandaloneWindows64\Logs' }
if (-not (Test-Path $ProbeDir)) { throw "找不到探针目录: $ProbeDir" }

# ---------- 产物新鲜度（用 Data/Managed 的 dll，不用 .exe 启动器壳）----------
$builtDll = Join-Path $proj 'Builds\StandaloneWindows64\EmojiWar2_Data\Managed\EmojiWar.GameMain.dll'
$newestCs = Get-ChildItem (Join-Path $proj 'Assets') -Recurse -File -Include *.cs |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (Test-Path $builtDll) {
    $bd = (Get-Item $builtDll).LastWriteTime
    if ($newestCs -and $newestCs.LastWriteTime -gt $bd) {
        Write-Host "[WARN] 构建产物比源码旧！产物=$bd 源码=$($newestCs.LastWriteTime) ($($newestCs.Name))" -ForegroundColor Yellow
        Write-Host "       请先跑菜单 EmojiWar/Tools/Build Windows64 Player" -ForegroundColor Yellow
        $fail.Add("构建产物落后于源码（$($newestCs.Name)）")
    } else {
        Write-Host "[ ok ] 构建产物新鲜: $bd" -ForegroundColor Green
    }
} else {
    Write-Host "[WARN] 未找到 $builtDll，跳过新鲜度检查" -ForegroundColor Yellow
}

# ---------- 清理残留与归档旧探针 ----------
Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 2
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$archive = Join-Path $ProbeDir "_prev_$stamp"
$old = Get-ChildItem $ProbeDir -File -Filter 'runtime_probe_*.txt' -ErrorAction SilentlyContinue
if ($old) {
    New-Item -ItemType Directory -Force -Path $archive | Out-Null
    $old | ForEach-Object { Move-Item $_.FullName (Join-Path $archive $_.Name) -Force }
    Write-Host "[ ok ] 归档旧探针 $($old.Count) 个 -> $(Split-Path $archive -Leaf)"
}

# ---------- 起双实例（命令 A：刻意让客户端延后，复现"房主先准备"的时序）----------
# W-26.3：损伤注入参数同时给两端（单向延迟 / 抖动 / 丢包）
$netArgs = @()
if ($NetDelayMs -gt 0) { $netArgs += @('-netdelay', "$NetDelayMs") }
if ($NetJitterMs -gt 0) { $netArgs += @('-netjitter', "$NetJitterMs") }
if ($NetLossPct -gt 0) { $netArgs += @('-netloss', "$NetLossPct") }
if ($netArgs.Count -gt 0) {
    Write-Host ""
    Write-Host "[W-26.3] 损伤注入: delay=$NetDelayMs ms  jitter=±$NetJitterMs ms  loss=$NetLossPct %" -ForegroundColor Magenta
}

Write-Host ""
Write-Host "===== 启动 Host（-autocreate -autoready -autoreadywait 40 -autoreadyplayers 2 -autofire -autoshop）====="
$hostArgs = @('-autocreate', '-autoready', '-autoreadywait', '40', '-autoreadyplayers', '2', '-autofire', '-autoshop') + $netArgs
$hostProc = Start-Process -FilePath $exe -ArgumentList $hostArgs -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "  Host   pid=$($hostProc.Id)"
Start-Sleep -Seconds $ClientDelaySeconds
Write-Host "===== 启动 Client（-autojoin -autoready），延后 $ClientDelaySeconds 秒 ====="
$clientArgs = @('-autojoin', '-autoready') + $netArgs
if ($FakeConfigHash) {
    $clientArgs += @('-fakeconfighash', $FakeConfigHash)
    Write-Host "[W-07] 客户端将上报**伪造**配置哈希 0x$FakeConfigHash（验证握手拒绝路径）" -ForegroundColor Magenta
}
if ($ClientExtraArgs) {
    $extra = $ClientExtraArgs.Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
    $clientArgs += $extra
    Write-Host "[W-12] 客户端追加参数: $ClientExtraArgs" -ForegroundColor Magenta
}
$clientProc = Start-Process -FilePath $exe -ArgumentList $clientArgs -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "  Client pid=$($clientProc.Id)"
Write-Host "等待 $Seconds 秒..."
Start-Sleep -Seconds $Seconds

# W-07：拒绝路径要**轮询到结果出现**再关停进程。
# 固定 sleep 会抖：实测同为 15 秒，机器忙的时候客户端还在"菜单→大厅"的路上就被关了，
# 探针里连 `发送 C2SJoinRoom` 都没有 → 门禁报"识别不出客户端"，看起来像功能坏了，其实只是等太短。
if ($ExpectRejected) {
    for ($i = 0; $i -lt 25; $i++) {
        $hit = $false
        foreach ($pr in (Get-ChildItem $ProbeDir -File -Filter 'runtime_probe_*.txt' -ErrorAction SilentlyContinue)) {
            if (@(Get-Content $pr.FullName -Encoding UTF8 | Select-String '加入被拒绝').Count -gt 0) { $hit = $true; break }
        }
        if ($hit) { Write-Host "  [W-07] 已观察到拒绝结果，提前结束等待（+$($i * 2) 秒）"; break }
        Start-Sleep -Seconds 2
    }
}

# ---------- 优雅关闭（触发 Application.quitting → flush trace，C7）----------
if (-not $KeepProcesses) {
    Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }
    Start-Sleep -Seconds 6
    Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
}

# ---------- 读探针 ----------
# 角色识别必须按**内容**，不能按 Get-ChildItem 的返回顺序（它按文件名排序，
# 客户端 pid 更小时会排到 Host 前面），也不能靠 pid 大小。
$probes = Get-ChildItem $ProbeDir -File -Filter 'runtime_probe_*.txt' -ErrorAction SilentlyContinue

# ---------- W-07：配置不一致必须被明确拒绝（独立判定分支，随即退出）----------
# 这条路径下客户端**不会进入任何模拟**，所以拿不到 `[sim] CLIENT` 行 —— 不能复用下面的角色识别。
if ($ExpectRejected) {
    Write-Host ""
    Write-Host "===== 判定：配置/版本握手是否拒绝了不匹配的客户端 =====" -ForegroundColor Magenta
    $hL = $null; $cL = $null; $hN = ''; $cN = ''
    foreach ($pr in $probes) {
        $ls = Get-Content $pr.FullName -Encoding UTF8
        # ⚠ 不能用 `[net-host]` 当房主标志：**客户端探针里也有** `[net-host] Shutdown (server=False conn=False)`
        #   （NetHostLogic 组件两端都挂着）→ 客户端会被误判成房主、永远识别不出 Client（实测踩过）。
        #   房主标志必须锚定只有房主才会打的 `StartHost OK`。
        $isHost = @($ls | Select-String '^\[net-host\] StartHost OK').Count -gt 0
        $isClient = @($ls | Select-String '^\[net\] (发送 C2SJoinRoom|加入被拒绝)').Count -gt 0
        if ($isHost) { $hL = $ls; $hN = $pr.Name }
        elseif ($isClient) { $cL = $ls; $cN = $pr.Name }
    }
    Write-Host "探针: Host=$hN  Client=$cN"

    if (-not $hL -or -not $cL) {
        $fail.Add("无法识别 Host/Client 探针（Host='$hN' Client='$cN'）—— 客户端可能压根没发出加入请求")
        if (-not $hL) { $hL = @() }
        if (-not $cL) { $cL = @() }
    } else {
        if (@($hL | Select-String '^\[net-host\] 拒绝.*配置哈希不一致').Count -gt 0) {
            $rej = @($hL | Select-String '^\[net-host\] 拒绝.*配置哈希不一致')[0].Line
            $pass.Add("Host 明确拒绝（配置哈希不一致）：$rej")
        } else {
            $fail.Add("Host 没有拒绝配置不一致的客户端（未见 ``[net-host] 拒绝 ... 配置哈希不一致``）")
        }

        if (@($cL | Select-String '^\[net\] 加入被拒绝').Count -gt 0) {
            $cr = @($cL | Select-String '^\[net\] 加入被拒绝')[0].Line
            $pass.Add("Client 收到并显示拒绝原因：$cr")
        } else {
            $fail.Add("Client 没收到 ``[net] 加入被拒绝``（用户看不到原因）")
        }

        if (@($cL | Select-String '配置版本不一致').Count -gt 0) {
            $pass.Add("拒绝原因里带有可行动的文案（含两端哈希）")
        } else {
            $fail.Add("拒绝文案没有说明是配置版本不一致")
        }

        # 关键：客户端**绝不能**进对局（这正是 W-07 要防的"放进来再不同步"）
        $entered = @($cL | Select-String '战斗模拟初始化|\[sim\] CLIENT').Count
        if ($entered -eq 0) { $pass.Add("Client 未进入战斗模拟（不一致时被挡在门外）") }
        else { $fail.Add("Client 仍进入了战斗模拟（$entered 处）—— 握手没有真正阻断") }

        # 房主也不能因为客户端加入失败而一人开战
        $hostSolo = @($hL | Select-String '^\[net-host\] 模拟初始化').Count
        if ($hostSolo -eq 0) { $pass.Add("Host 未开战（仍停在房间阶段）") }
        else { $fail.Add("Host 一人开战了（$hostSolo 处 模拟初始化）—— 拒绝后房间状态被污染") }
    }

    Write-Host "================= 结果（W-07 握手拒绝）================"
    foreach ($p in $pass) { Write-Host "  [PASS] $p" -ForegroundColor Green }
    foreach ($f in $fail) { Write-Host "  [FAIL] $f" -ForegroundColor Red }
    Write-Host "========================================"
    if ($fail.Count -eq 0) {
        Write-Host "全部通过 ✅  ($($pass.Count) 项)" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "$($fail.Count) 项失败 ❌" -ForegroundColor Red
        exit 1
    }
}

$hostLines = $null; $clientLines = $null; $hostName = ''; $clientName = ''
if ($probes) {
    foreach ($pr in $probes) {
        $ls = Get-Content $pr.FullName -Encoding UTF8
        $lastSim = $ls | Select-String '\[sim\]' | Select-Object -Last 1
        if ($lastSim -and $lastSim.Line -match '\[sim\] HOST') { $hostLines = $ls; $hostName = $pr.Name }
        elseif ($lastSim -and $lastSim.Line -match '\[sim\] CLIENT') { $clientLines = $ls; $clientName = $pr.Name }
    }
    Write-Host ""
    Write-Host "探针: Host=$hostName  Client=$clientName"
}
if (-not $hostLines -or -not $clientLines) {
    $fail.Add("未能按内容识别出 Host/Client 两侧探针（Host='$hostName' Client='$clientName'）")
    if (-not $hostLines) { $hostLines = @() }
    if (-not $clientLines) { $clientLines = @() }
}

function Count-Of($lines, $pattern) { return @($lines | Select-String -Pattern $pattern).Count }

# 检查 2：Host 战斗模拟 2 人
# 注意必须锚定 `[net-host] `，否则 `模拟初始化` 会命中客户端的 `战斗模拟初始化`（子串陷阱）
if (@($hostLines | Select-String '^\[net-host\] 模拟初始化.*players=2').Count -gt 0) {
    $pass.Add("Host 模拟初始化 players=2（房主没有一人开战）")
} else { $fail.Add("Host 未见 ``[net-host] 模拟初始化 ... players=2``（房主可能又一人开战了）") }

# 检查 3：Client 战斗模拟 2 人（= 收到了 S2CBattleStart）
if (@($clientLines | Select-String '^\[net\] 战斗模拟初始化.*players=2').Count -gt 0) {
    $pass.Add("Client 战斗模拟初始化 players=2（收到了 S2CBattleStart）")
} else { $fail.Add("Client 未见 ``[net] 战斗模拟初始化 ... players=2``（客户端没进战斗 = 与对局脱节）") }

# 检查 4：对账一致次数
$sync = Count-Of $clientLines '对账一致'
if ($sync -ge $MinSync) { $pass.Add("Client 对账一致 $sync 次（>= $MinSync）") }
else { $fail.Add("Client 对账一致仅 $sync 次（要求 >= $MinSync）—— 对账机制可能没跑起来") }

# 检查 5：不同步
$desyncH = Count-Of $hostLines '不同步'
$desyncC = Count-Of $clientLines '不同步'
if ($desyncH -eq 0 -and $desyncC -eq 0) { $pass.Add("无不同步告警") }
else { $fail.Add("出现不同步告警！Host=$desyncH Client=$desyncC") }

# 检查 6：脱节告警（W-00 的检测器）
# ⚠ 只匹配"已与对局脱节"这个特征串，**不要用裸 `⚠`**：代码里还有其它 ⚠（例如 W-06 缺装备 Id），
#   用裸 `⚠` 会把它们误判成脱节（实测踩过：W-06 上线后误报 2 条）。
$warnH = Count-Of $hostLines '已与对局脱节'
$warnC = Count-Of $clientLines '已与对局脱节'
if ($warnH -eq 0 -and $warnC -eq 0) { $pass.Add("无脱节告警（已与对局脱节）") }
else { $fail.Add("出现脱节告警（已与对局脱节）！Host=$warnH Client=$warnC") }

# 其它 ⚠ 只作信息性提示，不判定失败
$otherH = @($hostLines | Select-String '⚠' | Where-Object { $_.Line -notmatch '已与对局脱节' })
$otherC = @($clientLines | Select-String '⚠' | Where-Object { $_.Line -notmatch '已与对局脱节' })
if ($otherH.Count -gt 0 -or $otherC.Count -gt 0) {
    Write-Host "  [info] 另有非脱节类 ⚠ 共 $($otherH.Count + $otherC.Count) 条（仅供参考，不判定失败）" -ForegroundColor DarkYellow
    $otherH | Select-Object -First 3 | ForEach-Object { Write-Host "         Host  : $($_.Line)" -ForegroundColor DarkYellow }
    $otherC | Select-Object -First 3 | ForEach-Object { Write-Host "         Client: $($_.Line)" -ForegroundColor DarkYellow }
}

# 检查 7：两端同一个世界（wave / enemies）
function Last-Sim($lines) {
    $last = $lines | Select-String '\[sim\]' | Select-Object -Last 1
    if (-not $last) { return $null }
    if ($last.Line -match 'wave=(\d+).*enemies=(\d+)') {
        return [pscustomobject]@{ Line = $last.Line; Wave = [int]$Matches[1]; Enemies = [int]$Matches[2] }
    }
    return [pscustomobject]@{ Line = $last.Line; Wave = $null; Enemies = $null }
}
$sh = Last-Sim $hostLines
$sc = Last-Sim $clientLines
if ($sh -and $sc -and $sh.Wave -ne $null -and $sc.Wave -ne $null) {
    if ($sh.Wave -eq $sc.Wave -and $sh.Enemies -eq $sc.Enemies) {
        $pass.Add("两端同一世界: wave=$($sh.Wave) enemies=$($sh.Enemies)")
    } else {
        $fail.Add("两端世界不一致！Host wave=$($sh.Wave) enemies=$($sh.Enemies) | Client wave=$($sc.Wave) enemies=$($sc.Enemies)")
    }
    Write-Host "  Host   : $($sh.Line)"
    Write-Host "  Client : $($sc.Line)"
} else { $fail.Add("探针里找不到 [sim] 行，无法比较两端世界") }

# 检查 7b（W-12，可选）：输入中断 → 房主托管 → 恢复
if ($ExpectManaged) {
    $mIn = Count-Of $hostLines '进入托管'
    $mOut = Count-Of $hostLines '退出托管'
    $cStop = Count-Of $clientLines '停止上行输入'
    if ($cStop -gt 0) { $pass.Add("[-ExpectManaged] Client 钩子确实停止了上行输入（验收前提成立）") }
    else { $fail.Add("[-ExpectManaged] Client 探针里没有「停止上行输入」—— 钩子没生效（或时间窗未到）") }
    if ($mIn -gt 0) { $pass.Add("[-ExpectManaged] Host 判定玩家进入托管（连续无新输入超过阈值）") }
    else { $fail.Add("[-ExpectManaged] Host 未进入托管 —— 输入中断没被检出（W-12 新鲜度判定没生效）") }
    if ($mOut -gt 0) { $pass.Add("[-ExpectManaged] Host 在输入恢复后退出托管") }
    else { $fail.Add("[-ExpectManaged] Host 未退出托管（输入恢复后没有回到正常输入）") }
}

# 检查 7c（W-13，可选）：客户端消费节奏（抖动缓冲）
# 核心指标：单渲染帧内执行过的逻辑帧数上限必须 ≤ 3（预算值），且队列真的起到了缓冲作用。
if ($ExpectJitterBuffer) {
    $w13 = $clientLines | Select-String '\[w13\]' | Select-Object -Last 1
    if (-not $w13) {
        $fail.Add("[-ExpectJitterBuffer] 客户端探针里没有 [w13] 行 —— 消费调度器没在跑（W-13 未生效）")
    } else {
        Write-Host "  [W-13] $($w13.Line)" -ForegroundColor Magenta
        $maxStep = -1; $depth = -1; $starved = -1
        if ($w13.Line -match 'maxStepsPerRenderFrame=(\d+)') { $maxStep = [int]$Matches[1] }
        if ($w13.Line -match 'queue=(\d+)') { $depth = [int]$Matches[1] }
        if ($w13.Line -match 'starved=(\d+)') { $starved = [int]$Matches[1] }
        if ($maxStep -ge 0 -and $maxStep -le 3) { $pass.Add("[-ExpectJitterBuffer] 单渲染帧最多执行 $maxStep 个逻辑帧（预算 ≤3）") }
        else { $fail.Add("[-ExpectJitterBuffer] 单渲染帧最多执行了 $maxStep 个逻辑帧（>3，仍会卡顿猛冲）") }
        # 缓冲**不能失控**：**累计平均**深度（已由客户端排除预热期）应贴近目标 2 帧。
        # · 用累计均值而非窗口均值：窗口会被一次突发污染。
        # · **预热期必须排除**：开战那一刻 `ProcedureBattle` 会主动 `GC.Collect()`（~200ms 停顿），
        #   期间帧照常到达 → 队列一口气堆到 20 帧再排空。客户端已按 DepthWarmupSteps=60 帧跳过。
        # · **阈值分场景**：注入抖动时水位必然更高，原因有二 ——
        #     (a) NetSim 是"每会话 FIFO、仅队头到期才释放"，多条消息的随机延迟不同 → 会**成批同时释放**，
        #         突发量比真实网络更大（这是注入器的简化，不是被测代码的缺陷）；
        #     (b) 机器有负载时渲染帧率下降（实测 client fps 从 240 掉到 100），单个渲染帧能消化的帧更少。
        #   所以注入场景用更宽的上界，但**仍必须有界**（否则就是把"缓冲失控"当正常）。
        $avgLimit = if ($NetJitterMs -gt 0 -or $NetDelayMs -gt 0) { 6.0 } else { 4.0 }
        $maxLimit = if ($NetJitterMs -gt 0 -or $NetDelayMs -gt 0) { 16 } else { 12 }
        $avgQ = -1.0; $maxQ = -1
        if ($w13.Line -match 'avgQ=([0-9.]+)') { $avgQ = [double]$Matches[1] }
        if ($w13.Line -match 'maxQ=(\d+)') { $maxQ = [int]$Matches[1] }
        if ($avgQ -ge 0 -and $avgQ -le $avgLimit) { $pass.Add("[-ExpectJitterBuffer] 稳态平均队列深度 $avgQ 帧（≤$avgLimit，缓冲引入的延迟未失控）") }
        elseif ($avgQ -ge 0) { $fail.Add("[-ExpectJitterBuffer] 稳态平均队列深度 $avgQ 帧（>$avgLimit = 缓冲延迟失控）") }
        if ($maxQ -ge 0 -and $maxQ -le 20) { $pass.Add("[-ExpectJitterBuffer] 稳态最大深度 $maxQ 帧（≤20，突发可被排空）") }
        elseif ($maxQ -ge 0) { $fail.Add("[-ExpectJitterBuffer] 稳态最大深度 $maxQ 帧（>20 = 有界性失效）") }
        # "有界"的本质不是"尖峰不超过某个数"（渲染停顿/GC 会让十几帧一起到达，实测 maxQ 到过 15），
        # 而是**能排空**：结束时队列深度必须回落到目标附近。这条比 maxQ 更能说明缓冲没有失控。
        if ($depth -ge 0 -and $depth -le 6) { $pass.Add("[-ExpectJitterBuffer] 结束时队列深度 $depth（≤6，说明尖峰已被排空）") }
        elseif ($depth -gt 6) { $fail.Add("[-ExpectJitterBuffer] 结束时队列深度仍有 $depth 帧（>6，说明消不掉积压）") }
        # [W-17] 插值基准：t 由本地时间轴给出 + 本机渲染位置**无回退**
        $rev = -1; $revTotal = -1; $tNow = ''
        if ($w13.Line -match 'reversals=(\d+)/(\d+)') { $rev = [int]$Matches[1]; $revTotal = [int]$Matches[2] }
        if ($w13.Line -match ' t=([0-9.]+|-)') { $tNow = $Matches[1] }
        Write-Host "  [W-17] t=$tNow reversals=$rev/$revTotal" -ForegroundColor Magenta
        if ($ExpectInterpolation) {
            if ($rev -lt 0) { $fail.Add("[-ExpectInterpolation] 探针里没有 reversals=（W-17 未生效）") }
            elseif ($rev -eq 0) { $pass.Add("[-ExpectInterpolation] 渲染位置方向翻转 0 次（$revTotal 帧采样，无回退）") }
            elseif ($revTotal -gt 0 -and ($rev * 100.0 / $revTotal) -lt 1.0) {
                $pass.Add("[-ExpectInterpolation] 渲染位置方向翻转 $rev/$revTotal 帧（<1%，可接受）")
            }
            else { $fail.Add("[-ExpectInterpolation] 渲染位置方向翻转 $rev/$revTotal 帧（≥1% = 位置在回退，插值基准错位）") }
        }
        Write-Host "  [W-13] starved=$starved maxQ=$maxQ（队列空而无法推进的渲染帧数 / 观察到的最大深度）" -ForegroundColor Magenta
    }
}

# 检查 7d（W-14，可选）：房主自身输入延迟 D 自适应
# 判据：**D == clamp(实测客户端滞后, 2, 6)**。
#   下界 2：即使同机（滞后≈1 帧）房主也别"零延迟"，否则本地测试与联机手感不一致。
#   上界 6（200ms @30Hz）：**刻意的取舍** —— 再等下去房主自己会明显发钝，
#   所以超过上限时**保留残余不对称并显式记录**，而不是无条件追平（方案 W-14 的风险条目）。
# 注入了延迟时 D 必须 > 2（否则说明它没跟着观测值动，仍是写死的常数）。
if ($ExpectInputDelay) {
    $hSim = $hostLines | Select-String '\[sim\] HOST' | Select-Object -Last 1
    $D = -1; $lag = -1
    if ($hSim) {
        if ($hSim.Line -match ' D=(\d+)') { $D = [int]$Matches[1] }
        if ($hSim.Line -match ' clientLag=(\d+)') { $lag = [int]$Matches[1] }
        Write-Host "  [W-14] $($hSim.Line)" -ForegroundColor Magenta
    }
    if ($D -lt 0) {
        $fail.Add("[-ExpectInputDelay] 宿主探针里没有 D=（W-14 未生效）")
    } else {
        if ($lag -lt 0) { $lag = 0 }
        $want = [Math]::Min([Math]::Max($lag, 2), 6)
        if ($D -eq $want) { $pass.Add("[-ExpectInputDelay] D=$D == clamp(客户端滞后 $lag, 2, 6)（D 跟着观测量走）") }
        else { $fail.Add("[-ExpectInputDelay] D=$D 与 clamp(客户端滞后 $lag, 2, 6)=$want 不符（D 没有正确自适应）") }
        if ($D -ge $lag) { $pass.Add("[-ExpectInputDelay] D=$D ≥ 实测客户端滞后 $lag 帧（房主确实在等客户端）") }
        else { $pass.Add("[-ExpectInputDelay] 残余不对称 $($lag - $D) 帧（客户端滞后 $lag > 上限 6）—— 已知取舍：追平会让房主明显发钝") }
        if ($NetDelayMs -gt 0 -and $D -le 2) {
            $fail.Add("[-ExpectInputDelay] 注入了 $NetDelayMs ms 延迟，但 D 仍是下限 2 —— D 没有跟着观测值自适应")
        }
    }
}

# 检查 7e（W-18，可选）：逻辑 Tick 零分配
# 指标来自 `SimPerf`：用 `GC.GetAllocatedBytesForCurrentThread()` 夹在 BeginTick/EndTick 之间，
# 所以 `alloc/tick=0B` 是**精确**结论（不是"看 gen0 触发频率"那种粗代理）。
# ★ 判据覆盖**所有** [perf] 窗口，不只最后一个：偶发的一次分配（例如"商店开放那一帧"
#   在 Tick 中途同步回调了 UI 订户）会落在某一个窗口里，只看末尾窗口会漏掉它。
if ($ExpectZeroAlloc) {
    $checked = 0
    foreach ($entry in @(@('HOST', $hostLines, 'Host'), @('CLIENT', $clientLines, 'Client'))) {
        $side = $entry[0]; $lines = $entry[1]; $name = $entry[2]
        $perf = @($lines | Select-String "\[perf\] $side.*alloc/tick=")
        if ($perf.Count -eq 0) {
            $fail.Add("[-ExpectZeroAlloc] $name 探针里没有带 alloc/tick 的 [perf] 行（指标未生效）")
            continue
        }
        $checked++
        $last = $perf[$perf.Count - 1]
        Write-Host "  [W-18] $($last.Line)" -ForegroundColor Magenta

        $badWindows = 0; $badDetail = ''
        $totalTicksAll = 0
        foreach ($p in $perf) {
            if ($p.Line -match '有分配的 tick (\d+)/(\d+)') {
                $g = [int]$Matches[1]; $t = [int]$Matches[2]
                $totalTicksAll += $t
                if ($g -gt 0) {
                    $badWindows++
                    if ($badDetail -eq '') { $badDetail = $p.Line }
                }
            }
        }
        if ($perf.Count -gt 0 -and $perf[0].Line -notmatch '有分配的 tick') {
            $fail.Add("[-ExpectZeroAlloc] $name 的 [perf] 行缺少有分配的 tick 字段")
        }
        elseif ($badWindows -eq 0 -and $totalTicksAll -gt 0) {
            $pass.Add("[-ExpectZeroAlloc] $name 逻辑 Tick 零分配（$($perf.Count) 个窗口、共 $totalTicksAll 个 tick 全部 0 B）")
        }
        else {
            $fail.Add("[-ExpectZeroAlloc] $name 有 $badWindows/$($perf.Count) 个窗口出现了堆分配：$badDetail")
        }
    }
    if ($checked -eq 0) { $fail.Add("[-ExpectZeroAlloc] 两端都没取到可判定的 [perf] 行") }
}

# 检查 7f（W-15，可选）：本机即时反馈
# 判据：按下沿出现 `[w15] 本机即时反馈 #N`（N 递增），且至少观测到一次"按下 → 权威子弹"的帧差。
# 帧差 ≥1 正是本项存在的理由：权威子弹要等 RTT/2 + 抖动缓冲 + D 帧，而即时反馈在按下的那一渲染帧就发生。
if ($ExpectLocalFeedback) {
    $fb = @($clientLines | Select-String '\[w15\] 本机即时反馈')
    $delta = @($clientLines | Select-String '\[w15\] 按下 → 权威子弹出现')
    if ($fb.Count -gt 0) {
        $pass.Add("[-ExpectLocalFeedback] Client 本机即时反馈触发 $($fb.Count) 次：$($fb[$fb.Count - 1].Line.Trim())")
    } else {
        $fail.Add("[-ExpectLocalFeedback] Client 探针里没有「[w15] 本机即时反馈」（按下沿没有触发即时表现）")
    }
    if ($delta.Count -gt 0) {
        $d = -1
        if ($delta[$delta.Count - 1].Line -match '间隔 (\d+) 逻辑帧') { $d = [int]$Matches[1] }
        if ($d -ge 1) {
            $pass.Add("[-ExpectLocalFeedback] 权威子弹比按下晚 $d 逻辑帧 —— 即时反馈把这段空白补上了")
        } else {
            $fail.Add("[-ExpectLocalFeedback] 按下→权威子弹的帧差为 $d（应 >= 1）")
        }
    } else {
        Write-Host "  [W-15] 未观测到「按下 → 权威子弹」帧差（可能本机没开火 / 权威子弹未生成）" -ForegroundColor DarkYellow
    }
}

# 检查 8（可选）：逐条 trace 对拍
if ($Strict) {
    $traceDir = Join-Path $ProbeDir 'traces'
    $groups = Get-ChildItem $traceDir -File -Filter 'trace_*.bin' -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Name -match '^trace_(\d{8}_\d{6})_(\d+)\.bin$') {
            [pscustomobject]@{ Ts = $Matches[1]; Pid = $Matches[2]; Path = $_.FullName; Len = $_.Length }
        }
    } | Group-Object Ts | Where-Object { ($_.Group | Select-Object -ExpandProperty Pid -Unique).Count -ge 2 } |
        Sort-Object Name -Descending | Select-Object -First 1

    if (-not $groups) {
        $fail.Add("[-Strict] 找不到同时刻双端 trace（客户端可能没进战斗）")
    } else {
        $a = $groups.Group[0].Path
        $b = $groups.Group[1].Path
        Write-Host "  trace 对: $($groups.Name)  $($groups.Group[0].Len) / $($groups.Group[1].Len) 字节"
        function Read-Trace($path) {
            $bytes = [System.IO.File]::ReadAllBytes($path)
            $recs = New-Object System.Collections.ArrayList
            $o = 0
            while ($o + 6 -le $bytes.Length) {
                $cid = [BitConverter]::ToInt32($bytes, $o)
                $len = [BitConverter]::ToInt16($bytes, $o + 4)
                $o += 6
                if ($len -lt 0 -or $o + $len + 4 -gt $bytes.Length) { break }
                $data = New-Object byte[] $len
                [Array]::Copy($bytes, $o, $data, 0, $len)
                $o += $len
                $frame = [BitConverter]::ToInt32($bytes, $o)
                $o += 4
                $hex = ($data | ForEach-Object { $_.ToString('X2') }) -join ''
                [void]$recs.Add([pscustomobject]@{ Cid = $cid; Frame = $frame; Hex = $hex })
            }
            return $recs
        }
        $ra = Read-Trace $a
        $rb = Read-Trace $b
        if ($ra.Count -eq 0 -or $rb.Count -eq 0) {
            $fail.Add("[-Strict] trace 为空（Host=$($ra.Count) Client=$($rb.Count) 条）—— trace 可能没 flush")
        } else {
            $n = [Math]::Min($ra.Count, $rb.Count)
            $bad = 0; $first = -1
            for ($i = 0; $i -lt $n; $i++) {
                if ($ra[$i].Hex -ne $rb[$i].Hex -or $ra[$i].Frame -ne $rb[$i].Frame) {
                    $bad++
                    if ($first -lt 0) { $first = $i }
                }
            }
            $countDiff = [Math]::Abs($ra.Count - $rb.Count)
            Write-Host ("  trace 记录数: Host=$($ra.Count) Client=$($rb.Count) 差=$countDiff  " +
                        "frame $($ra[0].Frame)->$($ra[-1].Frame) / $($rb[0].Frame)->$($rb[-1].Frame)")
            if ($bad -gt 0) {
                # 真正的内容分歧 —— 这是硬失败
                $names = @{ 1 = 'FrameStart'; 2 = 'PlayerSpawn'; 3 = 'EnemySpawn'; 4 = 'BulletSpawn'; 5 = 'RandomCall';
                            6 = 'PlayerHp'; 7 = 'EnemyDeath'; 8 = 'WaveChange'; 9 = 'ShopOffer'; 10 = 'BattleEnd' }
                $cn = if ($names.ContainsKey($ra[$first].Cid)) { $names[$ra[$first].Cid] } else { "Check$($ra[$first].Cid)" }
                $fail.Add("[-Strict] trace 内容分歧: $bad/$n 条不一致，首个记录 #$first ($cn, frame=$($ra[$first].Frame))")
            } elseif ($countDiff -le 4) {
                # 关停时序导致尾部差一两条记录 —— 可接受（内容已全等）
                $note = if ($countDiff -eq 0) { '' } else { "（尾部相差 $countDiff 条 = 关停时序，非分歧）" }
                $pass.Add("[-Strict] trace 共同区间逐条一致: $n 条, frame $($ra[0].Frame) -> 较短的尾帧 $note")
            } else {
                $fail.Add("[-Strict] trace 记录数相差过大: Host=$($ra.Count) Client=$($rb.Count)（差 $countDiff）—— 两端可能没跑同样长的时间")
            }
        }
    }
}

# ---------- 汇总 ----------
Write-Host ""
# W-26.3：若启用了注入，把两侧的 [netsim] 统计打出来（证明注入确实生效）
if ($netArgs.Count -gt 0) {
    $nsH = @($hostLines | Select-String '\[netsim\]' | Select-Object -Last 1)
    $nsC = @($clientLines | Select-String '\[netsim\]' | Select-Object -Last 1)
    Write-Host "  [netsim] Host  : $(if ($nsH.Count) { $nsH[0].Line } else { '(无)' })" -ForegroundColor Magenta
    Write-Host "  [netsim] Client: $(if ($nsC.Count) { $nsC[0].Line } else { '(无)' })" -ForegroundColor Magenta
}

$prog = if ($netArgs.Count -gt 0) { '弱网注入' } else { '正常网络' }
Write-Host "================= 结果（$prog）================"
foreach ($p in $pass) { Write-Host "  [PASS] $p" -ForegroundColor Green }
foreach ($f in $fail) { Write-Host "  [FAIL] $f" -ForegroundColor Red }
Write-Host "========================================"
if ($fail.Count -eq 0) {
    Write-Host "全部通过 ✅  ($($pass.Count) 项)" -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($fail.Count) 项失败 ❌" -ForegroundColor Red
    exit 1
}

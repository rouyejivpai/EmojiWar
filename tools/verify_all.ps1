# =============================================================================
# EmojiWar2 —— 一键跑完全部门禁（W-19 追加）
#
# 为什么需要它：本项目有 5 条独立门禁，各自是独立进程 + 独立脚本。
#   实测踩过一次：**W-20 给模拟加了 7 个字段后漏跑了 `-hashguard`**，
#   直到下一轮才发现（守门测试立刻报"7 条字段路径没有登记"）。
#   门禁只要不是"一条命令"，就一定会有人忘 —— 所以把它收敛成一条命令。
#
# 用法：
#   pwsh -File tools/verify_all.ps1                     # 全套（约 6~8 分钟）
#   pwsh -File tools/verify_all.ps1 -Quick              # 跳过耗时最长的回放门禁
#   pwsh -File tools/verify_all.ps1 -Seconds 40         # 缩短双实例等待
#   pwsh -File tools/verify_all.ps1 -CrossBackend       # 追加 W-11 跨后端（Mono/IL2CPP）逐帧哈希一致性
#
# 退出码：0 = 全绿；非 0 = 有门禁失败（末尾会汇总哪几步红了）。
# =============================================================================

[CmdletBinding()]
param(
    [int]$Seconds = 70,
    [switch]$Quick,
    [switch]$KeepProbes,
    [switch]$CrossBackend
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot 'EmojiWar2'
$exe = Join-Path $proj 'Builds\StandaloneWindows64\EmojiWar2.exe'
$probeDir = Join-Path $proj 'Builds\StandaloneWindows64\Logs'

if (-not (Test-Path $exe)) { Write-Host "找不到构建产物: $exe（先跑菜单 EmojiWar/Tools/Build Windows64 Player）" -ForegroundColor Red; exit 2 }

$results = New-Object System.Collections.Generic.List[object]

function Add-Result($name, $ok, $detail) {
    $script:results.Add([pscustomobject]@{ Name = $name; Ok = $ok; Detail = $detail })
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1} {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail) -ForegroundColor $color
}

function Run-SelfTest($name, $arg, $pattern) {
    Write-Host ""
    Write-Host "===== $name（$arg）=====" -ForegroundColor Cyan
    $before = @(Get-ChildItem $probeDir -File -Filter 'runtime_probe_*.txt' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    $p = Start-Process $exe -ArgumentList $arg -WorkingDirectory (Split-Path $exe) -PassThru
    Start-Sleep -Seconds 26
    $probe = Join-Path $probeDir "runtime_probe_$($p.Id).txt"
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $probe)) { Add-Result $name $false "没有生成探针"; return }
    $lines = Get-Content $probe -Encoding UTF8
    $bad = @($lines | Where-Object { $_ -match $pattern })
    if ($bad.Count -eq 0) { Add-Result $name $true "无 FAIL" }
    else { Add-Result $name $false ("$($bad.Count) 条： " + $bad[0]) }
}

Write-Host "================= EmojiWar2 全门禁 =================" -ForegroundColor Cyan
Write-Host "产物: $exe"

# 1) 逻辑帧零分配 + 双实例确定性 + 缓冲/插值/输入延迟
Write-Host ""
Write-Host "===== 1) 双实例门禁（零分配 / 抖动缓冲 / 插值 / 输入延迟）=====" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'verify_determinism.ps1') -Seconds $Seconds -ExpectZeroAlloc -ExpectJitterBuffer -ExpectInterpolation -ExpectInputDelay
Add-Result "双实例门禁" ($LASTEXITCODE -eq 0) "verify_determinism.ps1"

# 2) 状态哈希守门（覆盖性 + 变更验证）
Run-SelfTest "状态哈希守门" '-hashguard' '\[HashGuard\] FAIL'

# 3) 配置哈希自检（区域无关 / 结构体递归 / 毫秒语义）
Run-SelfTest "配置哈希自检" '-configselftest' '\[ConfigTest\] FAIL'

# 4) 施法自检（按秒断言，帧率无关）
Run-SelfTest "施法自检" '-autospell' '\[SpellTest\] FAIL'

# 5) 无头回放（确定性回归的地基）
if (-not $Quick) {
    Write-Host ""
    Write-Host "===== 5) 无头回放门禁 =====" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'verify_replay.ps1')
    Add-Result "无头回放门禁" ($LASTEXITCODE -eq 0) "verify_replay.ps1"
} else {
    Add-Result "无头回放门禁" $true "（-Quick 跳过）"
}

# 6) [W-11] 跨后端浮点一致性（Mono / IL2CPP-Dev / IL2CPP-Release 同一录像逐帧哈希必须相同）
#    默认不跑：它要对**每个已存在的后端产物**各跑一遍完整回放（每个 1~4 分钟），
#    且需要先手工打 IL2CPP 包；只有显式 -CrossBackend 时才跑（发布前 / 改动模拟层数学时跑）。
if ($CrossBackend) {
    Write-Host ""
    Write-Host "===== 6) 跨后端一致性门禁（W-11）=====" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'verify_crossbackend.ps1')
    Add-Result "跨后端一致性门禁" ($LASTEXITCODE -eq 0) "verify_crossbackend.ps1"
} else {
    Add-Result "跨后端一致性门禁" $true "（未传 -CrossBackend 跳过；改动模拟层数学后应显式跑）"
}

Write-Host ""
Write-Host "================= 汇总 =================" -ForegroundColor Cyan
$failed = @($results | Where-Object { -not $_.Ok })
foreach ($r in $results) {
    $color = if ($r.Ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}  {2}" -f ($(if ($r.Ok) { 'PASS' } else { 'FAIL' })), $r.Name, $r.Detail) -ForegroundColor $color
}
if ($failed.Count -eq 0) {
    Write-Host "全部门禁通过 ✅  ($($results.Count) 项)" -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($failed.Count) 项门禁失败 ❌" -ForegroundColor Red
    exit 1
}

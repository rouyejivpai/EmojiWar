# =============================================================================
# EmojiWar2 - 无头回放门禁（W-03 / 报告 B4）
#
# 用途：把"同一段录像多次回放，哈希完全一致"从**手工 CLI** 变成可回归的门禁。
#       清单 §4 的验收指标之一 —— 也是 M1/M2/M3 所有改动"没有破坏确定性"的最强证据。
#
# 用法：
#   & tools/verify_replay.ps1                       # 用最新一局录像，跑两次回放
#   & tools/verify_replay.ps1 -ReplayPath <path>    # 指定录像
#   & tools/verify_replay.ps1 -MaxFrames 500        # 只回放前 N 帧（快速冒烟）
#
# 通过判据（全部必须满足）：
#   1. 探针出现 `[replay] run1 PASS`            —— 回放逐帧哈希与录像一致（= 能复现这一局）
#   2. 探针出现 `[replay] run2 PASS ... 序列一致=True` —— 同一录像跑两次，逐帧哈希序列相同
#   3. 探针出现 `[replay] FINAL PASS`
#   4. 不出现 `[replay] FINAL FAIL`
#
# 退出码：0 = 全部通过；1 = 有失败项。
#
# 为什么必须在**构建版**里跑：装备 Id → CastProgram 的重建需要物品数据表
# （ConfigItemTable 依赖 ConfigService/GameEntry.Data，不是自包含的；EditMode 里没这张表）。
# =============================================================================

[CmdletBinding()]
param(
    [string]$ReplayPath = '',
    [int]$Seconds = 75,
    [string]$ProbeDir = '',
    [switch]$KeepProcesses
)

$ErrorActionPreference = 'Stop'
$fail = New-Object System.Collections.Generic.List[string]
$pass = New-Object System.Collections.Generic.List[string]

# ---------- 定位项目 ----------
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot 'EmojiWar2'
if (-not (Test-Path $proj)) { throw "找不到项目目录: $proj" }
$exe = Join-Path $proj 'Builds\StandaloneWindows64\EmojiWar2.exe'
if (-not (Test-Path $exe)) { throw "找不到构建产物: $exe（先跑菜单 EmojiWar/Tools/Build Windows64 Player）" }
if ([string]::IsNullOrWhiteSpace($ProbeDir)) { $ProbeDir = Join-Path $proj 'Builds\StandaloneWindows64\Logs' }
$replayDir = Join-Path $ProbeDir 'replays'

# ---------- 产物新鲜度（用 Data/Managed 的 dll，不用 .exe 启动器壳）----------
$builtDll = Join-Path $proj 'Builds\StandaloneWindows64\EmojiWar2_Data\Managed\EmojiWar.GameMain.dll'
$newestCs = Get-ChildItem (Join-Path $proj 'Assets') -Recurse -File -Include *.cs |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (Test-Path $builtDll) {
    $bd = (Get-Item $builtDll).LastWriteTime
    if ($newestCs -and $newestCs.LastWriteTime -gt $bd) {
        Write-Host "[WARN] 构建产物比源码旧！产物=$bd 源码=$($newestCs.LastWriteTime) ($($newestCs.Name))" -ForegroundColor Yellow
        $fail.Add("构建产物落后于源码（$($newestCs.Name)）")
    } else {
        Write-Host "[ ok ] 构建产物新鲜: $bd" -ForegroundColor Green
    }
}

# ---------- 选录像 ----------
if ([string]::IsNullOrWhiteSpace($ReplayPath)) {
    if (-not (Test-Path $replayDir)) { throw "找不到录像目录: $replayDir" }
    $newest = Get-ChildItem $replayDir -File -Filter 'replay_*.bin' -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw "录像目录里没有 replay_*.bin: $replayDir" }
    $ReplayPath = $newest.FullName
}
if (-not (Test-Path $ReplayPath)) { throw "录像不存在: $ReplayPath" }
Write-Host "[ ok ] 录像: $(Split-Path $ReplayPath -Leaf) ($([math]::Round((Get-Item $ReplayPath).Length/1KB,1)) KB)" -ForegroundColor Green

# ---------- 清理与归档 ----------
Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 2
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$archive = Join-Path $ProbeDir "_prev_$stamp"
$old = Get-ChildItem $ProbeDir -File -Filter 'runtime_probe_*.txt' -ErrorAction SilentlyContinue
if ($old) {
    New-Item -ItemType Directory -Force -Path $archive | Out-Null
    $old | ForEach-Object { Move-Item $_.FullName (Join-Path $archive $_.Name) -Force }
    Write-Host "[ ok ] 归档旧探针 $($old.Count) 个"
}

# ---------- 跑回放（两次）----------
Write-Host ""
Write-Host "===== 启动回放: -autocreate -replay <file> -replaytwice ====="
$proc = Start-Process -FilePath $exe -ArgumentList @('-autocreate', '-replay', $ReplayPath, '-replaytwice') `
    -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "  pid=$($proc.Id)  等待 $Seconds 秒..."
Start-Sleep -Seconds $Seconds

if (-not $KeepProcesses) {
    Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }
    Start-Sleep -Seconds 5
    Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
}

# ---------- 读探针 ----------
$probe = Get-ChildItem $ProbeDir -File -Filter 'runtime_probe_*.txt' -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $probe) {
    $fail.Add("没有生成探针文件")
    $lines = @()
} else {
    Write-Host ""
    Write-Host "探针: $($probe.Name)"
    $lines = Get-Content $probe.FullName -Encoding UTF8
    $lines | Select-String -Pattern '\[replay\]' | ForEach-Object { Write-Host "  $($_.Line)" }
}

function Count-Of($ls, $pattern) { return @($ls | Select-String -Pattern $pattern).Count }

# 检查 0（W-08）：录像是不是**当前这份构建**录的
# 逐帧哈希 = f(哈希算法, 状态)。哈希覆盖的字段集一变（W-08 就改过一轮），旧录像从第 1 帧就对不上。
# 此时报"不同步/分歧"会把排查引向完全错误的方向 → 必须单独诊断为「旧录像，请重新录制」。
$fpMismatch = (Count-Of $lines '\[replay\] fingerprint\(rec\)=.* match=False') -gt 0
if ($fpMismatch) {
    $fp = $lines | Select-String '\[replay\] fingerprint\(rec\)=' | Select-Object -First 1
    Write-Host "  [W-08] $($fp.Line)" -ForegroundColor Magenta
}

# 检查 1：run1 逐帧哈希与录像一致
if ((Count-Of $lines '\[replay\] run1 PASS') -gt 0) { $pass.Add("run1 回放逐帧哈希与录像一致") }
elseif ((Count-Of $lines '无法重建 loadout') -gt 0) { $fail.Add("无法重建 loadout（物品表未就绪或数据不一致）—— 不是不同步") }
elseif ($fpMismatch) {
    # ⚠ PowerShell 5.1 不允许方法调用实参跨行拼接（`$x.Add("A"` 换行 `+ "B")` 是语法错误）→ 必须写在一行。
    $fail.Add("录像来自**另一个构建**（指纹不匹配）—— 这是旧录像，不是不同步。请先跑 tools/verify_determinism.ps1 用当前构建重新录一份，再回放")
}
else {
    $m = $lines | Select-String '\[replay\] run1 FAIL' | Select-Object -First 1
    $fail.Add("run1 回放与录像不一致：" + $(if ($m) { $m.Line } else { "未见 run1 结论行" }))
}

# 检查 2：两次回放的逐帧哈希序列相同
$run2 = $lines | Select-String '\[replay\] run2' | Select-Object -First 1
if ($run2) {
    if ($run2.Line -match '序列一致=True' -and $run2.Line -match 'run2 PASS') {
        $pass.Add("run2 两次回放逐帧哈希序列一致")
    } else {
        $fail.Add("run2 失败：" + $run2.Line)
    }
} else {
    $fail.Add("未见 run2 结论（-replaytwice 可能没被解析到 → 检查 AutoPlay 参数解析上界）")
}

# 检查 3：FINAL PASS
if ((Count-Of $lines '\[replay\] FINAL PASS') -gt 0) { $pass.Add("FINAL PASS") }
else { $fail.Add("未见 [replay] FINAL PASS") }

Write-Host ""
Write-Host "================= 结果 ================="
foreach ($p in $pass) { Write-Host "  [PASS] $p" -ForegroundColor Green }
foreach ($f in $fail) { Write-Host "  [FAIL] $f" -ForegroundColor Red }
Write-Host "========================================"
if ($fail.Count -eq 0) {
    Write-Host "回放门禁全部通过 ✅  ($($pass.Count) 项)" -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($fail.Count) 项失败 ❌" -ForegroundColor Red
    exit 1
}

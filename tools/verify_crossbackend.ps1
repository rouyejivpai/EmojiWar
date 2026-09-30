# =============================================================================
# EmojiWar2 - 跨后端浮点一致性门禁（W-11 的验收测试）
#
# 要回答的问题（清单 W-11「定点化」）：
#   **同一段录像，在 Mono / IL2CPP-Development / IL2CPP-Release 三种编译产物下回放，
#     逐帧哈希是否完全相同？**
#   若相同 → 当前 float 代码在本项目里**没有**跨编译器分歧（W-11 可降级/延后，
#             并留下本脚本作为"将来又出现分歧时"的复检手段）；
#   若不同 → W-11 成立，本脚本就是它的验收门禁（改为定点数后必须全绿）。
#
# 为什么用"回放"而不是"双实例对局"做跨后端对比：
#   录像里**逐帧存了录制时的状态哈希**。回放要求"每一帧都等于录像里的那一帧"。
#   于是：Mono 回放 PASS ⇒ Mono 逐帧 == 录制；IL2CPP 回放 PASS ⇒ IL2CPP 逐帧 == 录制；
#   两者**传递相等**，等价于直接对比两条逐帧哈希序列，且不需要 dump 出序列。
#
# 用法：
#   & tools/verify_crossbackend.ps1                       # 用最新录像，自动发现三种产物
#   & tools/verify_crossbackend.ps1 -ReplayPath <path>
#   & tools/verify_crossbackend.ps1 -TimeoutSec 600
#
# 产物目录约定（不存在就跳过并标注，不算失败 但会在结尾提示）：
#   Builds\StandaloneWindows64\EmojiWar2.exe    → Mono（编辑器的 Scripting Backend 也是 Mono）
#   Builds\EmojiWar2_il2cpp_dev\EmojiWar2.exe   → IL2CPP Development
#   Builds\EmojiWar2_il2cpp_rel\EmojiWar2.exe   → IL2CPP Release
#
# 退出码：0 = 至少两个后端跑完且逐帧哈希一致；1 = 有后端未跑完/哈希不一致/回放失败。
#
# ⚠ 指纹（fingerprint）跨后端**必然不同**（含 MVID = 每次编译都变），
#   它不是判定依据；判定依据是 run1/run2 的 PASS 与 finalHash。
# =============================================================================

[CmdletBinding()]
param(
    [string]$ReplayPath = '',
    [int]$TimeoutSec = 180,
    [string[]]$Only = @()      # 只跑指定标签，如 -Only Mono,IL2CPP-Dev
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot 'EmojiWar2'
if (-not (Test-Path $proj)) { throw "找不到项目目录: $proj" }
$builds = Join-Path $proj 'Builds'

# ---------- 选录像（默认取 Monitor 那份构建产出的最新录像）----------
if ([string]::IsNullOrWhiteSpace($ReplayPath)) {
    $dirs = @((Join-Path $builds 'StandaloneWindows64\Logs\replays'),
              (Join-Path $builds 'EmojiWar2_il2cpp_dev\Logs\replays'))
    $cands = @()
    foreach ($d in $dirs) {
        if (Test-Path $d) {
            $cands += Get-ChildItem $d -File -Filter 'replay_*.bin' -ErrorAction SilentlyContinue
        }
    }
    if ($cands.Count -eq 0) { throw "找不到任何 replay_*.bin（先跑一次双实例对局录制）" }
    $ReplayPath = ($cands | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not (Test-Path $ReplayPath)) { throw "录像不存在: $ReplayPath" }
Write-Host "[ ok ] 录像: $(Split-Path $ReplayPath -Leaf) ($([math]::Round((Get-Item $ReplayPath).Length/1KB,1)) KB)" -ForegroundColor Green
Write-Host "       最后写入: $((Get-Item $ReplayPath).LastWriteTime)"

# ---------- 后端清单 ----------
$backends = @(
    [pscustomobject]@{ Label = 'Mono';          Dir = (Join-Path $builds 'StandaloneWindows64') },
    [pscustomobject]@{ Label = 'IL2CPP-Dev';    Dir = (Join-Path $builds 'EmojiWar2_il2cpp_dev') },
    [pscustomobject]@{ Label = 'IL2CPP-Release';Dir = (Join-Path $builds 'EmojiWar2_il2cpp_rel') }
)
if ($Only.Count -gt 0) { $backends = @($backends | Where-Object { $Only -contains $_.Label }) }

$rows = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[string]

foreach ($b in $backends) {
    $exe = Join-Path $b.Dir 'EmojiWar2.exe'
    if (-not (Test-Path $exe)) {
        Write-Host "[skip] $($b.Label)：找不到 $exe" -ForegroundColor Yellow
        $skipped.Add("$($b.Label)（产物不存在：$($b.Dir)）")
        continue
    }

    Write-Host ""
    Write-Host "===== $($b.Label) 开始回放 =====" -ForegroundColor Cyan

    # 清掉可能残留的进程与旧日志
    Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    $logDir = Join-Path $b.Dir 'Logs'
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $log = Join-Path $logDir ("crossbackend_" + $b.Label + ".log")
    if (Test-Path $log) { Remove-Item $log -Force }

    $proc = Start-Process -FilePath $exe `
        -ArgumentList @('-logFile', $log, '-autocreate', '-replay', $ReplayPath, '-replaytwice') `
        -PassThru -WorkingDirectory $b.Dir

    # ★ 轮询等 FINAL：必须读**探针文件**，不是 -logFile 的日志 ——
    #   `[replay]` 行是 `AutoPlay.WriteProbe` 写进 `Logs/runtime_probe_<pid>.txt` 的，
    #   日志里根本没有（2026-09-29 实测：脚本误读日志后，三个后端全部"等满超时"，
    #   看上去像"IL2CPP 又崩了"，其实是**读错了文件** —— 这类"工具自己制造假故障"最耗时）。
    $probe = Join-Path $logDir ("runtime_probe_" + $proc.Id + ".txt")
    $elapsed = 0
    $final = $null
    while ($elapsed -lt $TimeoutSec) {
        Start-Sleep -Seconds 5
        $elapsed += 5
        if (Test-Path $probe) {
            $hit = Select-String -Path $probe -Pattern '\[replay\] FINAL ' -Encoding UTF8 -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { $final = $hit.Line; break }
        }
        $alive = $null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
        if (-not $alive -and -not $final) { Write-Host "  [warn] 进程已退出但未见 FINAL 行（可能崩溃）" -ForegroundColor Yellow; break }
        # 每 30s 报一次进度（含探针最后一行）：卡住时"卡在哪一行"就是最关键的归因线索
        # —— 上一轮 IL2CPP 崩溃正是靠"探针停在 configHash 之后"才定位到第一次 Tick。
        if (($elapsed % 30) -eq 0) {
            $tail = if (Test-Path $probe) { (Get-Content $probe -Encoding UTF8 -Tail 1) } else { '(探针未生成)' }
            Write-Host "    ...${elapsed}s  $tail" -ForegroundColor DarkGray
        }
    }

    $alive = $null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
    if ($alive) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name 'EmojiWar2*' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

    # 解析（从探针；探针缺失时把日志里的崩溃栈也带出来，便于归因）
    $lines = if (Test-Path $probe) { Get-Content $probe -Encoding UTF8 } else { @() }
    $replayLines = $lines | Select-String -Pattern '\[replay\]' | ForEach-Object { $_.Line }
    foreach ($l in $replayLines) { Write-Host "  $l" }
    if (@($replayLines).Count -eq 0) {
        Write-Host "  [warn] 探针里没有 [replay] 行，探针末尾 3 行：" -ForegroundColor Yellow
        if ($lines.Count -gt 0) { $lines | Select-Object -Last 3 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow } }
        else { Write-Host "    (探针文件不存在: $probe)" -ForegroundColor DarkYellow }
        $crash = Select-String -Path $log -Pattern 'Crash!!!|Exception|ReplayPlayer_Run' -Encoding UTF8 -ErrorAction SilentlyContinue | Select-Object -First 3
        foreach ($c in $crash) { Write-Host "    [log] $($c.Line)" -ForegroundColor DarkYellow }
    }

    $run1 = $replayLines | Where-Object { $_ -match '\[replay\] run1 ' } | Select-Object -First 1
    $run2 = $replayLines | Where-Object { $_ -match '\[replay\] run2 ' } | Select-Object -First 1

    $row = [pscustomobject]@{
        Label        = $b.Label
        Rows1Pass    = ($run1 -match 'run1 PASS')
        Frames       = -1
        FirstMismatch= -2
        FinalHash    = ''
        Run2Ok       = $false
        SeqSame      = $false
        ElapsedSec   = $elapsed
        Note         = ''
    }
    if ($run1 -match 'run1 (PASS|FAIL) frames=(\d+) firstMismatch=(-?\d+) finalHash=0x([0-9A-Fa-f]+)') {
        $row.Frames        = [int]$Matches[2]
        $row.FirstMismatch = [int]$Matches[3]
        $row.FinalHash     = $Matches[4].ToUpperInvariant()
    } else {
        $row.Note = '未见 run1 结论行（探针停在：' + $(if ($replayLines.Count -gt 0) { $replayLines[-1] } else { '(空)' }) + '）'
    }
    if ($run2 -match 'run2 (PASS|FAIL)') { $row.Run2Ok = ($Matches[1] -eq 'PASS') }
    if ($run2 -match '序列一致=(True|False)') { $row.SeqSame = ($Matches[1] -eq 'True') }
    if (-not $final) { $row.Note = ($row.Note + ' 未在 ' + $TimeoutSec + 's 内出现 FINAL').Trim() }

    $rows.Add($row)
}

# ---------- 汇总 ----------
Write-Host ""
Write-Host "================= 跨后端对比 ================="
$rows | Format-Table -AutoSize Label, Rows1Pass, Frames, FirstMismatch, FinalHash, Run2Ok, SeqSame, ElapsedSec
if ($skipped.Count -gt 0) {
    Write-Host "跳过的后端（不算失败，但 W-11 的完整验收需要三者齐备）：" -ForegroundColor Yellow
    foreach ($s in $skipped) { Write-Host "  - $s" -ForegroundColor Yellow }
}

$fail = New-Object System.Collections.Generic.List[string]
if ($rows.Count -lt 2) { $fail.Add("能跑的后端少于 2 个 → 无法做跨后端对比") }

foreach ($r in $rows) {
    if (-not $r.Rows1Pass) { $fail.Add("$($r.Label)：run1 未 PASS（firstMismatch=$($r.FirstMismatch)）$($r.Note)") }
    elseif ($r.FirstMismatch -ne -1) { $fail.Add("$($r.Label)：run1 报出首个分歧帧 $($r.FirstMismatch)") }
    if (-not $r.Run2Ok) { $fail.Add("$($r.Label)：run2 未 PASS") }
    if (-not $r.SeqSame) { $fail.Add("$($r.Label)：两次回放逐帧哈希序列不一致") }
}

$hashes = @($rows | ForEach-Object { $_.FinalHash } | Where-Object { $_ -ne '' } | Select-Object -Unique)
if ($hashes.Count -gt 1) {
    $fail.Add("跨后端 finalHash 不一致：" + (($rows | ForEach-Object { "$($_.Label)=0x$($_.FinalHash)" }) -join ' vs ') + " → 存在跨编译器浮点分歧（W-11 成立）")
} elseif ($hashes.Count -eq 1 -and $rows.Count -ge 2) {
    Write-Host "跨后端 finalHash 全部相同：0x$($hashes[0])  → 未发现跨编译器分歧" -ForegroundColor Green
}

$frameSet = @($rows | ForEach-Object { $_.Frames } | Where-Object { $_ -ge 0 } | Select-Object -Unique)
if ($frameSet.Count -gt 1) { $fail.Add("跨后端回放帧数不同：$($frameSet -join ', ')") }

Write-Host "=============================================="
if ($fail.Count -eq 0) {
    Write-Host "跨后端一致性门禁通过 ✅  (后端数=$($rows.Count) 帧数=$($frameSet -join ',') finalHash=0x$($hashes[0]))" -ForegroundColor Green
    exit 0
} else {
    foreach ($f in $fail) { Write-Host "  [FAIL] $f" -ForegroundColor Red }
    Write-Host "$($fail.Count) 项失败 ❌" -ForegroundColor Red
    exit 1
}

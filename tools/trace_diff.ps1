# =============================================================================
# EmojiWar2 - 确定性 trace 对拍（报告 B2 / W-10）
#
# 用途：把两端（或两局）的 DeterminismTracer 二进制 trace 对比，输出**第一个有效分歧**。
#       这是"发现不同步之后定位根因"的主力工具。
#
# 用法：
#   & tools/trace_diff.ps1 -A <trace_a.bin> -B <trace_b.bin>
#   & tools/trace_diff.ps1 -DirA <目录A> -DirB <目录B>     # 自动取各自最新的 trace
#   & tools/trace_diff.ps1 -PairTs 20260928_192000 -DirA <目录>   # 按时间戳前缀找成对文件
#
# 相比 `tools/trace_diff.py`（按**记录序号**对齐）的关键改进：
#   原实现一条记录缺失/多余就会让之后**全部错位**，报出的"首个分歧"完全不可信。
#   本脚本按 **(CheckID, 帧号, 同帧内第几次出现)** 作为键对齐：
#     · 只在一侧出现的记录 → 报"缺失/多余"，不会污染其余记录的比对；
#     · 两侧都有的键但数据不同 → 真分歧，按帧号从小到大报第一个；
#     · 顺带输出各 CheckID 的计数表（一眼看出"某个检查点根本没写"）。
#
# 记录格式（DeterminismTracer.cs）：[CheckID:int32][len:int16][data][frame:int32]，小端。
#
# 退出码：0 = 完全一致；1 = 有分歧或缺漏；2 = 参数/文件问题。
# =============================================================================

[CmdletBinding()]
param(
    [string]$A = '',
    [string]$B = '',
    [string]$DirA = '',
    [string]$DirB = '',
    [string]$PairTs = '',
    [int]$MaxPrint = 10,
    # 一侧"落后未到"的帧数上限（超过说明落后得离谱，值得告警）。默认 30 帧 ≈ 1 秒 @30Hz。
    [int]$Tolerance = 30
)

$ErrorActionPreference = 'Stop'

$CheckNames = @{
    1 = 'FrameStart'; 2 = 'PlayerSpawn'; 3 = 'EnemySpawn'; 4 = 'BulletSpawn'; 5 = 'RandomCall';
    6 = 'PlayerHp'; 7 = 'EnemyDeath'; 8 = 'WaveChange'; 9 = 'ShopOffer'; 10 = 'BattleEnd'
}
function CheckName([int]$cid) { if ($CheckNames.ContainsKey($cid)) { return $CheckNames[$cid] } return "Check$cid" }

# ---------- 解析 ----------
function Read-Trace([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $recs = New-Object System.Collections.ArrayList
    $o = 0
    while ($o + 6 -le $bytes.Length) {
        $cid = [BitConverter]::ToInt32($bytes, $o)
        $len = [BitConverter]::ToInt16($bytes, $o + 4)
        $o += 6
        if ($len -lt 0 -or $o + $len + 4 -gt $bytes.Length) { break }   # 截断（未 flush 的尾帧）
        $data = New-Object byte[] $len
        [Array]::Copy($bytes, $o, $data, 0, $len)
        $o += $len
        $frame = [BitConverter]::ToInt32($bytes, $o)
        $o += 4
        $vals = @()
        for ($i = 0; $i + 4 -le $len; $i += 4) { $vals += [BitConverter]::ToInt32($data, $i) }
        [void]$recs.Add([pscustomobject]@{
            Cid = $cid; Frame = $frame
            Hex = (($data | ForEach-Object { $_.ToString('X2') }) -join '')
            Vals = ($vals -join ',')
        })
    }
    return $recs
}

# ---------- 解析输入路径 ----------
function Newest-Trace([string]$dir) {
    $f = Get-ChildItem $dir -File -Filter 'trace_*.bin' -ErrorAction SilentlyContinue |
         Where-Object { $_.Length -gt 0 } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($f) { return $f.FullName }
    return ''
}
function Pair-Trace([string]$ts) {
    $f = Get-ChildItem 'D:\EmojiWarStudio\EmojiWar2\Builds\StandaloneWindows64\Logs\traces' -File -Filter "trace_$ts`_*.bin" -ErrorAction SilentlyContinue |
         Where-Object { $_.Length -gt 0 } | Sort-Object Name
    return $f
}

if (-not $A -and $DirA) { $A = Newest-Trace $DirA }
if (-not $B -and $DirB) { $B = Newest-Trace $DirB }
if ($PairTs) {
    $pair = Pair-Trace $PairTs
    if ($pair -and $pair.Count -ge 2) { $A = $pair[0].FullName; $B = $pair[1].FullName }
    elseif ($pair -and $pair.Count -eq 1) { Write-Host "[WARN] 只找到 1 个匹配 $PairTs 的 trace" -ForegroundColor Yellow; $A = $pair[0].FullName }
}
if (-not $A -or -not (Test-Path $A)) { Write-Host "找不到 trace A（-A 或 -DirA）" -ForegroundColor Red; exit 2 }
if (-not $B -or -not (Test-Path $B)) { Write-Host "找不到 trace B（-B 或 -DirB）" -ForegroundColor Red; exit 2 }

Write-Host "A = $A  ($([math]::Round((Get-Item $A).Length/1KB,1)) KB)"
Write-Host "B = $B  ($([math]::Round((Get-Item $B).Length/1KB,1)) KB)"

$ra = Read-Trace $A
$rb = Read-Trace $B
Write-Host "记录数: A=$($ra.Count)  B=$($rb.Count)"
if ($ra.Count -gt 0) { Write-Host "帧范围: A $($ra[0].Frame) -> $($ra[-1].Frame)   B $($rb[0].Frame) -> $($rb[-1].Frame)" }

# ---------- 各 CheckID 计数（一眼看出"某个检查点根本没写"）----------
Write-Host ""
Write-Host "---- 各检查点计数（A / B）----"
$allCids = @($ra.Cid + $rb.Cid | Sort-Object -Unique)
foreach ($cid in $allCids) {
    $ca = @($ra | Where-Object { $_.Cid -eq $cid }).Count
    $cb = @($rb | Where-Object { $_.Cid -eq $cid }).Count
    $mark = if ($ca -eq $cb) { ' ' } else { ' *' }
    Write-Host ("  {0,-12} {1,7} / {2,-7}{3}" -f (CheckName $cid), $ca, $cb, $mark)
}
Write-Host "  （* = 两侧计数不同）"

# ---------- 按 (CheckID, 帧号, 同帧序号) 建索引 ----------
function Build-Index($recs) {
    $idx = @{}
    $seen = @{}
    foreach ($r in $recs) {
        $base = "$($r.Cid)|$($r.Frame)"
        $n = 0
        if ($seen.ContainsKey($base)) { $n = $seen[$base] }
        $seen[$base] = $n + 1
        $idx["$base|$n"] = $r
    }
    return $idx
}
$ia = Build-Index $ra
$ib = Build-Index $rb

$onlyA = @($ia.Keys | Where-Object { -not $ib.ContainsKey($_) })
$onlyB = @($ib.Keys | Where-Object { -not $ia.ContainsKey($_) })

function Sort-KeysByFrame($keys) {
    # ★ 必须用 @() 强制成数组：只有 1 个元素时 Sort-Object 返回**标量**，
    #   调用方再写 $sorted[0] 就会取到字符串的第一个字符（实测踩过：显示成空的 `Check0 @ frame=`）。
    return @($keys | Sort-Object { [int]($_ -split '\|')[1] }, { [int]($_ -split '\|')[0] }, { [int]($_ -split '\|')[2] })
}

$diffs = New-Object System.Collections.ArrayList
foreach ($k in $ia.Keys) {
    if ($ib.ContainsKey($k) -and $ia[$k].Hex -ne $ib[$k].Hex) { [void]$diffs.Add($k) }
}

# ---------- 报告 ----------
# ★ 判定规则（W-13/W-14 之后重写）：**按"共同帧上界"判定，不再用固定条数容差。**
#
#   客户端**有意落后**（抖动缓冲 + 网络延迟 + 房主输入延迟 D），所以收尾那一刻它还没 tick 到
#   房主已经跑过的那些帧 —— 那些记录**在客户端根本还不存在**，不是"分歧"。
#   固定条数容差（原来 4，后来 12）在这件事上必然过时：同机落后 6 帧、注入 150ms 双向后落后 18 帧，
#   同一个数字无法同时描述两种场景。
#
#   新规则：
#     · 帧号 <= min(两侧最大帧) 的记录里，任何单侧多出 / 数据不等 => **真分歧 → exit 1**
#     · 帧号 >  共同上界的记录 => **落后未到**（统计条数与最大落后帧数，不判失败；超过上限才告警）
$maxFrameA = 0; $maxFrameB = 0
foreach ($r in $ra) { if ($r.Frame -gt $maxFrameA) { $maxFrameA = $r.Frame } }
foreach ($r in $rb) { if ($r.Frame -gt $maxFrameB) { $maxFrameB = $r.Frame } }
$commonMax = [Math]::Min($maxFrameA, $maxFrameB)

$behindA = 0; $behindB = 0; $realOnlyA = 0; $realOnlyB = 0
foreach ($k in $onlyA) { if ($ia[$k].Frame -gt $commonMax) { $behindA++ } else { $realOnlyA++ } }
foreach ($k in $onlyB) { if ($ib[$k].Frame -gt $commonMax) { $behindB++ } else { $realOnlyB++ } }
$realOnly = $realOnlyA + $realOnlyB
$behindLag = [Math]::Max($behindA, $behindB)
$tolerated = ($realOnly -eq 0) -and ($behindLag -le $Tolerance)

$exitCode = 0
if ($onlyA.Count -gt 0 -or $onlyB.Count -gt 0) {
    if (-not $tolerated) { $exitCode = 1 }
    Write-Host ""
    Write-Host ("---- 只在一侧出现的记录（共同帧上界 = {0}）----" -f $commonMax) -ForegroundColor Yellow
    if ($realOnly -gt 0) {
        Write-Host ("  ★ 共同区间内单侧多出 {0} 条（A 侧 {1} / B 侧 {2}）—— 这才是真分歧" -f $realOnly, $realOnlyA, $realOnlyB) -ForegroundColor Red
    }
    if ($behindLag -gt 0) {
        Write-Host ("  一侧落后未到 {0} 条（A 侧 {1} / B 侧 {2}，最大落后 {3} 帧）—— 收尾时序，不是分歧" -f ($behindA + $behindB), $behindA, $behindB, $behindLag) -ForegroundColor DarkYellow
    }
    $sk = @(Sort-KeysByFrame $onlyA)
    $lim = [Math]::Min($MaxPrint, $sk.Count)
    for ($i = 0; $i -lt $lim; $i++) {
        $r = $ia[$sk[$i]]
        Write-Host ("  仅在 A: {0} frame={1} vals=[{2}]" -f (CheckName $r.Cid), $r.Frame, $r.Vals) -ForegroundColor Yellow
    }
    if ($sk.Count -gt $MaxPrint) { Write-Host "  ...（A 侧共 $($sk.Count) 条）" -ForegroundColor Yellow }
    $sk2 = @(Sort-KeysByFrame $onlyB)
    $lim2 = [Math]::Min($MaxPrint, $sk2.Count)
    for ($i = 0; $i -lt $lim2; $i++) {
        $r = $ib[$sk2[$i]]
        Write-Host ("  仅在 B: {0} frame={1} vals=[{2}]" -f (CheckName $r.Cid), $r.Frame, $r.Vals) -ForegroundColor Yellow
    }
    if ($sk2.Count -gt $MaxPrint) { Write-Host "  ...（B 侧共 $($sk2.Count) 条）" -ForegroundColor Yellow }
}

if ($diffs.Count -gt 0) {
    $exitCode = 1
    $sd = @(Sort-KeysByFrame $diffs)
    Write-Host ""
    Write-Host "===== 第一个有效分歧 =====" -ForegroundColor Red
    $k0 = $sd[0]
    $a0 = $ia[$k0]; $b0 = $ib[$k0]
    Write-Host ("  {0} @ frame={1}" -f (CheckName $a0.Cid), $a0.Frame) -ForegroundColor Red
    Write-Host ("    A: vals=[{0}]  hex={1}" -f $a0.Vals, $a0.Hex)
    Write-Host ("    B: vals=[{0}]  hex={1}" -f $b0.Vals, $b0.Hex)
    Write-Host ""
    Write-Host "  共 $($diffs.Count) 处数据分歧，前 $([Math]::Min($MaxPrint,$sd.Count)) 处："
    $lim3 = [Math]::Min($MaxPrint, $sd.Count)
    for ($i = 0; $i -lt $lim3; $i++) {
        $a = $ia[$sd[$i]]; $b = $ib[$sd[$i]]
        Write-Host ("    {0,-12} frame={1,-6} A=[{2}]  B=[{3}]" -f (CheckName $a.Cid), $a.Frame, $a.Vals, $b.Vals)
    }
    Write-Host ""
    Write-Host ">>> 这是「状态注入点」：该检查点之后状态开始不同。" -ForegroundColor Red
} else {
    Write-Host ""
    Write-Host ">>> 两侧 trace 逐条（按 CheckID+帧号+序号 对齐）完全一致 ✅" -ForegroundColor Green
    if ($onlyA.Count -eq 0 -and $onlyB.Count -eq 0) {
        Write-Host "    （记录数也完全相同：A=$($ra.Count) B=$($rb.Count)）" -ForegroundColor Green
    } elseif ($tolerated) {
        Write-Host ("    （共同帧上界 {0} 内完全一致；一侧落后未到 {1} 条、最大落后 {2} 帧 —— 收尾时序，判定通过）" -f $commonMax, ($behindA + $behindB), $behindLag) -ForegroundColor Green
    } else {
        Write-Host ("    ★ 共同帧上界 {0} 内有 {1} 条单侧记录（真分歧），或落后 {2} 帧超过上限 {3} —— 判定失败" -f $commonMax, $realOnly, $behindLag, $Tolerance) -ForegroundColor Red
    }
}

exit $exitCode

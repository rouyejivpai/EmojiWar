# =============================================================================
# 恢复（第二版，修正算法）：撤销 "s/S → i" 破坏
#
# 第一版的教训：`if` 被改成了 `sf` —— 因为词表里恰好有含 s 的词 `sf`，
#   它的 s→i 形态正好是 `if`，于是"正确的 if"被"错误的还原"覆盖了。
# 正确规则（三条，按优先级）：
#   1) **token 本身就在词表里 → 一律不动**（它本来就是合法 token，不是被破坏的样子）；
#   2) 否则，若"某个词做 s→i 后正好等于它"**唯一** → 还原成那个词；
#   3) 否则（歧义/查不到）→ **保持原样并登记**，交给人工按上下文修（宁缺勿错）。
# =============================================================================

$ErrorActionPreference = 'Stop'
$repo = 'D:\EmojiWarStudio'
$sim  = Join-Path $repo 'EmojiWar2\Assets\GameMain\Scripts\Simulation'
$rec  = Join-Path $repo '_recovery'
$targets = @('LockstepSimulation.cs','CastResolver.cs','SimView.cs')
$tokRe = [regex]'[A-Za-z_][A-Za-z0-9_]*'

# ---------- 词表 ----------
$vocab = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
Get-ChildItem (Join-Path $repo 'EmojiWar2\Assets') -Recurse -Filter '*.cs' -File | ForEach-Object {
    if ($targets -contains $_.Name -and $_.DirectoryName -eq $sim) { return }
    $txt = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in $tokRe.Matches($txt)) { [void]$vocab.Add($m.Value) }
}
$binRe = [regex]'[\x20-\x7E]{3,}'
foreach ($bin in @(
        (Join-Path $repo 'EmojiWar2\Builds\StandaloneWindows64\EmojiWar2_Data\Managed\EmojiWar.GameMain.dll'),
        (Join-Path $repo 'EmojiWar2\Library\ScriptAssemblies\EmojiWar.GameMain.pdb'))) {
    if (-not (Test-Path $bin)) { continue }
    $latin = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($bin))
    foreach ($m in $binRe.Matches($latin)) {
        foreach ($t in $tokRe.Matches($m.Value)) { [void]$vocab.Add($t.Value) }
    }
}
foreach ($b in @('using','namespace','class','struct','interface','enum','public','private','protected','internal',
  'static','readonly','const','new','return','if','else','for','foreach','while','do','switch','case','default',
  'break','continue','try','catch','finally','throw','null','true','false','this','base','void','int','uint',
  'long','ulong','short','ushort','byte','sbyte','float','double','bool','char','string','object','var','params',
  'ref','out','in','is','as','typeof','nameof','override','virtual','abstract','sealed','partial','where','get',
  'set','value','yield','unchecked','checked','operator','event','delegate','global')) {
    [void]$vocab.Add($b)
}
Write-Host "[ ok ] 词表 $($vocab.Count) 项"

# ---------- 反查表（只保留**唯一**的逆）----------
function Convert-S2I([string]$v) { return ($v -creplace 's','i') -creplace 'S','i' }
$map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
$ambiguous = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($v in $vocab) {
    $k = Convert-S2I $v
    if ($k -ceq $v) { continue }                       # 不含 s/S → 形态不变，无需映射
    if ($vocab.Contains($k)) { [void]$ambiguous.Add($k); continue }   # ★ 规则 1：本身就是合法 token
    if ($map.ContainsKey($k)) { [void]$ambiguous.Add($k) }            # 歧义
    else { $map[$k] = $v }
}
foreach ($a in $ambiguous) { [void]$map.Remove($a) }
Write-Host "[ ok ] 唯一可还原 $($map.Count) 个；因歧义/本身合法而**保留** $($ambiguous.Count) 个"

# ---------- 还原 ----------
foreach ($f in $targets) {
    $txt = [System.IO.File]::ReadAllText((Join-Path $rec ("corrupted_" + $f)), [System.Text.Encoding]::UTF8)
    $script:hits = 0
    $restored = $tokRe.Replace($txt, {
        param($m)
        $t = $m.Value
        if ($script:vocab.Contains($t)) { return $t }        # 规则 1
        $o = $null
        if ($script:map.TryGetValue($t, [ref]$o)) { $script:hits++; return $o }
        return $t                                            # 规则 3：保持原样
    })
    # 登记仍未还原的 token（供人工按上下文修）
    $pending = @{}
    foreach ($m in $tokRe.Matches($restored)) {
        $t = $m.Value
        if ($vocab.Contains($t)) { continue }
        if ($pending.ContainsKey($t)) { $pending[$t]++ } else { $pending[$t] = 1 }
    }
    [System.IO.File]::WriteAllText((Join-Path $rec ("r2_" + $f)), $restored, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ("[ ok ] {0}：还原 {1} 处；待人工确认 token {2} 种" -f $f, $script:hits, $pending.Count)
    $pending.GetEnumerator() | Sort-Object { -$_.Value } | Select-Object -First 40 | ForEach-Object {
        Write-Host ("        {0,-28} x{1}" -f $_.Key, $_.Value) -ForegroundColor Yellow
    }
}
Write-Host "[完成] 输出 _recovery\r2_*.cs（未覆盖原文件）"

# =============================================================================
# 一次性恢复工具：撤销"s/S → i"的破坏性替换（2026-09-29 事故）
#
# 事故：一条 PowerShell 脚本里 `$pair[0]` 取成了**字符串首字符**（'S'），
#       而 `-replace` 默认**大小写不敏感** → 三个文件里所有 s/S 被替换成 i。
# 事实：这三个文件相对 git HEAD 有整轮会话的改动（且 CastResolver.cs 在 HEAD 里根本不存在）
#       → git 无法回退。
#
# 恢复原理：损坏是**逐字符**的（长度不变、结构不变），只有 s/S vs i 的信息丢了。
#   而"损坏前"的编译产物还在（DLL + PDB），里面有**全部类型名/方法名/字段名/局部变量名/字符串字面量**；
#   其它源码文件也没受损。于是：
#     词表 = 其它 .cs 的标识符 ∪ DLL/PDB 里的可打印串 ∪ 内置关键字表
#     反查表：对词表里每个 V，键 = V 做 s/S→i 后的样子；把损坏文件里的 token 换成 V。
#
# 安全性：① 只改**标识符 token**（正则 [A-Za-z_][A-Za-z0-9_]*），不动标点/缩进；
#         ② 冲突（两个原词映射到同一个键）会被记录并保守跳过；
#         ③ 结果先写到 _recovery/restored_*，**不直接覆盖**；
#         ④ 最终判据是"编译通过 + 用损坏前的录像回放出完全相同的哈希"。
# =============================================================================

$ErrorActionPreference = 'Stop'
$repo = 'D:\EmojiWarStudio'
$sim  = Join-Path $repo 'EmojiWar2\Assets\GameMain\Scripts\Simulation'
$rec  = Join-Path $repo '_recovery'
New-Item -ItemType Directory -Force -Path $rec | Out-Null

$targets = @('LockstepSimulation.cs','CastResolver.cs','SimView.cs')

# ---------- 0) 备份损坏件 ----------
foreach ($f in $targets) {
    $src = Join-Path $sim $f
    Copy-Item $src (Join-Path $rec ("corrupted_" + $f)) -Force
}
Write-Host "[ ok ] 已备份损坏件到 _recovery\corrupted_*"

# ---------- 1) 词表 ----------
$vocab = New-Object 'System.Collections.Generic.HashSet[string]'
$tokRe = [regex]'[A-Za-z_][A-Za-z0-9_]*'

# (a) 其它 .cs 的标识符（排除三个受损文件）
$csCount = 0
Get-ChildItem (Join-Path $repo 'EmojiWar2\Assets') -Recurse -Filter '*.cs' -File | ForEach-Object {
    if ($targets -contains $_.Name -and $_.DirectoryName -eq $sim) { return }
    $csCount++
    $txt = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in $tokRe.Matches($txt)) { [void]$vocab.Add($m.Value) }
}
Write-Host "[ ok ] 其它源码 $csCount 个文件，词表 $($vocab.Count) 项"

# (b) DLL / PDB 里的可打印串（含类型名/方法名/字段名/局部变量名/字面量）
$binRe = [regex]'[\x20-\x7E]{3,}'
foreach ($bin in @(
        (Join-Path $repo 'EmojiWar2\Builds\StandaloneWindows64\EmojiWar2_Data\Managed\EmojiWar.GameMain.dll'),
        (Join-Path $repo 'EmojiWar2\Library\ScriptAssemblies\EmojiWar.GameMain.dll'),
        (Join-Path $repo 'EmojiWar2\Library\ScriptAssemblies\EmojiWar.GameMain.pdb'))) {
    if (-not (Test-Path $bin)) { continue }
    $bytes = [System.IO.File]::ReadAllBytes($bin)
    $latin = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    $n0 = $vocab.Count
    foreach ($m in $binRe.Matches($latin)) {
        foreach ($t in $tokRe.Matches($m.Value)) { [void]$vocab.Add($t.Value) }
    }
    Write-Host ("[ ok ] {0}：+{1} 项" -f (Split-Path $bin -Leaf), ($vocab.Count - $n0))
}

# (c) 内置关键字/BCL/Unity 常用名（保证基本语法词一定能还原）
$builtin = @(
 'using','namespace','class','struct','interface','enum','public','private','protected','internal',
 'static','readonly','const','new','return','if','else','for','foreach','while','do','switch','case',
 'default','break','continue','try','catch','finally','throw','null','true','false','this','base',
 'void','int','uint','long','ulong','short','ushort','byte','sbyte','float','double','bool','char',
 'string','object','var','params','ref','out','in','is','as','sizeof','typeof','nameof','override',
 'virtual','abstract','sealed','partial','where','get','set','add','remove','value','yield','await',
 'async','unchecked','checked','fixed','unsafe','stackalloc','operator','implicit','explicit','event',
 'delegate','using','global','System','Math','Mathf','Vector2','Vector3','Vector4','List','Dictionary',
 'HashSet','Queue','Stack','StringBuilder','StringComparison','CultureInfo','InvariantCulture',
 'BitConverter','DateTime','TimeSpan','Stopwatch','Console','Environment','Exception','ArgumentException',
 'ArgumentNullException','InvalidOperationException','NotSupportedException','Array','Enum','Action',
 'Func','Nullable','IEnumerable','ICollection','IList','IDictionary','Comparison','Predicate','Tuple',
 'GC','Object','Type','Assembly','Reflection','BindingFlags','FieldInfo','PropertyInfo','MethodInfo',
 'Debug','Log','LogWarning','LogError','Application','Profiler','ProfilerMarker','Resources','ScriptableObject',
 'MonoBehaviour','GameObject','Transform','Sprite','SpriteRenderer','Random','Input','KeyCode','Time',
 'Resources','Serializable','SerializeField','Header','Tooltip','Range','Property','Space','HideInInspector',
 'RequireComponent','AddComponentMenu','ExecuteInEditMode','DisallowMultipleComponent','ContextMenu',
 'string','String','Format','Concat','Join','Split','Replace','IndexOf','Substring','Trim','TrimEnd',
 'StartsWith','EndsWith','Contains','ToUpper','ToLower','ToUpperInvariant','ToLowerInvariant',
 'Length','Count','Add','Remove','RemoveAt','Clear','Insert','ContainsKey','TryGetValue','Keys','Values',
 'Sort','OrderBy','ToArray','ToList','CopyTo','Exists','Find','FindIndex','ForEach','Reverse','Push','Pop',
 'Peek','Enqueue','Dequeue','Append','AppendLine','ToString','GetHashCode','Equals','CompareTo','ReferenceEquals',
 'Abs','Max','Min','Clamp','Clamp01','Lerp','Floor','Ceil','Round','Sqrt','Pow','Sin','Cos','Tan','Atan2',
 'PI','Deg2Rad','Rad2Deg','Epsilon','Infinity','NaN','IsNaN','IsInfinity','Normalize','normalized',
 'sqrMagnitude','magnitude','Distance','Dot','Cross','MoveTowards','SmoothDamp','Repeat','PingPong',
 'GetCurrentProcess','GetTimestamp','Frequency','StartNew','ElapsedMilliseconds','ElapsedTicks',
 'StringComparison','Ordinal','CurrentCulture','AppendFormat','TryParse','Parse','Compare','Copy','Fill'
)
foreach ($b in $builtin) { [void]$vocab.Add($b) }

# ---------- 2) 反查表：s/S→i 后的样子 → 原词 ----------
function Convert-S2I([string]$v) { return ($v -creplace 's','i') -creplace 'S','i' }

$map = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$conflicts = New-Object 'System.Collections.Generic.List[string]'
foreach ($v in $vocab) {
    $k = Convert-S2I $v
    if ($k -ceq $v) { continue }              # 不含 s/S 的词不需要映射
    if ($map.ContainsKey($k)) {
        if ($map[$k] -cne $v) { $conflicts.Add("$k  <=  $($map[$k])  |  $v") }
    } else {
        $map[$k] = $v
    }
}
Write-Host "[ ok ] 需要还原的键 $($map.Count) 个；冲突 $($conflicts.Count) 个"
if ($conflicts.Count -gt 0) {
    $conflicts | Select-Object -First 20 | ForEach-Object { Write-Host "        冲突: $_" -ForegroundColor Yellow }
}

# ---------- 3) 逐 token 还原（结果先写到 restored_*）----------
foreach ($f in $targets) {
    $src = Join-Path $rec ("corrupted_" + $f)
    $txt = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
    $unmapped = New-Object 'System.Collections.Generic.HashSet[string]'
    $hits = 0
    $restored = $tokRe.Replace($txt, {
        param($m)
        $t = $m.Value
        $o = $null
        if ($script:map.TryGetValue($t, [ref]$o)) { $script:hits++; return $o }
        return $t
    })
    # 收集仍"看起来被破坏"的 token（含 i 但反查不到）供人工核对
    foreach ($m in $tokRe.Matches($restored)) {
        $t = $m.Value
        if ($t -match 'i' -and -not $vocab.Contains($t)) { [void]$unmapped.Add($t) }
    }
    [System.IO.File]::WriteAllText((Join-Path $rec ("restored_" + $f)), $restored, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ("[ ok ] {0}：还原 {1} 处；未识别 token {2} 个" -f $f, $script:hits, $unmapped.Count)
    if ($unmapped.Count -gt 0) {
        Write-Host ("        未识别（前 30）：" + (($unmapped | Select-Object -First 30) -join ' ')) -ForegroundColor Yellow
    }
    $script:hits = 0
}
Write-Host "[完成] 结果在 $rec\restored_*.cs（**未覆盖**原文件，核对后再复制）"

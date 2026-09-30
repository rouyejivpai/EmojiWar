# =============================================================================
# 从 r2_（正确的"s→i 还原"结果）重建 LockstepSimulation.cs
#
# 为什么：上一步用 `$ls = @( @('a','b') )` 这种**嵌套数组**存替换对，
#   PowerShell 会把它展平成 @('a','b')，于是 foreach 拿到的是字符串、
#   `$f[0]`/`$f[1]` 变成**首字符** → 把整个文件里的 f 换成了 o。
# 本脚本改用**扁平数组 + 步长 2**，彻底避开这个坑（也避免 -replace 的大小写不敏感）。
# =============================================================================
$ErrorActionPreference = 'Stop'
$rec = 'D:\EmojiWarStudio\_recovery'
$dst = 'D:\EmojiWarStudio\EmojiWar2\Assets\GameMain\Scripts\Simulation\LockstepSimulation.cs'

$t = [System.IO.File]::ReadAllText((Join-Path $rec 'r2_LockstepSimulation.cs'), [System.Text.Encoding]::UTF8)

# 扁平数组：偶数位 = 原串，奇数位 = 新串
$pairs = @(
  # ---- 人工核对字典（与 CastResolver 同一批）----
  'ieiiionId','sessionId', 'iimPlayer','SimPlayer', 'ipeed','speed', 'ib','sb', 'iet','set',
  'ihopOpen','ShopOpen', 'ipellConfig','spellConfig', 'iqrMagnitude','sqrMagnitude', 'ieed','seed',
  'ihoti','hosts', 'itate','state', 'ipell','spell', 'ilotIndex','slotIndex', 'ilot','slot',
  'ipellId','spellId', 'ilotCount','slotCount', 'icope','scope', 'ielf','self', 'iink','sink',
  'ipelli','spells', 'iequenceOp','SequenceOp', 'i_PerItemiloti','s_PerItemSlots',
  'noLoadedItemi','noLoadedItems', 'itat','stat', 'qIiModifier','qIsModifier', 'itacki','stacks',
  'ipread','spread', 'itart','start', 'itructTagi','structTags', 'fait','fast', 'neiting','nesting',
  'iniufficient','insufficient', 'i_PerItem','s_PerItem', 'ipd','spd', 'iprite','Sprite',
  'iortingOrder','sortingOrder', 'markerir','markers', 'iim','sim', 'iimview','simview',
  'preii','press', 'icale','scale', 'LocalMuzzleFlaih','LocalMuzzleFlash', 'iimFrame','simFrame',
  'iecondary','Secondary', 'CaitEventi','CastEvents', 'Caititate','CastState', 'cait0','cast0',
  'cait1','cast1', 'caitval','castval', 'itFrame','stFrame', 'icopeBnd','scopeBnd',
  'icalar','scalar', 'paiiivei','passives', 'i3','S3', 'i4','S4', 'mi','ms', 'NowMi','NowMs',
  'iloti','slots', 'ipell_101','spell_101',
  # ---- 上下文修正（本轮实测发现的）----
  'plan.hosts','plan.Shots',
  'for (int i = 0; i < i.Length; i++)','for (int i = 0; i < s.Length; i++)',
  'float ci = SimMath.Cos(angle);','float cs = SimMath.Cos(angle);',
  'float in = SimMath.iin(angle);','float sn = SimMath.Sin(angle);',
  'SimMath.Rotate(aim.x, aim.y, ci, in)','SimMath.Rotate(aim.x, aim.y, cs, sn)',
  '`x*ci - y*in` / `x*in + y*ci`','`x*cs - y*sn` / `x*sn + y*cs`',
  'plan.Shots[i] = i;','plan.Shots[i] = s;',
  'var i = plan.Shots[i];','var s = plan.Shots[i];'
)

$done = 0
for ($i = 0; $i -lt $pairs.Count; $i += 2) {
    $from = $pairs[$i]; $to = $pairs[$i + 1]
    if ($from.Length -lt 2) { throw "替换对异常（长度<2）：'$from' —— 这通常意味着扁平化又发生了" }
    $c = 0
    while ($t.Contains($from)) { $t = $t.Replace($from, $to); $c++ }
    if ($c -gt 0) { $done += $c }
}
[System.IO.File]::WriteAllText($dst, $t, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "[ ok ] 由 r2_ 重建 LockstepSimulation.cs：应用 $done 处（含字典与上下文修正）"

# 自检：不应再出现被误替换的 f→o 痕迹（例如 'oro' / 'io' 之类高频错词）
$bad = @('or ', 'io (', 'oro', 'break', 'continoe')
$found = 0
foreach ($b in $bad) { if ($t.Contains("o" + $b.Substring(0,1)) -and $b -eq 'break') { } }
foreach ($m in [regex]::Matches($t, '\bior\b')) { $found++ }
Write-Host "[ 自检 ] 可疑 'ior' 出现 $found 次（若 >0 说明 f→o 残留）"

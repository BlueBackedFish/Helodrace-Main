$asm = [Reflection.Assembly]::LoadFrom('C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll')
$opcodes = @{}
[Reflection.Emit.OpCodes].GetFields() | ForEach-Object { $o=$_.GetValue($null); $opcodes[[int]$o.Value -band 65535]=$o }
function Dump-IL($method) {
 if($null -eq $method){return}
 $method.ToString()
 $b=$method.GetMethodBody().GetILAsByteArray(); $i=0
 while($i -lt $b.Length) {
  $offset=$i; $code=[int]$b[$i++]; if($code -eq 254){$code=65024+[int]$b[$i++]}; $op=$opcodes[$code]; $value=''; $n=0
  switch($op.OperandType.ToString()) {
   'InlineNone' {} 'ShortInlineI' {$n=1;$value=$b[$i]} 'ShortInlineVar' {$n=1;$value=$b[$i]} 'ShortInlineBrTarget' {$n=1;$delta=[int]$b[$i];if($delta -gt 127){$delta-=256};$value=$offset+2+$delta}
   'InlineVar' {$n=2;$value=[BitConverter]::ToUInt16($b,$i)} 'InlineI' {$n=4;$value=[BitConverter]::ToInt32($b,$i)} 'InlineBrTarget' {$n=4;$value=$offset+5+[BitConverter]::ToInt32($b,$i)}
   'ShortInlineR' {$n=4;$value=[BitConverter]::ToSingle($b,$i)} 'InlineR' {$n=8;$value=[BitConverter]::ToDouble($b,$i)} 'InlineI8' {$n=8;$value=[BitConverter]::ToInt64($b,$i)}
   'InlineSwitch' {$c=[BitConverter]::ToInt32($b,$i);$n=4+4*$c}
   'InlineString' {$n=4;$token=[BitConverter]::ToInt32($b,$i);$value=$method.Module.ResolveString($token)}
   default {$n=4;$token=[BitConverter]::ToInt32($b,$i);try {$value=$method.Module.ResolveMember($token)} catch {$value=$token}}
  }
  '{0:X4} {1} {2}' -f $offset,$op.Name,$value
  $i+=$n
 }
}
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
foreach($tn in @('Verse.ProjectileProperties','Verse.Tool','Verse.VerbProperties','RimWorld.StatWorker_MarketValue','RimWorld.StatDef','Verse.GenStuff','Verse.XmlInheritance')) {
 $t=$asm.GetType($tn); "TYPE $tn"
 if($null -eq $t){continue}
 foreach($m in $t.GetMethods($flags) | Where-Object {$_.DeclaringType -eq $t -and $_.Name -match 'Damage|Armor|Melee|Market|ValueUnfinalized|Resolve|Apply|Inherit|RecursiveNodeCopy|CalculableRecipe|DefaultStuffFor'}){ Dump-IL $m }
 if($tn -ne 'Verse.XmlInheritance') {foreach($m in $t.GetConstructors($flags)){Dump-IL $m}}
}

$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom('C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll')
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$method=$asm.GetType('Verse.XmlInheritance').GetMethod('RecursiveNodeCopyOverwriteElements',$flags)
$doc=New-Object System.Xml.XmlDocument
$doc.Load((Join-Path $PSScriptRoot 'patched.xml'))
$named=@{};$cache=@{}
foreach($n in $doc.DocumentElement.ChildNodes){if($n.HasAttribute('Name')){$named[$n.GetAttribute('Name')]=$n}}
function Resolve-Node([System.Xml.XmlNode]$n) {
 $key=$n.GetHashCode()
 if($cache.ContainsKey($key)){return ,$cache[$key]}
 $pn=$n.GetAttribute('ParentName')
 if($pn -and $named.ContainsKey($pn)) {
  $parent=Resolve-Node $named[$pn]
  $r=$parent.CloneNode($true)
  $null=$method.Invoke($null,@($n,$r))
 }else{$r=$n.CloneNode($true)}
 foreach($attr in @('Abstract','Name','ParentName')) {
  if($n.HasAttribute($attr)){$r.SetAttribute($attr,$n.GetAttribute($attr))}else{$r.RemoveAttribute($attr)}
 }
 $cache[$key]=$r
 return ,$r
}
$result=New-Object System.Xml.XmlDocument
$null=$result.AppendChild($result.CreateElement('Defs'))
foreach($n in $doc.DocumentElement.ChildNodes){if($n.SelectSingleNode('defName')){$r=Resolve-Node $n;$null=$result.DocumentElement.AppendChild($result.ImportNode($r,$true))}}
$result.Save((Join-Path $PSScriptRoot 'game_resolved.xml'))
"Resolved $($result.DocumentElement.ChildNodes.Count) definitions using RimWorld XML inheritance."

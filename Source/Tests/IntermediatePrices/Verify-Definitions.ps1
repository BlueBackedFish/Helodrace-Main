$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$expected=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'expected-prices.json') -Raw | ConvertFrom-Json
$found=@{}
foreach($file in @('WildWest/Items/Materials_WildWest.xml','GreatWar/Items/Materials_GreatWar.xml','ColdWar/Items/Materials_ColdWar.xml')) {
    [xml]$xml=Get-Content -LiteralPath (Join-Path $repository ('Defs/'+$file)) -Raw
    foreach($def in $xml.SelectNodes('/Defs/ThingDef[defName]')) {
        if($def.defName -eq 'HD_PlainDogChew'){continue}
        $found[[string]$def.defName]=$def
    }
}
if($found.Count -ne $expected.Count){throw 'Intermediate coverage changed; review every industrial material.'}
foreach($row in $expected) {
    if(-not $found.ContainsKey($row.defName)){throw ('Missing intermediate: '+$row.defName)}
    $value=[double]$found[$row.defName].statBases.MarketValue
    if($value -ne $row.after -or $value -ge 32 -or $value -ge $row.before){throw ('Incorrect reduced value: '+$row.defName)}
}
[xml]$electronic=Get-Content -LiteralPath (Join-Path $repository 'Defs/ColdWar/Items/ElectronicComponent.xml') -Raw
if([double]$electronic.Defs.ThingDef.statBases.MarketValue -ne 32){throw 'Electronic component anchor must match actual vanilla component baseline.'}
$research=@{}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $repository 'Defs/Research') -Recurse -Filter '*.xml') {
    [xml]$xml=Get-Content -LiteralPath $file.FullName -Raw
    foreach($def in $xml.SelectNodes('/Defs/ResearchProjectDef[defName]')){$research[[string]$def.defName]=$def}
}
function Has-Transistor([string]$Name,[Collections.Generic.HashSet[string]]$Seen) {
    if($Name -eq 'HelodTransistor'){return $true}
    if(-not $Seen.Add($Name) -or -not $research.ContainsKey($Name)){return $false}
    foreach($prerequisite in $research[$Name].SelectNodes('prerequisites/li')){if(Has-Transistor $prerequisite.InnerText $Seen){return $true}}
    return $false
}
foreach($file in @('ColdWar/Buildings/HydraulicPress1970.xml','ColdWar/Buildings/MachiningCenter1970.xml','ColdWar/Buildings/RollingMill1970.xml','ModernWar/Buildings/MultiTaskMachine2000.xml')) {
    [xml]$xml=Get-Content -LiteralPath (Join-Path $repository ('Defs/'+$file)) -Raw
    foreach($def in $xml.SelectNodes('/Defs/ThingDef[costList/HD_ElectronicComponent]')) {
        $available=$false
        foreach($req in $def.SelectNodes('researchPrerequisites/li')) {
            if(Has-Transistor $req.InnerText ([Collections.Generic.HashSet[string]]::new())){$available=$true}
        }
        if(-not $available){throw ('Electronic consumer can unlock before its supply: '+$def.defName)}
    }
}
Write-Output ('PASS: '+$found.Count+' intermediate prices, 32-silver electronics, and post-transistor supply access.')

param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference = 'Stop'
$records = [Collections.Generic.List[object]]::new()
$parents = @{}
$statDefs = @{}
$partDefs = @{}
foreach ($root in (Join-Path $RimWorldPath 'Data/Core/Defs'), (Join-Path $Repo 'Defs')) {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -Filter *.xml) {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        foreach ($node in $document.SelectNodes('/Defs/*')) {
            $record = [pscustomobject]@{ Node=$node; File=$file.FullName; Local=$file.FullName.StartsWith((Join-Path $Repo 'Defs'), [StringComparison]::OrdinalIgnoreCase) }
            $name = $node.GetAttribute('Name')
            if ($name) { $parents[$node.LocalName + ':' + $name] = $record }
            if ($node.LocalName -eq 'StatDef' -and $node.defName) { $statDefs[[string]$node.defName] = $record }
            if ($node.LocalName -eq 'Helodrace.ModernWar.ModularArmorPartDef' -and $node.defName) { $partDefs[[string]$node.defName] = $record }
            if ($node.LocalName -eq 'ThingDef' -and $record.Local -and $node.defName -and $node.GetAttribute('Abstract') -ne 'True') { $records.Add($record) }
        }
    }
}
function Get-Chain($record) {
    $chain = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new()
    while ($record) {
        $chain.Add($record)
        $parentName = $record.Node.GetAttribute('ParentName')
        if (!$parentName) { break }
        $key = $record.Node.LocalName + ':' + $parentName
        if (!$seen.Add($key) -or !$parents.ContainsKey($key)) { throw "Unresolved/cyclic XML parent: $key" }
        $record = $parents[$key]
    }
    return $chain.ToArray()
}
function Read-Value($chain, $xpath) {
    foreach ($record in $chain) {
        $node = $record.Node.SelectSingleNode($xpath)
        if ($node) { return $node.InnerText }
        $containerName = ($xpath -split '/')[0]
        $container = $record.Node.SelectSingleNode($containerName)
        if ($container -and $container.GetAttribute('Inherit') -eq 'False') { break }
    }
    return $null
}
function Stat-Value($chain, $name) {
    $value = Read-Value $chain ('statBases/' + $name)
    if ($null -ne $value) { return [double]$value }
    if ($statDefs.ContainsKey($name)) {
        $value = Read-Value @(Get-Chain $statDefs[$name]) 'defaultBaseValue'
        if ($null -ne $value) { return [double]$value }
    }
    return 0.0
}
function Thermal-Status($chain, $type, $madeFromStuff) {
    $base = Stat-Value $chain ('Insulation_' + $type)
    $factor = Stat-Value $chain ('StuffEffectMultiplierInsulation_' + $type)
    if ($madeFromStuff -and $factor -ne 0) { return "소재 배율 $factor (기본 $base)" }
    if ($base -ne 0) { return "기본 $base" }
    if (!$madeFromStuff -and $factor -ne 0) { return "소재 배율 $factor 비활성 → 0" }
    if ($null -ne (Read-Value $chain ('statBases/Insulation_' + $type))) { return '명시적 0' }
    return '미설정 → 0'
}
$rows = [Collections.Generic.List[object]]::new()
foreach ($record in $records) {
    $chain = @(Get-Chain $record)
    if ((Read-Value $chain 'thingClass') -ne 'Apparel' -and $null -eq (Read-Value $chain 'apparel')) { continue }
    $madeFromStuff = $null -ne (Read-Value $chain 'stuffCategories')
    $comp = $null
    foreach ($ancestor in $chain) {
        $comp = $ancestor.Node.SelectSingleNode('comps/li[@Class="Helodrace.ModernWar.CompProperties_ModularArmor"]')
        if ($comp) { break }
        if ($ancestor.Node.comps -and $ancestor.Node.comps.GetAttribute('Inherit') -eq 'False') { break }
    }
    $ventilation = '대상 아님 (커스텀 Comp 없음)'
    $metricsHidden = ''
    $conversion = '없음'
    if ($comp) {
        $baseVentilation = if ($comp.baseVentilation) { [double]$comp.baseVentilation } else { 0.0 }
        $defaultVentilation = $baseVentilation
        foreach ($entry in $comp.SelectNodes('defaultParts/li')) {
            if ($partDefs.ContainsKey([string]$entry.part)) {
                $partValue = Read-Value @(Get-Chain $partDefs[[string]$entry.part]) 'ventilation'
                if ($partValue) { $defaultVentilation += [double]$partValue }
            }
        }
        $ventilation = "기본 $baseVentilation / 기본부품 포함 $defaultVentilation"
        $hidden = @('HD_ArmorErgonomics','HD_ArmorVentilation','HD_ArmorLoadDistribution' | Where-Object { $null -eq (Read-Value $chain ('statBases/' + $_)) })
        $metricsHidden = $hidden -join ', '
        $rules = @($comp.SelectNodes('statConversions/li[source="Ventilation"]') | ForEach-Object { $_.targetStat + ' (기준 ' + $_.referenceValue + ', 배율 ' + $_.factor + ')' })
        if ($rules.Count) { $conversion = $rules -join '; ' }
    }
    $missingBasic = @('Mass','MaxHitPoints','EquipDelay','WorkToMake','MarketValue' | Where-Object { $null -eq (Read-Value $chain ('statBases/' + $_)) })
    $missingArmor = @('ArmorRating_Sharp','ArmorRating_Blunt','ArmorRating_Heat' | Where-Object { $null -eq (Read-Value $chain ('statBases/' + $_)) -and !($madeFromStuff -and (Stat-Value $chain 'StuffEffectMultiplierArmor') -ne 0) })
    $inactiveMultipliers = @('StuffEffectMultiplierArmor','StuffEffectMultiplierInsulation_Cold','StuffEffectMultiplierInsulation_Heat' | Where-Object { !$madeFromStuff -and (Stat-Value $chain $_) -ne 0 })
    $rows.Add([pscustomobject]@{
        DefName=[string]$record.Node.defName
        Label=(Read-Value $chain 'label')
        File=$record.File.Substring($Repo.Length + 1).Replace('\','/')
        Layers=(Read-Value $chain 'apparel/layers')
        BodyParts=(Read-Value $chain 'apparel/bodyPartGroups')
        Cold=(Thermal-Status $chain 'Cold' $madeFromStuff)
        Heat=(Thermal-Status $chain 'Heat' $madeFromStuff)
        Ventilation=$ventilation
        VentilationConversion=$conversion
        HiddenModularMetrics=$metricsHidden
        MissingBasicStats=($missingBasic -join ', ')
        DefaultOnlyArmorStats=($missingArmor -join ', ')
        InactiveStuffMultipliers=($inactiveMultipliers -join ', ')
    })
}
$sorted = @($rows | Sort-Object File,DefName)
$reportPath = Join-Path $Repo 'Docs/Apparel_Stat_Audit.csv'
$sorted | Export-Csv -LiteralPath $reportPath -NoTypeInformation -Encoding UTF8
$report = [Collections.Generic.List[string]]::new()
$report.Add('# 의상 스탯 전수 조사')
$report.Add('')
$report.Add('저장소의 실제 착용 의상을 Core 부모 정의와 함께 조사했습니다. 상속값, 소재 배율, 명시적 0, 미설정 기본값을 구분합니다. 다른 모드의 패치나 실제 아이템의 품질·소재·장착 부품에 따른 최종 수치는 포함하지 않습니다. 이 조사 스크립트는 의상 정의나 스탯 값을 변경하지 않습니다.')
$report.Add('')
$report.Add('단열 미설정 항목은 게임 기본값 0이며 정보창에서 숨겨집니다. 소재 배율이 있으면 직접 단열값이 없어도 소재가 단열을 제공합니다. ArmorRating 기본값도 0입니다. MarketValue 등의 최종 값은 게임의 별도 계산이 있으므로 정의 누락을 곧바로 기능 누락으로 판단하지 않습니다.')
$report.Add('')
$report.Add('통풍은 HD_ArmorVentilation 커스텀 지표입니다. CompModularArmor가 있는 장비에만 적용되며 기본값과 장착 부품 합계로 계산합니다. 일반 의상에 Comp가 없는 것은 이 시스템에서 통풍을 제공하지 않는다는 뜻입니다.')
$report.Add('')
$zeroThermal = @($sorted | Where-Object { $_.Cold.EndsWith('→ 0') -and $_.Heat.EndsWith('→ 0') })
$garments = @($sorted | Where-Object { $_.Layers -ne 'Belt' -and $_.Layers -ne 'HD_Pouch' })
$zeroGarments = @($garments | Where-Object { $_.Cold.EndsWith('→ 0') -and $_.Heat.EndsWith('→ 0') })
$report.Add("총 $($sorted.Count)종 중 추위·더위 단열이 모두 0인 항목은 $($zeroThermal.Count)종입니다. Belt와 HD_Pouch를 제외한 의복·방어구·모자 $($garments.Count)종 중에서는 $($zeroGarments.Count)종입니다. 나머지에는 소재에 따른 단열이 있으므로 제작 소재에 따라 실제 값이 달라집니다.")
$report.Add('')
$report.Add('## 소재 배율이 작동하지 않는 의상')
$report.Add('')
$report.Add('아래 의상은 StuffEffectMultiplier 값이 있지만 stuffCategories가 없고 Cloth 등의 고정 costList로 제작됩니다. 게임 StatPart_Stuff는 StuffDef가 없으면 소재 단열·방어력 기여를 0으로 계산합니다. 배율만 넣어서는 기능이 생기지 않습니다.')
$report.Add('')
$report.Add('| 의상 | 비활성 배율 |')
$report.Add('| --- | --- |')
foreach ($row in $sorted | Where-Object InactiveStuffMultipliers) { $report.Add("| $($row.DefName) | $($row.InactiveStuffMultipliers) |") }
$report.Add('')
$report.Add('## 단열 / 통풍 전체 목록')
$report.Add('')
$report.Add('| defName | 의상 | 추위 단열 | 더위 단열 | 통풍 |')
$report.Add('| --- | --- | --- | --- | --- |')
foreach ($row in $sorted) { $report.Add("| $($row.DefName) | $($row.Label) | $($row.Cold) | $($row.Heat) | $($row.Ventilation) |") }
$report.Add('')
$report.Add('## 통풍 정보창 표시 문제')
$report.Add('')
$report.Add('ModularArmorStats.xml의 세 커스텀 StatDef는 showIfUndefined=false인데 의상의 statBases에는 해당 항목이 없습니다. StatWorker_ModularArmorMetric는 값 계산만 재정의하고 ShouldShowFor를 재정의하지 않습니다. 설치된 게임의 StatWorker.ShouldShowFor는 StatBases에 정의되지 않은 항목을 먼저 숨깁니다. 따라서 커스텀 화면에는 값이 있어도 일반 정보창에서는 통풍·인체공학·하중 분산이 표시되지 않습니다.')
$report.Add('')
$report.Add('| 의상 | 통풍의 착용자 스탯 변환 | 정보창에서 누락된 커스텀 스탯 |')
$report.Add('| --- | --- | --- |')
foreach ($row in $sorted | Where-Object HiddenModularMetrics) { $report.Add("| $($row.DefName) | $($row.VentilationConversion) | $($row.HiddenModularMetrics) |") }
$report.Add('')
$report.Add('## 기본 스탯 / 방어력 정의 누락')
$report.Add('')
$report.Add('아래는 상속과 소재 배율을 확인한 뒤에도 개별 정의가 없는 항목입니다. 방어력이 없는 보조 장비나 제작 불가능 장비는 의도된 설정일 수 있습니다.')
$report.Add('')
$report.Add('| 의상 | 기본 스탯 미정의 | 기본값만 사용하는 방어력 |')
$report.Add('| --- | --- | --- |')
foreach ($row in $sorted | Where-Object { $_.MissingBasicStats -or $_.DefaultOnlyArmorStats }) { $report.Add("| $($row.DefName) | $($row.MissingBasicStats) | $($row.DefaultOnlyArmorStats) |") }
[IO.File]::WriteAllLines((Join-Path $Repo 'Docs/Apparel_Stat_Audit.md'),$report,[Text.UTF8Encoding]::new($false))
Write-Output "Audited $($sorted.Count) apparel definitions; $($zeroThermal.Count) have zero thermal values; $($zeroGarments.Count)/$($garments.Count) garments/headwear have zero thermal values."
$sorted | Where-Object InactiveStuffMultipliers | Select-Object DefName,InactiveStuffMultipliers | Format-Table -AutoSize

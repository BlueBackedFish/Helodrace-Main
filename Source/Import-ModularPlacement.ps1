param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [string]$RepositoryRoot,
    [ValidateSet('Summary','Json','Patch')][string]$Format = 'Summary',
    [int]$Offset = 0,
    [int]$Limit = 1000
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($RepositoryRoot)) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
$culture = [Globalization.CultureInfo]::InvariantCulture
$compClass = 'Helodrace.ModernWar.CompProperties_ModularWeaponNode'
$document = New-Object System.Xml.XmlDocument
$document.LoadXml('<Defs/>')
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Defs') -Filter '*.xml' -Recurse)) {
    $source = New-Object System.Xml.XmlDocument
    $source.Load($file.FullName)
    foreach ($node in $source.DocumentElement.ChildNodes) {
        if ($node.NodeType -eq [Xml.XmlNodeType]::Element) {
            [void]$document.DocumentElement.AppendChild($document.ImportNode($node,$true))
        }
    }
}
# Balance includes integral sight axes. Compare against effective authored values,
# including previously imported placements, without changing files on disk.
$patchFiles = @((Join-Path $RepositoryRoot 'Patches/ModularWeapon_Balance.xml'))
$patchFiles += @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Patches') -Filter 'ModularWeapon_Placement_*.xml' | Sort-Object Name | ForEach-Object FullName)
foreach ($patchFile in $patchFiles) {
    $patch = New-Object System.Xml.XmlDocument
    $patch.Load($patchFile)
    foreach ($op in $patch.SelectNodes('/Patch/Operation')) {
        foreach ($node in @($document.SelectNodes($op.SelectSingleNode('xpath').InnerText))) {
            $value = $op.SelectSingleNode('value/*')
            switch ($op.GetAttribute('Class')) {
                'PatchOperationAdd' {
                    foreach ($child in $op.SelectNodes('value/*')) { [void]$node.AppendChild($document.ImportNode($child,$true)) }
                }
                'PatchOperationReplace' { [void]$node.ParentNode.ReplaceChild($document.ImportNode($value,$true),$node) }
                'PatchOperationRemove' { [void]$node.ParentNode.RemoveChild($node) }
                default { throw "Unsupported placement baseline operation: $($op.GetAttribute('Class'))" }
            }
        }
    }
}

function Equivalent([Xml.XmlNode]$a,[Xml.XmlNode]$b) {
    if ($null -eq $a -or $null -eq $b) { return $null -eq $a -and $null -eq $b }
    $left = @($a.SelectNodes('*')); $right = @($b.SelectNodes('*'))
    if ($left.Count -ne $right.Count) { return $false }
    if ($left.Count -gt 0) {
        for ($index=0;$index -lt $left.Count;$index++) {
            if ($left[$index].Name -ne $right[$index].Name -or !(Equivalent $left[$index] $right[$index])) { return $false }
        }
        return $true
    }
    $first=$a.InnerText.Trim(); $second=$b.InnerText.Trim()
    $numbersA=$first.Trim('(',')').Split(','); $numbersB=$second.Trim('(',')').Split(',')
    if ($numbersA.Count -eq $numbersB.Count) {
        $numeric=$true
        for ($index=0;$index -lt $numbersA.Count;$index++) {
            $numberA=0.0; $numberB=0.0
            if (![double]::TryParse($numbersA[$index],[Globalization.NumberStyles]::Float,$culture,[ref]$numberA) -or
                ![double]::TryParse($numbersB[$index],[Globalization.NumberStyles]::Float,$culture,[ref]$numberB)) { $numeric=$false; break }
            if ([Math]::Abs($numberA-$numberB) -gt 0.0000005) { return $false }
        }
        if ($numeric) { return $true }
    }
    return $first -ceq $second
}

$export = New-Object System.Xml.XmlDocument
$export.Load((Resolve-Path -LiteralPath $InputPath))
if ($export.DocumentElement.Name -ne 'ModularWeaponPlacementBatchExport') { throw 'Expected a placement batch export.' }
$changes = New-Object 'System.Collections.Generic.List[object]'
$seen = @{}
function CompareField([string]$defName,[Xml.XmlNode]$current,[Xml.XmlNode]$saved,[string]$path) {
    if ($null -eq $saved -or (Equivalent $current $saved)) { return }
    if ($null -eq $current) {
        if ($defaults.ContainsKey($saved.Name)) {
            $defaultValue=$export.CreateElement($saved.Name);$defaultValue.InnerText=$defaults[$saved.Name]
            if (Equivalent $defaultValue $saved) { return }
        }
        $slash=$path.LastIndexOf('/')
        $target=$path.Substring(0,$slash)
        $operation='PatchOperationAdd'
    } else { $target=$path; $operation='PatchOperationReplace' }
    $changes.Add([PSCustomObject]@{Def=$defName;Operation=$operation;XPath=$target;Value=$saved.OuterXml})
}

$visualFields = @('graphicOffset','graphicAngle','graphicScale','graphicLayer','additionalGraphics',
    'outlinePriority','assemblyScale','animationSpeed','animatedPart','animationTravel','animationPivot',
    'animationAngle','verticalOccupancy','verticalOccupancyOffset')
$defaults = @{graphicOffset='(0,0,0)';graphicAngle='0';graphicScale='(1,1)';graphicLayer='0';outlinePriority='0';
    assemblyScale='1';animationSpeed='1';animatedPart='None';animationTravel='(0,0,0)';
    animationPivot='(0,0,0)';animationAngle='0';verticalOccupancy='0';verticalOccupancyOffset='0';
    isRail='false';railSurface='Unspecified';railOccupancy='0';railContactLength='0';physicalLength='0';
    physicalCenterOffset='0';oppositeSurfaceOffset='(0,0,0)';maxAttachments='0';railOffset='0'}
foreach ($part in $export.SelectNodes('/ModularWeaponPlacementBatchExport/parts/li')) {
    $defName=$part.SelectSingleNode('thingDef').InnerText
    if ($defName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $seen.ContainsKey($defName)) { throw "Invalid/duplicate part: $defName" }
    $seen[$defName]=$true
    $basePath='/Defs/ThingDef[defName="{0}"]/comps/li[@Class="{1}"]' -f $defName,$compClass
    $current=$document.SelectSingleNode($basePath)
    if ($null -eq $current) { throw "Unknown modular part: $defName" }
    $settings=$part.SelectSingleNode('settings')
    foreach ($field in $visualFields) {
        $old=$current.SelectSingleNode($field); $saved=$settings.SelectSingleNode($field)
        if ($null -eq $saved -and $null -ne $old -and $defaults.ContainsKey($field)) {
            $saved=$export.CreateElement($field);$saved.InnerText=$defaults[$field]
        }
        CompareField $defName $old $saved "$basePath/$field"
    }
    foreach ($listName in @('mounts','sockets')) {
        $savedList=$settings.SelectSingleNode($listName)
        if ($null -ne $savedList -and $null -eq $current.SelectSingleNode($listName)) {
            CompareField $defName $null $savedList "$basePath/$listName"
            continue
        }
        foreach ($entry in $settings.SelectNodes("$listName/li")) {
            $id=$entry.SelectSingleNode('id').InnerText
            if ($id -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw "Invalid interface id: $id" }
            $relative='{0}/li[id="{1}"]' -f $listName,$id
            $oldEntry=$current.SelectSingleNode($relative)
            if ($null -eq $oldEntry) {
                $changes.Add([PSCustomObject]@{Def=$defName;Operation='PatchOperationAdd';XPath="$basePath/$listName";Value=$entry.OuterXml})
                continue
            }
            $fields=if($listName -eq 'mounts'){@('transform','railOccupancy','railContactLength','physicalLength','physicalCenterOffset','oppositeSurfaceOffset','railSurface')}else{@('transform','railLength','isRail','railSurface')}
            if ($listName -eq 'sockets' -and $entry.SelectSingleNode('isRail').InnerText -eq 'true') { $fields += 'maxAttachments' }
            foreach ($field in $fields) {
                $old=$oldEntry.SelectSingleNode($field);$saved=$entry.SelectSingleNode($field)
                if ($null -eq $saved -and $null -ne $old -and $defaults.ContainsKey($field)) {
                    $saved=$export.CreateElement($field);$saved.InnerText=$defaults[$field]
                }
                CompareField $defName $old $saved "$basePath/$relative/$field"
            }
        }
    }
    foreach ($relative in @('sight/axisOffset','sight/sightClearanceRadius','laser/emitterOffset','casingPort/offset','casingPort/feedStartOffset','casingPort/feedEndOffset','casingPort/feedingRoundDrawSize')) {
        $old=$current.SelectSingleNode($relative);$saved=$settings.SelectSingleNode($relative)
        if ($null -ne $saved -and $null -eq $current.SelectSingleNode($relative.Substring(0,$relative.IndexOf('/')))) { throw "Missing geometry owner: $defName/$relative" }
        CompareField $defName $old $saved "$basePath/$relative"
    }
}

# Instance rail offsets are not properties of the part Def. Reuse a saved value
# only for the same parent Def/socket/child Def/mount in an existing preset. If
# several saved instances disagree, retain the preset rather than guessing.
$railContexts=@{};$ambiguousRailContexts=@{}
foreach ($assembly in $export.SelectNodes('/ModularWeaponPlacementBatchExport/assemblies/li')) {
    $byPath=@{}
    foreach ($part in $assembly.SelectNodes('parts/li')) { $byPath[$part.SelectSingleNode('path').InnerText]=$part }
    foreach ($part in $assembly.SelectNodes('parts/li[railOffset]')) {
        $parentPart=$byPath[$part.SelectSingleNode('parentPath').InnerText]
        if ($null -eq $parentPart) { throw 'Missing parent in saved assembly.' }
        $key=@($parentPart.SelectSingleNode('part').InnerText,$part.SelectSingleNode('socketId').InnerText,
            $part.SelectSingleNode('part').InnerText,$part.SelectSingleNode('mountId').InnerText) -join '|'
        $value=$part.SelectSingleNode('railOffset')
        if ($railContexts.ContainsKey($key) -and !(Equivalent $railContexts[$key] $value)) { $ambiguousRailContexts[$key]=$true }
        else { $railContexts[$key]=$value }
    }
}
function ComparePresetRails([Xml.XmlNode]$entries,[string]$parentDef,[string]$path,[string]$presetName) {
    foreach ($entry in $entries.SelectNodes('li')) {
        $part=$entry.SelectSingleNode('part').InnerText
        $entryPath='{0}/li[part="{1}" and socketId="{2}"]' -f $path,$part,$entry.SelectSingleNode('socketId').InnerText
        if ($document.SelectNodes($entryPath).Count -ne 1) { continue }
        $key=@($parentDef,$entry.SelectSingleNode('socketId').InnerText,$part,$entry.SelectSingleNode('mountId').InnerText) -join '|'
        if ($railContexts.ContainsKey($key) -and !$ambiguousRailContexts.ContainsKey($key)) {
            CompareField $presetName $entry.SelectSingleNode('railOffset') $railContexts[$key] "$entryPath/railOffset"
        }
        $children=$entry.SelectSingleNode('children')
        if ($null -ne $children) { ComparePresetRails $children $part "$entryPath/children" $presetName }
    }
}
foreach ($preset in $document.SelectNodes('/Defs/Helodrace.ModernWar.ModularWeaponPresetDef')) {
    $presetName=$preset.SelectSingleNode('defName').InnerText
    $path='/Defs/Helodrace.ModernWar.ModularWeaponPresetDef[defName="{0}"]/parts' -f $presetName
    ComparePresetRails $preset.SelectSingleNode('parts') $preset.SelectSingleNode('weaponDef').InnerText $path $presetName
}

switch ($Format) {
    'Summary' {
        [PSCustomObject]@{Input=(Split-Path -Leaf $InputPath);Parts=$seen.Count;Assemblies=$export.SelectNodes('/ModularWeaponPlacementBatchExport/assemblies/li').Count;ChangedDefinitions=@($changes | Select-Object -ExpandProperty Def -Unique).Count;Operations=$changes.Count;AmbiguousRailContexts=$ambiguousRailContexts.Count}
        $changes | Group-Object Def | ForEach-Object { [PSCustomObject]@{Part=$_.Name;Changes=$_.Count} }
    }
    'Json' { ConvertTo-Json -InputObject @($changes | Select-Object -Skip $Offset -First $Limit) -Depth 3 -Compress }
    'Patch' {
        $output=New-Object System.Xml.XmlDocument;$output.LoadXml('<Patch/>')
        foreach ($change in $changes) {
            $op=$output.CreateElement('Operation');$op.SetAttribute('Class',$change.Operation)
            $xpath=$output.CreateElement('xpath');$xpath.InnerText=$change.XPath;[void]$op.AppendChild($xpath)
            $value=$output.CreateElement('value');$fragment=$output.CreateDocumentFragment();$fragment.InnerXml=$change.Value;[void]$value.AppendChild($fragment);[void]$op.AppendChild($value)
            [void]$output.DocumentElement.AppendChild($op)
        }
        $output.OuterXml
    }
}

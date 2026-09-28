param(
    [string]$ModuleRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
$managed = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($name in 'UnityEngine.CoreModule','UnityEngine','Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $managed "$name.dll"))
}
[Verse.DeepProfiler]::enabled = $false
# Build only the standard patch-operation objects used by these files, then execute
# RimWorld's actual Apply methods. This is not a simulation of XML mutation rules.
function Read-Operation([System.Xml.XmlElement]$node) {
    $class = $node.GetAttribute('Class')
    Assert ($class -in 'PatchOperationSequence','PatchOperationConditional','PatchOperationAdd',
        'PatchOperationReplace','PatchOperationRemove','PatchOperationAddModExtension') "Unsupported operation: $class"
    $type = [Verse.PatchOperation].Assembly.GetType("Verse.$class", $true)
    $operation = [Activator]::CreateInstance($type)
    foreach ($child in $node.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $owner = $type
        $field = $null
        while ($owner -and !$field) {
            $field = $owner.GetField($child.Name, [Reflection.BindingFlags]'Instance,Public,NonPublic,DeclaredOnly')
            $owner = $owner.BaseType
        }
        Assert ($null -ne $field) "Unknown patch field: $($child.Name)"
        switch ($child.Name) {
            'xpath' { $value = $child.InnerText }
            'value' { $value = [Verse.XmlContainer]::new(); $value.node = $child }
            'operations' {
                $value = [Collections.Generic.List[Verse.PatchOperation]]::new()
                foreach ($entry in $child.SelectNodes('li')) { $value.Add((Read-Operation $entry)) }
            }
            { $_ -in 'match','nomatch' } { $value = Read-Operation $child }
            'success' { $value = [Enum]::Parse($field.FieldType, $child.InnerText) }
            default { throw "Unhandled patch field: $($child.Name)" }
        }
        $field.SetValue($operation, $value)
    }
    return $operation
}
$baseXml = [System.Xml.XmlDocument]::new()
$baseXml.LoadXml('<Defs/>')
foreach ($file in Get-ChildItem "$repo/Defs" -Recurse -Filter *.xml) {
    [xml]$source = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($node in $source.DocumentElement.ChildNodes) {
        if ($node.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $null = $baseXml.DocumentElement.AppendChild($baseXml.ImportNode($node, $true))
        }
    }
}
[xml]$ceXml = Get-Content "$ModuleRoot/Helodrace-CombatExtended/Patches/CombatExtended_HelodRaceAndArmor.xml" -Raw -Encoding UTF8
[xml]$facialXml = Get-Content "$ModuleRoot/Helodrace-Facial/Patches/Helodrace/FacialAnimationComps_Helod.xml" -Raw -Encoding UTF8
$baselinePath = Join-Path $PSScriptRoot 'Tests/CompatibilityPatchBaseline.json'
$baseline = Get-Content $baselinePath -Raw -Encoding UTF8 | ConvertFrom-Json
function Canonical-Xml([xml]$document) {
    $copy = $document.Clone()
    foreach ($comment in @($copy.SelectNodes('//comment()'))) { $null = $comment.ParentNode.RemoveChild($comment) }
    return $copy.OuterXml
}
Assert ($baseXml.SelectSingleNode('/Defs/ThingDef[defName="Helod"]/race/intelligence').InnerText -eq 'Humanlike') 'Helod must remain humanlike.'
Assert ($baseXml.SelectNodes('/Defs/AlienRace.ThingDef_AlienRace[defName="Helod"]').Count -eq 0) 'Legacy race selector unexpectedly matches.'
# Reproduce the previous sequence failure against the current race definition.
$legacyXml = [xml]($ceXml.OuterXml.Replace('/Defs/ThingDef[defName="Helod"]', '/Defs/AlienRace.ThingDef_AlienRace[defName="Helod"]'))
$legacy = Read-Operation $legacyXml.Patch.Operation.match
Assert (!$legacy.Apply($baseXml.Clone())) 'Legacy CE race patch should fail on the standalone Def.'
Write-Output 'PASS: reproduced original CE selector failure; Humanlike identity unchanged.'
foreach ($order in @(@('CE'), @('Facial'), @('CE','Facial'), @('Facial','CE'))) {
    $document = $baseXml.Clone()
    $expected = $baseXml.Clone()
    foreach ($module in $order) {
        # The CE wrapper is a FindMod gate; explicitly test its installed-mod branch.
        $node = if ($module -eq 'CE') { $ceXml.Patch.Operation.match } else { $facialXml.Patch.Operation }
        Assert ((Read-Operation $node).Apply($document)) "$module full patch failed in order $order"
        [xml]$previous = $baseline.$module
        $previousNode = if ($module -eq 'CE') { $previous.Patch.Operation.match } else { $previous.Patch.Operation }
        Assert ((Read-Operation $previousNode).Apply($expected)) 'Baseline patch no longer matches fixture.'
    }
    Assert ((Canonical-Xml $document) -ceq (Canonical-Xml $expected)) "Initial patch behavior changed for $order"
    foreach ($module in $order) {
        $node = if ($module -eq 'CE') { $ceXml.Patch.Operation.match } else { $facialXml.Patch.Operation }
        Assert ((Read-Operation $node).Apply($document)) "Repeated patch failed: $module"
    }
    Assert ((Canonical-Xml $document) -ceq (Canonical-Xml $expected)) "Repeated patch changed XML: $order"
    $helod = $document.SelectSingleNode('/Defs/ThingDef[defName="Helod"]')
    foreach ($comp in 'CombatExtended.CompPawnGizmo','CombatExtended.CompAmmoGiver','FacialAnimation.DrawFaceGraphicsComp','FacialAnimation.FacialAnimationControllerComp') {
        if ($comp.StartsWith('CombatExtended.') -and $order -notcontains 'CE') { continue }
        if ($comp.StartsWith('FacialAnimation.') -and $order -notcontains 'Facial') { continue }
        Assert ($helod.SelectNodes("comps/li[compClass='$comp']").Count -eq 1) "Missing/duplicate comp: $comp"
    }
    if ($order -contains 'CE') {
    Assert ($helod.SelectNodes('comps/li[@Class="CombatExtended.CompProperties_Suppressable"]').Count -eq 1) 'Missing/duplicate suppression comp.'
    Assert ($helod.SelectSingleNode('modExtensions/li[@Class="CombatExtended.RacePropertiesExtensionCE"]/bodyShape').InnerText -eq 'Humanoid') 'CE hit profile missing.'
    Assert ($helod.SelectNodes('tools/li[@Class="CombatExtended.ToolCE"]').Count -eq 3) 'CE melee tools missing.'
    }
    Assert ($helod.SelectNodes('modExtensions/li[@Class="Helodrace.HelodRaceExtension"]').Count -eq 1) 'Standalone race extension lost.'
    Write-Output "PASS: $order standalone/ordered/repeated application equals pre-refactor XML; components unique."
}
foreach ($module in 'Helodrace-CombatExtended','Helodrace-Facial','Helodrace-blancasdrugs','Helodrace-LobosArsenal') {
    $root = Join-Path $ModuleRoot $module
    if (!(Test-Path -LiteralPath $root)) { continue }
    $files = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.Extension -in '.xml','.cs','.csproj' -and $_.FullName -notmatch '\\(obj|Archive)\\'
    }
    Assert (!(Select-String -LiteralPath $files.FullName -Pattern 'AlienRace|humanoidalienraces')) "Legacy dependency in $module"
}
Write-Output 'PASS: four companion modules have no active HAR references. In-game gizmo checks still required.'

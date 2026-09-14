param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld', [switch]$PatchSmokeTest)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
$files = Get-ChildItem -LiteralPath "$repo/About", "$repo/Defs", "$repo/Patches", "$repo/Languages" -Recurse -Filter *.xml
foreach ($file in $files) { $null = [xml](Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8) }
Write-Output "PASS: $($files.Count) XML files parse."
$active = Get-ChildItem -LiteralPath "$repo/About", "$repo/Defs", "$repo/Patches", "$repo/Source/Helodrace", "$repo/Languages" -Recurse -File |
    Where-Object { $_.Extension -in '.xml', '.cs', '.csproj' -and $_.FullName -notmatch '\\obj\\' }
Assert (!(Select-String -LiteralPath $active.FullName -Pattern 'AlienRace|humanoidalienraces')) 'Active framework reference found.'
[xml]$race = Get-Content -LiteralPath "$repo/Defs/Helod/Race/GeneralRace.xml" -Raw -Encoding UTF8
$settings = $race.Defs.ThingDef.modExtensions.li
Assert ($settings.Class -eq 'Helodrace.HelodRaceExtension') 'Standalone extension missing.'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead("$repo/Archive/PreHAR-7725683-2026-09-14.zip")
try {
    foreach ($entry in $zip.Entries) {
        $stream = $entry.Open()
        try { $stream.CopyTo([IO.Stream]::Null) } finally { $stream.Dispose() }
    }
    $reader = [IO.StreamReader]::new($zip.GetEntry('Defs/Helod/Race/GeneralRace.xml').Open())
    try { [xml]$old = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $oldSettings = $old.Defs.'AlienRace.ThingDef_AlienRace'.alienRace
    foreach ($name in 'apparelList', 'whiteApparelList', 'blackGeneList', 'blackEndoCategories', 'xenotypeList') {
        $before = @($oldSettings.SelectNodes(".//$name/li") | ForEach-Object { $_.InnerText })
        $after = @($settings.SelectNodes("$name/li") | ForEach-Object { $_.InnerText })
        Assert (!(Compare-Object $before $after)) "Migrated list differs: $name"
        Write-Output "PASS: $name ($($after.Count)) preserved."
    }
    $before = @($oldSettings.generalSettings.alienPartGenerator.headTypes.li)
    Assert (!(Compare-Object $before @($settings.headTypes.li))) 'Head choices changed.'
    $before = @($oldSettings.SelectNodes('.//individualPaths/li') | ForEach-Object { $_.InnerXml })
    $after = @($settings.apparelGraphics.li | ForEach-Object { $_.InnerXml })
    Assert (!(Compare-Object $before $after)) 'Apparel paths changed.'
    Write-Output "PASS: $($before.Count) apparel graphic mappings and head choices preserved; archive readable."
} finally { $zip.Dispose() }
$managed = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($name in 'UnityEngine.CoreModule', 'UnityEngine', 'Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $managed "$name.dll"))
}
$null = [Reflection.Assembly]::LoadFrom("$repo/Assemblies/0Harmony.dll")
$assembly = [Reflection.Assembly]::LoadFrom("$repo/Assemblies/Helodrace.dll")
Assert (!($assembly.GetReferencedAssemblies() | Where-Object Name -Match 'AlienRace')) 'Compiled dependency found.'
$types = @($assembly.GetTypes() | Where-Object Name -Like 'Patch_Helod*')
Assert ($types.Count -ge 15) 'Standalone patches missing from build.'
Write-Output "PASS: compiled assembly has no framework reference; $($types.Count) standalone patch classes loaded."
# Minimal managed fixtures; no game session, native graphics, or save files are touched.
function New-Fixture([Type]$type) { [Runtime.Serialization.FormatterServices]::GetUninitializedObject($type) }
$fixtureRace = New-Fixture ([Verse.ThingDef])
[Verse.Def].GetField('defName').SetValue($fixtureRace, 'Helod')
$fixtureSettings = [Helodrace.HelodRaceExtension]::new()
$extensions = [Collections.Generic.List[Verse.DefModExtension]]::new()
$extensions.Add($fixtureSettings)
[Verse.Def].GetField('modExtensions').SetValue($fixtureRace, $extensions)
[Verse.DefDatabase[Verse.ThingDef]]::Add($fixtureRace)
$fixtureKind = New-Fixture ([Verse.PawnKindDef])
$fixtureKind.defName = 'HD_WW_HelodColonist'
$fixtureKind.race = $fixtureRace
[Verse.DefDatabase[Verse.PawnKindDef]]::Add($fixtureKind)
$fixtureXenotype = New-Fixture ([RimWorld.XenotypeDef])
$fixtureXenotype.defName = 'TestHelod'
$fixtureSettings.xenotypeList.Add($fixtureXenotype)
$baseliner = New-Fixture ([RimWorld.XenotypeDef])
$baseliner.defName = 'Baseliner'
$request = [Activator]::CreateInstance([Verse.PawnGenerationRequest])
$request.KindDef = $fixtureKind
$request.FixedGender = [Verse.Gender]::Female
$request.AllowedDevelopmentalStages = [Verse.DevelopmentalStage]::Newborn
$request.ForcedXenotype = $baseliner
[Helodrace.Patch_HelodPawnRequest]::Prefix([ref]$request)
Assert ([object]::ReferenceEquals($request.ForcedXenotype, $baseliner)) 'Newborn inherited xenotype request was overwritten.'
$fixturePawn = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Verse.Pawn])
[Verse.Thing].GetField('def').SetValue($fixturePawn, $fixtureRace)
$traitDef = New-Fixture ([RimWorld.TraitDef])
$traitDef.defName = 'SpeedOffset'
$slow = [RimWorld.Trait]::new($traitDef, -1, $false)
$fast = [RimWorld.Trait]::new($traitDef, 1, $false)
Assert (![Helodrace.HelodRace]::CanHaveTrait($fixturePawn, $slow)) 'Forbidden slow trait accepted.'
Assert ([Helodrace.HelodRace]::CanHaveTrait($fixturePawn, $fast)) 'Allowed fast trait rejected.'
$geneDef = New-Fixture ([Verse.GeneDef])
$fixtureSettings.blackGeneList.Add($geneDef)
Assert (![Helodrace.HelodRace]::CanHaveGene($fixturePawn, $geneDef)) 'Forbidden gene accepted.'
$choices = [Collections.Generic.Dictionary[RimWorld.XenotypeDef, single]]::new()
$choices.Add($fixtureXenotype, 0.5)
$choices.Add($baseliner, 0.5)
[Helodrace.Patch_HelodXenotypeChoices]::Postfix($fixtureKind, $choices)
Assert ($choices.Count -eq 1 -and $choices.ContainsKey($fixtureXenotype)) 'Helod xenotype filtering failed.'
$humanKind = New-Fixture ([Verse.PawnKindDef])
$humanKind.race = New-Fixture ([Verse.ThingDef])
$choices.Add($baseliner, 0.5)
[Helodrace.Patch_HelodXenotypeChoices]::Postfix($humanKind, $choices)
Assert ($choices.Count -eq 1 -and $choices.ContainsKey($baseliner)) 'Human xenotype filtering failed.'
Write-Output 'PASS: newborn inheritance, trait degree, gene blocking, and bidirectional xenotype filtering regressions.'
if ($PatchSmokeTest) {
    $harmony = [HarmonyLib.Harmony]::new('Helodrace.Standalone.SmokeTest')
    try {
        foreach ($type in $types) {
            $null = $harmony.CreateClassProcessor($type).Patch()
            Write-Output "PASS: Harmony installed $($type.Name)"
        }
    } finally { $harmony.UnpatchAll('Helodrace.Standalone.SmokeTest') }
}
Write-Output 'PASS: standalone static verification complete. In-game visual/play testing is still required.'

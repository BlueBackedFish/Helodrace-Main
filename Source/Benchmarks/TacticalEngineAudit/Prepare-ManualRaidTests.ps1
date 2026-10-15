param(
    [Parameter(Mandatory=$true)][string]$SaveFile,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$source = (Get-Item -LiteralPath $SaveFile).FullName
$output = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
$allowed = 'C:\Users\Public\Documents\ESTsoft\CreatorTemp\'
if (-not $output.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a new directory under CreatorTemp.' }
if (Test-Path -LiteralPath $output) { throw 'Output already exists; preserve prior test saves and evidence.' }
if ($Launch -and (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue)) { throw 'Preserve the running game. Prepare without -Launch or wait until it exits.' }

$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
[xml]$save = [IO.File]::ReadAllText($source)
if (-not $save.savegame.meta.modIds.li -or -not $save.savegame.game) { throw 'Expected a RimWorld save with a game and active mod IDs.' }
$installed = Join-Path $GameRoot 'Mods\HelodRace-Main'
$content = @('Assemblies/Helodrace.dll', 'Defs/Helod/Pawns/PawnKinds.xml', 'Defs/Organization/NewTacticalJobs.xml', 'Defs/GreatWar/Items/Grenades_GreatWar.xml')
$hashes = @{}
foreach ($relative in $content) {
    $built = (Get-FileHash -LiteralPath (Join-Path $repository $relative) -Algorithm SHA256).Hash
    $deployed = (Get-FileHash -LiteralPath (Join-Path $installed $relative) -Algorithm SHA256).Hash
    if ($built -ne $deployed) { throw "Installed content differs: $relative. Deploy it before preparing this test session." }
    $hashes[$relative] = $built
}

New-Item -ItemType Directory -Path (Join-Path $output 'Saves'), (Join-Path $output 'Config') | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $output 'Saves\Base.rws')
Copy-Item -LiteralPath $source -Destination (Join-Path $output 'Saves\Autostart.rws')
# Retain the user's mod preferences, but only inside this separate save root.
$userConfig = Join-Path (Split-Path (Split-Path $source)) 'Config'
if (Test-Path -LiteralPath $userConfig) {
    Get-ChildItem -LiteralPath $userConfig -File -Filter '*.xml' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output 'Config')
    }
}
$utf8 = New-Object Text.UTF8Encoding($false)
$mods = New-Object Xml.XmlDocument
$root = $mods.CreateElement('ModsConfigData'); [void]$mods.AppendChild($root)
$version = $mods.CreateElement('version'); $version.InnerText = $save.savegame.meta.gameVersion; [void]$root.AppendChild($version)
$active = $mods.CreateElement('activeMods'); [void]$root.AppendChild($active)
foreach ($id in $save.savegame.meta.modIds.li) { $li = $mods.CreateElement('li'); $li.InnerText = [string]$id; [void]$active.AppendChild($li) }
$mods.Save((Join-Path $output 'Config\ModsConfig.xml'))
$prefsPath = Join-Path $output 'Config\Prefs.xml'
[xml]$prefs = if (Test-Path -LiteralPath $prefsPath) { [IO.File]::ReadAllText($prefsPath) } else { '<PrefsData />' }
foreach ($setting in @{devMode='True'; runInBackground='False'}.GetEnumerator()) {
    $node = $prefs.PrefsData.SelectSingleNode($setting.Key)
    if ($null -eq $node) { $node = $prefs.CreateElement($setting.Key); [void]$prefs.DocumentElement.AppendChild($node) }
    $node.InnerText = $setting.Value
}
$prefs.Save($prefsPath)

# Launching is opt-in. This is an interactive session: no audit arguments,
# forced speed, injected raid, invulnerability, time limit or automatic quit.
$arguments = @(('"-savedatafolder=' + $output + '"'), '-hdTacticalEngine=new', '-hdMethodProfileDisabled=true',
    '-logFile', ('"' + $output + '\Player.log"'))
$launchText = @'
$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'A RimWorld process is already running; it will not be closed.' }
'@
$launchText += "`r`n" + '$arguments = @(' + (($arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', ') + ")`r`n"
$executableLiteral = "'" + (Join-Path $GameRoot 'RimWorldWin64.exe').Replace("'", "''") + "'"
$gameRootLiteral = "'" + $GameRoot.Replace("'", "''") + "'"
$launchText += "Start-Process -FilePath $executableLiteral -WorkingDirectory $gameRootLiteral -WindowStyle Normal -ArgumentList `$arguments`r`n"
[IO.File]::WriteAllText((Join-Path $output 'Start-TestGame.ps1'), $launchText, $utf8)
$manifest = [ordered]@{
    preparedUtc = [DateTime]::UtcNow.ToString('o'); sourceSave = $source; sourceSha256 = $sourceHash
    baseSave = (Join-Path $output 'Saves\Base.rws'); outputRoot = $output; contentSha256 = $hashes
    checklist = (Join-Path $repository 'Docs\전술\습격 기능 약식 테스트 목록.md')
    methodProfilerEnabled = $false; raidSpawned = $false; gameLaunched = [bool]$Launch; arguments = $arguments
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'preparation.json') -Encoding UTF8
$instructions = @"
Manual raid tests - original map preserved

1. Read the 12 batches in: $($manifest.checklist)
2. Run Start-TestGame.ps1 (powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-TestGame.ps1).
3. Developer mode loads Autostart.rws. If needed, select Base from Load game.
4. Start B01. No raid or test scenario has been spawned automatically.
5. Save batch checkpoints under separate names; reload Base for incompatible branches.
6. Enable the tactical plan-node View switch manually once per game session.
7. Record only completed F IDs or problems; untouched branches remain pending.

Base.rws and Autostart.rws match the original save. Do not overwrite the original.
All new saves, preferences and Player.log stay inside: $output
This launcher does not profile, force speed, protect pawns or quit automatically.
Required DLL and key definitions matched the installed mod at preparation time.
Reprepare in a fresh directory after changing the DLL/definitions.
"@
[IO.File]::WriteAllText((Join-Path $output 'START-HERE.txt'), $instructions, $utf8)
foreach ($copy in @($source, (Join-Path $output 'Saves\Base.rws'), (Join-Path $output 'Saves\Autostart.rws'))) {
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Original/copy hash mismatch.' }
}
if ($Launch) { & ([scriptblock]::Create($launchText)) }
Write-Output "Manual test environment prepared: $output (launched=$([bool]$Launch))"

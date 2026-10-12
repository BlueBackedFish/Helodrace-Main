param(
    [Parameter(Mandatory=$true)][string]$SaveFile,
    [Parameter(Mandatory=$true)][string]$AuditRoot,
    [int]$Ticks = 6000,
    [int]$SpawnX = 90,
    [int]$SpawnZ = 169,
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'Preserve the running game; launch this isolated replay after it exits.' }
$auditPath = [IO.Path]::GetFullPath($AuditRoot)
if (-not $auditPath.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Use a fresh CreatorTemp directory.' }
if (Test-Path -LiteralPath $auditPath) { throw 'Preserve existing replay evidence.' }
$sourceHash = (Get-FileHash -LiteralPath $SaveFile).Hash
[xml]$saveXml = [IO.File]::ReadAllText($SaveFile)
New-Item -ItemType Directory -Path (Join-Path $auditPath 'Saves') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $auditPath 'Config') -Force | Out-Null
Copy-Item -LiteralPath $SaveFile -Destination (Join-Path $auditPath 'Saves\Autostart.rws')
$mods = '<ModsConfigData><version>1.6.4871 rev591</version><activeMods>'
foreach ($id in $saveXml.savegame.meta.modIds.li) { $mods += '<li>' + [Security.SecurityElement]::Escape($id) + '</li>' }
$mods += '</activeMods></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $auditPath 'Config\ModsConfig.xml'),$mods,[Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $auditPath 'Config\Prefs.xml'),'<PrefsData><devMode>True</devMode><runInBackground>True</runInBackground></PrefsData>',[Text.Encoding]::UTF8)
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
foreach ($name in @('Helodrace.dll','Helodrace.pdb')) {
    $source = Join-Path $repository "Assemblies\$name"
    $target = Join-Path $GameRoot "Mods\HelodRace-Main\Assemblies\$name"
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Installed assembly mismatch.' }
}
$launchArguments = @(('"-savedatafolder=' + $auditPath + '"'),('"-hdCqbSavedMapAudit=' + $auditPath + '\audit.json"'),
    '-hdTacticalEngine=new','-hdMethodProfileDisabled=true',"-hdTacticalAuditSample=$Ticks",
    "-hdCqbSavedSpawnX=$SpawnX","-hdCqbSavedSpawnZ=$SpawnZ",
    '-screen-fullscreen','0','-screen-width','800','-screen-height','600','-logFile',('"' + $auditPath + '\Player.log"'))
@{ saveSource=$SaveFile; sourceSha256=$sourceHash; assemblySha256=(Get-FileHash -LiteralPath (Join-Path $repository 'Assemblies\Helodrace.dll')).Hash;
    originalMapPreserved=$true; profilerDisabled=$true; ticks=$Ticks; arguments=$launchArguments } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $auditPath 'launcher.json') -Encoding UTF8
$process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorldWin64.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList $launchArguments
$process.Id | Set-Content -LiteralPath (Join-Path $auditPath 'process-id.txt')
if ((Get-FileHash -LiteralPath $SaveFile).Hash -ne $sourceHash) { throw 'Source save changed unexpectedly.' }
Write-Output "Saved-map replay started: pid=$($process.Id) root=$auditPath"

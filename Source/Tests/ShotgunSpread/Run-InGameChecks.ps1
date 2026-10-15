param(
    [Parameter(Mandatory = $true)][string]$RuntimeRoot,
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyRoot = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [switch]$ImpactAudit,
    [switch]$PhysicalPosition
)
$ErrorActionPreference = 'Stop'
if ($PhysicalPosition -and -not $ImpactAudit) { throw 'PhysicalPosition is an A/B option for ImpactAudit only.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
if (Test-Path -LiteralPath $runtime) { throw 'Use a fresh runtime directory to preserve previous evidence.' }
$staging = [IO.Path]::GetFullPath((Join-Path $repository '.codex-staging')) + [IO.Path]::DirectorySeparatorChar
$temporary = 'C:\Users\Public\Documents\ESTsoft\CreatorTemp\'
if (-not $runtime.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -and
    -not $runtime.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a dedicated .codex-staging or CreatorTemp directory.' }
if (-not (Test-Path -LiteralPath (Join-Path $HarmonyRoot 'About/About.xml'))) { throw 'HarmonyRoot must point to the installed Harmony mod.' }
New-Item -ItemType Directory -Path (Join-Path $runtime 'Mods/Main/Assemblies') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtime 'SaveData/Config') -Force | Out-Null
foreach ($name in @('Data', 'MonoBleedingEdge', 'RimWorldWin64_Data')) {
    New-Item -ItemType Junction -Path (Join-Path $runtime $name) -Target (Join-Path $GameRoot $name) | Out-Null
}
foreach ($name in @('RimWorldWin64.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $GameRoot $name) -Destination (Join-Path $runtime $name)
}
foreach ($name in @('About', 'Defs', 'Languages', 'Patches', 'Sounds', 'Textures')) {
    New-Item -ItemType Junction -Path (Join-Path $runtime ('Mods/Main/' + $name)) -Target (Join-Path $repository $name) | Out-Null
}
New-Item -ItemType Junction -Path (Join-Path $runtime 'Mods/Harmony') -Target $HarmonyRoot | Out-Null
$config = '<ModsConfigData><version>1.6.4871 rev591</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>bluebackedfish.helodrace.main</li></activeMods></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $runtime 'SaveData/Config/ModsConfig.xml'), $config)
[IO.File]::WriteAllText((Join-Path $runtime 'SaveData/Config/Prefs.xml'), '<PrefsData><devMode>True</devMode><runInBackground>True</runInBackground></PrefsData>')
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') --no-restore -p:DefineConstants=SHOTGUN_TESTS -p:IntermediateOutputPath=obj/ShotgunGame/ ("-p:OutputPath=" + (Join-Path $runtime 'Mods/Main/Assemblies/')) -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Shotgun in-game validation build failed.' }
$arguments = @('-quicktest', '-batchmode', '-nographics', ('"-savedatafolder=' + (Join-Path $runtime 'SaveData') + '"'),
    '-hdMethodProfileDisabled=true', '-logFile', ('"' + (Join-Path $runtime 'Player.log') + '"'))
if ($ImpactAudit) { $arguments += '-hdShotgunImpactAudit=true' }
if ($PhysicalPosition) { $arguments += '-hdShotgunAuditPhysicalPosition=true' }
$process = Start-Process -FilePath (Join-Path $runtime 'RimWorldWin64.exe') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -ArgumentList $arguments
Write-Output "Shotgun in-game checks started: pid=$($process.Id) results=$(Join-Path $runtime 'SaveData/shotgun-results.txt')"

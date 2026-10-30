# Shared by actual-game checks. This file defines functions and does not start a game.
function Get-Game17Processes {
    # CIM remains authoritative when Get-Process cannot observe an elevated host.
    Get-CimInstance Win32_Process -Filter "Name='RimWorld.exe' OR Name='RimWorldWin64.exe'"
}

function Wait-Game17Host {
    param([Parameter(Mandatory)][string]$RuntimeRoot,[int]$TimeoutSeconds=300)
    & (Join-Path $PSScriptRoot 'Wait-Game17Host.ps1') -RuntimeRoot $RuntimeRoot -TimeoutSeconds $TimeoutSeconds
}

function New-Game17Runtime {
    param(
        [Parameter(Mandatory)][string]$RuntimeRoot,
        [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
        [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
        [string]$BlancasRoot,
        [string[]]$ExtraPackageIds=@(),
        [string[]]$ExtraModRoots=@(),
        [switch]$PrepareOnly
    )
    $ErrorActionPreference='Stop'
    $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $runtime=[IO.Path]::GetFullPath($RuntimeRoot)
    $allowed=[IO.Path]::GetFullPath((Join-Path $repository '.codex-staging'))+[IO.Path]::DirectorySeparatorChar
    $temporary='C:\Users\Public\Documents\ESTsoft\CreatorTemp\'
    if(-not $runtime.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -and
        -not $runtime.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a dedicated runtime under .codex-staging or CreatorTemp.'}
    if(Test-Path -LiteralPath $runtime){throw 'Use a fresh runtime to preserve earlier evidence.'}
    if(-not $PrepareOnly -and @(Get-Game17Processes).Count){throw 'Preserve the running game; wait for it to exit before collecting another host log.'}
    $version=(Get-Content -LiteralPath (Join-Path $GameRoot 'Version.txt') -Raw).Trim()
    if(-not $version.StartsWith('1.7.')){throw 'An actual RimWorld 1.7 runtime is required.'}
    if(-not $BlancasRoot){$BlancasRoot=Join-Path $repository '../Helodrace-blancasdrugs'}
    $harmony=Join-Path $HarmonyRoot '1.7/Assemblies/0Harmony.dll'
    if([Reflection.AssemblyName]::GetAssemblyName($harmony).Version.Major -ne 3){throw 'Harmony 3 is required.'}
    foreach($file in @('RimWorld.exe','Assembly-CSharp.dll','UnityStubs.dll','Waylith.dll')) {
        if(-not(Test-Path -LiteralPath (Join-Path $GameRoot $file))){throw "Missing 1.7 host file: $file"}
    }
    if(-not(Test-Path -LiteralPath (Join-Path $BlancasRoot '1.7/Assemblies/HelodBlancasDrug.dll'))){throw 'Build the required Blancas 1.7 assembly first.'}
    $extraMods=@(foreach($path in $ExtraModRoots){
        $modRoot=(Get-Item -LiteralPath $path).FullName
        [xml]$about=Get-Content -LiteralPath (Join-Path $modRoot 'About/About.xml') -Raw
        $package=([string]$about.ModMetaData.packageId).Trim()
        if(-not $package){throw ('An additional mod needs a packageId: '+$modRoot)}
        [pscustomobject]@{Root=$modRoot;PackageId=$package}
    })
    $ExtraPackageIds+=@($extraMods | ForEach-Object {$_.PackageId})
    $mods=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','BlueBackedFish.Helod.BlancasDrugs','BlueBackedFish.Helodrace.Main')+$ExtraPackageIds
    $unique=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($id in $mods){if(-not $unique.Add($id)){throw ('Duplicate runtime packageId: '+$id)}}
    $game=Join-Path $runtime 'Game'; $profile=Join-Path $runtime 'Profile'; $evidence=Join-Path $runtime 'Evidence'
    foreach($directory in @('Game/Mods/Main/Assemblies','Profile/Config','Evidence')){
        New-Item -ItemType Directory -Path (Join-Path $runtime $directory) -Force | Out-Null
    }
    foreach($directory in Get-ChildItem -LiteralPath $GameRoot -Directory | Where-Object {$_.Name -ne 'Mods'}){
        New-Item -ItemType Junction -Path (Join-Path $game $directory.Name) -Target $directory.FullName | Out-Null
    }
    foreach($file in Get-ChildItem -LiteralPath $GameRoot -File){
        New-Item -ItemType HardLink -Path (Join-Path $game $file.Name) -Target $file.FullName | Out-Null
    }
    foreach($directory in @('About','Defs','Patches','Languages','Textures','Sounds')){
        New-Item -ItemType Junction -Path (Join-Path $game ('Mods/Main/'+$directory)) -Target (Join-Path $repository $directory) | Out-Null
    }
    New-Item -ItemType Junction -Path (Join-Path $game 'Mods/Harmony') -Target $HarmonyRoot | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $game 'Mods/Blancas') -Target $BlancasRoot | Out-Null
    for($i=0;$i -lt $extraMods.Count;$i++){
        New-Item -ItemType Junction -Path (Join-Path $game ('Mods/Extra-'+$i)) -Target $extraMods[$i].Root | Out-Null
    }
    foreach($file in @('Helodrace.dll','Helodrace.pdb')){
        $source=Join-Path $repository ('Assemblies/'+$file)
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $game ('Mods/Main/Assemblies/'+$file))}
    }
    $xml=[xml]'<ModsConfigData><version/><activeMods/></ModsConfigData>'
    $xml.ModsConfigData.version=$version
    foreach($id in $mods){$item=$xml.CreateElement('li');$item.InnerText=$id.ToLowerInvariant();$null=$xml.SelectSingleNode('/ModsConfigData/activeMods').AppendChild($item)}
    $xml.Save((Join-Path $profile 'Config/ModsConfig.xml'))
    [IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<PrefsData><devMode>True</devMode><runInBackground>True</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic></PrefsData>')
    [pscustomobject]@{Root=$runtime;Game=$game;Profile=$profile;Evidence=$evidence;Repository=$repository;Version=$version;GameRoot=$GameRoot;HarmonyAssembly=$harmony;BlancasRoot=$BlancasRoot;ExtraMods=$extraMods;ExplicitMods=@('brrainz.harmony','BlueBackedFish.Helod.BlancasDrugs','BlueBackedFish.Helodrace.Main')+$ExtraPackageIds}
}

function Start-Game17Runtime {
    param([Parameter(Mandatory)]$Runtime,[string[]]$Arguments=@(),[switch]$Headless,[switch]$Visible,[string]$ResultFile,[string]$CompletionPattern='(?m)^COMPLETE\r?$',[switch]$EnableMethodProfile,[hashtable]$Metadata=@{},[switch]$SkipQuicktest)
    $ErrorActionPreference='Stop'
    if(@(Get-Game17Processes).Count){throw 'A game started during setup; preserve it and start this prepared runtime after it exits.'}
    $tokens=@(('-savedatafolder='+$Runtime.Profile),('-mod='+($Runtime.ExplicitMods -join ';')))
    if(-not $SkipQuicktest){$tokens+='-quicktest'}
    if(-not $EnableMethodProfile){$tokens+='-hdMethodProfileDisabled=true'}
    $tokens+=$Arguments
    if($Headless){$tokens+='-Headless'}elseif(-not $Visible){$tokens+='-NoWindow'}
    # Start-Process joins ArgumentList; callers supply unquoted tokens.
    $quoted=foreach($token in $tokens){if($token.Contains('"') -or $token.EndsWith('\')){throw 'Pass unquoted argument tokens without trailing backslashes.'};'"'+$token+'"'}
    $manifest=@{version=$Runtime.Version;gameRoot=$Runtime.GameRoot;arguments=$tokens;startedUtc=[DateTime]::UtcNow.ToString('o');resultFile=$ResultFile;completionPattern=$CompletionPattern}
    $manifest.engineLog=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
    $manifest.engineAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $Runtime.Game 'Assembly-CSharp.dll')).Hash
    $manifest.mainAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $Runtime.Game 'Mods/Main/Assemblies/Helodrace.dll')).Hash
    $manifest.blancasAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $Runtime.BlancasRoot '1.7/Assemblies/HelodBlancasDrug.dll')).Hash
    $manifest.harmonyAssemblySha256=(Get-FileHash -LiteralPath $Runtime.HarmonyAssembly).Hash
    $manifest.extraMods=@(foreach($mod in $Runtime.ExtraMods){
        $hashes=@{}
        foreach($file in Get-ChildItem -LiteralPath $mod.Root -Recurse -File -Filter '*.dll'){
            $hashes[$file.FullName.Substring($mod.Root.TrimEnd('\').Length+1)]=(Get-FileHash -LiteralPath $file.FullName).Hash
        }
        @{root=$mod.Root;packageId=$mod.PackageId;allAssemblySha256=$hashes}
    })
    foreach($entry in $Metadata.GetEnumerator()){
        if($manifest.ContainsKey($entry.Key)){throw ('Reserved host manifest field: '+$entry.Key)}
        $manifest[$entry.Key]=$entry.Value
    }
    $style=if($Visible){'Normal'}else{'Hidden'}
    $process=Start-Process -FilePath (Join-Path $Runtime.Game 'RimWorld.exe') -WorkingDirectory $Runtime.Game -WindowStyle $style -PassThru -ArgumentList $quoted
    $null=$process.Handle
    $manifest.processId=$process.Id
    $manifest.processStartedUtc=$process.StartTime.ToUniversalTime().ToString('o')
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Runtime.Root 'launcher.json') -Encoding UTF8
    Write-Host ('1.7 check started: pid='+$process.Id+' root='+$Runtime.Root)
    return $process
}

function Wait-Game17Runtime {
    param([Parameter(Mandatory)]$Runtime,[int]$TimeoutSeconds=300,[switch]$KeepCaches,[Diagnostics.Process]$Process)
    $ErrorActionPreference='Stop'
    $manifest=Get-Content -LiteralPath (Join-Path $Runtime.Root 'launcher.json') -Raw | ConvertFrom-Json
    if(-not $Process){$Process=Get-Process -Id $manifest.processId -ErrorAction SilentlyContinue}
    $exitCode=$null
    if($process){
        $null=$process.Handle
        if([IO.Path]::GetFullPath($process.Path) -ne [IO.Path]::GetFullPath((Join-Path $Runtime.Game 'RimWorld.exe')) -or
            $process.StartTime.ToUniversalTime() -ne ([DateTime]$manifest.processStartedUtc).ToUniversalTime()){throw 'The recorded PID belongs to another process.'}
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw 'Observation timed out; this game was left running. Inspect the same PID before another launch.'}
        $exitCode=$process.ExitCode
    }
    elseif(Get-Game17Processes | Where-Object {$_.ProcessId -eq $manifest.processId}){
        throw 'The recorded host is still running but its process handle is unavailable. Preserve it and inspect the same PID.'
    }
    $log=Get-Item -LiteralPath $manifest.engineLog
    if($log.LastWriteTimeUtc -lt ([DateTime]$manifest.startedUtc).ToUniversalTime()){throw 'The global host log predates this run.'}
    Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $Runtime.Evidence 'engine.log') -Force
    $result=Get-Content -LiteralPath $manifest.resultFile -Raw
    $complete=$result -match $manifest.completionPattern
    @{processExitCode=$exitCode;completedUtc=[DateTime]::UtcNow.ToString('o');checksComplete=$complete} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Runtime.Evidence 'result.json') -Encoding UTF8
    if(-not $complete -or $result -match '(?m)^FAIL[: ]' -or ($null -ne $exitCode -and $exitCode -ne 0)){throw ('Game check failed. Inspect '+$manifest.resultFile+' and '+$Runtime.Evidence+'/engine.log')}
    if(-not $KeepCaches){$null=& (Join-Path $PSScriptRoot 'EngineMigration/Clear-MigrationCaches.ps1') -RuntimeRoot $Runtime.Root}
    Write-Output ('PASS: '+$manifest.resultFile)
}

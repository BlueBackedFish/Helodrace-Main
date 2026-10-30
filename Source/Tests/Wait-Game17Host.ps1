param([Parameter(Mandatory)][string]$RuntimeRoot,[int]$TimeoutSeconds=300)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Game17Runtime.ps1')
$runtime=[IO.Path]::GetFullPath($RuntimeRoot)
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$staging=[IO.Path]::GetFullPath((Join-Path $repository '.codex-staging'))+[IO.Path]::DirectorySeparatorChar
if(-not $runtime.StartsWith($staging,[StringComparison]::OrdinalIgnoreCase) -and
    -not $runtime.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use an isolated test runtime.'}
$manifest=Get-Content -LiteralPath (Join-Path $runtime 'launcher.json') -Raw | ConvertFrom-Json
$expectedExecutable=[IO.Path]::GetFullPath((Join-Path $runtime 'Game/RimWorld.exe'))
$clock=[Diagnostics.Stopwatch]::StartNew()
$exitCode=$null
while($true){
    $live=Get-Game17Processes | Where-Object {$_.ProcessId -eq $manifest.processId}
    if(-not $live){break}
    # WMI CreationDate has microsecond precision; Process.StartTime retains 100 ns.
    if($live.ExecutablePath -ne $expectedExecutable -or
        [Math]::Abs(($live.CreationDate.ToUniversalTime()-([DateTime]$manifest.processStartedUtc).ToUniversalTime()).Ticks) -gt 10){
        throw 'The recorded PID belongs to another process.'
    }
    if($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds){throw 'Observation timed out; preserve the same live host and inspect it before any new launch.'}
    $process=Get-Process -Id $manifest.processId -ErrorAction SilentlyContinue
    if($process){
        try{
            $null=$process.Handle
            if($process.WaitForExit([int][Math]::Max(1,[Math]::Min(1000,($TimeoutSeconds-$clock.Elapsed.TotalSeconds)*1000)))){$exitCode=$process.ExitCode;break}
        }finally{$process.Dispose()}
    }else{Start-Sleep -Milliseconds 500}
}
if(@(Get-Game17Processes).Count){throw 'Another host is already using the shared log; do not attribute its log to this run.'}
$log=Get-Item -LiteralPath $manifest.engineLog
if($log.LastWriteTimeUtc -lt ([DateTime]$manifest.startedUtc).ToUniversalTime()){throw 'The host log predates this run.'}
Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $runtime 'Evidence/engine.log') -Force
# Existing audit collectors consume this filename; it is a post-exit copy.
Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $runtime 'Player.log') -Force
@{processId=$manifest.processId;processExitCode=$exitCode;completedUtc=[DateTime]::UtcNow.ToString('o')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'Evidence/host-result.json') -Encoding UTF8
if($null -ne $exitCode -and $exitCode -ne 0){throw ('The host exited with code '+$exitCode)}
Write-Output ('Host exited; collected log: '+$runtime+'/Player.log')

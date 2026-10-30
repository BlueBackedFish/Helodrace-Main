param(
    [Parameter(Mandatory=$true)][string]$RuntimeRoot,
    [int]$TimeoutSeconds=300,
    [switch]$KeepCaches
)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=[IO.Path]::GetFullPath($RuntimeRoot)
$allowed=[IO.Path]::GetFullPath((Join-Path $repository '.codex-staging'))+[IO.Path]::DirectorySeparatorChar
if(-not $runtime.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a migration runtime under .codex-staging.'}
$manifest=Get-Content -LiteralPath (Join-Path $runtime 'launcher.json') -Raw | ConvertFrom-Json
$process=Get-Process -Id $manifest.processId -ErrorAction SilentlyContinue
if($process){
    if([IO.Path]::GetFullPath($process.Path) -notlike ($runtime+'\Game\RimWorld*.exe')){throw 'The recorded PID now belongs to another process.'}
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw 'Migration timed out. The game was left running; inspect it before starting another run.'}
    $exitCode=$process.ExitCode
}else{$exitCode=$null}
$checks=Join-Path $runtime 'Evidence/checks.txt'
if(-not(Test-Path -LiteralPath $checks)){throw 'The game exited without producing migration checks.'}
$result=Get-Content -LiteralPath $checks -Raw
if($manifest.engineLog -and (Test-Path -LiteralPath $manifest.engineLog)){
    $log=Get-Item -LiteralPath $manifest.engineLog
    if($log.LastWriteTimeUtc -lt ([DateTime]$manifest.startedUtc).ToUniversalTime()){throw 'The host log predates this run.'}
    Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $runtime 'Evidence/engine.log') -Force
}
@{processExitCode=$exitCode;completedUtc=[DateTime]::UtcNow.ToString('o');checksComplete=($result -match '(?m)^COMPLETE\r?$')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'Evidence/result.json') -Encoding UTF8
if($result -match '(?m)^FAIL ' -or $result -notmatch '(?m)^COMPLETE\r?$' -or ($null -ne $exitCode -and $exitCode -ne 0)){throw "Migration failed. See $checks"}
if(-not $KeepCaches){
    $freed=& (Join-Path $PSScriptRoot 'Clear-MigrationCaches.ps1') -RuntimeRoot $runtime
    Write-Output ('Cleared regenerable profile caches: '+[Math]::Round($freed/1MB)+' MiB. Use -KeepCaches to retain them.')
}
Write-Output ('PASS: '+$manifest.fixtureCase+'; '+([regex]::Matches($result,'(?m)^PASS ')).Count+' checks. Evidence: '+$runtime+'/Evidence')

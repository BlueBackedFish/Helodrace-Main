$ErrorActionPreference = 'Stop'
# Mock queue control only: these results are not native RimWorld evidence.
$runner = Join-Path $PSScriptRoot '..\Run-R7Functional.ps1'
$source = Get-Content -LiteralPath $runner -Raw -Encoding UTF8
$tokens=$null; $parseErrors=$null
[Management.Automation.Language.Parser]::ParseFile($runner,[ref]$tokens,[ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$queue = $source.Substring($source.IndexOf('$records = @()'))
$specs = & $runner -Preview | ConvertFrom-Json
$tail = @($specs | Select-Object -Skip 28 | ForEach-Object { $_.name })
if ($specs.Count -ne 44 -or $tail.Count -ne 16 -or $tail[0] -ne 'care-interrupt' -or $tail[-1] -ne 'reload-support') {
    throw 'Diagnostic selection must be exactly cases 29-44.'
}
$preview = & $runner -Preview -Cases $tail -ContinueOnFailure | ConvertFrom-Json
if (($preview.name -join ',') -ne ($tail -join ',')) { throw 'Selected Preview differs from explicit tail.' }

function Test-Queue([string]$Label, [bool]$Continue, [bool]$FailFirst, [bool]$Live, [bool]$LaunchFails,
    [string[]]$Expected, [bool]$Throws, [bool]$Complete, [bool]$Passed) {
    $root = Join-Path $env:TEMP ('hd-r7-mock-queue-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $selected = @($specs | Select-Object -First 2)
    $Cases = @($selected.name)
    $ContinueOnFailure = $Continue
    $content = @(); $pinned = @{}; $repository = ''
    $launcher = 'Invoke-MockLauncher'
    function Get-Process { param($Name,$Id,$ErrorAction) if ($Live) { [pscustomobject]@{ ProcessName='RimWorldWin64' } } }
    function Invoke-MockLauncher {
        param($Engine,$Workload,$Case,$Population,$Speed,$WarmupTicks,$SampleTicks,$Seed,$AuditRoot,
            [switch]$DefaultEngine,[switch]$High,[switch]$Reload,[switch]$RetainedReload,[switch]$NoMethodProfile)
        if ($LaunchFails) { throw 'Mock unsafe launch failure' }
        New-Item -ItemType Directory -Path $AuditRoot | Out-Null
        '999999' | Set-Content -LiteralPath (Join-Path $AuditRoot 'process-id.txt')
    }
    function Assert-R7FunctionalRun($Spec,$RunRoot,$Pinned,$Record) {
        if ($FailFirst -and $Spec.name -eq $selected[0].name) { throw 'Mock terminal validation failure' }
    }
    $caught=$false
    try { Invoke-Expression $queue | Out-Null } catch { $caught=$true }
    $journal = Get-Content -LiteralPath (Join-Path $root 'checks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($caught -ne $Throws -or ($journal.records.status -join ',') -ne ($Expected -join ',') -or
        $journal.collectionComplete -ne $Complete -or $journal.allSpecifiedPassed -ne $Passed -or
        $journal.finalR7Complete -or $journal.cpuGateEvaluated -or $journal.fullFunctionalQueue -or
        $journal.continueOnFailure -ne $Continue) { throw "Queue control assertion failed: $Label" }
    $failed=@($journal.records | Where-Object status -eq 'failed' | ForEach-Object name)
    if (($journal.failedCases -join ',') -ne ($failed -join ',')) { throw 'Failed-case journal mismatch' }
    Write-Output "Mock queue control passed: $Label"
}
Test-Queue 'default fail-fast' $false $true $false $false @('failed') $true $false $false
Test-Queue 'diagnostic continues after terminal failure' $true $true $false $false @('failed','passed') $false $true $false
Test-Queue 'all selected pass remains partial proof' $true $false $false $false @('passed','passed') $false $true $true
Test-Queue 'live native process stops diagnostic queue' $true $false $true $false @() $true $false $false
Test-Queue 'launch failure is not swallowed' $true $false $false $true @() $true $false $false

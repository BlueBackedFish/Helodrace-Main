param([Parameter(Mandatory=$true)][string]$Root, [switch]$AllowPartial, [switch]$RequireFinal)
$ErrorActionPreference='Stop'
if ($AllowPartial -and $RequireFinal) { throw 'A partial verification cannot be final functional proof.' }
. (Join-Path $PSScriptRoot 'R7FunctionalEvidence.ps1')
$journal=Get-Content -Encoding UTF8 -LiteralPath (Join-Path $Root 'checks.json') -Raw | ConvertFrom-Json
$specs=& (Join-Path $PSScriptRoot 'Run-R7Functional.ps1') -Preview | ConvertFrom-Json
if ($RequireFinal -and (-not $journal.fullFunctionalQueue -or -not $journal.allSpecifiedPassed -or
    $journal.requestedCases.Count -ne $specs.Count -or $journal.records.Count -ne $specs.Count -or
    @($journal.records | Where-Object { $_.status -ne 'passed' }).Count)) { throw 'Full final functional queue has not passed.' }
if ($RequireFinal) {
    $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    foreach ($property in $journal.pinned.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $repository $property.Name)).Hash.ToLowerInvariant() -ne $property.Value) {
            throw "Final functional evidence uses different content: $($property.Name)"
        }
    }
}
$verified=@()
foreach ($record in $journal.records) {
    if ($record.status -ne 'passed') {
        if ($AllowPartial) { continue }
        throw "Preserve incomplete/failed native evidence: $($record.name)"
    }
    $spec=@($specs | Where-Object { $_.name -eq $record.name })
    if ($spec.Count -ne 1) { throw "Unknown or duplicate functional case: $($record.name)" }
    $verified += Assert-R7FunctionalRun $spec[0] (Join-Path $Root $record.name) $journal.pinned
}
if ($verified.Count -eq 0 -or @($verified.name | Sort-Object -Unique).Count -ne $verified.Count) { throw 'No unique completed functional evidence.' }
if (-not $AllowPartial -and $verified.Count -ne $journal.requestedCases.Count) { throw 'Requested native cases are missing.' }
[pscustomobject]@{ scope='Read-only native functional evidence verification'; partial=[bool]$AllowPartial;
    finalFunctionalVerified=[bool]$RequireFinal; finalR7Complete=$false; cpuGateEvaluated=$false; verified=$verified } | ConvertTo-Json -Depth 8

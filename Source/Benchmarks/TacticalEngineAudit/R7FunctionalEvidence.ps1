function Assert-R7FunctionalRun($Spec, [string]$RunRoot, $Pinned, $Record = $null) {
    $audit = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $RunRoot 'audit.json') -Raw | ConvertFrom-Json
    $manifest = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $RunRoot 'launcher.json') -Raw | ConvertFrom-Json
    $exceptions = @(Select-String -LiteralPath (Join-Path $RunRoot 'Player.log') -Pattern 'Exception:|Exception while|Error in ' -Encoding UTF8).Count
    $proof=[pscustomobject]@{ name=$Spec.name; actualMinimumSpeed=$audit.r7TickRateMinimum;
        actualMaximumSpeed=$audit.r7TickRateMaximum; reloads=$audit.r7Reloads;
        functionalComplete=$audit.newFunctionalComplete; nativeExceptions=$exceptions }
    if ($Record) {
        foreach ($field in @('actualMinimumSpeed','actualMaximumSpeed','reloads','functionalComplete','nativeExceptions')) {
            $Record.$field=$proof.$field
        }
    }
    # Reload rates remain functional evidence only, never a matched CPU gate.
    $speedValid = $audit.speed -eq 3 -and $audit.r7RateSamples -gt 0 -and
        $audit.r7TickRateMinimum -ge 1 -and $audit.r7TickRateMaximum -le 3 -and
        ($Spec.reloads -gt 0 -or ($audit.r7TickRateMinimum -eq 3 -and $audit.r7TickRateMaximum -eq 3))
    $content = @{
        assemblySha256='Assemblies\Helodrace.dll'; newJobDefinitionsSha256='Defs\Organization\NewTacticalJobs.xml';
        breachDefinitionsSha256='Defs\ColdWar\BreachExplosive_ColdWar.xml'; hammerJobDefinitionsSha256='Defs\GreatWar\Sledgehammer_Breach.xml'
    }
    foreach ($field in $content.Keys) {
        if ($manifest.$field -ne $Pinned.($content[$field])) { throw "Native content mismatch: $field" }
    }
    if (-not $audit.complete -or $audit.error -or -not $audit.isolationVerified -or -not $audit.environmentControlled -or
        -not $audit.r7RetiredTypesAbsent -or -not $audit.r7RetiredDefinitionsAbsent -or $exceptions -gt 0 -or
        $manifest.methodProfile -or $manifest.headless -or -not $manifest.defaultEngine -or
        $audit.engine -ne 'new' -or $audit.effectiveEngine -ne 'new' -or $audit.newUnsafeEntries -ne 0 -or -not $speedValid -or
        $audit.fixtureCase -ne $Spec.case -or $audit.workload -ne $Spec.workload -or $audit.requestedPopulation -ne $Spec.population -or
        $audit.warmupTicks -ne 0 -or $audit.sampleTicks -ne $Spec.ticks -or $audit.measuredTicks -lt $Spec.ticks -or
        $audit.seed -ne 'hd-r1-20261007' -or $manifest.seed -ne $audit.seed -or
        $manifest.fixtureCase -ne $audit.fixtureCase -or $manifest.requestedPopulation -ne $audit.requestedPopulation -or
        $manifest.high -ne $Spec.high -or $manifest.reload -ne $Spec.reload -or $manifest.retainedReload -ne $Spec.retained) {
        throw 'Native run, content, environment, retirement, fixture, speed or safety check failed.'
    }
    if ($Spec.functional -and -not $audit.newFunctionalComplete) { throw 'Full native functional completion failed.' }
    foreach ($flag in $Spec.flags) { if ($audit.$flag -ne $true) { throw "Required native proof missing: $flag" } }
    $minimums = @{}
    if ($Spec.minimums -is [Collections.IDictionary]) {
        foreach ($field in $Spec.minimums.Keys) { $minimums[$field]=$Spec.minimums[$field] }
    } elseif ($Spec.minimums) {
        foreach ($property in $Spec.minimums.PSObject.Properties) { $minimums[$property.Name]=$property.Value }
    }
    foreach ($field in $minimums.Keys) {
        if ($null -eq $audit.$field -or $audit.$field -lt $minimums[$field]) { throw "Required native count missing: $field >= $($minimums[$field])" }
    }
    if ($audit.r7Reloads -lt $Spec.reloads) { throw 'Required actual save/load count missing.' }
    return $proof
}

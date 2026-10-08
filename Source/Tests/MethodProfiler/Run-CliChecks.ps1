param([Parameter(Mandatory=$true)][string]$Capture)
$ErrorActionPreference = 'Stop'
$cli = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Tools\AgentProfiler\bin\Debug\net10.0\AgentProfiler.dll'))
$fixture = Join-Path 'C:\Users\Public\Documents\ESTsoft\CreatorTemp' ('hd-profile-cli-check-' + [guid]::NewGuid().ToString('N') + '.json')
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$response = & dotnet $cli compare $Capture $Capture
if ($LASTEXITCODE -ne 0) { throw 'Identical complete capture comparison failed.' }
$sample | Add-Member -NotePropertyName spikeTracing -NotePropertyValue (-not [bool]$sample.spikeTracing) -Force
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1) { throw 'Different spike tracing overhead was not rejected.' }
$response = & dotnet $cli benchmark-compare $Capture $fixture
if ($LASTEXITCODE -ne 1) { throw 'Different tracing modes were aggregated.' }
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$sample | Add-Member -NotePropertyName spikePawnId -NotePropertyValue 999999 -Force
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1) { throw 'Different pawn detail filter was accepted.' }
$response = & dotnet $cli benchmark-compare $Capture $fixture
if ($LASTEXITCODE -ne 1) { throw 'Different pawn detail filters were aggregated.' }
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
if ($sample.spikeTracing) {
    if ($sample.schema -ge 8) {
        $rejectedThreads = ($sample.methods | Measure-Object foreignThreadCalls -Sum).Sum
        $rejectedDepth = ($sample.methods | Measure-Object depthLimitCalls -Sum).Sum
        if ($sample.dropped -ne ($rejectedThreads + $rejectedDepth)) { throw 'Rejection reason accounting failed.' }
        $response = & dotnet $cli initial $Capture
        if ($LASTEXITCODE -ne 0) { throw 'Initial-call query failed.' }
        $initial = ($response -join "`n") | ConvertFrom-Json
        if ($initial.methods.Count -ne $sample.methods.Count -or $initial.initialCallCapacity -ne 8) {
            throw 'Initial-call method/capacity mapping failed.'
        }
        foreach ($method in $initial.methods) {
            if ($method.initialRecordedCalls -gt 8 -or $method.laterCalls -lt 0 -or
                @($method.initialCalls | Where-Object { $_.invocation -lt 1 -or $_.invocation -gt 8 }).Count -gt 0) {
                throw 'Initial-call bounds or invocation mapping failed.'
            }
        }
        $response = & dotnet $cli initial $Capture DoSingleTick
        if ($LASTEXITCODE -ne 0 -or (($response -join "`n") | ConvertFrom-Json).methods.Count -ne 1) {
            throw 'Initial-call method filter failed.'
        }
    }
    $sample.schema = $sample.schema - 1
    $sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
    $response = & dotnet $cli compare $Capture $fixture
    if ($LASTEXITCODE -ne 1) { throw 'Different tracing schema overhead was accepted.' }
    $response = & dotnet $cli benchmark-compare $Capture $fixture
    if ($LASTEXITCODE -ne 1) { throw 'Different tracing schema overhead was aggregated.' }
    $sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
    $response = & dotnet $cli spikes $Capture
    if ($LASTEXITCODE -ne 0) { throw 'Spike query failed.' }
    if ($sample.tickSpikes.Count -gt 0) {
        $tick = $sample.tickSpikes[0].root.tick
        $response = & dotnet $cli spikes $Capture $tick
        if ($LASTEXITCODE -ne 0) { throw 'Tick-filtered spike query failed.' }
        $filtered = ($response -join "`n") | ConvertFrom-Json
        if ($filtered.spikes.Count -ne 1 -or $filtered.spikes[0].root.tick -ne $tick -or
            $filtered.spikes[0].calls.Count -ne $sample.tickSpikes[0].calls.Count -or
            -not $filtered.spikes[0].root.method.Contains('DoSingleTick')) { throw 'Spike timeline/filter/name mapping failed.' }
        if ($sample.schema -ge 7) {
            if (-not $filtered.spikes[0].methodsComplete -or
                $filtered.spikes[0].wholeTickMethods.Count -ne $sample.tickSpikes[0].methods.Count) { throw 'Whole-tick method totals missing.' }
            $totalSelf = ($filtered.spikes[0].wholeTickMethods | Measure-Object trackedSelfMs -Sum).Sum
            if ([Math]::Abs($totalSelf - $sample.tickSpikes[0].root.milliseconds) -gt 0.0001) { throw 'Whole-tick self accounting failed.' }
            foreach ($method in $filtered.spikes[0].wholeTickMethods) {
                if ($method.method -eq 'unknown' -or $method.calls -le 0 -or $null -eq $method.referencePercentPerTick) {
                    throw 'Whole-tick names/counts/Core reference mapping failed.'
                }
            }
        }
        $actor = $sample.tickSpikes[0].calls | Where-Object pawnId -GE 0 | Select-Object -First 1
        if ($null -ne $actor) {
            $response = & dotnet $cli pawn $Capture $actor.pawnId $tick
            if ($LASTEXITCODE -ne 0) { throw 'Pawn query failed.' }
            $pawnResult = ($response -join "`n") | ConvertFrom-Json
            $expected = @($sample.tickSpikes[0].calls | Where-Object pawnId -EQ $actor.pawnId)
            if ($pawnResult.calls.Count -ne $expected.Count -or
                @($pawnResult.calls | Where-Object tick -NE $tick).Count -ne 0) { throw 'Pawn ID/tick filter failed.' }
        }
    }
    $sample.spikeTracing = $false
    $sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
    $response = & dotnet $cli spikes $fixture
    if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'disabled') { throw 'Disabled tracing query was accepted.' }
}
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$sample.methods = @($sample.methods | Select-Object -Skip 1)
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'target sets differ') { throw 'Different target set was not rejected.' }
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$sample.reference.workload = 'different reference workload'
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'reference') { throw 'Different Core reference was not rejected.' }
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$sample.benchmark.mapFingerprint = 'different map'
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'benchmark') { throw 'Different benchmark map was not rejected.' }
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$sample.benchmark.startPhases = 'different phase'
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'benchmark') { throw 'Different benchmark phase was not rejected.' }
$sample.benchmark.fixtureCase = 'different event'
$sample.benchmark.startPhases = (Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json).benchmark.startPhases
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'benchmark') { throw 'Different event fixture was not rejected.' }
$response = & dotnet $cli aggregate $Capture $Capture
if ($LASTEXITCODE -ne 0 -or (($response -join "`n") | ConvertFrom-Json).groups[0].runs -ne 2) { throw 'Repeated compatible captures did not aggregate.' }
$response = & dotnet $cli benchmark-compare $Capture $Capture
if ($LASTEXITCODE -ne 0 -or (($response -join "`n") | ConvertFrom-Json).groups[0].methods[0].deltaReferencePercentagePoints -ne 0) { throw 'Repeated self comparison was not zero.' }
$response = & dotnet $cli benchmark-compare $Capture $fixture
if ($LASTEXITCODE -ne 1 -or ($response -join "`n") -notmatch 'matching benchmark') { throw 'Different phase aggregated comparison was not rejected.' }
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$sample.endTick = $sample.startTick
$sample | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -LiteralPath $fixture
$response = & dotnet $cli compare $Capture $fixture
if ($LASTEXITCODE -ne 1) { throw 'Zero-tick comparison was not rejected.' }
$response = & dotnet $cli hotspots $fixture inclusive 1
if ($LASTEXITCODE -ne 0) { throw 'Zero-tick hotspot query failed.' }
$hotspot = ($response -join "`n") | ConvertFrom-Json
if ($null -ne $hotspot.methods[0].inclusiveMsPerTick) { throw 'Zero ticks did not produce null normalization.' }
if ($null -ne $hotspot.methods[0].referencePercentPerTick) { throw 'Zero ticks did not produce null reference normalization.' }
Remove-Item -LiteralPath $fixture
Write-Output 'CLI checks passed: tracing/pawn-filter comparison guards, spike and pawn queries, self comparison, targets/reference, zero ticks.'
exit 0

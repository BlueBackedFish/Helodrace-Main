# Preserve two focused native game runs and verify their source evidence.
$ErrorActionPreference = 'Stop'
$base = 'C:/Users/Public/Documents/ESTsoft/CreatorTemp'
$outRoot = $PSScriptRoot
$manifest = [Collections.Generic.List[object]]::new()
function ReadJson($path) { Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json }
function Preserve($root, $label) {
    $relativePaths = @('audit.json','launcher.json','Player.log','pawn-manifest.txt','profiles/capabilities.json','profiles/status.json')
    $status = ReadJson (Join-Path $root 'profiles/status.json')
    if ($status.latestCapture) { $relativePaths += ('profiles/' + $status.latestCapture) }
    foreach ($relative in $relativePaths) {
        $source = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $source)) { continue }
        $target = Join-Path $outRoot ('raw/' + $label + '/' + $relative + '.gz')
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        $data = [IO.File]::ReadAllBytes($source)
        $stream = [IO.MemoryStream]::new()
        $zip = [IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Compress,$true)
        $zip.Write($data,0,$data.Length); $zip.Dispose()
        $packed = $stream.ToArray(); $stream.Dispose(); [IO.File]::WriteAllBytes($target,$packed)
        $stream = [IO.MemoryStream]::new($packed)
        $zip = [IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Decompress)
        $sha = [Security.Cryptography.SHA256]::Create()
        $restoredHash = ([BitConverter]::ToString($sha.ComputeHash($zip))).Replace('-','').ToLowerInvariant()
        $sha.Dispose(); $zip.Dispose(); $stream.Dispose()
        $rawHash = (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
        if ($restoredHash -ne $rawHash) { throw 'Preserved payload hash mismatch.' }
        $manifest.Add(@{file=('raw/'+$label+'/'+$relative+'.gz');rawBytes=$data.Length;rawSha256=$rawHash})
    }
}
$runs = foreach ($index in 1,2) {
    $root = Join-Path $base ('hd-stack-spike-20261012-valid' + $index)
    $audit = ReadJson (Join-Path $root 'audit.json')
    $status = ReadJson (Join-Path $root 'profiles/status.json')
    $p = ReadJson (Join-Path $root ('profiles/' + $status.latestCapture))
    if (-not $audit.complete -or $audit.error -or -not $audit.isolationVerified -or -not $audit.environmentControlled -or
        $audit.unexpectedPawns.Count -ne 0 -or $audit.population -ne 12 -or $audit.newEnteredByOrder -ne 12 -or
        $audit.alive -ne 12 -or -not $audit.newPhysicalPlansValid -or -not $audit.newConnectedStacks -or
        -not $p.complete -or $p.dropped -ne 0 -or @($p.methods | Where-Object exceptions -ne 0).Count -gt 0) {
        throw 'Invalid native run/capture evidence.'
    }
    $ids = @{}; foreach ($m in $p.methods) { $ids[$m.id] = $m.method.Split('(')[0] }
    $rows = foreach ($m in $p.methods) {
        $initial = @($p.initialCalls | Where-Object methodId -eq $m.id)
        $afterFirst = if ($initial.Count -gt 1) { ($initial | Select-Object -Skip 1 | Measure-Object milliseconds -Average).Average } else { $null }
        [ordered]@{id=$m.id;name=$ids[$m.id];signature=$m.method;calls=$m.calls;exceptions=$m.exceptions;
            inclusiveMs=$m.inclusiveMs;trackedSelfMs=$m.trackedSelfMs;maxMs=$m.maxMs;
            meanMs=$(if($m.calls){$m.inclusiveMs/$m.calls}else{$null});callsPerTick=$m.calls/($p.endTick-$p.startTick);
            p50Ms=$m.p50Ms;p95Ms=$m.p95Ms;p99Ms=$m.p99Ms;cpuMeasured=$m.cpuMeasured;threadCpuMs=$m.threadCpuMs;
            referencePercentPerCall=$m.referencePercentPerCall;referencePercentPerTick=$m.referencePercentPerTick;
            initialCalls=$initial;afterFirstInitialMeanMs=$afterFirst}
    }
    $planCall = $p.initialCalls | Where-Object { $ids[$_.methodId].EndsWith('TacticalLocalPlanner.Find') } | Select-Object -First 1
    $planningTick = $p.tickSpikes | Where-Object { $_.root.tick -eq $planCall.tick }
    if (-not $planningTick.methodsComplete) { throw 'Planning tick method totals are missing.' }
    $ticks = foreach ($spike in $p.tickSpikes) {
        $methods = foreach ($m in $spike.methods) { [ordered]@{name=$ids[$m.methodId];calls=$m.calls;
            inclusiveMs=$m.inclusiveMs;trackedSelfMs=$m.trackedSelfMs;maxMs=$m.maxMs} }
        [ordered]@{root=$spike.root;gc0=$spike.gc0;gc1=$spike.gc1;gc2=$spike.gc2;
            detailsDropped=$spike.detailsDropped;methodsComplete=$spike.methodsComplete;methods=$methods}
    }
    $reference = @($p.reference.sampleMs | Sort-Object)
    Preserve $root ('run' + $index)
    [ordered]@{run=('run'+$index);assemblySha256=$p.assemblySha256;mapFingerprint=$audit.mapFingerprint;
        pawnFingerprint=$audit.pawnFingerprint;captureTicks=$p.endTick-$p.startTick;measuredTicks=$audit.measuredTicks;
        captureComplete=$p.complete;dropped=$p.dropped;population=$audit.population;units=$audit.units;
        entered=$audit.newEnteredByOrder;missionCompletedUnits=$audit.newCompletedUnits;contactShots=$audit.newContactShots;
        unsafeEntries=$audit.newUnsafeEntries;jobFailures=$audit.newJobFailures;
        referenceBatchMedianMs=$reference[[int][Math]::Floor($reference.Count/2)];mainThreadWindowCpuMs=$p.mainThreadWindowCpuMs;
        spikeCandidates=$p.spikeCandidates;planningTick=$planCall.tick;methods=$rows;retainedSpikes=$ticks}
}
foreach ($key in 'assemblySha256','mapFingerprint','pawnFingerprint') {
    if ($runs[0][$key] -ne $runs[1][$key]) { throw "Mismatched repeated-run $key." }
}
Preserve (Join-Path $base 'hd-stack-spike-20261012-run1') 'excluded-isolation-failure'
$summary = [ordered]@{condition=@{engine='new';case='normal';workload='sapper-wall';population=12;squads=1;
    roomInterior='39x35';warmupTicks=0;sampleTicks=1800;seed='hd-stack-spike-20261012';preset='spikes';thresholdMs=1};
    limitations=@('Method costs are elapsed; inclusive parent/child times overlap.',
    'Core 100% is a 10000-call batch, not CPU utilization.',
    'All 12 entered; mission remained Enter while firing at the protected owner.',
    'Two new processes do not establish large-raid or warm repeated-plan behavior.',
    'First-call timings suggest cold setup/JIT; JIT itself was not measured.',
    'Planning tick call details truncate, but complete method totals are retained.');runs=$runs;rawManifest=$manifest.ToArray()}
[IO.File]::WriteAllText((Join-Path $outRoot 'summary.json'),($summary | ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
$runs | ForEach-Object { $_.methods | Where-Object {$_.name -match 'AdvanceCore|LocalPlanner.Find|BuildStack|BuildPositions|AllocateEntry'} |
    ForEach-Object { [pscustomobject]@{name=$_.name;calls=$_.calls;totalMs=$_.inclusiveMs;maxMs=$_.maxMs;afterFirstMs=$_.afterFirstInitialMeanMs} } } | Format-Table -AutoSize
Write-Output "Preserved $($manifest.Count) verified raw files; repeated DLL/map/pawn fingerprints match."

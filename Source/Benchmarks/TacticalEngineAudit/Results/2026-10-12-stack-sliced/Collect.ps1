$ErrorActionPreference='Stop'
$base='C:/Users/Public/Documents/ESTsoft/CreatorTemp'
$manifest=[Collections.Generic.List[object]]::new()
function ReadJson($file){Get-Content -Raw -LiteralPath $file -Encoding UTF8 | ConvertFrom-Json}
function Preserve($root,$label){
    $paths=@('audit.json','launcher.json','Player.log','pawn-manifest.txt','profiles/capabilities.json','profiles/status.json')
    if(Test-Path (Join-Path $root 'profiles/status.json')){
        $status=ReadJson (Join-Path $root 'profiles/status.json')
        if($status.latestCapture){$paths+=('profiles/'+$status.latestCapture)}
    }
    foreach($relative in $paths){
        $source=Join-Path $root $relative;if(-not(Test-Path -LiteralPath $source)){continue}
        $target=Join-Path $PSScriptRoot ('raw/'+$label+'/'+$relative+'.gz')
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        $bytes=[IO.File]::ReadAllBytes($source);$stream=[IO.MemoryStream]::new()
        $zip=[IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Compress,$true)
        $zip.Write($bytes,0,$bytes.Length);$zip.Dispose();$packed=$stream.ToArray();$stream.Dispose()
        [IO.File]::WriteAllBytes($target,$packed)
        $stream=[IO.MemoryStream]::new($packed);$zip=[IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Decompress)
        $sha=[Security.Cryptography.SHA256]::Create();$restored=([BitConverter]::ToString($sha.ComputeHash($zip))).Replace('-','')
        $sha.Dispose();$zip.Dispose();$stream.Dispose();$hash=(Get-FileHash -LiteralPath $source).Hash
        if($restored -ne $hash){throw 'Archive payload hash mismatch'}
        $manifest.Add(@{file=('raw/'+$label+'/'+$relative+'.gz');rawBytes=$bytes.Length;sha256=$hash.ToLowerInvariant()})
    }
}
$captures=foreach($case in @(@{folder='hd-stack-sliced-20261012-check1';label='intermediate-allocation'},
    @{folder='hd-stack-sliced-20261012-final';label='sliced-allocation'})){
    $root=Join-Path $base $case.folder;$audit=ReadJson (Join-Path $root 'audit.json')
    $status=ReadJson (Join-Path $root 'profiles/status.json');$p=ReadJson (Join-Path $root ('profiles/'+$status.latestCapture))
    if(-not$p.complete -or $p.dropped -ne 0 -or @($p.methods|Where-Object exceptions -ne 0).Count -gt 0 -or
        -not$audit.complete -or $audit.error -or $audit.newEnteredByOrder -ne 12 -or -not$audit.newPhysicalPlansValid){throw 'Invalid focused capture'}
    $ids=@{};foreach($m in $p.methods){$ids[$m.id]=$m.method.Split('(')[0]}
    $rows=foreach($m in $p.methods){if($m.method -match 'Tactical|DoSingleTick|PathFinderTick'){
        @{method=$m.method;calls=$m.calls;inclusiveMs=$m.inclusiveMs;trackedSelfMs=$m.trackedSelfMs;maxMs=$m.maxMs;
        meanMs=$(if($m.calls){$m.inclusiveMs/$m.calls}else{$null});p95Ms=$m.p95Ms;p99Ms=$m.p99Ms;
        corePerCall=$m.referencePercentPerCall;corePerTick=$m.referencePercentPerTick;
        initialCalls=@($p.initialCalls|Where-Object methodId -eq $m.id)}
    }}
    Preserve $root $case.label
    @{label=$case.label;assemblySha256=$p.assemblySha256;captureComplete=$p.complete;dropped=$p.dropped;
    ticks=$p.endTick-$p.startTick;mapFingerprint=$audit.mapFingerprint;pawnFingerprint=$audit.pawnFingerprint;
    entered=$audit.newEnteredByOrder;reference=$p.reference;methods=$rows}
}
$functional=foreach($case in @(@{folder='hd-stack-sliced-20261012-room-check';label='old-fixture-condition'},
    @{folder='hd-stack-sliced-20261012-room-final';label='following-room-final'})){
    $root=Join-Path $base $case.folder;$a=ReadJson (Join-Path $root 'audit.json');$launcher=ReadJson (Join-Path $root 'launcher.json')
    if(-not$a.complete -or $a.error -or $a.newEnteredByOrder -ne 12 -or $a.newRoomsSecured -lt 1 -or $a.newRoomPlansAttempted -lt 1 -or
        $a.newJobFailures -ne 0 -or $a.newUnsafeEntries -ne 0){throw 'Invalid room continuation'}
    if($case.label -eq 'following-room-final' -and (-not$a.newPhysicalPlansValid -or -not$a.newConnectedStacks)){throw 'Invalid final footprints'}
    Preserve $root $case.label
    @{label=$case.label;assemblySha256=$launcher.assemblySha256;complete=$a.complete;entered=$a.newEnteredByOrder;
    physicalValid=$a.newPhysicalPlansValid;connected=$a.newConnectedStacks;roomsSecured=$a.newRoomsSecured;
    roomPlans=$a.newRoomPlansAttempted;unsafeEntries=$a.newUnsafeEntries;jobFailures=$a.newJobFailures;
    missionComplete=$a.newFunctionalComplete;diagnostics=$a.newRoomDiagnostics}
}
$dll=Join-Path $PSScriptRoot '../../../../../Assemblies/Helodrace.dll'
if((Get-FileHash -LiteralPath $dll).Hash -ne $functional[1].assemblySha256){throw 'Final functional run does not match production DLL'}
$summary=@{captures=$captures;functional=$functional;rawManifest=$manifest.ToArray();
    caveats=@('Method elapsed times are not per-method CPU times. Parent and child overlap.',
    'Different build/selector sets: these are focused observations, not an aggregate CPU gate.',
    'The final functional build also invalidates newly confirmed agreements and slices reported open footprints.',
    'Following-room audits remain engaged with a protected target, rather than complete the whole mission.',
    'The first room audit used the old contiguous-stack fixture condition; preserve its false flag as recorded.')}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'summary.json'),($summary|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
Write-Output "Preserved $($manifest.Count) raw files with verified payload hashes; final functional DLL matches production."

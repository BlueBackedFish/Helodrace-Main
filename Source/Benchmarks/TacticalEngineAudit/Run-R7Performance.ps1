param(
    [string]$FunctionalRoot,
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-r7-performance-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string[]]$Groups = @(),
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
# Collect evidence only. Successful collection is not final R7 acceptance.
$specs = @()
foreach ($speed in @(1,3)) {
    foreach ($population in @(0,12,50,100,200,400)) {
        $ticks = switch ($population) { 0 {3000}; 12 {6000}; 50 {8000}; 100 {10000}; 200 {14000}; 400 {20000} }
        $specs += [pscustomobject]@{ name="scale-$population-${speed}x"; population=$population; speed=$speed;
            fixtureCase='normal'; workload='sapper-wall'; high=$false; repeats=1; ticks=$ticks; methodProfile=$true; category='scale' }
    }
}
$representatives = @(
    @{ name='cqb-low'; population=48; fixtureCase='r7-cqb-cpu'; workload='sapper-wall'; high=$false; ticks=20000 },
    @{ name='cqb-high'; population=52; fixtureCase='r7-cqb-cpu'; workload='sapper-wall'; high=$true; ticks=20000 },
    @{ name='field-low'; population=48; fixtureCase='r6-field-cpu'; workload='sapper-wall'; high=$false; ticks=12000 },
    @{ name='field-high'; population=52; fixtureCase='r6-field-cpu'; workload='sapper-wall'; high=$true; ticks=12000 },
    @{ name='ordinary-open'; population=48; fixtureCase='normal'; workload='open-approach'; high=$false; ticks=8000 },
    @{ name='cqb-400'; population=400; fixtureCase='r7-cqb-cpu'; workload='sapper-wall'; high=$false; ticks=20000 }
)
foreach ($profile in @($true,$false)) {
    foreach ($source in $representatives) {
        $suffix = if ($profile) { 'profiled' } else { 'unprofiled' }
        $specs += [pscustomobject]@{ name="$($source.name)-$suffix"; population=$source.population; speed=3;
            fixtureCase=$source.fixtureCase; workload=$source.workload; high=$source.high; repeats=3; ticks=$source.ticks;
            methodProfile=$profile; category=$suffix }
    }
}
foreach ($name in $Groups) { if ($specs.name -notcontains $name) { throw "Unknown R7 performance group: $name" } }
$selected = @($specs | Where-Object { $Groups.Count -eq 0 -or $Groups -contains $_.name })
if ($Preview) {
    [pscustomobject]@{ scope='Collection plan only'; finalR7Complete=$false; cpuGateEvaluated=$false;
        groups=$selected; nativeRuns=($selected | Measure-Object -Property repeats -Sum).Sum * 2;
        requiresFinalFunctionalPass=$true } | ConvertTo-Json -Depth 8
    return
}
$root = [IO.Path]::GetFullPath($AuditRoot)
if (-not $root.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $root)) { throw 'Use a fresh R7 performance directory under CreatorTemp.' }
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'Preserve the existing RimWorld process; do not start another queue.' }
if (-not $FunctionalRoot) { throw 'Supply the completed final-DLL44-case functional root before collecting final performance.' }
$functional = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $FunctionalRoot 'checks.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Verify-R7Functional.ps1') -Root $FunctionalRoot -RequireFinal | Out-Null
if (-not $functional.fullFunctionalQueue -or -not $functional.allSpecifiedPassed -or
    $functional.requestedCases.Count -ne 44 -or $functional.records.Count -ne 44 -or
    @($functional.records | Where-Object { $_.status -ne 'passed' -or $_.nativeExceptions -ne 0 }).Count) {
    throw 'Final functional queue is incomplete or failed; preserve its live process or diagnose the failed evidence first.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$content = @('Assemblies\Helodrace.dll','Defs\Organization\NewTacticalJobs.xml',
    'Defs\ColdWar\BreachExplosive_ColdWar.xml','Defs\GreatWar\Sledgehammer_Breach.xml')
$pinned = @{}
foreach ($file in $content) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $repository $file)).Hash.ToLowerInvariant()
    if ($functional.pinned.$file -ne $hash) { throw "Functional proof uses different content: $file" }
    $pinned[$file] = $hash
}
New-Item -ItemType Directory -Path $root | Out-Null
$records = @()
function Write-Journal {
    [pscustomobject]@{ scope='R7 CPU scaling and repeated comparison collection only'; finalR7Complete=$false; cpuGateEvaluated=$false;
        functionalRoot=$FunctionalRoot; pinned=$pinned; requestedGroups=@($selected.name); records=$records;
        allSpecifiedCollected=($records.Count -eq $selected.Count -and @($records | Where-Object { $_.status -ne 'collected' }).Count -eq 0)
    } | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'checks.json')
}
Write-Journal
foreach ($spec in $selected) {
    foreach ($file in $content) {
        if ((Get-FileHash -LiteralPath (Join-Path $repository $file)).Hash.ToLowerInvariant() -ne $pinned[$file]) {
            throw "Content changed during final performance collection; preserve existing runs: $file"
        }
    }
    $groupRoot = Join-Path $root $spec.name
    $record = [pscustomobject]@{ name=$spec.name; root=$groupRoot; category=$spec.category; status='running'; error=$null }
    $records += $record; Write-Journal
    # Matrix owns and journals the actual native PID+creation identity. An
    # observation timeout leaves that live process intact; never restart it.
    try {
        & (Join-Path $PSScriptRoot 'Run-EngineMatrix.ps1') -Repeats $spec.repeats -Engines vanilla,new `
            -Workloads $spec.workload -Population $spec.population -Speed $spec.speed -Case $spec.fixtureCase `
            -WarmupTicks 0 -SampleTicks $spec.ticks -High:$spec.high -NoMethodProfile:(-not $spec.methodProfile) `
            -CpuWindows -Seed hd-r1-20261007 -AuditRoot $groupRoot
        $matrix = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $groupRoot 'checks.json') -Raw | ConvertFrom-Json
        if (-not $matrix.allSpecifiedPassed) { throw "Matrix incomplete: $groupRoot" }
        $record.status='collected'; Write-Journal
    } catch {
        $record.error=$_.Exception.Message
        # Preserve 'running' if a child process still exists at observation
        # expiry. A journal alone is never proof of a live process.
        $matrixPath = Join-Path $groupRoot 'checks.json'
        $live = $false
        if (Test-Path -LiteralPath $matrixPath) {
            $latest = (Get-Content -Encoding UTF8 -LiteralPath $matrixPath -Raw | ConvertFrom-Json).records | Select-Object -Last 1
            if ($latest -and $latest.status -eq 'running') {
                $process = Get-Process -Id $latest.pid -ErrorAction SilentlyContinue
                $live = $process -and $process.ProcessName -eq 'RimWorldWin64' -and $latest.startedUtc -and
                    $process.StartTime.ToUniversalTime().ToString('o') -eq $latest.startedUtc
            }
        }
        if (-not $live) { $record.status='failed' }
        Write-Journal; throw
    }
}
Write-Output "R7 performance collection complete. Run CPU comparison, unprofiled controls and final requirement audit separately: $root"

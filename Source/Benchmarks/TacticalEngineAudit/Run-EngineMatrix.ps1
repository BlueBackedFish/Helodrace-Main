param(
    [ValidateRange(1,10)][int]$Repeats = 3,
    [ValidateSet('vanilla','new')][string[]]$Engines = @('vanilla','new'),
    [ValidateSet('open-approach','sapper-wall','sapper-door')][string[]]$Workloads = @('open-approach','sapper-wall'),
    [ValidateRange(0,400)][int]$Population = 50,
    [ValidateSet(1,3)][int]$Speed = 3,
    [string]$Case = 'normal',
    [int]$WarmupTicks = 600,
    [int]$SampleTicks = 1200,
    [switch]$High,
    [switch]$NoMethodProfile,
    [switch]$CpuWindows,
    [switch]$Preview,
    [string]$ProfileTargets = 'Verse.AI.Pawn_JobTracker::StartJob;Verse.PathFinder::CreateRequest',
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-engine-matrix-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$Seed = 'hd-r1-20261007'
)
$ErrorActionPreference = 'Stop'
$launcher = Join-Path $PSScriptRoot 'Run-EngineAudit.ps1'
$allowedCases = (Get-Command $launcher).Parameters['Case'].Attributes |
    Where-Object { $_ -is [Management.Automation.ValidateSetAttribute] } | Select-Object -ExpandProperty ValidValues
if ($allowedCases -notcontains $Case) { throw "Unsupported launcher case: $Case" }
if ($CpuWindows -and $Case -notin @('normal','r7-cqb-cpu','r6-field-cpu')) { throw 'CPU windows require normal or fixed-input CQB/field cases.' }
if ($Case -eq 'r7-cqb-cpu' -and $WarmupTicks + $SampleTicks -lt 7500) { throw 'The fixed CQB input timeline requires at least 7500 ticks.' }
$planned = @()
foreach ($workload in $Workloads) {
    for ($repeat=1; $repeat -le $Repeats; $repeat++) {
        for ($slot=0; $slot -lt $Engines.Count; $slot++) {
            $planned += [pscustomobject]@{ engine=$Engines[($slot + $repeat - 1) % $Engines.Count];
                workload=$workload; repeat=$repeat; requestedPopulation=$Population; speed=$Speed; fixtureCase=$Case;
                warmupTicks=$WarmupTicks; sampleTicks=$SampleTicks; methodProfile=(-not $NoMethodProfile); cpuWindows=[bool]$CpuWindows }
        }
    }
}
if ($Preview) { $planned | ConvertTo-Json -Depth 6; return }
$root = [IO.Path]::GetFullPath($AuditRoot)
if (-not $root.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) { throw 'Use a fresh matrix directory under CreatorTemp.' }
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'Preserve the existing RimWorld process; do not start another matrix.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$content = @('Assemblies\Helodrace.dll','Defs\Organization\NewTacticalJobs.xml',
    'Defs\ColdWar\BreachExplosive_ColdWar.xml','Defs\GreatWar\Sledgehammer_Breach.xml')
$pinned = @{}
foreach ($file in $content) { $pinned[$file] = (Get-FileHash -LiteralPath (Join-Path $repository $file)).Hash.ToLowerInvariant() }
New-Item -ItemType Directory -Path $root | Out-Null
$records = @()
function Write-Journal {
    [pscustomobject]@{ scope='Fixed-input engine comparison collection'; finalR7Complete=$false; cpuGateEvaluated=$false;
        pinned=$pinned; planned=$planned; records=$records;
        allSpecifiedPassed=($records.Count -eq $planned.Count -and @($records | Where-Object { $_.status -ne 'passed' }).Count -eq 0)
    } | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'checks.json')
}
Write-Journal
foreach ($workload in $Workloads) {
    for ($repeat=1; $repeat -le $Repeats; $repeat++) {
        # Rotate order to avoid always warming the machine with the same engine.
        for ($slot=0; $slot -lt $Engines.Count; $slot++) {
            $engine = $Engines[($slot + $repeat - 1) % $Engines.Count]
            $runRoot = Join-Path $root "$workload\$engine\run-$repeat"
            foreach ($file in $content) {
                if ((Get-FileHash -LiteralPath (Join-Path $repository $file)).Hash.ToLowerInvariant() -ne $pinned[$file]) {
                    throw "Content changed during matrix; preserve results and collect a new final-code matrix: $file"
                }
            }
            & $launcher -Engine $engine -DefaultEngine:($engine -eq 'new') -Workload $workload -Population $Population -Speed $Speed -Case $Case -WarmupTicks $WarmupTicks -SampleTicks $SampleTicks -High:$High -NoMethodProfile:$NoMethodProfile -CpuWindows:$CpuWindows -ProfileTargets $ProfileTargets -Seed $Seed -AuditRoot $runRoot
            $auditProcess = [int](Get-Content -LiteralPath (Join-Path $runRoot 'process-id.txt'))
            $record = [pscustomobject]@{ root=$runRoot; engine=$engine; workload=$workload; repeat=$repeat; pid=$auditProcess;
                status='running'; error=$null; population=$null; entered=$null; objectiveReached=$null; speed=$Speed; fixtureCase=$Case }
            $records += $record; Write-Journal
            $process = Get-Process -Id $auditProcess -ErrorAction SilentlyContinue
            $creation = if ($process) { $process.StartTime } else { $null }
            $deadline = (Get-Date).AddMinutes(30)
            while ($process) {
                if ($process.ProcessName -ne 'RimWorldWin64' -or $process.StartTime -ne $creation) { break }
                if ((Get-Date) -gt $deadline) { throw "Audit $auditProcess exceeded 30 minutes; preserve the live process/logs for diagnosis." }
                Start-Sleep -Seconds 5
                $process = Get-Process -Id $auditProcess -ErrorAction SilentlyContinue
            }
            try {
                $audit = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $runRoot 'audit.json') -Raw | ConvertFrom-Json
                $manifest = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $runRoot 'launcher.json') -Raw | ConvertFrom-Json
                $record.population=$audit.population; $record.entered=$audit.entered; $record.objectiveReached=$audit.objectiveReached
                $errors = @(Select-String -LiteralPath (Join-Path $runRoot 'Player.log') -Pattern 'Exception:|Exception while|Error in ' -Encoding UTF8).Count
                if (-not $audit.complete -or -not $audit.isolationVerified -or $audit.error -or -not $audit.environmentControlled -or
                    -not $audit.r7RetiredTypesAbsent -or -not $audit.r7RetiredDefinitionsAbsent -or $errors -gt 0 -or
                    $audit.r7RateSamples -lt 1 -or $audit.r7TickRateMinimum -ne $Speed -or $audit.r7TickRateMaximum -ne $Speed -or
                    $manifest.assemblySha256 -ne $pinned['Assemblies\Helodrace.dll'] -or $manifest.cpuWindows -ne [bool]$CpuWindows) {
                    throw "Audit/content/environment/rate/exception check failed: $runRoot"
                }
                if ($engine -eq 'new' -and $audit.population -gt 0 -and $SampleTicks -ge 1000 -and -not $audit.newFunctionalComplete) {
                    throw "New functional progress failed; preserve this run: $runRoot"
                }
                if ($audit.population -eq 0 -and ($audit.newJobsIssued -ne 0 -or $audit.r7SchedulerAdvances -ne 0 -or $audit.newCommands)) {
                    throw "Empty fixture performed tactical work: $runRoot"
                }
                $record.status='passed'; Write-Journal
            } catch {
                $record.status='failed'; $record.error=$_.Exception.Message; Write-Journal; throw
            }
            $records | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'matrix.json')
            Write-Output "Completed $engine/$workload repeat=$repeat actual=$($audit.population) entered=$($audit.entered) reached=$($audit.objectiveReached)"
        }
    }
}
$records | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'matrix.json')
Write-Output "Engine matrix complete: $root"

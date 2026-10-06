param(
    [Parameter(Mandatory=$true)][string]$BeforeRoot,
    [Parameter(Mandatory=$true)][string]$AfterRoot,
    [string]$OutputRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-reviewed-profile-' + [guid]::NewGuid().ToString('N'))
)
$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
if (-not $outputPath.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Reviewed comparison output must be a new directory under CreatorTemp.'
}
if (Test-Path -LiteralPath $outputPath) { throw 'Use a new output directory to preserve earlier reviews.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$reviews = [System.Collections.Generic.List[object]]::new()
function Review([string]$root, [string]$side) {
    $accepted = Join-Path $outputPath $side
    New-Item -ItemType Directory -Path $accepted | Out-Null
    $auditFiles = @(Get-ChildItem -LiteralPath $root -Recurse -Filter 'measurements.ndjson')
    if ($auditFiles.Count -eq 0) { throw "No audit records in $root." }
    $index = 0
    foreach ($auditFile in $auditFiles) {
        $audit = @(Get-Content -LiteralPath $auditFile.FullName | ForEach-Object { $_ | ConvertFrom-Json })
        $profiles = Join-Path $auditFile.Directory.FullName 'profiles'
        foreach ($file in Get-ChildItem -LiteralPath $profiles -Filter 'capture-*.json') {
            $capture = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $sample = @($audit | Where-Object { $null -ne $_.scenario -and $_.scenario -eq $capture.scenario })
            $reasons = [System.Collections.Generic.List[string]]::new()
            if (@($audit | Where-Object { $_.error }).Count -gt 0 -or -not ($audit | Where-Object { $_.complete -eq $true })) { $reasons.Add('audit incomplete or failed') }
            if (-not $capture.complete -or $capture.dropped -ne 0 -or @($capture.methods | Where-Object { $_.exceptions -ne 0 }).Count -gt 0) { $reasons.Add('capture incomplete, dropped or measured exceptions') }
            if ($sample.Count -ne 1) { $reasons.Add('one matching scenario audit required') }
            else {
                $sample = $sample[0]
                if ($sample.actualPopulation -ne $capture.population -or $sample.mapFingerprint -ne $capture.benchmark.mapFingerprint -or
                    $sample.faction -ne $capture.benchmark.faction -or $sample.startPhases -ne $capture.benchmark.startPhases) { $reasons.Add('audit/capture metadata differs') }
                if ($sample.alive -ne $sample.actualPopulation) { $reasons.Add('casualties during measurement') }
                if ($sample.actualPopulation -gt 0 -and $sample.moved -lt [math]::Ceiling($sample.actualPopulation * 0.95)) { $reasons.Add('fewer than 95% moved') }
            }
            $eligible = $reasons.Count -eq 0
            $reviews.Add([pscustomobject]@{ side=$side; capture=$file.FullName; scenario=$capture.scenario; population=$capture.population;
                alive=$sample.alive; moved=$sample.moved; eligible=$eligible; reasons=@($reasons.ToArray()) })
            if ($eligible) { $index++; Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $accepted "capture-$index.json") }
        }
    }
    if ($index -eq 0) { throw "No eligible captures on $side." }
}
Review $BeforeRoot 'before'
Review $AfterRoot 'after'
$reviews.ToArray() | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputPath 'review.json') -Encoding UTF8
$cli = Join-Path $PSScriptRoot '..\..\Tools\AgentProfiler\bin\Debug\net10.0\AgentProfiler.dll'
$comparison = & dotnet $cli benchmark-compare (Join-Path $outputPath 'before') (Join-Path $outputPath 'after')
if ($LASTEXITCODE -ne 0) { throw ($comparison -join "`n") }
$comparison | Set-Content -LiteralPath (Join-Path $outputPath 'comparison.json') -Encoding UTF8
Write-Output "Reviewed comparison: $outputPath"
# This only rejects obvious stopped/casualty contamination. Matching start
# phases and movement occurrence still do not prove identical tactical work.

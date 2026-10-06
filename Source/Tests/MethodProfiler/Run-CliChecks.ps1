param([Parameter(Mandatory=$true)][string]$Capture)
$ErrorActionPreference = 'Stop'
$cli = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Tools\AgentProfiler\bin\Debug\net10.0\AgentProfiler.dll'))
$fixture = Join-Path 'C:\Users\Public\Documents\ESTsoft\CreatorTemp' ('hd-profile-cli-check-' + [guid]::NewGuid().ToString('N') + '.json')
$sample = Get-Content -LiteralPath $Capture -Raw | ConvertFrom-Json
$response = & dotnet $cli compare $Capture $Capture
if ($LASTEXITCODE -ne 0) { throw 'Identical complete capture comparison failed.' }
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
Write-Output 'CLI checks passed: self comparison, differing targets/reference rejected, zero ticks rejected, paused reference normalization.'
exit 0

param([Parameter(Mandatory=$true)][string]$RuntimeRoot)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=[IO.Path]::GetFullPath($RuntimeRoot)
$allowed=[IO.Path]::GetFullPath((Join-Path $repository '.codex-staging'))+[IO.Path]::DirectorySeparatorChar
if(-not $runtime.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Cache target must be a migration runtime inside .codex-staging.'}
$manifest=Get-Content -LiteralPath (Join-Path $runtime 'launcher.json') -Raw | ConvertFrom-Json
if(Get-Process -Id $manifest.processId -ErrorAction SilentlyContinue){throw 'Do not clear caches while the recorded game is running.'}
$profile=[IO.Path]::GetFullPath((Join-Path $runtime 'Profile'))
foreach($parent in @($runtime,$profile)){
    if((Get-Item -LiteralPath $parent).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Runtime/Profile cannot be a junction.'}
}
$freed=0L
foreach($name in @('ShaderCache','TextureCache')){
    $target=[IO.Path]::GetFullPath((Join-Path $profile $name))
    if(-not $target.StartsWith(($profile+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)){throw 'Cache escaped the test profile.'}
    if(-not(Test-Path -LiteralPath $target)){continue}
    $resolved=(Resolve-Path -LiteralPath $target).ProviderPath
    if($resolved -ne $target -or ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Cache target is redirected.'}
    $entries=@(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if($entries | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Cache contains a junction or symbolic link.'}
    $freed+=($entries | Where-Object {-not $_.PSIsContainer} | Measure-Object Length -Sum).Sum
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output $freed

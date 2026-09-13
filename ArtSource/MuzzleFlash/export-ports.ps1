$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root 'Textures/Things/Mote'
$generated = 'C:/Users/human/.codex/generated_images/01a08e22-67f6-7152-b130-2fd551da1121'
$items = @(
    @{Name='Radial'; File='exec-1b64e304-359b-465f-8633-101d168fb129.png'; X=366; Y=627; SX=0.20; SY=0.15},
    @{Name='Upward'; File='exec-b61b9f36-68e6-4ac8-9fc6-d03e1cb94f43.png'; X=282; Y=800; SX=0.20; SY=0.14}
)
foreach ($item in $items) {
    $source = Join-Path $generated $item.File
    Copy-Item -LiteralPath $source -Destination (Join-Path $PSScriptRoot ($item.Name + '-ports-matte.png')) -Force
    $sx = $item.SX; $sy = $item.SY
    $tx = 48 - $sx * $item.X; $ty = 128 - $sy * $item.Y
    $matrix = "$sx,0,0,$sy,$tx,$ty"
    $target = Join-Path $out ('MuzzleFlash_' + $item.Name + '.png')
    & magick $source -virtual-pixel black -define 'distort:viewport=256x256+0+0' -distort AffineProjection $matrix +repage -alpha on -channel A -fx 'max(r,max(g,b))<0.015?0:max(r,max(g,b))' -channel RGB -fx 'a>0?u/a:0' +channel -depth 8 -define png:color-type=6 $target
    if ($LASTEXITCODE -ne 0) { throw "Export failed: $target" }
}
$files = 'Radial','Upward','Bare','Suppressed' | ForEach-Object { Join-Path $out ('MuzzleFlash_' + $_ + '.png') }
& magick montage @files -background '#30343a' -alpha remove -alpha off -font Arial -pointsize 15 -fill white -label '%t' -geometry 256x256+8+8 -tile 4x1 (Join-Path $PSScriptRoot 'comparison.png')
& magick montage @files -background '#30343a' -alpha remove -alpha off -geometry 64x64+12+12 -tile 4x1 (Join-Path $PSScriptRoot 'comparison-64.png')
& magick montage @files -background '#d0d0d0' -alpha remove -alpha off -geometry 32x32+12+12 -tile 4x1 (Join-Path $PSScriptRoot 'comparison-32.png')
$beforeAfter = @((Join-Path $PSScriptRoot 'Radial-before-ports.png'),$files[0],(Join-Path $PSScriptRoot 'Upward-before-ports.png'),$files[1])
& magick montage @beforeAfter -background '#30343a' -alpha remove -alpha off -font Arial -pointsize 15 -fill white -label '%t' -geometry 256x256+8+8 -tile 2x2 (Join-Path $PSScriptRoot 'ports-before-after.png')

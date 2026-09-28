$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$generated = 'C:/Users/human/.codex/generated_images/01a08e22-67f6-7152-b130-2fd551da1121'
$out = Join-Path $root 'Textures/Things/Mote'
$items = @(
    @{Name='Radial'; File='exec-d20e0f32-2111-4a53-9f7f-d5b113b13ca8.png'; X=278; Y=625; Scale=0.148; Opacity=1},
    @{Name='Upward'; File='exec-3ab90ea5-ce2e-411a-816b-e0867837790d.png'; X=266; Y=627; Scale=0.20; Opacity=1},
    @{Name='Bare'; File='exec-6b13f26d-f656-469a-a55d-3f9fcca8519b.png'; X=184; Y=622; Scale=0.18; Opacity=1},
    @{Name='Suppressed'; File='exec-e2e5e82e-5340-43ff-9ecc-bc84a08074d6.png'; X=270; Y=627; Scale=0.16; Opacity=0.78}
)
foreach ($item in $items) {
    $source = Join-Path $generated $item.File
    Copy-Item -LiteralPath $source -Destination (Join-Path $PSScriptRoot ($item.Name + '-matte.png')) -Force
    $s = $item.Scale
    $tx = 48 - $s * $item.X
    $ty = 128 - $s * $item.Y
    $matrix = "$s,0,0,$s,$tx,$ty"
    $target = Join-Path $out ('MuzzleFlash_' + $item.Name + '.png')
    & magick $source -virtual-pixel black -define 'distort:viewport=256x256+0+0' -distort AffineProjection $matrix +repage -alpha on -channel A -fx 'max(r,max(g,b))<0.015?0:max(r,max(g,b))' -channel RGB -fx 'a>0?u/a:0' -channel A -evaluate multiply $item.Opacity +channel -depth 8 -define png:color-type=6 $target
    if ($LASTEXITCODE -ne 0) { throw "Export failed: $target" }
}
$files = $items | ForEach-Object { Join-Path $out ('MuzzleFlash_' + $_.Name + '.png') }
& magick montage @files -background '#30343a' -alpha remove -alpha off -set label '%t' -font Arial -pointsize 15 -fill white -label '%t' -geometry 256x256+8+8 -tile 4x1 (Join-Path $PSScriptRoot 'comparison.png')
& magick montage @files -background '#30343a' -alpha remove -alpha off -geometry 64x64+12+12 -tile 4x1 (Join-Path $PSScriptRoot 'comparison-64.png')
& magick montage @files -background '#d0d0d0' -alpha remove -alpha off -geometry 32x32+12+12 -tile 4x1 (Join-Path $PSScriptRoot 'comparison-32.png')

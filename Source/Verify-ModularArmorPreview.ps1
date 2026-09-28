param(
    [string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assemblies/Helodrace.dll'),
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
$managed = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($name in 'UnityEngine.CoreModule','UnityEngine','Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom("$managed/$name.dll")
}
$null = [Reflection.Assembly]::LoadFrom("$repo/Assemblies/0Harmony.dll")
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$boundsType = $assembly.GetType('Helodrace.ModernWar.ModularArmorPreviewBounds',$true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$find = $boundsType.GetMethod('TryFindVisibleBounds',$flags)
$layout = $boundsType.GetMethod('DestinationFor',$flags)
function Assert($condition, $message) { if (!$condition) { throw $message } }
function Bounds([byte[]]$alpha, [int]$width, [int]$height) {
    $arguments = [object[]]@($alpha,$width,$height,$null)
    $found = $find.Invoke($null,$arguments)
    return [pscustomobject]@{Found=$found; Rect=$arguments[3]}
}
function Verify-Layout($bounds, $width, $height) {
    # Cover wide, tall and square viewports with an off-origin available rect.
    foreach ($viewport in @(@(420,340),@(240,550),@(320,320))) {
        $available = [UnityEngine.Rect]::new(38,48,$viewport[0],$viewport[1])
        $minimum = [UnityEngine.Rect]::new(0.28,0.38,0.44,0.48)
        $destination = $layout.Invoke($null,[object[]]@($available,$bounds,$width,$height,$minimum))
        Assert ([Math]::Abs($destination.center.x - $available.center.x) -lt 0.001) 'Preview is horizontally off-center.'
        Assert ([Math]::Abs($destination.center.y - $available.center.y) -lt 0.001) 'Preview is vertically off-center.'
        Assert ($destination.xMin -ge $available.xMin - 0.001 -and $destination.xMax -le $available.xMax + 0.001) 'Preview exceeds viewport width.'
        Assert ($destination.yMin -ge $available.yMin - 0.001 -and $destination.yMax -le $available.yMax + 0.001) 'Preview exceeds viewport height.'
        $sourceAspect = $bounds.width * $width / ($bounds.height * $height)
        Assert ([Math]::Abs($destination.width / $destination.height - $sourceAspect) -lt 0.001) 'Preview changes image proportions.'
    }
}
$empty = Bounds ([byte[]]::new(100)) 10 10
Assert (!$empty.Found) 'Transparent texture creates a visible area.'
$alpha = [byte[]]::new(100)
$alpha[28] = 255; $alpha[49] = 32; $alpha[0] = 1
$synthetic = Bounds $alpha 10 10
Assert ($synthetic.Found -and [Math]::Abs($synthetic.Rect.xMin - 0.8) -lt 0.00001 -and [Math]::Abs($synthetic.Rect.yMin - 0.2) -lt 0.00001) 'Offset/translucent content bounds are incorrect.'
Assert ([Math]::Abs($synthetic.Rect.width - 0.2) -lt 0.00001 -and [Math]::Abs($synthetic.Rect.height - 0.3) -lt 0.00001) 'Content edge pixels are clipped.'
Verify-Layout $synthetic.Rect 10 10
Write-Output 'PASS: empty, off-center and translucent textures; centered layouts preserve proportions and fit three viewport shapes.'

[xml]$defs = Get-Content "$repo/Defs/ModernWar/Items/Apparel_ModernWar.xml" -Raw -Encoding UTF8
$count = 0
foreach ($def in $defs.SelectNodes('/Defs/ThingDef[comps/li[@Class="Helodrace.ModernWar.CompProperties_ModularArmor"]]')) {
    $prefix = [string]$def.apparel.wornGraphicPath
    foreach ($direction in 'north','east','south') {
        $path = "$repo/Textures/${prefix}_Female_${direction}.png"
        if (!(Test-Path -LiteralPath $path)) { $path = "$repo/Textures/${prefix}_${direction}.png" }
        Assert (Test-Path -LiteralPath $path) "Missing preview fixture: $path"
        $image = [Drawing.Bitmap]::new($path)
        try {
            $width = $image.Width; $height = $image.Height
            $bits = $image.LockBits([Drawing.Rectangle]::new(0,0,$width,$height),[Drawing.Imaging.ImageLockMode]::ReadOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $row = [byte[]]::new($width * 4)
                $alpha = [byte[]]::new($width * $height)
                for ($y = 0; $y -lt $height; $y++) {
                    [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($bits.Scan0,$y * $bits.Stride),$row,0,$row.Length)
                    for ($x = 0; $x -lt $width; $x++) { $alpha[($height - 1 - $y) * $width + $x] = $row[$x * 4 + 3] }
                }
            } finally { $image.UnlockBits($bits) }
            $content = Bounds $alpha $width $height
            Assert $content.Found "Preview texture is empty: $path"
            Verify-Layout $content.Rect $width $height
            $count++
        } finally { $image.Dispose() }
    }
}
Write-Output "PASS: $count real armor, belt, pants and helmet directional PNGs center correctly in all three viewport shapes."

param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }

$magick = Get-Command magick -ErrorAction SilentlyContinue
Assert ($null -ne $magick) 'ImageMagick (magick) is required for texture-mask verification.'

$pairs = @(
    @('HD_BasicPumpJack_north.png', 'HD_BasicPumpJack_northm.png'),
    @('HD_CableToolRig_north.png', 'HD_CableToolRig_northm.png'),
    @('HD_LineShaftPress_north.png', 'HD_LineShaftPress_northm.png'),
    @('HD_LineShaftPress_east.png', 'HD_LineShaftPress_eastm.png'),
    @('HD_LineShaftPress_south.png', 'HD_LineShaftPress_southm.png'),
    @('HD_RollingMC_north.png', 'HD_RollingMC_northm.png'),
    @('HD_RollingMC_east.png', 'HD_RollingMC_eastm.png'),
    @('HD_RollingMC_south.png', 'HD_RollingMC_southm.png')
)

$textureRoot = Join-Path $RepositoryRoot 'Textures/Buildings'
foreach ($pair in $pairs) {
    $base = Join-Path $textureRoot $pair[0]
    $mask = Join-Path $textureRoot $pair[1]
    Assert (Test-Path -LiteralPath $base) "Missing base texture: $($pair[0])"
    Assert (Test-Path -LiteralPath $mask) "Missing color mask: $($pair[1])"

    $baseSize = & $magick.Source identify -format '%wx%h' $base
    $maskSize = & $magick.Source identify -format '%wx%h' $mask
    Assert ($baseSize -eq $maskSize) "Texture/mask dimensions differ: $($pair[0])"

    $alphaDifference = [double](& $magick.Source $base -alpha extract -write mpr:base +delete $mask -alpha extract mpr:base -compose difference -composite -format '%[fx:mean]' info:)
    Assert ($alphaDifference -eq 0) "Texture/mask alpha differs: $($pair[0])"

    $greenMaximum = [double](& $magick.Source $mask -channel G -separate -format '%[fx:maxima]' info:)
    $blueMaximum = [double](& $magick.Source $mask -channel B -separate -format '%[fx:maxima]' info:)
    Assert ($greenMaximum -eq 0 -and $blueMaximum -eq 0) "Unexpected green/blue data in primary-color mask: $($pair[1])"
}

[xml]$industry = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Defs/WildWest/Buildings/Industry_WildWest.xml') -Raw -Encoding UTF8
foreach ($defName in 'HD_CableToolRig', 'HD_BasicPumpJack', 'HD_LineShaftHydraulicPress', 'HD_LineShaftRollingMachine') {
    $node = $industry.SelectSingleNode("/Defs/ThingDef[defName='$defName']")
    Assert ($null -ne $node) "Missing building: $defName"
    Assert ([string]$node.graphicData.shaderType -eq 'CutoutComplex') "$defName must use CutoutComplex."
}

Write-Output "PASS: $($pairs.Count) building masks match source dimensions and alpha exactly."
Write-Output 'PASS: masks contain only primary red/black channels and all four buildings use CutoutComplex.'


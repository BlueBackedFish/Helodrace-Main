param([string]$GamePath='C:/Program Files (x86)/Steam/steamapps/common/RimWorld')
$ErrorActionPreference='Stop'
$script:hmgManaged=Join-Path $GamePath 'RimWorldWin64_Data/Managed'
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
    param($sender,$eventArgs)
    $file=Join-Path $script:hmgManaged (([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll')
    if(Test-Path -LiteralPath $file){return [Reflection.Assembly]::ReflectionOnlyLoadFrom($file)}
    return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
})
$assemblyPath=Join-Path $script:hmgManaged 'Assembly-CSharp.dll'
$assembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom($assemblyPath)
$type=$assembly.GetType('RimWorld.TurretTop',$true)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$field=$type.GetField('ArtworkRotation',$flags)
$initializer=$type.TypeInitializer.GetMethodBody().GetILAsByteArray()
if($initializer[0] -ne 0x1f -or $initializer[2] -ne 0x80){throw 'Inspect changed TurretTop initializer before exporting artwork.'}
if($assembly.ManifestModule.ResolveField([BitConverter]::ToInt32($initializer,3)).MetadataToken -ne $field.MetadataToken){throw 'Unexpected artwork rotation initializer.'}
$rotation=[int]$initializer[1]
if($rotation -ge 128){$rotation-=256}
$draw=($type.GetMethods($flags)|Where-Object Name -eq 'DrawTurret')
$bytes=$draw.GetMethodBody().GetILAsByteArray();$usesArtworkRotation=$false
for($i=0;$i -le $bytes.Length-5;$i++){
    if($bytes[$i] -eq 0x7e -and [BitConverter]::ToInt32($bytes,$i+1) -eq $field.MetadataToken){$usesArtworkRotation=$true;break}
}
if(!$usesArtworkRotation){throw 'DrawTurret no longer reads ArtworkRotation.'}
$sha=[Security.Cryptography.SHA256]::Create()
try{$hash=[BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($assemblyPath))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
# Avoid module autoload differences when Python starts Windows PowerShell.
Write-Output ('{"artwork_rotation_degrees":'+$rotation+',"draw_turret_reads_artwork_rotation":true,"game_assembly_sha256":"'+$hash+'"}')

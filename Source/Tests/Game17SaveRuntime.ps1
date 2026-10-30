. (Join-Path $PSScriptRoot 'Game17Runtime.ps1')

function Resolve-Game17SavedMods {
    param([string[]]$PackageIds,[string]$GameRoot,[string[]]$ExtraModRoots)
    $base=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','bluebackedfish.helod.blancasdrugs','bluebackedfish.helodrace.main')
    $provided=@(foreach($path in $ExtraModRoots){[xml]$about=Get-Content -LiteralPath (Join-Path $path 'About/About.xml') -Raw;[string]$about.ModMetaData.packageId})
    $needed=@($PackageIds | Where-Object {$base -notcontains $_ -and $provided -notcontains $_})
    $resolved=@($ExtraModRoots)
    if(-not $needed.Count){return $resolved}
    $candidates=@(foreach($registry in @((Join-Path $GameRoot 'Mods'),(Join-Path $GameRoot '../../workshop/content/294100'))){
        if(-not(Test-Path -LiteralPath $registry)){continue}
        foreach($directory in Get-ChildItem -LiteralPath $registry -Directory){
            $aboutPath=Join-Path $directory.FullName 'About/About.xml'
            if(-not(Test-Path -LiteralPath $aboutPath)){continue}
            try{[xml]$about=Get-Content -LiteralPath $aboutPath -Raw}catch{continue}
            $id=([string]$about.ModMetaData.packageId).Trim()
            if($needed -contains $id){[pscustomobject]@{Root=$directory.FullName;PackageId=$id;Declares17=@($about.ModMetaData.supportedVersions.li) -contains '1.7'}}
        }
    })
    foreach($id in $needed){
        $matches=@($candidates | Where-Object {$_.PackageId -eq $id} | Sort-Object Root -Unique)
        $current=@($matches | Where-Object {$_.Declares17})
        if($current.Count -eq 1){$matches=$current}
        if($matches.Count -ne 1){throw ('Supply an explicit ExtraModRoots entry for '+$id+'; installed matching roots: '+$matches.Count)}
        $resolved+=$matches[0].Root
    }
    return $resolved
}

function New-Game17SaveRuntime {
    param(
        [Parameter(Mandatory)][string]$SaveFile,
        [Parameter(Mandatory)][string]$RuntimeRoot,
        [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
        [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
        [string]$BlancasRoot,
        [string[]]$ExtraModRoots=@(),
        [switch]$PrepareOnly
    )
    $ErrorActionPreference='Stop'
    $source=(Get-Item -LiteralPath $SaveFile).FullName
    $hash=(Get-FileHash -LiteralPath $source).Hash
    $reader=[Xml.XmlReader]::Create($source)
    try{
        if($reader.MoveToContent() -ne [Xml.XmlNodeType]::Element -or $reader.Name -ne 'savegame' -or
            -not $reader.ReadToDescendant('meta')){throw 'Expected a RimWorld savegame with metadata.'}
        [xml]$meta=$reader.ReadOuterXml()
        if($reader.NodeType -ne [Xml.XmlNodeType]::Element -or $reader.Name -ne 'game'){
            if(-not $reader.ReadToFollowing('game')){throw 'Expected a saved game body.'}
        }
    }finally{$reader.Dispose()}
    if(-not ([string]$meta.meta.gameVersion).StartsWith('1.7.')){throw 'Main replays require a 1.7 save. Legacy Blancas fixtures use the separate migration runner.'}
    $ExtraModRoots=@(Resolve-Game17SavedMods -PackageIds @($meta.meta.modIds.li) -GameRoot $GameRoot -ExtraModRoots $ExtraModRoots)
    $runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot -ExtraModRoots $ExtraModRoots -PrepareOnly:$PrepareOnly
    [xml]$config=Get-Content -LiteralPath (Join-Path $runtime.Profile 'Config/ModsConfig.xml') -Raw
    $available=@($config.ModsConfigData.activeMods.li)
    $missing=@($meta.meta.modIds.li | Where-Object {$available -notcontains [string]$_})
    if($missing.Count){throw ('Supply ExtraModRoots for saved dependencies: '+($missing -join ', '))}
    # Keep saved inter-mod ordering and expansion selection. Add Main's required
    # dependencies if this is a plain 1.7 save being used as a new test base.
    $ordered=[Collections.Generic.List[string]]::new()
    $ordered.Add('brrainz.harmony')
    foreach($id in @($meta.meta.modIds.li)+@('ludeon.rimworld','ludeon.rimworld.biotech','bluebackedfish.helod.blancasdrugs','bluebackedfish.helodrace.main')+@($runtime.ExtraMods | ForEach-Object {$_.PackageId})){
        $normalized=([string]$id).Trim().ToLowerInvariant()
        if($ordered -notcontains $normalized){$ordered.Add($normalized)}
    }
    $blancasIndex=$ordered.IndexOf('bluebackedfish.helod.blancasdrugs');$mainIndex=$ordered.IndexOf('bluebackedfish.helodrace.main')
    if($blancasIndex -gt $mainIndex){$ordered.RemoveAt($blancasIndex);$ordered.Insert($mainIndex,'bluebackedfish.helod.blancasdrugs')}
    $active=$config.SelectSingleNode('/ModsConfigData/activeMods');$active.RemoveAll()
    foreach($id in $ordered){$item=$config.CreateElement('li');$item.InnerText=$id.ToLowerInvariant();$null=$active.AppendChild($item)}
    $config.Save((Join-Path $runtime.Profile 'Config/ModsConfig.xml'))
    $runtime.ExplicitMods=@($ordered | Where-Object {$_ -notin @('ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey')})
    New-Item -ItemType Directory -Path (Join-Path $runtime.Profile 'Saves') | Out-Null
    foreach($name in @('Base','Autostart')){
        $copy=Join-Path $runtime.Profile ('Saves/'+$name+'.rws')
        Copy-Item -LiteralPath $source -Destination $copy
        if((Get-FileHash -LiteralPath $copy).Hash -ne $hash){throw 'Saved-game copy hash mismatch.'}
    }
    if((Get-FileHash -LiteralPath $source).Hash -ne $hash){throw 'Source save changed during preparation.'}
    $runtime | Add-Member -NotePropertyName SourceSave -NotePropertyValue $source
    $runtime | Add-Member -NotePropertyName SourceSha256 -NotePropertyValue $hash
    return $runtime
}

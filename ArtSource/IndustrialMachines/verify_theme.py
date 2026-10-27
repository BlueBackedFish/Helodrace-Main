"""Check saved material roles, exported masks and pre-theme geometry/gameplay provenance."""
import bpy,json,sys,subprocess,xml.etree.ElementTree as ET
from pathlib import Path
import numpy as np
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[1]
sys.path.insert(0,str(OUT))
from normalize_density import ASSETS,ALIASES
from building_theme import ADVANCED,COMPUTER,EXCLUDED,STEEL,srgb,mesh_shape_hash
from integrate_asset import gameplay_hash
from render_west import read_pixels
BASELINE='ceb93af4'

def previous(path):
    return subprocess.check_output(['git','show',BASELINE+':'+str(path.relative_to(ROOT)).replace('\\','/')],cwd=ROOT)

steel=ET.parse('C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Data/Core/Defs/ThingDefs_Items/Items_Resource_Stuff.xml').getroot()
steel=next(n for n in steel.findall('ThingDef') if n.findtext('defName')=='Steel')
assert tuple(map(int,steel.findtext('stuffProps/color').strip('()').split(',')))==STEEL
definitions={}
for path in (ROOT/'Defs').rglob('*.xml'):
    try:root=ET.parse(path).getroot()
    except ET.ParseError:continue
    for n in root.findall('ThingDef'):definitions[n.findtext('defName')]=(n,path)

report={}
for stem,folder,blend,_ in ASSETS:
    if stem in EXCLUDED:continue
    directory=ROOT/'ArtSource'/folder;path=directory/'validation.json'
    meta=json.loads(path.read_text(encoding='utf8'));old=json.loads(previous(path))
    assert meta['theme_original_palette_sha256']==old['density_export_geometry_palette_sha256'],stem
    for key in ['footprint','texture_resolution','expected_draw_size','expected_draw_offset','outline_pixels','camera_target']:
        assert meta[key]==old[key],(stem,key)
    bpy.ops.wm.open_mainfile(filepath=str(directory/blend))
    scene=bpy.context.scene
    assert mesh_shape_hash(scene)==meta['theme_mesh_sha256']==scene['building_theme_geometry_sha256']
    mats={m for o in scene.objects if o.type=='MESH' for m in o.data.materials}
    roles={m['theme_role'] for m in mats}
    assert ('white' in roles)==(stem in ADVANCED),(stem,roles)
    assert ('cyan' in roles)==(stem in COMPUTER),(stem,roles)
    assert any(m['theme_channel']=='metal' for m in mats),stem
    for m in mats:
        channel=m['theme_channel'];base=list(m['theme_base_rgb'])
        expected=[round(c*STEEL[i]/255) if channel=='metal' else c for i,c in enumerate(base)]
        assert srgb(m)==expected,(stem,m.name,srgb(m),expected)
        emission=next(n for n in m.node_tree.nodes if n.type=='EMISSION')
        assert max(abs(a-b) for a,b in zip(emission.inputs[0].default_value,m.diffuse_color))<1e-7
        if m['theme_role']=='cyan':
            r,g,b=srgb(m);assert channel=='fixed' and r<g<=b and b<=110
        if 'vanilla_timber' in m['theme_source_name'].lower():assert channel=='fixed'
    node,xml=definitions[ALIASES.get(stem,stem)]
    baseline=next(n for n in ET.fromstring(previous(xml)).findall('ThingDef') if n.findtext('defName')==ALIASES.get(stem,stem))
    assert gameplay_hash(node)==gameplay_hash(baseline)==meta['gameplay_fields_preserved_sha256']
    assert node.findtext('graphicData/color')=='(255,255,255)'
    assert node.findtext('graphicData/colorTwo')=='(105,105,105)'
    views={};cyan_total=0;red_total=0
    for direction in ['north','east','south','west']:
        color=read_pixels(str(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png'))
        mask=read_pixels(str(ROOT/'Textures/Buildings'/f'{stem}_{direction}m.png'))
        metal=(mask[:,:,1]>.98)&(color[:,:,3]>.98)
        white=(mask[:,:,0]>.98)&(color[:,:,3]>.98)
        fixed=(mask[:,:,:3].max(axis=2)<.01)&(color[:,:,3]>.98)
        assert metal.sum()>100,(stem,direction,'missing metal')
        assert np.max(abs(mask[:,:,3]-color[:,:,3]))<.001
        assert mask[:,:,2].max()<=1.01/255
        if stem not in ADVANCED:assert white.sum()==0,(stem,direction,'unexpected white paint')
        # Verify the native albedo remains untinted; applying Steel exactly once
        # produces the same gray range as the saved display materials.
        assert color[:,:,:3][metal].mean()>.66,(stem,direction,'double baked steel')
        cyan=fixed&(color[:,:,2]>.29)&(color[:,:,2]<.45)&(color[:,:,1]>.28)&(color[:,:,1]<.42)&(color[:,:,0]<.25)
        cyan_total+=int(cyan.sum());red_total+=int(white.sum())
        views[direction]={'metal_pixels':int(metal.sum()),'white_pixels':int(white.sum()),'fixed_cyan_pixels':int(cyan.sum())}
    if stem in COMPUTER:assert cyan_total>100,(stem,'cyan not visible')
    if stem in ADVANCED:assert red_total>10000,(stem,'missing broad white exterior')
    if stem in ['HD_Converter','HD_BlastFurnace']:
        uppers=[o for o in scene.objects if o.type=='MESH' and any(k in o.name.lower() for k in ['mouth','charging','stove','spindle'])]
        assert uppers
        for o in uppers:
            for m in o.data.materials:
                if 'concrete' in m['theme_source_name'].lower():continue
                assert m['theme_channel']=='metal' and max(srgb(m))<=105,(stem,o.name,m.name)
    report[stem]={'family':meta['building_theme_family'],'unchanged_shape':True,'original_palette_provenance_verified':True,
                  'unchanged_gameplay':True,'unchanged_cameras_scale_outline':True,'roles':sorted(roles),'views':views}
assert len(report)==29
report['contract']={'primary':'white paint','secondary':list(STEEL),'secondary_source':'installed vanilla Steel',
                    'fixed':'wood, stone, cyan accents, working silver and small fittings',
                    'advanced':sorted(ADVANCED),'computer_accents':sorted(COMPUTER),'in_game_tested':False}
(OUT/'theme_validation.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('Verified 29 themed scenes, 116 color/mask pairs, original geometry/palette provenance, unchanged gameplay, white/Steel roles and visible cyan accents.')

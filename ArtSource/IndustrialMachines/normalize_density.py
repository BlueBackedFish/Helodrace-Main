"""Re-export saved buildings at a fixed tile density without rebuilding geometry."""
import bpy,os,sys,json,xml.etree.ElementTree as ET
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,OUT)
import sprite_common as s
from render_west import ASSETS as INDUSTRIAL_ASSETS
from integrate_asset import integrate
ASSETS=INDUSTRIAL_ASSETS+[(stem,folder,stem+'.blend',True) for stem,folder in [
 ('HD_SAFurnace','ElectricArcFurnace'),('HD_VIMFurnace','VacuumInductionFurnace'),('HD_CableToolRig','CableToolRig'),
 ('HD_Converter','Converter'),('HD_BlastFurnace','BlastFurnace'),('HD_BatchStill','BatchStill'),
 ('HD_CNCLathe','CNCLathe'),('HD_CNCMillingMachine','CNCMillingMachine'),('HD_SemiconductorFab','SemiconductorFab'),
 ('HD_MachiningCenter1970','MachiningCenter1970'),('HD_MultiTaskMachine2000','MultiTaskMachine2000'),
 ('HD_HydraulicPress1970','HydraulicPress1970'),('HD_RollingMill1970','RollingMill1970')]]
ASSETS += [('HD_'+n,'Reconstructed/'+n,'HD_'+n+'.blend',True) for n in ['TreadleLathe','LineShaftTurretLathe','LineShaftMillingMachine']]
ASSETS += [('HD_LineShaftPress','LargeIndustrialPress','large_industrial_press.blend',True),
 ('HD_RollingMC','IndustrialRollingMill','industrial_rolling_mill.blend',True),
 ('HD_ClothingBench','ClothingBench','clothing_bench.blend',True),
 ('HD_GunSmithTable','GunBench','gun_bench.blend',True),
 ('HD_Radio','CollinsRadioDesk','collins_radio_desk.blend',True)]
ALIASES={'HD_LineShaftPress':'HD_LineShaftHydraulicPress','HD_RollingMC':'HD_LineShaftRollingMachine'}
FIXED={'HD_Radio','HD_ClothingBench','HD_GunSmithTable'}

def normalize(entry):
    stem,folder,blend,active=entry;path=os.path.join(ROOT,'ArtSource',folder)
    metadata=os.path.join(path,'validation.json');old={}
    if os.path.isfile(metadata):
        with open(metadata) as f:old=json.load(f)
    if 'footprint' not in old:
        for folder_xml,_,files in os.walk(os.path.join(ROOT,'Defs')):
            for file in files:
                if not file.endswith('.xml'):continue
                try:nodes=ET.parse(os.path.join(folder_xml,file)).getroot().findall('ThingDef')
                except ET.ParseError:continue
                for node in nodes:
                    if node.findtext('defName')==ALIASES.get(stem,stem):
                        old['footprint']=[int(v) for v in node.findtext('size').strip('()').split(',')]
    assert 'footprint' in old,stem
    bpy.ops.wm.open_mainfile(filepath=os.path.join(path,blend))
    from building_theme import mesh_shape_hash
    source_shape=mesh_shape_hash(bpy.context.scene);source_palette=s.geometry_hash()
    themed=s.apply_building_theme(stem);before=s.geometry_hash()
    assert mesh_shape_hash(bpy.context.scene)==source_shape
    oldnorth=bpy.data.objects.get('Camera_North');north=90 if oldnorth and oldnorth.location.y>0 else -90
    if stem in ['HD_ClothingBench','HD_GunSmithTable','HD_LineShaftPress']:north=-90
    for cam in [o for o in bpy.context.scene.objects if o.type=='CAMERA']:bpy.data.objects.remove(cam,do_unlink=True)
    for data in list(bpy.data.cameras):
        if not data.users:bpy.data.cameras.remove(data)
    s.scene=bpy.context.scene;s.scene['outline_pixels']=12;s.scene['north_azimuth']=north
    s.scene['presentation_rule']='one_model_unit_per_game_tile'
    # Older source scenes predate explicit tint metadata. Preserve their broad
    # neutral-metal selection and baked colored parts; benches/radio stay untinted.
    policy={}
    for obj in [o for o in s.scene.objects if o.type=='MESH']:
        for m in obj.data.materials:
            if 'tintable' in m:continue
            def srgb(c):return 255*(12.92*c if c<=.0031308 else 1.055*c**(1/2.4)-.055)
            rgb=[srgb(c) for c in m.diffuse_color[:3]]
            policy[m.name]=stem not in FIXED and max(rgb)-min(rgb)<17 and max(rgb)>45
    s.scene['density_mask_policy']=json.dumps(policy)
    s.export(path,stem,old['footprint'],old.get('features',['existing authored building; geometry and colors retained']),blend_name=blend)
    assert s.geometry_hash()==before,'Density normalization changed meshes or colors'
    with open(metadata) as f:new=json.load(f)
    old.update(new);old.update({'density_export_preserves_geometry_and_palette':True,'density_export_geometry_palette_sha256':before,
                'rotation_export_geometry_palette_sha256':before})
    if themed:
        old.update({'theme_geometry_preserved':True,'theme_mesh_sha256':source_shape,
                    'theme_original_palette_sha256':old.get('theme_original_palette_sha256',source_palette),
                    'theme_palette_intentionally_revised':True})
        if 'geometry_and_palette_unchanged' in old:old['geometry_and_palette_unchanged']=False
    with open(metadata,'w') as f:json.dump(old,f,indent=2)
    if active:integrate(stem,folder,ALIASES.get(stem,stem))
    if stem=='HD_Radio':
        for direction in ['north','east','south','west']:
            os.remove(os.path.join(ROOT,'Textures/Buildings',stem+'_'+direction+'m.png'))
        with open(os.path.join(path,'active_validation.json'),'w') as f:
            json.dump({'texture_prefix':'Buildings/HD_Radio','resolution':256,'outline_pixels':12,'pixels_per_game_unit':256/1.5,
              'color_mask':False,'north':'operating faces','south':'cabinet backs','west_is_actual_render':True,
              'geometry_palette_sha256':before,'geometry_and_palette_preserved':True,'cameras':new['cameras'],'in_game_tested':False},f,indent=2)
    print('DENSITY_COMPLETE '+stem+' '+str(new['texture_resolution']),flush=True)

if __name__=='__main__':
    wanted=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    for entry in ASSETS:
        if not wanted or entry[0] in wanted or entry[1] in wanted:normalize(entry)

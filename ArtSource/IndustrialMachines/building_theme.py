"""Building material roles and vanilla CutoutComplex two-color palette contract.

Green uses graphicData.colorTwo (vanilla Steel); red uses primary white paint.
This keeps metal independent of woody/stony construction costs without new C#.
"""
import hashlib,json,math
VERSION=1
STEEL=(105,105,105)
ADVANCED={'HD_VIMFurnace','HD_SemiconductorFab','HD_MultiTaskMachine2000'}
COMPUTER={'HD_CNCLathe','HD_CNCMillingMachine','HD_MachiningCenter1970',
          'HD_MultiTaskMachine2000','HD_SemiconductorFab','HD_RollingMill1970'}
EXCLUDED={'HD_Radio','HD_ClothingBench','HD_GunSmithTable'}
CYAN=[(57,102,110),(48,89,97),(42,79,87)]

def srgb(material):
    return [round(255*(12.92*c if c<=.0031308 else 1.055*c**(1/2.4)-.055)) for c in material.diffuse_color[:3]]

def mesh_shape_hash(scene):
    scene.view_layers[0].update()
    data=[[o.name,[list(row) for row in o.matrix_world],[list(v.co) for v in o.data.vertices],
           [list(p.vertices) for p in o.data.polygons]] for o in sorted(scene.objects,key=lambda o:o.name) if o.type=='MESH']
    return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()

def shade_index(name):
    tail=name.split('__')[0].rsplit('_',1)[-1].lower()
    return {'top':0,'front':1,'side':2,'crown':0,'flank':1,'bottom':2,'end':2}.get(tail,int(tail) if tail.isdigit() and int(tail)<3 else 1)

def accent(stem,obj):
    if stem not in COMPUTER:return False
    if stem in {'HD_CNCLathe','HD_CNCMillingMachine'}:
        return obj.startswith(('NC03_','NC10_'))
    if stem=='HD_MachiningCenter1970':return obj.startswith(('24_1970s_control_cabinet','21_Low_guard_frame','26_Left_access_panel'))
    if stem=='HD_MultiTaskMachine2000':return obj.startswith('24_2000s_operator_panel_cabinet')
    if stem=='HD_SemiconductorFab':return obj.startswith(('14_Furnace_temperature_controller','26_Wet_bench_extract_canopy','41_Mask_frame','42_Mask_frame'))
    return obj.startswith('23_Roll_gap_control_cabinet')

def role(stem,obj,source,rgb):
    name=source.lower();o=obj.lower()
    if accent(stem,obj):return 'cyan'
    if any(k in name for k in ['vanilla_timber','timber_end','concrete','pit_interior','insulator','drive_belt','refractory','tap_heat','tap_ember','porcelain','glass','rinse_water','silicon_sample','rolled_stock','sheet_']):return 'fixed'
    if 'brown_drive_belt' in o:return 'fixed'
    if any(k in name for k in ['brass','copper']):
        if stem=='HD_BatchStill' and obj.startswith(('06_','08_','09_','11_')):return 'metal'
        return 'fixed'
    if any(k in o for k in ['dial_face','gauge_face','thermometer_face','exposed_paper_tape']):return 'dial'
    if any(k in name for k in ['_crt_','_display_','guard_window','guard_glass']):return 'screen'
    if stem in {'HD_Converter','HD_BlastFurnace'} and any(k in o for k in ['mouth','charging','stove','spindle']):return 'hotmetal'
    if 'furnace_stack' in name or 'dark_furnace_shell' in name:return 'hotmetal'
    if any(k in name for k in ['cast_recess','working_charcoal','working_']):return 'recess'
    if stem in ADVANCED:
        if name.startswith('neutral_cast_iron') or 'furnace_ceramic' in name:return 'white'
        if stem=='HD_VIMFurnace' and obj.startswith(('06_Clamped_top_lid','08_Charging_cap','10_Access_door')):return 'white'
        if stem=='HD_SemiconductorFab' and any(k in o for k in ['worktop','fixture_table']):return 'white'
        if stem=='HD_MultiTaskMachine2000' and 'end_roof_cap' in o:return 'white'
    if any(k in name for k in ['machined_iron','polished_rod','ram_steel','roll_crown','roll_flank','roll_bottom','roll_end']):return 'silver'
    if any(k in name for k in ['cast_bed','base_gray','base_top','base_front','base_side']):return 'metal_base'
    if any(k in name for k in ['neutral_cast_iron','cast_gray','cast_top','cast_front','cast_side','untinted_iron','agitator_motor']):return 'metal'
    if name.startswith('fill_'):
        if any(k in o for k in ['jaw','quill','spindle','turret_tool','machined_rail','rail_top_face','tool_slide']):return 'silver'
        if any(k in o for k in ['slot','feed_screw','toolpost_lock','lock_lever','eight_lobed','recess','handwheel']):return 'recess'
        return 'metal_legacy'
    return 'fixed'

def color_for(which,source,rgb):
    idx=shade_index(source)
    if which=='cyan':return CYAN[idx],'fixed'
    if which=='white':return [(239,239,237),(228,228,225),(216,216,213)][idx],'primary'
    if which=='metal':return [(255,255,255),(234,234,234),(219,219,219)][idx],'metal'
    if which=='metal_base':return [(210,210,210),(190,190,190),(177,177,177)][idx],'metal'
    if which=='hotmetal':return [(248,248,248),(226,226,226),(210,210,210)][idx],'metal'
    if which=='metal_legacy':
        value=max(rgb);v=255 if value>=220 else 238 if value>=170 else 219 if value>=120 else 202
        return (v,v,v),'metal'
    if which=='silver':return [(193,193,190),(180,180,177),(166,166,163)][idx],'fixed'
    if which=='recess':return [(57,57,55),(49,49,47),(42,42,40)][idx],'fixed'
    if which=='dial':return (223,223,218),'fixed'
    if which=='screen':return (66,103,111),'fixed'
    return tuple(rgb),'fixed'

def apply(scene,stem):
    if stem in EXCLUDED:return False
    import bpy
    before=mesh_shape_hash(scene);cache={};counts={}
    def linear(c):
        c/=255;return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    for obj in [o for o in scene.objects if o.type=='MESH']:
        for i,material in enumerate(list(obj.data.materials)):
            source=material.get('theme_source_name',material.name)
            original=list(material.get('theme_source_rgb',srgb(material)))
            which=role(stem,obj.name,source,original);base,channel=color_for(which,source,original)
            key=(source,which)
            if key not in cache:
                name=source+'__Theme_'+which
                m=bpy.data.materials.get(name) or material.copy();m.name=name
                display=[c*STEEL[j]/255 if channel=='metal' else c for j,c in enumerate(base)]
                m.diffuse_color=tuple(linear(c) for c in display)+(1,)
                emission=next(n for n in m.node_tree.nodes if n.type=='EMISSION')
                emission.inputs[0].default_value=m.diffuse_color
                m['theme_source_name']=source;m['theme_source_rgb']=original;m['theme_role']=which
                m['theme_base_rgb']=list(base);m['theme_channel']=channel;m['tintable']=channel=='primary'
                cache[key]=m
            obj.data.materials[i]=cache[key];counts[which]=counts.get(which,0)+1
    assert mesh_shape_hash(scene)==before
    scene['building_theme_version']=VERSION;scene['building_theme_family']='advanced_white' if stem in ADVANCED else 'industrial_steel'
    scene['building_theme_stem']=stem;scene['building_theme_secondary_color']=list(STEEL)
    scene['building_theme_geometry_sha256']=before;scene['building_theme_roles']=json.dumps(counts)
    return True

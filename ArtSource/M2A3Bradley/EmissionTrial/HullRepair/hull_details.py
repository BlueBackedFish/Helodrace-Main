"""Bradley exterior hull restoration: source components, fixed role colors, no ERA."""
import bpy,json,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;ROOT=TRIAL.parent
sys.path[:0]=[str(ROOT),str(TRIAL/'DetailRepair')]
from source_details import read_parts,source_mesh
from mesh_regions import regions,position_key

# Each seed identifies one complete connected source region, including UV seams.
# Tiny independent screws, skirt hinge arrays, internal machinery and ERA stay out.
REPLACE={
 'WT_hatch_01':[(0,'hatch'),(1423,'fitting'),(112,'hatch'),(136,'hatch'),(96,'fitting'),(877,'fitting')],
 'WT_hatch_05':[(0,'hatch'),(369,'raised_lid'),(1,'fitting'),(2470,'fitting'),(5,'fitting'),(3,'fitting'),(25,'fitting'),(37,'fitting'),(49,'fitting'),(61,'fitting')],
 'WT_hatch_06':[(0,'service_panel'),(2770,'fitting'),(2880,'fitting')],
 'WT_hatch_04':[(0,'rear_door'),(34,'door_panel'),(2,'door_handle'),(4,'door_handle'),(20,'door_handle'),(32,'door_handle'),(493,'door_handle')],
 'WT_ex_lantern_l_01':[(i,'lamp') for i in [22,10,2026,149,38,1349,1472,3310,428,3240,36,32,3390]],
 'WT_ex_lantern_r_01':[(i,'lamp') for i in [0,599,2130,546,792,1994,26,64,374,2862,2792,2942]],
 'WT_ex_decor_l_01':[(0,'tow_eye')], 'WT_ex_decor_r_01':[(0,'tow_eye')],
 'WT_ex_decor_l_02':[(0,'tow_eye')], 'WT_ex_decor_r_02':[(0,'tow_eye')],
 'WT_ex_decor_l_05':[(20,'tow_eye'),(0,'tow_eye')],
 'WT_ex_decor_r_06':[(20,'tow_eye'),(0,'tow_eye')],
 'WT_ex_decor_04':[(118,'tow_connector'),(0,'tow_connector'),(37,'tow_connector'),(65,'tow_connector')],
}
# name, OBJ group, source seeds, surface role, animation parent
ADD=[
 ('WT_Hull_DriverRearLinkage','WT_@root',[44887,44893,54491,55285],'fitting','HULL'),
 ('WT_Hull_RoofHatchFixedHinges','WT_@root',[22387,22431,22415,22449,22463,22471,27888,28958,27613,27743,27657,27527,27968,28804,28838,30854],'fitting','HULL'),
 ('WT_Hull_LiftEye_FL','WT_@root',[43789],'lift_eye','HULL'),
 ('WT_Hull_LiftEye_FR','WT_@root',[44185],'lift_eye','HULL'),
 ('WT_Hull_LiftEye_RL','WT_@root',[44301],'lift_eye','HULL'),
 ('WT_Hull_LiftEye_RR','WT_@root',[44171],'lift_eye','HULL'),
 ('WT_Hull_FillerBase','WT_@root',[53901],'hatch','HULL'),
 ('WT_Hull_FillerCap','WT_@root',[53935],'raised_lid','HULL'),
 ('WT_Hull_FillerLatch','WT_@root',[44657],'fitting','HULL'),
 ('WT_Hull_FrontRoofHousing','WT_@root',[43793,43827],'hatch','HULL'),
 ('WT_Hull_FrontRoofCover','WT_@root',[43799,65518],'raised_lid','HULL'),
 ('WT_Hull_LeftRoofHandle','WT_@root',[62689],'roof_handle','HULL'),
 ('WT_Hull_RampLatchUpper','WT_@root',[41997],'door_handle','REAR_RAMP_HINGE'),
 ('WT_Hull_RampLatchLower','WT_@root',[42709],'door_handle','REAR_RAMP_HINGE'),
 ('WT_Hull_TailLamp_L','WT_ex_decor_l_09',[0,12,140,382,20,22],'tail_lamp','HULL'),
 ('WT_Hull_TailLamp_R','WT_ex_decor_r_04',[0,12,120,382,20,22],'tail_lamp','HULL'),
]


def apply(fixed):
    groups=set(REPLACE)|{r[1] for r in ADD}
    source_groups=json.loads((ROOT/'ReferenceFit/source_groups.json').read_text())
    skirts=['WT_'+n for n in source_groups if n.startswith(('ex_armor_l_','ex_armor_r_'))]
    parts,source=read_parts(groups|set(skirts))
    components={}
    for group,items in parts.items():
        me=source_mesh(items,'hull source connectivity')
        components[group]=regions(me);bpy.data.meshes.remove(me)
    records={};replaced=[];added=[];accents={}
    def accent(name,srgb):
        if name in accents:return accents[name]
        m=bpy.data.materials.new('Hull fixed '+name);m.use_nodes=True
        rgb=tuple(v/255/12.92 if v/255<=.04045 else ((v/255+.055)/1.055)**2.4 for v in srgb)
        m.diffuse_color=(*rgb,1);m['fixed_accent']=name
        nodes=m.node_tree.nodes;nodes.clear();e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=(*rgb,1);e.inputs['Strength'].default_value=1
        out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs['Surface']);accents[name]=m;return m
    def select(group,spec):
        roles={}
        for seed,role in spec:
            for i in next(c for c in components[group] if seed in c):
                assert i not in roles or roles[i]==role,(group,'Overlapping source selections')
                roles[i]=role
        return sorted(roles),roles
    def populate(obj,group,ids,roles):
        items=[parts[group][i] for i in ids]
        mesh=source_mesh(items,obj.name+' source exterior',obj.matrix_world.inverted())
        desired=[]
        for p,i,(_,mat) in zip(mesh.polygons,ids,items):
            role=roles[i];n=obj.matrix_world.to_3x3()@p.normal
            if 'headlight_glass' in mat:
                desired.append(accent('headlamp glass',(204,199,171)));continue
            if 'optic' in mat or 'dynamic_glass' in mat:
                desired.append(accent('optics',(38,51,53)));continue
            if role=='tail_lamp':
                # The raised rear lens is a separate source material region.
                center=obj.matrix_world@p.center
                if 'glass' in mat or (center.y>3.101 and abs(n.y)>.6):
                    desired.append(accent('tail lens',(141,67,55)));continue
                y=168 if n.z>.55 else 132
            elif role in {'tow_eye','tow_connector'}:y=156 if role=='tow_eye' else 120
            elif role=='skirt_handle':y=168
            elif role=='roof_handle':y=216
            elif role=='door_handle':y=180
            elif role=='rear_door':y=156 if abs(n.y)>.55 else 132 if n.z>-.55 else 108
            elif role=='door_panel':y=180 if abs(n.y)>.55 or n.z>.55 else 144
            elif role=='service_panel':y=168 if n.z>.25 else 120
            elif role=='lamp':y=192 if n.z>.55 else 168 if abs(n.y)>.55 else 144 if n.z>-.55 else 108
            elif role=='lift_eye':y=192 if n.z>.55 else 168
            else:
                top={'hatch':192,'raised_lid':216,'fitting':216}[role]
                # Small hinges and rods retain one side color around the axis.
                y=top if n.z>.65 else top-24 if n.z>-.65 else top-72
            desired.append(fixed('hull_detail',y))
        slots={}
        for m in desired:
            if m.name not in slots:slots[m.name]=len(slots);mesh.materials.append(m)
        for p,m in zip(mesh.polygons,desired):p.material_index=slots[m.name];p.use_smooth=False
        obj.data=mesh;obj['source_group']=group.removeprefix('WT_');obj['hull_detail_restored']=True
        records[obj.name]={'source_group':group,'source_polygon_indices':ids,'faces':len(ids),
                           'parent':obj.parent.name,'source_roles':{str(i):roles[i] for i in ids}}
    for name,spec in REPLACE.items():
        obj=bpy.data.objects[name];ids,roles=select(name,spec);populate(obj,name,ids,roles);replaced.append(name)
    additions=list(ADD)
    for group in skirts:
        candidates=[]
        for c in components[group]:
            coords=[Vector(co) for i in c for co in parts[group][i][0]]
            extent=[max(p[k] for p in coords)-min(p[k] for p in coords) for k in range(3)]
            # Confirmed U-shaped handle family, not the plate or skirt hinges.
            if len(c)==316 and .25<extent[1]<.5 and .07<extent[2]<.2:candidates.append(c[0])
        assert len(candidates)==1,(group,'Expected one main skirt handle',candidates)
        additions.append(('WT_Hull_SkirtHandle_'+group.removeprefix('WT_ex_armor_'),group,candidates,'skirt_handle','HULL'))
    existing={position_key(o.matrix_world@v.co) for o in bpy.context.scene.objects if o.type=='MESH' for v in o.data.vertices}
    for name,group,seeds,role,parent in additions:
        ids,roles=select(group,[(seed,role) for seed in seeds])
        keys={position_key(co) for i in ids for co in parts[group][i][0]}
        assert not (keys&existing),(name,'Selected source component already present')
        obj=bpy.data.objects.new(name,bpy.data.meshes.new(name));bpy.context.scene.collection.objects.link(obj)
        obj.parent=bpy.data.objects[parent];obj.matrix_parent_inverse=obj.parent.matrix_world.inverted();bpy.context.view_layer.update()
        populate(obj,group,ids,roles);added.append(name);existing.update(keys)
    bpy.context.view_layer.update()
    return {'source':source['source'],'source_sha256':source['source_sha256'],'baseline_commit':'cbf17877',
        'replaced_objects':replaced,'added_objects':added,'parts':records,
        'fixed_accent_materials':list(accents),'ERA':'deferred, not restored',
        'preserved':'hull armor shell, tracks, wheels, simplified grille, all turret geometry and colors'}

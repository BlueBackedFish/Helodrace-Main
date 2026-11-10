"""Build only the supplied clean F-15EX, without external stores or gear."""
from pathlib import Path
import bpy,sys,json,math,hashlib,zipfile,tempfile
import numpy as np
from collections import Counter
from mathutils import Vector,Matrix

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'VehiclePipeline'));from pipeline import read_obj
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
style=json.loads((ROOT/'style.json').read_text(encoding='utf-8'))
color_plan=json.loads((ROOT/'panel_colors.json').read_text(encoding='utf-8'))
assert color_plan['source_sha256']==style['source_sha256']
face_roles={fi:part['palette_role'] for part in color_plan['parts'] for fi in part['source_face_ids']}
color_counts=Counter()
paint_boundaries=json.loads((ROOT/'paint_boundaries.json').read_text(encoding='utf-8'))
assert paint_boundaries['source_sha256']==style['source_sha256']
archive=Path(style['source_archive']);assert hashlib.sha256(archive.read_bytes()).hexdigest()==style['source_sha256']
with zipfile.ZipFile(archive) as package:
    data=package.read(style['obj_member'])
with tempfile.TemporaryDirectory(dir=OUT) as temporary:
    path=Path(temporary)/'source.obj';path.write_bytes(data);model=read_obj(path)
selected={g['object']:g for g in model['groups'] if g['object'] in [style['airframe_object'],style['hidden_gear_object']]}
assert set(selected)=={style['airframe_object'],style['hidden_gear_object']}
airframe=selected[style['airframe_object']]
assert len(airframe['faces'])==13886
assert {model['faces'][i]['material'] for i in airframe['faces']}=={'US_F15EX','US_1F15EX_Glass'}
scene=m.start();materials={key:m.material('F15EX '+key,rgb) for key,rgb in style['palette'].items()}
paint=materials['body']
def brighten(value):
    v=value/255;linear=v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
    v=min(1,linear*1.12);return round(255*(12.92*v if v<=.0031308 else 1.055*v**(1/2.4)-.055))
fin=m.material('F15EX upright fin fixed +12% linear',[brighten(v) for v in style['palette']['body']])
angle=math.radians(style['source_yaw_degrees']);c,s=math.cos(angle),math.sin(angle)
rotation=Matrix(((-c,0,s,0),(s,0,c,0),(0,1,0,0),(0,0,0,1)))
used=sorted({i for fi in airframe['faces'] for i in model['faces'][fi]['vertices']})
points=[rotation@Vector(model['vertices'][i]) for i in used]
lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)])
center=(lo+hi)/2;scale=style['physical_length_m']/(hi.y-lo.y)
transform=Matrix.Diagonal((scale,scale,scale,1))@Matrix.Translation((-center.x,-center.y,-lo.z))@rotation
origin=bpy.data.objects.new('F15EX_ART_ORIGIN',None);scene.collection.objects.link(origin);origin.matrix_world=transform
objects=[];inventory=[]
for name,group in selected.items():
    faces=[model['faces'][i] for i in group['faces']];ids=sorted({i for f in faces for i in f['vertices']});local={i:j for j,i in enumerate(ids)}
    coords=[model['vertices'][i] for i in ids];mesh=bpy.data.meshes.new(name+' unchanged source mesh')
    mesh.from_pydata(coords,[],[[local[i] for i in f['vertices']] for f in faces]);mesh.update()
    uv=mesh.uv_layers.new(name='Original OBJ UV')
    for polygon,face in zip(mesh.polygons,faces):
        assert tuple(polygon.vertices)==tuple(local[i] for i in face['vertices'])
        for loop,index in zip(polygon.loop_indices,face['uv']):uv.data[loop].uv=model['uv'][index]
    actual=np.empty(len(coords)*3,dtype=np.float32);mesh.vertices.foreach_get('co',actual)
    assert np.array_equal(actual.reshape(-1,3),np.asarray(coords,dtype=np.float32))
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj);obj.parent=origin
    roles=[paint,materials['glass'],materials['radome'],materials['exhaust'],materials['exhaust_inside'],materials['intake'],fin,materials['gear']]
    color_slots={key:len(roles)+i for i,key in enumerate(color_plan['roles'])}
    roles.extend(materials[key] for key in color_plan['roles'])
    for material in roles:mesh.materials.append(material)
    hidden=name==style['hidden_gear_object'];obj.hide_render=hidden;obj.hide_viewport=hidden
    for polygon,face in zip(mesh.polygons,faces):
        p=rotation@polygon.center;normal=rotation@polygon.normal;x=p.x-center.x;y=p.y-center.y;z=p.z
        role=0
        if hidden:role=7
        elif face['material']=='US_1F15EX_Glass':role=1
        elif y>7.1:role=2
        elif y< -7.25 and abs(abs(x)-.70)<.65 and z<3.15:
            radial=math.hypot(abs(x)-.70,z-2.32);role=4 if radial<.43 else 3
        elif 1<y<4 and .65<abs(x)<1.85 and z<2.8 and normal.y>.5:role=5
        elif y< -4 and z>3.5:role=6
        source_face=group['faces'][polygon.index]
        if source_face in face_roles:
            assert role==0,('Panel tint overlaps protected glass/nozzle/gear',source_face,role)
            palette_role=face_roles[source_face];role=color_slots[palette_role];color_counts[palette_role]+=1
        polygon.material_index=role
    obj['source_group']=group['id'];obj['flight_visibility']='hidden landing gear' if hidden else 'clean airframe only'
    objects.append(obj);inventory.append(dict(object=name,faces=len(faces),vertices=len(coords),hidden_in_flight=hidden,
        source_coordinates_topology_uv_preserved_at_ingestion=True,source_face_ids=group['faces']))
assert sum(len(o.data.polygons) for o in objects)==19648
bpy.context.view_layer.update();visible=[o for o in objects if not o.hide_render]
# A physical Y-plane cuts the paint across long source triangles, keeping
# the radome's rear color boundary straight without changing its shape.
body_obj=visible[0]
paint_frame=bpy.data.objects.new('F15EX physical paint coordinates',None);scene.collection.objects.link(paint_frame)
paint_frame.parent=origin;paint_frame.matrix_parent_inverse=origin.matrix_world.inverted()
radome_material=bpy.data.materials.new('F15EX straight radome paint');radome_material.use_nodes=True
nodes=radome_material.node_tree.nodes;nodes.clear();links=radome_material.node_tree.links
coordinates=nodes.new('ShaderNodeTexCoord');coordinates.object=paint_frame
xyz=nodes.new('ShaderNodeSeparateXYZ');links.new(coordinates.outputs['Object'],xyz.inputs[0])
mask=nodes.new('ShaderNodeMath');mask.operation='GREATER_THAN';links.new(xyz.outputs['Y'],mask.inputs[0]);mask.inputs[1].default_value=paint_boundaries['radome_start_y_m']
linear=lambda v:v/255/12.92 if v/255<=.04045 else ((v/255+.055)/1.055)**2.4
mix=nodes.new('ShaderNodeMixRGB');links.new(mask.outputs[0],mix.inputs[0]);mix.inputs[1].default_value=tuple(linear(v) for v in style['palette']['body'])+(1,)
mix.inputs[2].default_value=tuple(linear(v) for v in style['palette']['radome'])+(1,)
emission=nodes.new('ShaderNodeEmission');output=nodes.new('ShaderNodeOutputMaterial');links.new(mix.outputs[0],emission.inputs[0]);links.new(emission.outputs[0],output.inputs[0])
slot=len(body_obj.data.materials);body_obj.data.materials.append(radome_material)
for polygon in body_obj.data.polygons:
    if polygon.material_index in (0,2):polygon.material_index=slot
# Shade the lower shoulder beyond the existing thin outboard boundary and
# both stabilators, preserving each panel's original relative paint tone.
shade_faces=set(paint_boundaries['tail_shoulder_source_faces']+paint_boundaries['stabilator_source_faces'])
assert len(shade_faces)==len(paint_boundaries['tail_shoulder_source_faces'])+len(paint_boundaries['stabilator_source_faces'])
assert shade_faces.issubset(set(airframe['faces']))
source_rgb={material.name:style['palette'][key] for key,material in materials.items()}
source_rgb[radome_material.name]=style['palette']['body']
dark_slots={};shade_counts=Counter()
for polygon,source_face in zip(body_obj.data.polygons,airframe['faces']):
    if source_face not in shade_faces:continue
    original=body_obj.data.materials[polygon.material_index];assert original.name in source_rgb
    if original.name not in dark_slots:
        rgb=[max(0,v+paint_boundaries['tail_shading_delta_rgb']) for v in source_rgb[original.name]]
        material=m.material(original.name+' lower tail shade',rgb)
        dark_slots[original.name]=len(body_obj.data.materials);body_obj.data.materials.append(material)
    polygon.material_index=dark_slots[original.name];shade_counts[original.name]+=1
assert sum(shade_counts.values())==len(shade_faces)
sys.path.insert(0,str(ROOT));from articulation import prepare as prepare_articulation
visible,articulation=prepare_articulation(scene,body_obj,airframe['faces'],origin,materials['panel'])
sys.path.insert(0,str(ROOT));from panel_edges import prepare as prepare_edges
panel_edges=[prepare_edges(scene,obj,materials['panel'],.010 if obj.name.startswith('EXHAUST') else .020)
    for obj in visible if not obj.name.startswith(('EXHAUST_FIXED','JOINT_FIXED')) and not obj.name.endswith('inner_seal')]
points=[o.matrix_world@v.co for o in visible for v in o.data.vertices]
low=Vector([min(p[i] for p in points) for i in range(3)]);high=Vector([max(p[i] for p in points) for i in range(3)])
assert abs(high.y-low.y-style['physical_length_m'])<1e-4
reference=json.loads((ROOT.parent/'F22Raptor/output/physical_scale.json').read_text(encoding='utf-8'))
length_tiles=style['physical_length_m']*reference['tiles_per_meter'];padding=style['outline_pixels']+style['transparent_margin_pixels']
resolution=round(length_tiles*reference['reference_pixels_per_tile']+2*padding)
draw_size=length_tiles/(1-2*padding/resolution);ortho=(high.y-low.y)/(1-2*padding/resolution)
camera_data=bpy.data.cameras.new('F15EX exact top');camera_data.type='ORTHO';camera_data.ortho_scale=ortho
camera=bpy.data.objects.new('F15EX exact top',camera_data);scene.collection.objects.link(camera)
camera.location=(0,0,high.z+10);camera.rotation_euler=(0,0,0);scene.camera=camera
bpy.data.orphans_purge(do_recursive=True)
m.save(OUT/'f15ex_eagle_ii.blend');m.render(OUT/'top_plain_2048.png',camera,style['master_resolution'])
detail_camera=m.camera('F15EX oblique inspection',-55,40,23,(0,0,2));m.render(OUT/'oblique.png',detail_camera,1000)
mount_camera=m.camera('F15EX empty mount inspection',-65,-35,12,(0,-1.8,.8));m.render(OUT/'mounts_underside.png',mount_camera,1000)
inferred_span=high.x-low.x;span_error=inferred_span/style['physical_wingspan_m']-1
assert abs(span_error)<.025
report=dict(passed=True,source_archive=str(archive),source_sha256=style['source_sha256'],
    obj_sha256=hashlib.sha256(data).hexdigest(),source_groups=len(model['groups']),
    selected_aircraft='1_F_15EX_clean',visible_faces=sum(len(o.data.polygons) for o in visible),source_airframe_faces=13886,archived_landing_gear_faces=5762,
    ingested_source_meshes=inventory,visible_meshes=[dict(object=o.name,faces=len(o.data.polygons)) for o in visible],external_weapons=0,external_drop_tanks=0,targeting_pods=0,panel_edges=panel_edges,
    other_lineup_aircraft_omitted=True,landing_gear_hidden=True,source_geometry_preserved_before_articulation=True,
    articulation=articulation,engine_fairing_rgb=style['palette']['body'],
    tail_shading=dict(shoulder_faces=len(paint_boundaries['tail_shoulder_source_faces']),stabilator_faces=len(paint_boundaries['stabilator_source_faces']),
        delta_rgb=paint_boundaries['tail_shading_delta_rgb'],source_material_counts=dict(shade_counts),thick_added_lines_removed=True),
    radome_paint=dict(boundary='Straight physical Y-plane',start_y_m=paint_boundaries['radome_start_y_m']),
    native_forward=[s,0,c],native_up=[0,1,0],source_to_physical_transform=[list(r) for r in transform],
    physical_bounds=[list(low),list(high)],physical_length_m=style['physical_length_m'],
    nominal_wingspan_m=style['physical_wingspan_m'],model_wingspan_m=inferred_span,model_span_relative_error=span_error,
    uniform_scale=True,blueprint_validation_performed=False,projection='Exact 90-degree top, nose up',
    lighting='Fixed emission only; no lights, AO, gloss, normal maps, reflections or shadows',
    source_albedo_sha256=style['source_albedo_sha256'],ortho_scale=ortho,
    source_albedo_used_for_paint=False,source_markings_used=False,
    panel_color_groups=[dict(name=p['name'],palette_role=p['palette_role'],source_faces=len(p['source_face_ids'])) for p in color_plan['parts']],
    panel_color_face_counts=dict(color_counts),
    delivery_resolution=resolution,draw_size_tiles=[draw_size,draw_size],pivot_normalized=[.5,.5],
    reference_scale='C-17 19 tiles / F-22 physical_scale.json',reference_url=style['reference_url'],
    game_definition_added=False,in_game_tested=False)
assert not any(o.type=='LIGHT' for o in scene.objects)
(OUT/'model_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
(OUT/'panel_color_validation.json').write_text(json.dumps(dict(passed=True,
    source_material_assignment_checked_before_articulation=True,part_groups=len(color_plan['parts']),
    painted_source_faces=sum(color_counts.values()),palette_roles=dict(color_counts),
    protected_intake_faces_excluded=color_plan['protected_intake_faces_excluded'],
    tail_shading=report['tail_shading'],engine_fairing_highlight_removed=True,radome=report['radome_paint'],source_albedo_weathering_used=False,
    geometry_revision='Control separation/caps and mirrored variable nozzles; see model_validation.json'),indent=2)+'\n',encoding='utf-8')
(OUT/'selection_manifest.json').write_text(json.dumps([dict(object=g['object'],faces=len(g['faces']),
    materials=dict(Counter(model['faces'][i]['material'] for i in g['faces'])),
    selection='visible clean airframe' if g['object']==style['airframe_object'] else
        'archived hidden landing gear' if g['object']==style['hidden_gear_object'] else 'omitted other aircraft/loadout')
    for g in model['groups']],indent=2)+'\n',encoding='utf-8')
print('F15EX_CLEAN_MODEL_PASS:',resolution,'px;',round(draw_size,4),'tiles; no external weapons, tanks or pods')

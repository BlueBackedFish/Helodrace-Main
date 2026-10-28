"""Derive an empty CROWS II station from the canonical, painted M2 station."""
from pathlib import Path
import bpy,bmesh,sys,json,hashlib

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT.parent/'output/crows_ii_m2.blend'
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import modeling as m


def surface_snapshot(obj,omit_feed=False):
    """Compare retained world geometry, source IDs, UV loops and face paint."""
    mesh=obj.data;faces=[]
    ids=mesh.attributes.get('source_face');vertices=mesh.attributes.get('source_vertex')
    for face in mesh.polygons:
        material=mesh.materials[face.material_index]
        if omit_feed and material.name=='feed':continue
        faces.append((ids.data[face.index].value if ids else face.index,
            tuple(vertices.data[i].value if vertices else i for i in face.vertices),
            tuple(tuple(obj.matrix_world@mesh.vertices[i].co) for i in face.vertices),
            tuple((uv.name,tuple(tuple(uv.data[i].uv) for i in face.loop_indices)) for uv in mesh.uv_layers),
            material.name,tuple(material.diffuse_color)))
    return faces


source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
scene.frame_set(1);bpy.context.view_layer.update();bpy.context.preferences.filepaths.save_version=0
retained=[o for o in scene.objects if o.type=='MESH' and o.get('layer') in ['base','frame']]
before={o.name:surface_snapshot(o,True) for o in retained}
frame=next(o for o in retained if o.get('source_object')=='Cylinder.003' and o['layer']=='frame')
removed_faces=[p.index for p in frame.data.polygons if frame.data.materials[p.material_index].name=='feed']
assert len(removed_faces)==568,'Unexpected source chute selection'
source_face=frame.data.attributes['source_face']
removed_source_faces=[source_face.data[i].value for i in removed_faces]
removed_weapons=[]
for obj in list(scene.objects):
    if obj.type=='MESH' and obj not in retained:
        assert obj.get('layer') in ['body','barrel'] and obj.get('donor_object'),obj.name
        removed_weapons.append(obj.name);bpy.data.objects.remove(obj,do_unlink=True)
assert len(removed_weapons)==20,'Expected the complete canonical M2 QCB donor'
recoil=bpy.data.objects.get('BARREL_RECOIL');assert recoil is not None
bpy.data.objects.remove(recoil,do_unlink=True)
bm=bmesh.new();bm.from_mesh(frame.data);bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm,geom=[bm.faces[i] for i in removed_faces],context='FACES')
bm.to_mesh(frame.data);bm.free();frame.data.update()
# Remove unused weapon/feed data and material slots, rather than hiding them.
for obj in retained:
    indices=[f.material_index for f in obj.data.polygons];used=sorted(set(indices))
    materials=[obj.data.materials[i] for i in used];remap={old:new for new,old in enumerate(used)}
    obj.data.materials.clear()
    for material in materials:obj.data.materials.append(material)
    for face,index in zip(obj.data.polygons,indices):face.material_index=remap[index]
bpy.context.view_layer.update()
assert before=={o.name:surface_snapshot(o) for o in retained},'Retained station surfaces changed'
assert not any(o.get('donor_object') or o.get('layer') in ['body','barrel','feed'] for o in scene.objects)
assert not any(mat.name=='feed' for o in retained for mat in o.data.materials)
yaw=scene.objects['CROWS_YAW'];assert tuple(yaw.location)==(0,0,0)
assert all(o.parent==yaw for o in retained if o['layer']=='frame')
assert not any(o.type=='LIGHT' for o in scene.objects)
if 'muzzle_direction' in scene:del scene['muzzle_direction']
scene['variant']='Unarmed';scene.frame_start=scene.frame_end=1
bpy.data.orphans_purge(do_recursive=True)
top=scene.objects['Camera_Top']
for label in ['frame','full']:
    for obj in retained:obj.hide_render=label=='frame' and obj['layer']!='frame'
    m.render(OUT/(label+'_raw.png'),top,2048)
for obj in retained:obj.hide_render=False
for direction in ['north','east','south','west']:
    m.render(OUT/(direction+'_raw.png'),scene.objects['Camera_'+direction],2048)
scene.camera=top;m.save(OUT/'crows_ii_unarmed.blend')
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash,'Canonical source was modified'
metadata=dict(source=str(SOURCE),source_sha256=source_hash,variant='Unarmed',
    removed_weapon_objects=removed_weapons,removed_feed_faces=len(removed_faces),
    removed_feed_source_faces=removed_source_faces,retained_objects=list(before),
    retained_face_counts={name:len(faces) for name,faces in before.items()},
    retained_geometry_uvs_paint_and_transforms_identical=True,source_file_unchanged=True,
    weapon_and_feed_geometry_removed=True,unused_weapon_data_purged=True,
    ammunition_box_retained=True,optics_and_empty_cradle_retained=True,
    yaw_pivot=[0,0,0],top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],
    master_resolution=2048,native_resolution=1024,outline_world_tiles=.026,
    projection_top=90,projection_directions=64,gameplay_definition_added=False,in_game_tested=False)
(OUT/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
print('CROWS_UNARMED_MODEL_PASS: 20 weapon objects and 568 chute faces removed; station geometry/UV/paint/pivot preserved')

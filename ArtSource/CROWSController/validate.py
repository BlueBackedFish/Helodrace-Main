"""Verify the saved addon preserves all original desk meshes and cameras."""
from pathlib import Path
import bpy,json,hashlib,struct
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';meta=json.loads((OUT/'metadata.json').read_text());source=Path(meta['source_desk'])
assert hashlib.sha256(source.read_bytes()).hexdigest()==meta['source_desk_sha256']
def signature(o):
    h=hashlib.sha256()
    for v in o.data.vertices:h.update(struct.pack('<3f',*v.co))
    for p in o.data.polygons:
        h.update(struct.pack('<I',len(p.vertices)));h.update(struct.pack('<'+str(len(p.vertices))+'I',*p.vertices))
        mat=o.data.materials[p.material_index];rgb=next(n for n in mat.node_tree.nodes if n.type=='EMISSION').inputs[0].default_value;h.update(struct.pack('<4f',*rgb))
    for uv in o.data.uv_layers:
        h.update(uv.name.encode())
        for v in uv.data:h.update(struct.pack('<2f',*v.uv))
    return h.hexdigest(),tuple(c for row in o.matrix_world for c in row)
bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update();original={o.name:signature(o) for o in bpy.context.scene.objects if o.type=='MESH'}
cameras={o.name:(o.data.ortho_scale,tuple(c for row in o.matrix_world for c in row)) for o in bpy.context.scene.objects if o.type=='CAMERA'}
bpy.ops.wm.open_mainfile(filepath=str(OUT/'controller_desk.blend'));bpy.context.view_layer.update();scene=bpy.context.scene
for name,expected in original.items():assert signature(bpy.data.objects[name])==expected,name
for name,expected in cameras.items():
    o=bpy.data.objects[name];assert (o.data.ortho_scale,tuple(c for row in o.matrix_world for c in row))==expected,name
def bounds(o):
    pts=[o.matrix_world@v.co for v in o.data.vertices];return [min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]
def separated(a,b):
    lo,hi=bounds(a);other_lo,other_hi=bounds(b);return any(hi[i]<=other_lo[i]+1e-7 or other_hi[i]<=lo[i]+1e-7 for i in range(3))
assert separated(bpy.data.objects['CROWS control pedestal'],bpy.data.objects['Keyboard base'])
for name in ['Keyboard base','Monitor bezel','Desktop tower']:assert separated(bpy.data.objects['CROWS rugged display case'],bpy.data.objects[name]),name
table_top=bounds(bpy.data.objects['Desk tabletop'])[1][2];feet=[o for o in scene.objects if o.name.startswith('CROWS rubber desk foot')]
assert len(feet)==4 and all(abs(bounds(o)[0][2]-table_top)<1e-6 for o in feet)
addon=[o for o in scene.objects if o.type=='MESH' and 'addon_part' in o];assert len(addon)==meta['controller_meshes']
assert all(o.parent and o.parent.name=='CROWS_CONTROLLER_MODULE' for o in addon)
assert not any(o.type=='LIGHT' for o in scene.objects)
assert not any(o.name.startswith(('Monitor title bar','Screen map ','Screen marker','Screen status row','CROWS crosshair ','CROWS target ','CROWS display status')) for o in scene.objects)
assert all(name in scene.objects for name in ['Monitor display','CROWS display screen'])
result=dict(passed=True,original_desk_meshes_verified=len(original),original_vertices_faces_uv_material_colors_and_transforms_identical=True,original_camera_projection_and_registration_identical=True,controller_meshes=len(addon),controller_feet_touch_table=True,screen_clears_keyboard_monitor_and_tower=True,old_keyboard_mouse_and_cables_preserved=True,source_desk_sha256=meta['source_desk_sha256'],in_game_tested=False)
result['both_displays_blank']=True
(OUT/'model_validation.json').write_text(json.dumps(result,indent=2)+'\n');print('CROWS_CONTROLLER_VALIDATION_PASS:',len(original),'source desk meshes/UVs/colors/transforms and four cameras identical; both displays blank; screen clearances and desk contact verified')

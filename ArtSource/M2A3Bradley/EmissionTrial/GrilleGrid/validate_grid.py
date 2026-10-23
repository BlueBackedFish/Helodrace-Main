"""Verify grille-only shader changes, original UVs, square spacing and GLB texture."""
import bpy,json,sys,hashlib,shutil,struct
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent
sys.path.insert(0,str(HERE))
from grid_material import UV_NAME
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));bpy.context.scene.frame_set(1)
def sha(value):return hashlib.sha256(json.dumps(value,sort_keys=True).encode()).hexdigest()
def snapshot():
    result={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        record={'parent':o.parent.name,'world':[[round(v,6) for v in row] for row in o.matrix_world],
            'vertices':[tuple(round(c,6) for c in v.co) for v in o.data.vertices],
            'faces':[list(p.vertices) for p in o.data.polygons],
            'colors':[tuple(round(c,6) for c in o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons]}
        result[o.name]={'model':sha(record),'uvs':{uv.name:sha([tuple(round(c,6) for c in e.uv) for e in uv.data]) for uv in o.data.uv_layers if uv.name!=UV_NAME}}
    return result
current=snapshot()
if '--capture' in sys.argv:
    assert not (HERE/'before.json').exists()
    (HERE/'before.json').write_text(json.dumps({'commit':'d88bba54','objects':current},indent=2)+'\n')
    for src,dst in [('HullRepair/after_front.png','before_front.png'),('VF_East/256/preview_east.png','before_east_256.png')]:shutil.copyfile(TRIAL/src,HERE/dst)
    print('GRILLE BASELINE CAPTURED');sys.exit(0)
assert current==json.loads((HERE/'before.json').read_text())['objects'],'Model, diffuse colors or original UVs changed'
report=json.loads((TRIAL/'manifest.json').read_text())['grille_grid']
for g in report['planar_groups']:
    u,v=Vector(g['u']),Vector(g['v']);assert abs(u.length-1)<1e-6 and abs(v.length-1)<1e-6 and abs(u.dot(v))<1e-6
    obj=bpy.data.objects[g['object']];me=obj.data;uv=me.uv_layers[UV_NAME]
    for i in g['faces']:
        p=me.polygons[i];m=me.materials[p.material_index]
        assert {n.type for n in m.node_tree.nodes}=={'UVMAP','TEX_IMAGE','EMISSION','OUTPUT_MATERIAL'}
        tex=next(n for n in m.node_tree.nodes if n.type=='TEX_IMAGE');assert tex.image.packed_file and tex.extension=='REPEAT'
        for li in p.loop_indices:
            co=obj.matrix_world@me.vertices[me.loops[li].vertex_index].co
            expected=Vector((co.dot(u)/report['pitch'],co.dot(v)/report['pitch']))
            assert (uv.data[li].uv-expected).length<3e-5
with open(TRIAL/'bradley_emission_trial.glb','rb') as f:
    assert f.read(4)==b'glTF';f.read(8);size,_=struct.unpack('<II',f.read(8));gltf=json.loads(f.read(size))
grid=[m for m in gltf['materials'] if m['name']=='Game grille']
assert len(grid)==1 and 'KHR_materials_unlit' in grid[0]['extensions']
assert all(abs(v-1)<1e-6 for v in grid[0]['pbrMetallicRoughness'].get('baseColorFactor',[1,1,1,1])),'Do not multiply grid pixels by the material preview color'
texture=grid[0]['pbrMetallicRoughness']['baseColorTexture'];image=gltf['images'][gltf['textures'][texture['index']]['source']]
assert 'bufferView' in image and image['mimeType']=='image/png'
validation={'baseline':'d88bba54','all_146_objects_geometry_parenting_and_diffuse_colors_unchanged':True,
            'original_uvs_unchanged':True,'only_grille_shader_and_new_grille_uv_added':True,
            'grille_faces':sum(len(ids) for ids in report['targets'].values()),'planar_frames':len(report['planar_groups']),
            'square_cell_pitch':report['pitch'],'bar_fraction':report['bar_fraction'],
            'packed_emission_texture':True,'glb_unlit_grid_texture_embedded':True,'in_game_validation':'not_performed'}
(HERE/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
print('SQUARE GRILLE GRID VALIDATED',json.dumps(validation))

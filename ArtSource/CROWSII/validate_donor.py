"""Check the mounted weapon against every canonical QCB donor mesh/material."""
from pathlib import Path
import bpy,json,hashlib
from mathutils import Matrix,Vector

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';meta=json.loads((OUT/'metadata.json').read_text())
donor=Path(meta['weapon_donor']);assert hashlib.sha256(donor.read_bytes()).hexdigest()==meta['weapon_donor_sha256']
bpy.ops.wm.open_mainfile(filepath=str(donor));bpy.context.view_layer.update();original={}


def color(obj,face):
    mat=obj.data.materials[face.material_index]
    return tuple(next(n for n in mat.node_tree.nodes if n.type=='EMISSION').inputs[0].default_value)


for o in bpy.context.scene.objects:
    if o.type!='MESH' or o.get('pose')!='gun' or o.get('layer') not in ['body','barrel']:continue
    original[o.name]=dict(layer=o['layer'],positions=[o.matrix_world@v.co for v in o.data.vertices],
                          faces=[(list(p.vertices),color(o,p)) for p in o.data.polygons],
                          uv={layer.name:[tuple(v.uv) for v in layer.data] for layer in o.data.uv_layers},
                          qcb=o.get('art_addition')=='QCB visual kit')

bpy.ops.wm.open_mainfile(filepath=str(OUT/'crows_ii_m2.blend'));bpy.context.view_layer.update();transform=Matrix(meta['weapon_mount_transform'])
scales=[transform.to_3x3().col[i].length for i in range(3)];assert max(scales)-min(scales)<1e-6
seen=set();vertices=faces=uv_loops=qcb=0
for o in bpy.context.scene.objects:
    if o.type!='MESH':continue
    assert o.get('source_object') not in meta['replaced_source_weapon_meshes']
    if 'donor_object' not in o:continue
    key=o['donor_object'];assert key not in seen;seen.add(key);src=original[key]
    assert o['layer']==src['layer'] and len(o.data.vertices)==len(src['positions']) and len(o.data.polygons)==len(src['faces'])
    for v,p in zip(o.data.vertices,src['positions']):assert (o.matrix_world@v.co-transform@p).length<2e-6,(key,v.index)
    for p,(indices,rgb) in zip(o.data.polygons,src['faces']):
        assert list(p.vertices)==indices
        assert max(abs(a-b) for a,b in zip(color(o,p),rgb))<1e-7,(key,p.index,'repainted donor')
    assert {v.name for v in o.data.uv_layers}==set(src['uv'])
    for layer in o.data.uv_layers:
        assert len(layer.data)==len(src['uv'][layer.name])
        for v,expected in zip(layer.data,src['uv'][layer.name]):assert (v.uv-Vector(expected)).length<1e-7
        uv_loops+=len(layer.data)
    assert o.get('pose')=='gun';vertices+=len(o.data.vertices);faces+=len(o.data.polygons);qcb+=src['qcb']
assert seen==set(original) and qcb==8
assert not any(o.type=='LIGHT' for o in bpy.context.scene.objects)
result=dict(passed=True,donor_sha256=meta['weapon_donor_sha256'],weapon_variant='M2HB QCB',donor_objects=len(seen),qcb_objects=qcb,vertices_verified=vertices,faces_verified=faces,uv_loops_verified=uv_loops,all_face_emission_colors_identical=True,uniform_scale=scales[0],old_weapon_removed=True,donor_tripod_and_ammunition_excluded=True,station_pivot=meta['pivot'],in_game_tested=False)
(OUT/'donor_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('CROWS_QCB_DONOR_PASS: every donor vertex, face, UV and face color identical under the single mounting transform; 8 QCB additions; old weapon removed')

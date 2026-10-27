"""Compare the saved layers against the actual ext models, polygon by polygon."""
from pathlib import Path
from collections import defaultdict
import bpy,json,hashlib,math
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';results={}
dimensions=json.loads((ROOT/'scale_reference.json').read_text())
for kind in ['M2HB','M1917']:
    meta=json.loads((OUT/kind/'metadata.json').read_text());source=Path(meta['source'])
    assert hashlib.sha256(source.read_bytes()).hexdigest()==meta['source_sha256']
    if source.suffix=='.blend':bpy.ops.wm.open_mainfile(filepath=str(source))
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(source))
    original={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        original[o.name]=dict(vertices=[o.matrix_world@v.co for v in o.data.vertices],faces=[dict(indices=list(p.vertices),uv={uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers}) for p in o.data.polygons])
    bpy.ops.wm.open_mainfile(filepath=str(OUT/kind/'model.blend'));scene=bpy.context.scene
    assert scene.camera.data.type=='ORTHO' and (scene.camera.rotation_euler.to_quaternion()@Vector((0,0,-1))-Vector((0,0,-1))).length<1e-6
    assert scene.render.film_transparent and not any(o.type=='LIGHT' for o in scene.objects)
    assert scene.render.resolution_x==scene.render.resolution_y==2048
    seen=defaultdict(set);vertex_ids=defaultdict(set);uv_loops=0;flat_colors=defaultdict(set);paint_counts=defaultdict(int)
    meshes=[o for o in scene.objects if o.type=='MESH']
    for o in meshes:
        assert o['source_object'] in original
        src=original[o['source_object']];flat=o['source_transform'];matrix=Matrix([flat[i:i+4] for i in range(0,16,4)])
        expected_det=meta['mount_fore_aft_fit'] if kind=='M1917' and o['layer']=='mount' else 1
        assert abs(matrix.to_3x3().determinant()-expected_det)<1e-6
        vi=o.data.attributes['source_vertex'];fi=o.data.attributes['source_face']
        for v,identifier in zip(o.data.vertices,vi.data):
            assert (o.matrix_world@v.co-matrix@src['vertices'][identifier.value]).length<2e-6,(kind,o.name,v.index)
            vertex_ids[o['source_object']].add(identifier.value)
        for p,identifier in zip(o.data.polygons,fi.data):
            assert identifier.value not in seen[o['source_object']]
            seen[o['source_object']].add(identifier.value);face=src['faces'][identifier.value]
            assert [vi.data[i].value for i in p.vertices]==face['indices']
            assert set(face['uv'])=={uv.name for uv in o.data.uv_layers}
            material=o.data.materials[p.material_index];paint_counts[material.name]+=1
            color=tuple(round(c,6) for c in next(n for n in material.node_tree.nodes if n.type=='EMISSION').inputs[0].default_value)
            target='tripod' if o['layer']=='mount' else 'barrel' if kind=='M2HB' and o['layer']=='barrel' else 'water_jacket' if material.name.startswith('fde_') else None
            if target:flat_colors[target].add(color)
            for uv in o.data.uv_layers:
                for index,expected in zip(p.loop_indices,face['uv'][uv.name]):
                    assert (uv.data[index].uv-Vector(expected)).length<1e-6
                    uv_loops+=1
        for material in o.data.materials:
            assert material and material.use_nodes and any(n.type=='EMISSION' for n in material.node_tree.nodes)
            assert not any(n.type=='TEX_IMAGE' for n in material.node_tree.nodes)
    for name,src in original.items():
        assert seen[name]==set(range(len(src['faces']))),(kind,name,'missing polygons')
        assert vertex_ids[name]=={i for f in src['faces'] for i in f['indices']},(kind,name,'missing used vertices')
    assert set(seen)==set(original)
    gun_points=[v.co for o in meshes if o['layer']!='mount' for v in o.data.vertices]
    visible_length=(max(v.y for v in gun_points)-min(v.y for v in gun_points))*meta['model_to_world']
    expected_length=dimensions['baseline_m2_visible_length_tiles']*dimensions['weapon_length_m'][kind]/dimensions['weapon_length_m']['M2HB']
    assert abs(visible_length-expected_length)<1e-6,'Incorrect relative gun scale'
    if kind=='M1917':
        mount_points=[v.co for o in meshes if o['layer']=='mount' for v in o.data.vertices]
        mount_metres=(max(v.y for v in mount_points)-min(v.y for v in mount_points))*dimensions['weapon_length_m'][kind]/meta['model_length']
        assert abs(mount_metres-dimensions['m1917_mount_fore_aft_length_m'])<1e-6,'Incorrect tripod fore-aft size'
    assert all(len(colors)==1 for colors in flat_colors.values()),('Unwanted surface shading',flat_colors)
    assert paint_counts['brass_top']>0 and paint_counts['copper_top']>0
    pose_check={}
    if kind=='M2HB':
        assert paint_counts['link_top']>0
        box=next(o for o in meshes if o['pose']=='ammo_box');lid=next(o for o in meshes if o['pose']=='ammo_lid')
        flat=box['source_transform'];matrix=Matrix([flat[i:i+4] for i in range(0,16,4)])
        assert abs(matrix[0][1])+abs(matrix[1][0])+abs(matrix[2][0])+abs(matrix[2][1])<1e-6,'Ammo box is tilted'
        normal=max(lid.data.polygons,key=lambda p:p.area).normal
        lid_tilt=math.degrees(math.acos(min(1,abs(normal.z))))
        assert lid_tilt<1,'Lid is still open'
        box_points=[v.co for v in box.data.vertices];lid_points=[v.co for v in lid.data.vertices]
        midpoint=lambda points,i:(min(v[i] for v in points)+max(v[i] for v in points))/2
        assert abs(midpoint(box_points,0)-midpoint(lid_points,0))<.04
        assert abs(midpoint(box_points,1)-midpoint(lid_points,1))<.01
        belt=sorted((o for o in meshes if o['pose'].startswith('ammo_belt_')),key=lambda o:o['pose'])
        assert len(belt)==16,'Missing belt cartridges'
        for o in belt:
            counts=defaultdict(int)
            for p in o.data.polygons:counts[o.data.materials[p.material_index].name]+=1
            assert dict(counts)=={'brass_top':206,'copper_top':140,'link_top':261},('Cartridge/link pairing',o.name,dict(counts))
        # Check the actual tail against the box, not just independent valid poses.
        box_lo=Vector([min(v[i] for v in box_points) for i in range(3)])
        box_hi=Vector([max(v[i] for v in box_points) for i in range(3)])
        lid_height=max(lid.data.polygons,key=lambda p:p.area).center.z
        for o in belt[-2:]:
            for v in o.data.vertices:
                assert all(box_lo[i]<v.co[i]<box_hi[i] for i in [0,1]),('Belt tail outside ammo box',o.name,tuple(v.co))
                assert box_lo.z<v.co.z<lid_height,('Belt tail above lid or below box',o.name,tuple(v.co))
        pose_check=dict(ammo_box_level=True,lid_closed=True,lid_surface_tilt_degrees=lid_tilt,cartridge_cases='brass',projectiles='copper',links='dark steel',rigid_cartridge_link_groups=len(belt),terminal_two_cartridges_inside_box=True)
    results[kind]=dict(source=meta['source_relative_to_ext'],source_sha256=meta['source_sha256'],projection='90 degree vertical orthographic',transparent=True,lights=0,
        materials='fixed RGB emission; no image textures',source_objects=len(original),polygons_preserved=sum(len(v) for v in seen.values()),used_source_vertices_preserved=sum(len(v) for v in vertex_ids.values()),uv_loops_preserved=uv_loops,
        added_geometry=False,gun_draw_tiles=meta['gun_draw_tiles'],mount_draw_tiles=meta['mount_draw_tiles'],visible_gun_length_tiles=visible_length,relative_length_to_m2=visible_length/dimensions['baseline_m2_visible_length_tiles'],uniform_surface_color_counts={name:len(colors) for name,colors in flat_colors.items()},**pose_check)
(OUT/'model_validation.json').write_text(json.dumps(dict(passed=True,models=results),indent=2)+'\n')
print('HMG_EXT_VALIDATION_PASS: source polygons/UVs retained; documented transforms; real relative gun lengths and tripod length; vertical cameras; fixed emission',flush=True)

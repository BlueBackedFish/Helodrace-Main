"""Targeted OBJ restoration and a flat replacement for the gun's fabric cover."""
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
SOURCE_RESTORED = ('WT_roll_01','WT_roll_02','WT_roll_03','WT_roll_06','WT_roll_07',
    'WT_ex_decor_02','WT_ex_decor_03','WT_bone_gun','WT_gun_barrel','WT_hatch_02','WT_hatch_03')
REPAIRED = set(SOURCE_RESTORED)|{'WT_ex_decor_01','WT_bone_turret'}
RESTORATION=json.loads((ROOT/'turret_restoration.json').read_text())
ADDED={p['name'] for p in RESTORATION['parts']}

def read_source_parts():
    manifest = json.loads((ROOT/'ReferenceFit/manifest.json').read_text())
    path = Path(manifest['source'])
    assert hashlib.sha256(path.read_bytes()).hexdigest() == manifest['source_sha256']
    vertices = []; parts = {}; group = ''; material = ''
    for line in path.open(encoding='utf-8'):
        tokens = line.split()
        if not tokens: continue
        if tokens[0] == 'v':
            x,y,z = map(float,tokens[1:4]); vertices.append((-z,-x,y+.012))
        elif tokens[0] == 'g': group = 'WT_'+tokens[1]
        elif tokens[0] == 'usemtl': material = tokens[1]
        elif tokens[0] == 'f' and group in REPAIRED:
            ids = [int(t.split('/')[0]) for t in tokens[1:]]
            parts.setdefault(group,[]).append(([vertices[i-1 if i>0 else i] for i in ids],material))
    return parts

def apply_repairs():
    parts = read_source_parts(); report = {}
    assert len(parts['WT_bone_turret'])==RESTORATION['source_face_count']
    def material(name,rgb):
        m=bpy.data.materials.get(name)
        if m:return m
        m=bpy.data.materials.new(name);m.use_nodes=True
        color=tuple((v/255/12.92 if v/255<=.04045 else ((v/255+.055)/1.055)**2.4) for v in rgb)+(1,)
        m.diffuse_color=color;nodes=m.node_tree.nodes;nodes.clear()
        e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=color
        out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);return m
    palette=[material('Restored paint '+str(g),[v*g for v in (221,207,172)]) for g in (.995,.88,.73,.48)]
    palette += [material('Restored Optics',(38,51,53)),material('Restored dark metal',(66,63,53))]

    def restore_mesh(obj,items):
        # Source-space positions retain every source triangle, including small ends.
        lookup = {}; points = []; faces = []
        for coordinates,_ in items:
            face=[]
            for co in coordinates:
                if co not in lookup:
                    lookup[co]=len(points); points.append(obj.matrix_world.inverted()@Vector(co))
                face.append(lookup[co])
            faces.append(face)
        mesh=bpy.data.meshes.new(obj.name+' source restored'); mesh.from_pydata(points,[],faces); mesh.update()
        for mat in palette:mesh.materials.append(mat)
        for p,(_,source_mat) in zip(mesh.polygons,items):
            p.material_index=4 if 'optic' in source_mat or 'dynamic_glass' in source_mat else 5 if 'rifled' in source_mat else 0 if p.normal.z>.6 else 3 if p.normal.z<-.4 else 1 if abs(p.normal.y)>.6 else 2
        obj.data=mesh
    for name in SOURCE_RESTORED:
        obj=bpy.data.objects[name];old_faces=len(obj.data.polygons);restore_mesh(obj,parts[name])
        report[name]={'before_faces':old_faces,'after_faces':len(obj.data.polygons),'method':'exact OBJ group positions and faces','parent':obj.parent.name}

    # Recover the embedded brackets and smoke pods without keeping their reduced
    # remnants on top of the source replacements (which would cause flickering).
    from mesh_regions import regions,position_key
    obj=bpy.data.objects['WT_bone_turret'];me=obj.data;old_faces=len(me.polygons)
    source_sets={p['name']:{position_key(co) for i in p['source_polygon_indices'] for co in parts[obj.name][i][0]} for p in RESTORATION['parts']}
    remove=set();replaced=[]
    for ids in regions(me):
        coords={position_key(obj.matrix_world@me.vertices[i].co) for pi in ids for i in me.polygons[pi].vertices}
        name,overlap=max(((name,len(coords&points)) for name,points in source_sets.items()),key=lambda v:v[1])
        if overlap>=3 and overlap/max(len(coords),1)>.45:remove.update(ids);replaced.append(name)
    keep=[p for p in me.polygons if p.index not in remove];used=sorted({i for p in keep for i in p.vertices});mapping={v:i for i,v in enumerate(used)}
    new=bpy.data.meshes.new('Turret shell without restored-region duplicates')
    new.from_pydata([me.vertices[i].co[:] for i in used],[],[[mapping[i] for i in p.vertices] for p in keep]);new.update()
    for m in me.materials:new.materials.append(m)
    for a,b in zip(new.polygons,keep):a.material_index=b.material_index
    obj.data=new
    report[obj.name]={'before_faces':old_faces,'after_faces':len(new.polygons),'method':'remove only reduced remnants of restored source regions','removed_faces':len(remove),'replaced_regions':replaced,'parent':obj.parent.name}
    for record in RESTORATION['parts']:
        obj=bpy.data.objects.new(record['name'],bpy.data.meshes.new(record['name']));bpy.context.scene.collection.objects.link(obj)
        obj.parent=bpy.data.objects['TURRET_YAW'];obj.matrix_parent_inverse=obj.parent.matrix_world.inverted()
        bpy.context.view_layer.update()
        obj['art_role']=record['role'];obj['source_group']='bone_turret';obj['source_component']=record['source_component']
        restore_mesh(obj,[parts['WT_bone_turret'][i] for i in record['source_polygon_indices']])
        report[obj.name]={'before_faces':0,'after_faces':len(obj.data.polygons),'method':'exact embedded OBJ component','source_component':record['source_component'],'parent':obj.parent.name}
    obj=bpy.data.objects['WT_ex_decor_01']; coordinates=np.array([co for face,_ in parts[obj.name] for co in face])
    # Fit the cloth's mounting plane, dropping wrinkles while preserving its footprint.
    x0,y0=coordinates[:,:2].min(axis=0); x1,y1=coordinates[:,:2].max(axis=0)
    a,b,c=np.linalg.lstsq(np.column_stack((coordinates[:,:2],np.ones(len(coordinates)))),coordinates[:,2],rcond=None)[0]
    normal=Vector((-a,-b,1)).normalized(); thickness=.008
    top=[Vector((x,y,a*x+b*y+c)) for x,y in ((x0,y0),(x1,y0),(x1,y1),(x0,y1))]
    world=top+[v-normal*thickness for v in top]
    local=[obj.matrix_world.inverted()@v for v in world]
    mesh=bpy.data.meshes.new('Flat gun shroud');mesh.from_pydata(local,[],[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]);mesh.update()
    mesh.materials.append(obj.data.materials[0]);old_faces=len(obj.data.polygons);obj.data=mesh
    report[obj.name]={'before_faces':old_faces,'after_faces':6,'method':'planar thin cover; source XY footprint and fitted mounting slope','thickness':thickness,'plane':[float(a),float(b),float(c)],'parent':obj.parent.name}
    return report

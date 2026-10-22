"""Targeted OBJ restoration and a flat replacement for the gun's fabric cover."""
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
REPAIRED = {'WT_roll_01', 'WT_roll_02', 'WT_ex_decor_01'}

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
    for name in ('WT_roll_01','WT_roll_02'):
        obj = bpy.data.objects[name]; old_faces = len(obj.data.polygons)
        old_materials = list(obj.data.materials)
        if not any('Optics' in m.name for m in old_materials):
            optics=bpy.data.materials.get('Restored Optics')
            if optics is None:
                optics=bpy.data.materials.new('Restored Optics');optics.use_nodes=True
                color=tuple(((v/255+.055)/1.055)**2.4 for v in (38,51,53))+(1,)
                optics.diffuse_color=color;nodes=optics.node_tree.nodes;nodes.clear()
                emission=nodes.new('ShaderNodeEmission');emission.inputs['Color'].default_value=color
                out=nodes.new('ShaderNodeOutputMaterial');optics.node_tree.links.new(emission.outputs[0],out.inputs[0])
            old_materials.append(optics)
        # Source-space positions retain every source triangle, including small ends.
        lookup = {}; points = []; faces = []
        for coordinates,_ in parts[name]:
            face=[]
            for co in coordinates:
                if co not in lookup:
                    lookup[co]=len(points); points.append(obj.matrix_world.inverted()@Vector(co))
                face.append(lookup[co])
            faces.append(face)
        mesh=bpy.data.meshes.new(name+' source restored'); mesh.from_pydata(points,[],faces); mesh.update()
        for mat in old_materials: mesh.materials.append(mat)
        for p,(_,source_mat) in zip(mesh.polygons,parts[name]):
            # Keep the original dark optical material when present in the OBJ.
            optics='optic' in source_mat or 'dynamic_glass' in source_mat
            choices=[i for i,m in enumerate(old_materials) if ('Optics' in m.name)==optics]
            if not optics:
                direction='upper' if p.normal.z>.6 else 'underside' if p.normal.z<-.4 else 'fore' if abs(p.normal.y)>.6 else 'side'
                choices=[i for i in choices if direction in old_materials[i].name] or choices
            assert choices,(name,source_mat)
            p.material_index=choices[0]
        obj.data=mesh
        report[name]={'before_faces':old_faces,'after_faces':len(faces),'method':'exact OBJ group positions and faces','parent':obj.parent.name}
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

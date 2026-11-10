"""Trace physical upper-skin creases without source-image scratches or decals."""
import bpy,math
from mathutils import Vector


def prepare(scene,obj,material,diameter=.020):
    mesh=obj.data;world=obj.matrix_world;normal_matrix=world.to_3x3()
    records={}
    for face in mesh.polygons:
        if face.material_index==1:continue
        normal=(normal_matrix@face.normal).normalized()
        indices=list(face.vertices)
        for a,b in zip(indices,indices[1:]+indices[:1]):
            pa,pb=world@mesh.vertices[a].co,world@mesh.vertices[b].co
            key=tuple(sorted([tuple(round(v,4) for v in pa),tuple(round(v,4) for v in pb)]))
            item=records.setdefault(key,dict(points=(pa,pb),normals=[]));item['normals'].append(normal)
    selected=[]
    for item in records.values():
        pa,pb=item['points'];normals=item['normals']
        if (pa-pb).length<.18 or not any(n.z>.7 for n in normals):continue
        crease=len(normals)==1 or any(a.dot(b)<math.cos(math.radians(35)) for a in normals for b in normals)
        if crease:selected.append((pa,pb))
    data=bpy.data.curves.new('Physical upper-skin joint lines','CURVE')
    data.dimensions='3D';data.resolution_u=1;data.bevel_depth=diameter/2;data.bevel_resolution=1;data.use_fill_caps=True
    for pa,pb in selected:
        spline=data.splines.new('POLY');spline.points.add(1)
        for point,co in zip(spline.points,[pa,pb]):point.co=(*tuple(co+Vector((0,0,.003))),1)
    line=bpy.data.objects.new('DETAIL physical upper-skin creases',data);scene.collection.objects.link(line);data.materials.append(material)
    line.name='DETAIL joints '+obj.name
    line.parent=obj;line.matrix_parent_inverse=obj.matrix_world.inverted()
    return dict(object=line.name,source='Actual source-mesh boundary/crease edges; source faces unchanged',
        segments=len(selected),diameter_m=diameter,minimum_edge_length_m=.18,minimum_dihedral_degrees=35)

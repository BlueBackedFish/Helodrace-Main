"""Vehicle-specific face bindings and coherent paint regions; no geometry edits."""
from collections import defaultdict
from math import cos,radians
from mathutils import Vector
from mesh_regions import position_key

def select_faces(obj,target):
    if target['selection']=='polygon_indices':
        ids=target['polygon_indices'];assert all(0<=i<len(obj.data.polygons) for i in ids)
        return ids
    assert target['selection']=='upward_faces'
    return [p.index for p in obj.data.polygons if p.normal.z>=target['minimum_normal_z']]

def smooth_regions(mesh,degrees):
    edge_faces=defaultdict(list);adjacent=defaultdict(set)
    for p in mesh.polygons:
        for a,b in zip(p.vertices,list(p.vertices[1:])+[p.vertices[0]]):
            edge_faces[tuple(sorted((position_key(mesh.vertices[a].co),position_key(mesh.vertices[b].co))))].append(p.index)
    limit=cos(radians(degrees))
    for faces in edge_faces.values():
        for i in faces:
            for j in faces:
                if i!=j and mesh.polygons[i].normal.dot(mesh.polygons[j].normal)>=limit:
                    adjacent[i].add(j)
    remaining=set(range(len(mesh.polygons)));groups=[]
    while remaining:
        seed=min(remaining);remaining.remove(seed);group=[seed];queue=[seed]
        while queue:
            for j in adjacent[queue.pop()] & remaining:
                remaining.remove(j);group.append(j);queue.append(j)
        groups.append(group)
    return groups

def authored_values(obj,corrections):
    values={};report=[];repair=corrections['uniform_linkage']
    if obj.name==repair['object']:
        groups=smooth_regions(obj.data,repair['smooth_angle_degrees'])
        for ids in groups:
            normal=sum((obj.data.polygons[i].normal*obj.data.polygons[i].area for i in ids),Vector())
            if normal.length:normal.normalize()
            value=repair['top'] if normal.z>=repair['upward_threshold'] else repair['underside'] if normal.z<=repair['downward_threshold'] else repair['side']
            for i in ids:values[i]=value
            report.append({'name':'coherent_linkage_surface','faces':ids,'display_luminance':value})
    for target in corrections['bright_surfaces']:
        if obj.name!=target['object']:continue
        ids=select_faces(obj,target);assert ids
        for i in ids:values[i]=target['display_luminance']
        report.append(dict(target,faces=ids))
    return values,report

def allowed_faces(scene,corrections):
    allowed={}
    for obj in scene.objects:
        if obj.type!='MESH':continue
        ids=set(authored_values(obj,corrections)[0])
        for p in obj.data.polygons:
            if obj.data.materials[p.material_index].get('paint_role')==corrections['handle']['paint_role']:ids.add(p.index)
        target=corrections['reverted_wrong_target']
        if obj.name==target['object']:
            ids.update(p.index for p in obj.data.polygons if p.normal.z>=target['minimum_normal_z'])
        if ids:allowed[obj.name]=sorted(ids)
    return allowed

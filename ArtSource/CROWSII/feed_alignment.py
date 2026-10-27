"""Fit the existing chute rings to the donor feedway; leave its box end fixed."""
from collections import Counter,defaultdict,deque
import numpy as np
from mathutils import Vector,Matrix,Quaternion


def align(obj,material,target,width,blend_rings):
    mesh=obj.data;faces=[p for p in mesh.polygons if mesh.materials[p.material_index]==material]
    indices={i for p in faces for i in p.vertices};key=lambda i:tuple(round(c,5) for c in (obj.matrix_world@mesh.vertices[i].co))
    points={key(i):obj.matrix_world@mesh.vertices[i].co for i in indices};edges=Counter();graph=defaultdict(set)
    for p in faces:
        for a,b in zip(p.vertices,list(p.vertices[1:])+[p.vertices[0]]):
            a,b=key(a),key(b);edges[tuple(sorted((a,b)))]+=1;graph[a].add(b);graph[b].add(a)
    boundary=defaultdict(set)
    for (a,b),count in edges.items():
        if count==1:boundary[a].add(b);boundary[b].add(a)
    ends=[];remaining=set(boundary)
    while remaining:
        seed=remaining.pop();end={seed};queue=[seed]
        while queue:
            for neighbor in boundary[queue.pop()]:
                if neighbor not in end:end.add(neighbor);remaining.discard(neighbor);queue.append(neighbor)
        ends.append(end)
    assert len(ends)==2 and all(len(end)==8 for end in ends),'Unexpected source chute topology'
    mouth=max(ends,key=lambda end:sum(points[k].x for k in end)/len(end));distance={k:0 for k in mouth};queue=deque(mouth)
    while queue:
        k=queue.popleft()
        for neighbor in graph[k]:
            if neighbor not in distance:distance[neighbor]=distance[k]+1;queue.append(neighbor)
    rings=defaultdict(list)
    for k,d in distance.items():rings[d].append(k)
    assert len(distance)==len(points) and all(len(r)==8 for r in rings.values()),'Chute ring traversal failed'
    def center(keys):return sum((points[k] for k in keys),Vector())/len(keys)
    old_center=center(mouth);delta=target-old_center;new={};fixed=0;vertex_keys={i:key(i) for i in indices}
    for index,keys in rings.items():
        c=center(keys);t=max(0,1-index/blend_rings);weight=t*t*(3-2*t)
        if not weight:
            for k in keys:new[k]=points[k]
            fixed+=len(keys);continue
        a=np.array([tuple(points[k]-c) for k in keys]);values,vectors=np.linalg.eigh(a.T@a)
        u=Vector(vectors[:,2]);v=Vector(vectors[:,1])
        if u.y<0:u=-u
        if v.z<0:v=-v
        n=u.cross(v).normalized();basis=Matrix((n,u,v)).transposed();q=basis.to_quaternion();rotation=q.slerp(Quaternion(),weight)
        local={k:basis.transposed()@(points[k]-c) for k in keys};old_width=max(p.y for p in local.values())-min(p.y for p in local.values())
        stretch=1+weight*(width/old_width-1);shifted=c+weight*delta
        for k,p in local.items():new[k]=shifted+rotation@Vector((p.x,p.y*stretch,p.z))
    inverse=obj.matrix_world.inverted()
    for i in indices:mesh.vertices[i].co=inverse@new[vertex_keys[i]]
    mesh.update()
    actual=sum((new[k] for k in mouth),Vector())/len(mouth)
    mouth_width=max(new[k].y for k in mouth)-min(new[k].y for k in mouth)
    plane_error=max(abs(new[k].x-target.x) for k in mouth)
    assert (actual-target).length<1e-6 and abs(mouth_width-width)<1e-6 and plane_error<1e-6
    return dict(passed=True,source_faces=len(faces),source_unique_vertices=len(points),rings=len(rings),blend_rings=blend_rings,
                original_mouth_center=list(old_center),target_mouth_center=list(target),actual_mouth_center=list(actual),
                target_width=width,actual_width=mouth_width,receiver_plane_error=plane_error,unchanged_chute_vertices=fixed,
                ammo_end_unchanged=True,only_chute_vertices_changed=True,source_faces_and_uv_preserved=True,
                terminal_vertex_indices=sorted(i for i in indices if vertex_keys[i] in mouth))

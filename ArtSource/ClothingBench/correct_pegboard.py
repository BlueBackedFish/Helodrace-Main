"""Focused correction of the existing front board into an above-table pegboard."""
import bpy
import os
import math
import json
import hashlib
import numpy as np
from mathutils import Vector

OUT=os.path.dirname(os.path.abspath(__file__))

def snapshot():
    result={}
    for obj in bpy.context.scene.objects:
        if obj.name.startswith(('12_','13_','16_')):continue
        values={'matrix':[list(row) for row in obj.matrix_world]}
        if obj.type=='MESH':
            values.update(vertices=[list(v.co) for v in obj.data.vertices],
                          faces=[list(p.vertices) for p in obj.data.polygons],
                          assignments=[p.material_index for p in obj.data.polygons],
                          materials=[(m.name,list(m.diffuse_color)) for m in obj.data.materials])
        elif obj.type=='CAMERA':values.update(type=obj.data.type,scale=obj.data.ortho_scale)
        result[obj.name]=hashlib.sha256(json.dumps(values,sort_keys=True).encode()).hexdigest()
    return result

def correct_pegboard():
    scene=bpy.context.scene
    if scene.get('rear_pegboard_correction'):return
    before=snapshot()
    old=bpy.data.objects['12_Front_hanging_panel']
    material=old.data.materials[0]
    bpy.data.objects.remove(old,do_unlink=True)

    # Twelve actual square through-holes: six columns, two rows. A single thin
    # mesh, including hole walls, provides a consistent board in every view.
    x0,x1=-.52,1.38
    z0,z1=1.32,1.82
    yc=.462;thickness=.036
    holes=[(x,z,.095) for x in [-.29,.01,.31,.61,.91,1.21] for z in [1.45,1.68]]
    xs=sorted(set([x0,x1]+[x+d*s/2 for x,z,s in holes for d in [-1,1]]))
    zs=sorted(set([z0,z1]+[z+d*s/2 for x,z,s in holes for d in [-1,1]]))
    nx,nz=len(xs),len(zs);layer=nx*nz
    vertices=[(x,y,z) for y in [yc-thickness/2,yc+thickness/2] for z in zs for x in xs]
    faces=[];edges={}
    for j in range(nz-1):
        for i in range(nx-1):
            mx=(xs[i]+xs[i+1])/2;mz=(zs[j]+zs[j+1])/2
            if any(abs(mx-x)<s/2 and abs(mz-z)<s/2 for x,z,s in holes):continue
            face=(j*nx+i,j*nx+i+1,(j+1)*nx+i+1,(j+1)*nx+i)
            faces.append(face);faces.append(tuple(v+layer for v in reversed(face)))
            for a,b in zip(face,face[1:]+face[:1]):
                edge=tuple(sorted((a,b)));edges[edge]=edges.get(edge,0)+1
    for (a,b),count in edges.items():
        if count==1:faces.append((a,b,b+layer,a+layer))
    data=bpy.data.meshes.new('Pegboard_twelve_through_holes')
    data.from_pydata(vertices,[],faces);data.update()
    board=bpy.data.objects.new('12_Rear_pegboard',data);scene.collection.objects.link(board)
    data.materials.append(material)
    board['perforation_grid']='6 columns x 2 rows, real through-holes'
    board['placement']='Rear +Y edge; board entirely above the tabletop'
    # Two narrow mounts connect the board to the rear edge; the raised lower
    # edge keeps the cutting surface visible from the rear North camera.
    for x in [-.46,1.32]:
        bpy.ops.mesh.primitive_cube_add(size=1,location=(x,yc,1.14))
        post=bpy.context.object;post.name='16_Pegboard_mount'
        post.dimensions=(.055,.048,.38)
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        post.data.materials.append(material)
    # Move the existing cloth, retaining its material and simple eight vertices.
    cloth=bpy.data.objects['13_Hanging_canvas']
    cloth.rotation_euler=(0,0,0)
    cloth.location=(1.01,yc,1.595)
    cloth.dimensions=(.45,.060,.45)
    bpy.context.view_layer.update()
    assert snapshot()==before,'Unrelated geometry, materials or cameras changed'
    scene['rear_pegboard_correction']=True
    with open(os.path.join(OUT,'pegboard_correction_validation.json'),'w') as f:
        json.dump({'unrelated_objects_and_cameras_unchanged':True,
                   'preserved_objects':list(before),'rear_y':yc,'board_z_range':[z0,z1],
                   'tabletop_z':.95,'through_holes':12,'front_panel_removed':True},f,indent=2)

if __name__=='__main__':
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'clothing_bench.blend'))
    correct_pegboard()
    scene=bpy.context.scene
    target=Vector(bpy.data.objects['Camera_South']['target_center'])
    scale=bpy.data.objects['Camera_South'].data.ortho_scale
    with open(os.path.join(OUT,'build_clothing_bench.py'),encoding='utf-8') as f:source=f.read()
    exec(compile(source[source.index('# Rendering and export:'):],'pegboard_export','exec'))

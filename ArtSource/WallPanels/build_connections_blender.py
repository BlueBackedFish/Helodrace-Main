"""Blender background: union-extruded, UV mapped wall junctions, all 16 NESW masks."""
import bpy, os, math, json
from mathutils import Vector
ROOT=os.path.dirname(os.path.abspath(__file__))
SOURCE=os.path.join(ROOT,'Blender','HD_StyrofoamSandwichWall.blend')
OUT=os.path.join(ROOT,'Connections')
RUNTIME=os.path.join(ROOT,'Runtime')
os.makedirs(OUT,exist_ok=True)
os.makedirs(RUNTIME,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=SOURCE)
old=bpy.data.collections.get('Sandwich wall | one-cell module')
for ob in list(old.objects):
    bpy.data.objects.remove(ob,do_unlink=True)

# One atlas, one runtime draw per wall. Front metal / foam / flashing / gasket.
S=512; A=1024
atlas=bpy.data.images.new('HD_SandwichWall_Atlas',width=A,height=A,alpha=True)
metal=bpy.data.images.get('HD_SandwichWall_Metal_BaseColor')
foam=bpy.data.images.get('HD_SandwichWall_EPS_BaseColor')
mp=list(metal.pixels); fp=list(foam.pixels)
pix=[1.0]*(A*A*4)
for y in range(A):
    for x in range(A):
        q=(x//S)+2*(y//S)
        i=((y%S)*S+x%S)*4
        col=mp[i:i+4] if q==0 else fp[i:i+4] if q==1 else [.53,.57,.58,1] if q==2 else [.23,.26,.27,1]
        idx=(y*A+x)*4;pix[idx:idx+4]=col
atlas.pixels.foreach_set(pix)
atlas.filepath_raw=os.path.join(RUNTIME,'HD_SandwichWall_Atlas.png')
atlas.file_format='PNG';atlas.save()
mat=bpy.data.materials.new('Unified game atlas')
mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF')
bs.inputs['Roughness'].default_value=.8
node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=atlas
mat.node_tree.links.new(node.outputs['Color'],bs.inputs['Base Color'])

r=.09;h=1.25
coords=[-.5,-r,r,.5]
manifest=[]
gallery=bpy.data.collections.new('All sixteen game connection masks')
bpy.context.scene.collection.children.link(gallery)

for mask in range(16):
    # NESW bits: 1 north(+Y), 2 east(+X), 4 south, 8 west.
    active={(1,1)}
    if mask==0: active.update({(0,1),(2,1)})
    if mask&1:active.add((1,2))
    if mask&2:active.add((2,1))
    if mask&4:active.add((1,0))
    if mask&8:active.add((0,1))
    vs=[];faces=[];slots=[];uvs=[]
    def quad(points,slot,uv):
        start=len(vs);vs.extend(points);faces.append(tuple(range(start,start+4)));slots.append(slot);uvs.append(uv)
    square=[(0,0),(1,0),(1,1),(0,1)]
    for i,j in sorted(active):
        x0,x1=coords[i:i+2];y0,y1=coords[j:j+2]
        quad([(x0,y0,h),(x1,y0,h),(x1,y1,h),(x0,y1,h)],2,square)
        quad([(x0,y1,0),(x1,y1,0),(x1,y0,0),(x0,y0,0)],2,square)
        for di,dj,a,b in [(-1,0,(x0,y1),(x0,y0)),(1,0,(x1,y0),(x1,y1)),(0,-1,(x0,y0),(x1,y0)),(0,1,(x1,y1),(x0,y1))]:
            if (i+di,j+dj) in active:continue
            # Thin terminal faces expose foam, long sides show ribbed metal.
            length=math.dist(a,b)
            # Hub subfaces on a continuous perpendicular run are metal sides,
            # not exposed foam. This avoids a white stripe halfway along walls.
            continuous = bool(mask&5) if di else bool(mask&10) or mask==0
            edge = abs(a[0])>.49 if di else abs(a[1])>.49
            iscut=length<.181 and (not continuous or edge)
            slot=1 if iscut else 0
            quad([(*a,0),(*b,0),(*b,h),(*a,h)],slot,[(0,0),(length,0),(length,1),(0,1)])
    mesh=bpy.data.meshes.new('Union outline %02d'%mask)
    mesh.from_pydata(vs,[],faces);mesh.materials.append(mat)
    uv=mesh.uv_layers.new(name='Game atlas UV')
    for p,slot,uvface in zip(mesh.polygons,slots,uvs):
        ox=(slot%2)*.5;oy=(slot//2)*.5
        for idx,local in zip(p.loop_indices,uvface):
            # Keep within atlas quadrant with two-pixel padding.
            uv.data[idx].uv=(ox+.002+local[0]*.496,oy+.002+local[1]*.496)
    obj=bpy.data.objects.new('Wall_NESW_%02d'%mask,mesh);gallery.objects.link(obj)
    mesh.calc_loop_triangles()
    # The actual 1.7 ObjMeshLoader reflects OBJ X and reverses triangle winding.
    # Pre-reflect X as well as swapping Blender Y/Z so loaded ports match NESW.
    lines=['# Blender-authored; pre-adapted for Waylith 1.7 ObjMeshLoader; NESW mask '+str(mask)]
    for v in mesh.vertices:lines.append('v %.7f %.7f %.7f'%(-v.co.x,v.co.z,v.co.y))
    loopvt={}
    for k,l in enumerate(mesh.loops):
        val=uv.data[k].uv;lines.append('vt %.7f %.7f'%(val.x,val.y));loopvt[k]=k+1
    for p in mesh.polygons:
        n=p.normal;lines.append('vn %.7f %.7f %.7f'%(-n.x,n.z,n.y))
    for tri in mesh.loop_triangles:
        ni=tri.polygon_index+1
        li=list(tri.loops)
        parts=[]
        for k in li:parts.append('%d/%d/%d'%(mesh.loops[k].vertex_index+1,loopvt[k],ni))
        lines.append('f '+' '.join(parts))
    path=os.path.join(RUNTIME,'Wall_%02d.obj'%mask)
    with open(path,'w',encoding='utf-8') as f:f.write('\n'.join(lines)+'\n')
    # Each connection arm reaches exactly the cell boundary; no internal walls.
    assert len(mesh.polygons)==len(active)*2+sum((i+di,j+dj) not in active for i,j in active for di,dj in [(-1,0),(1,0),(0,-1),(0,1)])
    assert all(p.area>0 for p in mesh.polygons)
    manifest.append({'mask':mask,'faces':len(mesh.polygons),'triangles':len(mesh.loop_triangles),'filename':os.path.basename(path)})
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'Wall_%02d.glb'%mask),use_selection=True,export_format='GLB')
    obj.location=((mask%4)*1.65-2.475,(mask//4)*1.65-2.475,0)

scene=bpy.context.scene
cam=scene.camera;cam.location=(7,-10,12)
cam.rotation_euler=(Vector((0,0,.5))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=10.0
scene.render.resolution_x=1024;scene.render.resolution_y=1024
scene.cycles.samples=24
scene.render.filepath=os.path.join(OUT,'WallConnections_Gallery.png')
bpy.ops.render.render(write_still=True)
atlas.pack()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'HD_SandwichWall_Connections.blend'))
with open(os.path.join(RUNTIME,'connections.json'),'w',encoding='utf-8') as f:json.dump({'axes':'X/Y-up/Z','bits':'N=1 E=2 S=4 W=8','thickness':.18,'variants':manifest},f,indent=2)
print('CONNECTIONS_COMPLETE',len(manifest))

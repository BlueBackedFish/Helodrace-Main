"""Native-space radome refinement and real recessed external airframe details."""
import bpy,bmesh,math
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
from articulation import bake_native


def mesh_object(scene,origin,name,vertices,faces,materials,indices=None):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    for material in materials:mesh.materials.append(material)
    if indices:
        for face,index in zip(mesh.polygons,indices):face.material_index=index
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    obj.parent=origin;obj.matrix_basis=Matrix.Identity(4)
    return obj


def apply_modifier(obj,modifier):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.modifier_apply(modifier=modifier.name);obj.select_set(False)


def radome(scene,origin,body,material):
    source=body.data.copy();seam=1.57189
    tip=max(v.co.x for v in source.vertices)
    meshes=[]
    for forward in [False,True]:
        mesh=source.copy();bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
            plane_co=(seam,0,0),plane_no=(1,0,0),dist=1e-6,
            clear_inner=forward,clear_outer=not forward)
        bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
        bm.to_mesh(mesh);bm.free();mesh.update();meshes.append(mesh)
    body.data=meshes[0]
    nose=mesh_object(scene,origin,'AIRFRAME smooth radome',[],[],[]);nose.data=meshes[1]
    before=(len(nose.data.vertices),len(nose.data.polygons))
    bm=bmesh.new();bm.from_mesh(nose.data)
    vc=bm.verts.layers.float.new('crease_vert');ec=bm.edges.layers.float.new('crease_edge')
    rim=[v.co.copy() for v in bm.verts if abs(v.co.x-seam)<2e-6]
    for vertex in bm.verts:
        if abs(vertex.co.x-seam)<2e-6:vertex[vc]=1
        if abs(vertex.co.x-tip)<2e-6:vertex[vc]=1
    for edge in bm.edges:
        if all(abs(v.co.x-seam)<2e-6 for v in edge.verts):edge[ec]=1
    bmesh.ops.join_triangles(bm,faces=list(bm.faces),angle_face_threshold=math.pi,
        angle_shape_threshold=math.pi,cmp_uvs=False)
    bm.to_mesh(nose.data);bm.free();nose.data.update()
    mod=nose.modifiers.new('Dense curved radome','SUBSURF');mod.levels=mod.render_levels=4
    apply_modifier(nose,mod)
    # Subdivision rounds the pole inward. Restore the original nose length
    # while leaving the complete attachment loop fixed at the seam plane.
    rounded_tip=max(v.co.x for v in nose.data.vertices)
    for vertex in nose.data.vertices:vertex.co.x=seam+(vertex.co.x-seam)*(tip-seam)/(rounded_tip-seam)
    nose.data.materials.clear();nose.data.materials.append(material)
    for face in nose.data.polygons:face.material_index=0;face.use_smooth=True
    error=max(min((v.co-p).length for v in nose.data.vertices) for p in rim)
    assert error<1e-6 and abs(max(v.co.x for v in nose.data.vertices)-tip)<1e-6
    nose['role']='Refined radome; original rear fitting and overall nose length'
    return dict(object=nose.name,original_nose_vertices=before[0],original_nose_faces=before[1],
        final_vertices=len(nose.data.vertices),final_faces=len(nose.data.polygons),
        subdivision_levels=4,seam_x_native=seam,mounting_loop_error=error,
        original_tip_x=tip,maximum_tip_error=abs(max(v.co.x for v in nose.data.vertices)-tip))


def prepare(scene,origin,materials,ortho_scale,border_pixels,delivery_resolution):
    body=scene.objects['Fuselage'];bake_native(body,origin)
    before=len(body.data.polygons)
    nose=radome(scene,origin,body,materials['radome'])
    tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],
        [list(f.vertices) for f in body.data.polygons])
    # Cache original skin triangles/UVs before cutting. New covers inherit
    # the underlying camouflage instead of stretching one color across them.
    body.data.calc_loop_triangles();source_uv=body.data.uv_layers.active
    uv_name=source_uv.name
    triangles=[([body.data.vertices[i].co.copy() for i in t.vertices],
        [Vector((*source_uv.data[i].uv,0)) for i in t.loops]) for t in body.data.loop_triangles]
    uv_tree=BVHTree.FromPolygons([v.co.copy() for v in body.data.vertices],
        [list(t.vertices) for t in body.data.loop_triangles],all_triangles=True)
    border_width=ortho_scale*border_pixels/delivery_resolution
    records=[]

    def surface(x,y):
        hit=tree.ray_cast(Vector((x,y,2)),Vector((0,0,-1)))
        assert hit[0] is not None,('Detail misses the aircraft skin',x,y)
        return hit[0].z

    def skin_uv(x,y):
        hit,_,index,_=uv_tree.ray_cast(Vector((x,y,2)),Vector((0,0,-1)))
        assert hit is not None,('Cover UV misses the source skin',x,y)
        points,uv=triangles[index]
        return barycentric_transform(hit,*points,*uv).xy

    def pocket(name,points,depth,cover=False,grille=False):
        area=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(points,points[1:]+points[:1]))
        if area<0:points=list(reversed(points))
        n=len(points);top=[Vector((x,y,surface(x,y))) for x,y in points]
        center=sum(top,Vector())/n
        inner=[center+(v-center)*.90 for v in top]
        for vertex in inner:vertex.z-=depth
        # Watertight polygon prism actually removes the existing upper skin.
        vertices=[(p.x,p.y,1.5) for p in top]+[tuple(p-Vector((0,0,depth+.005))) for p in top]
        faces=[list(range(n)),list(reversed(range(n,2*n)))]+[[i,i+n,(i+1)%n+n,(i+1)%n] for i in range(n)]
        cutter=mesh_object(scene,origin,'TEMP '+name,vertices,faces,[materials['vent']])
        cutter.hide_render=True
        mod=body.modifiers.new('Cut '+name,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT'
        mod.object=cutter;mod.use_hole_tolerant=True;mod.material_mode='TRANSFER'
        apply_modifier(body,mod);bpy.data.objects.remove(cutter,do_unlink=True)
        assert len(body.data.polygons)>1000,'Detail cutter damaged the airframe'
        after_tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],[list(f.vertices) for f in body.data.polygons])
        hit=after_tree.ray_cast(Vector((center.x,center.y,2)),Vector((0,0,-1)))[0]
        removed=hit is None or hit.z<surface(center.x,center.y)-depth*.4
        assert removed,('Detail failed to cut the original skin',name)
        well=mesh_object(scene,origin,'DETAIL '+name+' recessed well',list(top)+list(inner),
            [[i,(i+1)%n,(i+1)%n+n,i+n] for i in range(n)]+[list(range(n,2*n))],
            [materials['joint'],materials['vent']],[0]*n+[1])
        well['recess_depth_native']=depth
        # A modeled band lies outside the unchanged cut footprint. Parallel
        # edge offsets maintain constant thickness at concave sawtooth edges.
        outer=[]
        for i,point in enumerate(top):
            incoming=(point-top[i-1]).xy.normalized()
            outgoing=(top[(i+1)%n]-point).xy.normalized()
            a=Vector((incoming.y,-incoming.x));b=Vector((outgoing.y,-outgoing.x))
            miter=(a+b).normalized();distance=min(border_width/max(miter.dot(b),.1),border_width*2)
            xy=point.xy+miter*distance
            outer.append(Vector((xy.x,xy.y,surface(*xy)+.0004)))
        border=mesh_object(scene,origin,'DETAIL '+name+' outer border',
            [p+Vector((0,0,.0004)) for p in top]+outer,
            [[i,(i+1)%n,(i+1)%n+n,i+n] for i in range(n)],[materials['detail_border']])
        border['width_delivery_pixels']=border_pixels
        plate=None
        if cover:
            vertices=[center+(p-center)*.94 for p in top]
            for v in vertices:v.z-=min(.001,depth*.3)
            plate=mesh_object(scene,origin,'DETAIL '+name+' cover',vertices,[list(range(n))],[materials['camo']])
            uv=plate.data.uv_layers.new(name=uv_name)
            for loop in plate.data.loops:
                p=plate.data.vertices[loop.vertex_index].co
                uv.data[loop.index].uv=skin_uv(p.x,p.y)
        if grille:
            # Repeated narrow solid strips sit inside the well, below the
            # outer skin. They are geometry rather than a black texture decal.
            x0=min(v.x for v in inner);x1=max(v.x for v in inner)
            y0=min(v.y for v in inner);y1=max(v.y for v in inner)
            for i in range(1,7):
                x=x0+(x1-x0)*i/7;y=(y0+y1)/2
                # Keep strips clear of the stepped polygon ends.
                half=(y1-y0)*.22;z=surface(x,y)-depth*.55
                mesh_object(scene,origin,f'DETAIL {name} grille {i}',
                    [(x-.0013,y-half,z),(x+.0013,y-half,z),(x+.0013,y+half,z),(x-.0013,y+half,z)],
                    [[0,1,2,3]],[materials['mechanism']])
        records.append(dict(name=name,footprint_native_xy=points,depth_native=depth,
            original_skin_cut_verified=removed,well_mesh=well.name,cover_mesh=plate.name if plate else None,
            grille_bars=6 if grille else 0,border_mesh=border.name,
            border_width_delivery_pixels=border_pixels,border_width_native=border_width))
        return plate

    # Right side is native -Y (image right with the nose pointing up).
    shutter=pocket('cannon muzzle',[(.82,-.27),(.73,-.34),(.61,-.31),(.68,-.245)],.028,cover=True)
    shutter['role']='M61 muzzle shutter; closed in normal flight, hide for open inspection'
    for sign,label in [(1,'port'),(-1,'starboard')]:
        def mirror(points):return [(x,y*sign) for x,y in points]
        pocket(label+' canopy purge screen',mirror([(.72,.165),(.69,.205),(.64,.195),(.61,.22),(.54,.21),(.56,.16),(.61,.17),(.64,.145)]),.012,grille=True)
        pocket(label+' intake shoulder vent',mirror([(.48,.30),(.40,.37),(.34,.33),(.42,.265)]),.010,grille=True)
        for i,x in enumerate([-.14,-.30,-.46,-.62]):
            pocket(f'{label} engine bypass door {i+1}',mirror([(x+.055,.18),(x+.055,.285),(x-.015,.285),(x-.035,.265),(x-.055,.285),(x-.055,.18),(x-.035,.16),(x-.015,.18)]),.005,cover=True)
    pocket('APU intake', [(-.42,.07),(-.57,.07),(-.61,.145),(-.51,.20),(-.40,.145)],.014,grille=True)
    pocket('APU exhaust', [(-.67,.345),(-.74,.39),(-.84,.345),(-.79,.29)],.018,grille=True)
    pocket('refueling receptacle',[(.23,0),(.19,.058),(.045,.058),(.015,0),(.045,-.058),(.19,-.058)],.008,cover=True)
    return dict(radome=nose,details=records,edited_source_objects=['Fuselage'],
        body_faces_before=before,body_faces_after=len(body.data.polygons),
        default_cannon_shutter='closed',cannon_shutter_object=shutter.name,
        coordinates='Native model units; photo-informed positions, not engineering measurements',
        references=['Kubes walkaround 05 / 08 / 10 / 11 / 12 / 40 / 43',
            'USAF Judson Brohmer 021105-O-9999G-084 gun-door flight test',
            'USAF 8397555 and 8075211 aerial refueling photos'])

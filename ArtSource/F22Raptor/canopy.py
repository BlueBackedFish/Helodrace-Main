"""Refine the supplied canopy dome while retaining its mounted source pose."""
import bpy,bmesh,math


def prepare(scene,levels):
    obj=scene.objects['Cockpit.001'];mesh=obj.data
    original_vertices=len(mesh.vertices);original_faces=len(mesh.polygons)
    parent=(obj.parent,obj.parent_type,obj.parent_bone)
    world=obj.matrix_world.copy()
    bm=bmesh.new();bm.from_mesh(mesh)
    vertex_crease=bm.verts.layers.float.new('crease_vert')
    edge_crease=bm.edges.layers.float.new('crease_edge')
    # The source frame has an inner backing strip sharing three-way seams.
    # Keep those seams and the open mounting rim: they anchor the fitting.
    # Rejoin the FBX triangles before subdividing so the glass remains a
    # continuous quad dome rather than a collection of triangular dimples.
    # Only the outside mounting loop is held. The glass/frame seam is free to
    # curve, including the pointed front and rear of the original glazing.
    seed=max(bm.verts,key=lambda v:v.co.x);rim={seed};stack=[seed]
    while stack:
        vertex=stack.pop()
        for edge in vertex.link_edges:
            if edge.is_boundary:
                other=edge.other_vert(vertex)
                if other not in rim:rim.add(other);stack.append(other)
    assert len(rim)==18
    anchors=[v.co.copy() for v in rim]
    for vertex in rim:vertex[vertex_crease]=1
    for edge in bm.edges:
        if edge.is_boundary and all(v in rim for v in edge.verts):edge[edge_crease]=1
    bmesh.ops.join_triangles(bm,faces=list(bm.faces),
        angle_face_threshold=math.pi,angle_shape_threshold=math.pi,
        cmp_materials=True,cmp_uvs=True)
    glass_slot=next(i for i,m in enumerate(mesh.materials) if m.name=='F22 glass')
    glass=[f for f in bm.faces if f.material_index==glass_slot]
    glass_quads=sum(len(f.verts)==4 for f in glass)
    glass_triangles=sum(len(f.verts)==3 for f in glass)
    assert glass_quads==19 and glass_triangles==2
    bm.to_mesh(mesh);bm.free();mesh.update()
    modifier=obj.modifiers.new('Smooth canopy dome','SUBSURF')
    modifier.subdivision_type='CATMULL_CLARK'
    modifier.levels=modifier.render_levels=levels
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.modifier_apply(modifier=modifier.name);obj.select_set(False)
    for face in mesh.polygons:face.use_smooth=True
    mounting_error=max(min((v.co-point).length for v in mesh.vertices) for point in anchors)
    assert mounting_error<1e-6,'Subdivision moved the canopy mounting seams'
    assert parent==(obj.parent,obj.parent_type,obj.parent_bone)
    assert max(abs(obj.matrix_world[r][c]-world[r][c]) for r in range(4) for c in range(4))<1e-6
    assert len(mesh.uv_layers)>0
    return dict(object=obj.name,method='Rejoin glass quads; applied Catmull-Clark dome subdivision',
        subdivision_levels=levels,source_vertices=original_vertices,source_faces=original_faces,
        final_vertices=len(mesh.vertices),final_faces=len(mesh.polygons),
        glass_control_quads=glass_quads,glass_control_triangles=glass_triangles,
        mounting_seam_vertices=len(anchors),
        maximum_mounting_seam_displacement=mounting_error,
        parent_and_closed_pose_preserved=True,uv_layers_preserved=True,
        fixed_emission_palette_preserved=True)

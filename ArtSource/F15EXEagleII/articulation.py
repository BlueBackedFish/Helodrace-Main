"""Split the inspected OBJ surfaces and rig conventional variable-area nozzles.

All coordinates below are physical metres: X right, Y forward, Z up. Source
face selection happens before editing. No source-file names or indices are
guessed from the mesh after cuts, welding or mirroring.
"""
from pathlib import Path
import bpy, bmesh, json, math
from mathutils import Vector, Matrix
from mathutils.geometry import closest_point_on_tri, barycentric_transform

ROOT = Path(__file__).resolve().parent


def subset(scene, source, indices, name, rig):
    mesh = source.data
    faces = [mesh.polygons[i] for i in sorted(indices)]
    ids = sorted({i for f in faces for i in f.vertices})
    local = {v: i for i, v in enumerate(ids)}
    data = bpy.data.meshes.new(name)
    data.from_pydata([mesh.vertices[i].co for i in ids], [],
                     [[local[i] for i in f.vertices] for f in faces])
    for mat in mesh.materials:
        data.materials.append(mat)
    for dst, src in zip(data.polygons, faces):
        dst.material_index = src.material_index
    for layer in mesh.uv_layers:
        target = data.uv_layers.new(name=layer.name)
        for dst, src in zip(data.polygons, faces):
            for a, b in zip(dst.loop_indices, src.loop_indices):
                target.data[a].uv = layer.data[b].uv
    for layer in mesh.color_attributes:
        assert layer.domain == 'CORNER'
        target = data.color_attributes.new(name=layer.name, type=layer.data_type, domain='CORNER')
        for dst, src in zip(data.polygons, faces):
            for a, b in zip(dst.loop_indices, src.loop_indices):
                target.data[a].color = layer.data[b].color
    data.update()
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.parent = rig
    return obj


def close_and_gap(obj, pivot, axis, gap, material):
    """Close the original open hinge edges and trim a real neutral joint gap."""
    mesh = obj.data
    slot = len(mesh.materials)
    mesh.materials.append(material)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    # The source wing panels and rudders have open hinges and overlapping edge
    # fragments. Rebuild these convex volumes from their original vertices;
    # transfer skin colors/UVs and give the new closing walls joint material.
    if obj.name.startswith(('SURFACE_flap', 'SURFACE_aileron', 'SURFACE_rudder')):
        original = [(tuple(mesh.vertices[i].co.copy() for i in f.vertices), f.normal.copy(),
                     f.material_index, tuple(mesh.uv_layers[0].data[i].uv.copy() for i in f.loop_indices))
                    for f in mesh.polygons]
        bmesh.ops.delete(bm, geom=list(bm.faces), context='FACES_ONLY')
        bmesh.ops.delete(bm, geom=list(bm.edges), context='EDGES_FACES')
        bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
        bm.normal_update()
        uv = bm.loops.layers.uv.verify()
        for f in bm.faces:
            candidates = [s for s in original if s[1].dot(f.normal) > .995]
            if candidates:
                center = f.calc_center_median()
                src = min(candidates, key=lambda s:(center-closest_point_on_tri(center,*s[0])).length)
                # Body-colored lower faces use the same fixed RGB, without an
                # empty vertex-color attribute on newly generated hull loops.
                f.material_index = 0 if mesh.materials[src[2]].name.endswith('vertex paint') else src[2]
                for loop in f.loops:
                    v = barycentric_transform(loop.vert.co,*src[0],*[Vector((v.x,v.y,0)) for v in src[3]])
                    loop[uv].uv = v.xy
            else:
                f.material_index = slot
    else:
        boundary = [e for e in bm.edges if e.is_boundary]
        caps = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)['faces']
        for f in caps:
            f.material_index = slot
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    # A wing control moves aft from its actual hinge. A rudder moves towards
    # the tail. Cutting preserves the original trailing edge and outer shape.
    normal = Vector((0, 1, 0)).normalized() if 'rudder' not in obj.name else Vector((0, axis.z, -axis.y)).normalized()
    if 'aileron' in obj.name:
        normal = Vector((-axis.y, axis.x, 0)).normalized()
        if normal.y < 0:
            normal.negate()
    cut = bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
        plane_co=pivot-normal*gap, plane_no=normal, dist=1e-7, clear_outer=True)
    edges = [e for e in cut['geom_cut'] if isinstance(e, bmesh.types.BMEdge) and e.is_valid and e.is_boundary]
    for f in bmesh.ops.holes_fill(bm, edges=edges, sides=0)['faces']:
        f.material_index = slot
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    boundary_count = sum(e.is_boundary for e in bm.edges)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    assert len(mesh.polygons) >= 6, obj.name
    assert boundary_count == 0, (obj.name, 'open control volume', boundary_count)
    return dict(object=obj.name, closed_volume=True, boundary_edges=boundary_count, gap_m=gap)


def snapshot(obj):
    if obj.data.shape_keys:
        obj=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return [obj.matrix_world @ v.co for v in obj.data.vertices]


def prepare(scene, source, source_faces, origin, joint_material):
    settings = json.loads((ROOT/'articulation.json').read_text())
    assert settings['source_sha256']==json.loads((ROOT/'style.json').read_text())['source_sha256']
    rig = bpy.data.objects.new('F15EX_CONTROL_RIG', None)
    scene.collection.objects.link(rig)
    rig.parent = origin
    rig.matrix_parent_inverse = origin.matrix_world.inverted()
    # Bake only the visible airframe into a shared physical-metre basis. The
    # archived source gear continues to use the original calibration parent.
    source.data.transform(source.matrix_world)
    source.parent = rig
    source.matrix_parent_inverse = Matrix.Identity(4)
    source.matrix_basis = Matrix.Identity(4)
    source.data.update()
    index = {fi: i for i, fi in enumerate(source_faces)}
    taken = set()
    meshes = []
    controllers = []
    edits = []

    def extract(faces, name):
        assert not taken.intersection(faces), name
        taken.update(faces)
        obj = subset(scene, source, [index[fi] for fi in faces], name, rig)
        obj['source_face_count'] = len(faces)
        meshes.append(obj)
        return obj

    def hinge(identifier, pivot, axis, objects, nozzle=False):
        frame = bpy.data.objects.new('HINGE_'+identifier, None)
        scene.collection.objects.link(frame)
        frame.parent = rig
        axis = axis.normalized()
        frame.matrix_basis = Matrix.Translation(pivot) @ Vector((0, 1, 0)).rotation_difference(axis).to_matrix().to_4x4()
        control = bpy.data.objects.new('CTRL_'+identifier, None)
        scene.collection.objects.link(control)
        control.parent = frame
        control.empty_display_type = 'ARROWS'
        control.empty_display_size = .15
        bpy.context.view_layer.update()
        for obj in objects:
            world = obj.matrix_world.copy()
            obj.parent = control
            obj.matrix_world = world
        controllers.append(dict(id=identifier, object=control.name, pivot_m=list(pivot),
            axis=list(axis), channel='rotation_euler[1]', meshes=[o.name for o in objects], nozzle=nozzle))
        return control

    for item in settings['controls']:
        obj = extract(item['source_faces'], 'SURFACE_'+item['id'])
        pivot, axis = Vector(item['pivot_m']), Vector(item['axis']).normalized()
        if item['cap_open_boundary']:
            edits.append(close_and_gap(obj, pivot, axis, settings['joint_gap_m'], joint_material))
        hinge(item['id'], pivot, axis, [obj])

    # Retain one inspected engine as the geometric/material template. The
    # supplied left engine was visibly more open; don't hide that with paint.
    template = settings['nozzle_template']
    fixed = extract(template['fixed_source_faces'], 'EXHAUST_FIXED_right')
    # The source rear core disk was painted as body skin because it sits well
    # forward of the nozzle lips. Classify the actual inner barrel and core,
    # so opening the exhaust reveals a recess rather than a light gray plug.
    for face in fixed.data.polygons:
        radial=Vector((face.center.x-.703,0,face.center.z-1.571))
        face.material_index=4 if face.normal.y<-.6 or face.normal.dot(radial)<0 else 3
    parts = [(p, extract(p['source_faces'], 'EXHAUST_right_'+p['id'])) for p in template['petals']]
    removed_left = settings['nozzle_replaced_left_source_faces']
    assert not taken.intersection(removed_left)
    taken.update(removed_left)
    original_left = [source.data.vertices[i].co for fi in removed_left for i in source.data.polygons[index[fi]].vertices]
    original_right = [v.co.copy() for _, obj in parts for v in obj.data.vertices]
    # Nearest mirrored source surface discrepancy is measured before correction.
    import numpy as np
    a = np.asarray([[-v.x, v.y, v.z] for v in original_left if v.y < -7.75])
    b = np.asarray([list(v) for v in original_right if v.y < -7.75])
    distances = np.concatenate([np.sqrt(((chunk[:, None, :]-b[None, :, :])**2).sum(2).min(1)) for chunk in np.array_split(a, 30)])
    symmetry = dict(source_mirrored_outlet_max_nearest_vertex_difference_m=float(distances.max()),
        correction='Mirror right fixed barrel, 12 outer petals and 12 interleaved flaps onto left engine',
        template_side='right', source_left_replaced_faces=len(removed_left),
        template_reflection_exact_before_parent_float_rounding=True)

    def mirrored(obj, name):
        copy = obj.copy()
        copy.data = obj.data.copy()
        copy.name = name
        scene.collection.objects.link(copy)
        copy.data.transform(Matrix.Diagonal((-1, 1, 1, 1)))
        bm = bmesh.new()
        bm.from_mesh(copy.data)
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
        bm.to_mesh(copy.data)
        bm.free()
        copy.data.update()
        meshes.append(copy)
        return copy

    mirrored(fixed, 'EXHAUST_FIXED_left')
    left_parts = [(p, mirrored(obj, 'EXHAUST_left_'+p['id'])) for p, obj in parts]
    points = [v.co for _, obj in parts[:12] for v in obj.data.vertices]
    engine_x = (min(v.x for v in points)+max(v.x for v in points))/2
    engine_z = (min(v.z for v in points)+max(v.z for v in points))/2
    engine_controls = []
    for side, sign, assembly in [('right', 1, parts), ('left', -1, left_parts)]:
        engine = bpy.data.objects.new('CTRL_nozzle_'+side, None)
        scene.collection.objects.link(engine)
        engine.parent = rig
        engine['aperture'] = .5
        engine.id_properties_ui('aperture').update(min=0, max=1, description='Variable exhaust area: 0 contracted, 1 open; 0.5 neutral')
        engine_controls.append(engine)
        for p, obj in assembly:
            # Each leaf has its own tangential root hinge; the engine barrel
            # and neighbouring petals are never rotated as one solid cone.
            verts = [v.co for v in obj.data.vertices]
            front = max(v.y for v in verts)
            root = [v for v in verts if v.y > front-.035]
            pivot = sum(root, Vector())/len(root)
            radial = Vector((pivot.x-sign*engine_x, 0, pivot.z-engine_z)).normalized()
            axis = radial.cross(Vector((0, 1, 0)))
            leaf = hinge('nozzle_'+side+'_'+p['id'], pivot, axis, [obj], True)
            driver = leaf.driver_add('rotation_euler', 1).driver
            variable = driver.variables.new()
            variable.name = 'aperture'
            variable.targets[0].id = engine
            variable.targets[0].data_path = '["aperture"]'
            driver.expression = f'(aperture-0.5)*{math.radians(2*settings["nozzle_half_travel_degrees"]):.12f}'

        # Continuous inner sealing cone behind the overlapping external leaves.
        # Its lip follows each engine's aperture; it closes the source leaf
        # gaps without stretching the detailed outer petal meshes sideways.
        n=96
        def liner_points(radius,outlet_y):
            return [(sign*engine_x+r*math.cos(i*2*math.pi/n),y,engine_z+r*math.sin(i*2*math.pi/n))
                for r,y in [(.37,-7.69),(radius,outlet_y)] for i in range(n)]
        data=bpy.data.meshes.new('Continuous variable inner exhaust seal')
        data.from_pydata(liner_points(.35,-8.30),[],[[i,(i+1)%n,(i+1)%n+n,i+n] for i in range(n)])
        data.materials.append(fixed.data.materials[4])
        liner=bpy.data.objects.new('EXHAUST_'+side+'_inner_seal',data)
        scene.collection.objects.link(liner);liner.parent=rig;meshes.append(liner)
        liner.shape_key_add(name='Neutral')
        for name,radius,outlet_y,expression in [('Contracted',.22,-8.25,'max(0,0.5-aperture)*2'),
                                               ('Open',.48,-8.32,'max(0,aperture-0.5)*2')]:
            key=liner.shape_key_add(name=name)
            for v,co in zip(key.data,liner_points(radius,outlet_y)):v.co=co
            driver=key.driver_add('value').driver
            variable=driver.variables.new();variable.name='aperture';variable.targets[0].id=engine
            variable.targets[0].data_path='["aperture"]';driver.expression=expression

    body = subset(scene, source, [i for i, fi in enumerate(source_faces) if fi not in taken], 'AIRFRAME_FIXED', rig)
    meshes.append(body)
    assert len(taken)+len(body.data.polygons) == len(source_faces)
    bpy.data.objects.remove(source, do_unlink=True)
    bpy.context.view_layer.update()
    # Fixed fin-side hinge closures: the original source rudder has no front
    # wall. These small caps remain on the fin while its rudder rotates away.
    for item in settings['controls']:
        if not item['id'].startswith('rudder'):
            continue
        pivot, axis = Vector(item['pivot_m']), Vector(item['axis'])
        pts = [pivot+Vector((dx, 0, 0))+axis*t for t, dx in [(0, -.033), (0, .033), (1, .021), (1, -.021)]]
        mesh = bpy.data.meshes.new('Fixed fin hinge closure')
        mesh.from_pydata(pts, [], [[0, 1, 2, 3]])
        mesh.materials.append(joint_material)
        cap = bpy.data.objects.new('JOINT_FIXED_'+item['id'], mesh)
        scene.collection.objects.link(cap)
        cap.parent = rig
        meshes.append(cap)
    bpy.context.view_layer.update()

    # Independent movement, no fixed-body drag, neutral return, and real nozzle
    # radius change. Sample actual world vertices, not just driver variables.
    baseline = {o.name: snapshot(o) for o in meshes}
    checks = []
    for item in [c for c in controllers if not c['nozzle']]:
        control = scene.objects[item['object']]
        control.rotation_euler.y = math.radians(settings['demo_degrees'])
        bpy.context.view_layer.update()
        changed = set(item['meshes'])
        travel = max((a-b).length for name in changed for a, b in zip(baseline[name], snapshot(scene.objects[name])))
        assert travel > .005, item['id']
        assert all(max((a-b).length for a, b in zip(baseline[o.name], snapshot(o))) < 1e-5 for o in meshes if o.name not in changed)
        control.rotation_euler.y = 0
        bpy.context.view_layer.update()
        assert all(max((a-b).length for a, b in zip(baseline[name], snapshot(scene.objects[name]))) < 1e-5 for name in changed)
        checks.append(dict(id=item['id'],travel_m=travel,other_parts_fixed=True,neutral_return=True))
    for engine in engine_controls:
        side = engine.name.rsplit('_', 1)[1]
        assembly = [o for o in meshes if o.name.startswith('EXHAUST_'+side+'_')]
        other = [o for o in meshes if o not in assembly]
        sign = 1 if side == 'right' else -1
        radii = []
        for aperture in [0, 1, .5]:
            engine['aperture'] = aperture
            engine.update_tag()
            bpy.context.view_layer.update()
            outlet = [v for o in assembly for v in snapshot(o) if v.y < -8.15]
            radii.append(sum(math.hypot(v.x-sign*engine_x, v.z-engine_z) for v in outlet)/len(outlet))
            assert all(max((a-b).length for a, b in zip(baseline[o.name], snapshot(o))) < 1e-5 for o in other)
        assert radii[1]-radii[0] > .15, (side, radii)
        assert all(max((a-b).length for a, b in zip(baseline[o.name], snapshot(o))) < 1e-5 for o in assembly)
        checks.append(dict(id=engine.name,contracted_radius_m=radii[0],open_radius_m=radii[1],neutral_radius_m=radii[2],other_parts_fixed=True,neutral_return=True))

    # Explicit sample poses, not measured aerodynamic/actuator limits.
    for item in [c for c in controllers if not c['nozzle']]:
        control = scene.objects[item['object']]
        sign = -1 if item['id']=='flap_left' else 1
        for frame, angle in [(1, 0), (20, -12*sign), (40, 12*sign), (60, 0)]:
            control.rotation_euler.y = math.radians(angle)
            control.keyframe_insert(data_path='rotation_euler', index=1, frame=frame)
        control.animation_data.action.name = 'DEMO_'+item['id']
    for engine in engine_controls:
        for frame, aperture in [(1, .5), (20, 0), (40, 1), (60, .5)]:
            engine['aperture'] = aperture
            engine.keyframe_insert(data_path='["aperture"]', frame=frame)
        engine.animation_data.action.name = 'DEMO_'+engine.name
    scene.frame_start = 1
    scene.frame_end = 60
    scene.render.fps = 30
    scene.frame_set(1)
    bpy.context.view_layer.update()
    return meshes, dict(passed=True,controls=controllers,nozzle_engine_controls=[o.name for o in engine_controls],
        nozzle_type='Conventional variable area; independent tangential petal hinges, no thrust vectoring',
        nozzle_petal_meshes_per_engine=24,continuous_inner_seals=2,nozzle_symmetry=symmetry,closed_control_volumes=edits,
        independent_movement_checks=checks,joint_gap_m=settings['joint_gap_m'],
        source_face_partition_count=len(taken)+len(body.data.polygons),demo_frames=[1,20,40,60],
        travel_values_are_art_preview_only=True,runtime_flight_controller_added=False)

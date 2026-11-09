"""Make geometric control joints, split rudders and independent TVC pivots."""
from pathlib import Path
import bpy,bmesh,json,math
from mathutils import Vector,Matrix

ROOT=Path(__file__).resolve().parent


def snapshot(obj):
    dg=bpy.context.evaluated_depsgraph_get();evaluated=obj.evaluated_get(dg)
    return [evaluated.matrix_world@v.co for v in evaluated.data.vertices]


def bevel(obj,width,material):
    mesh=obj.data;slot=len(mesh.materials);mesh.materials.append(material)
    mod=obj.modifiers.new('Physical control edge bevel','BEVEL')
    mod.width=width;mod.segments=1;mod.limit_method='ANGLE';mod.angle_limit=math.radians(35)
    mod.material=slot;mod.use_clamp_overlap=True
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.modifier_apply(modifier=mod.name);obj.select_set(False)


def control_rim(obj,width,material):
    """Inset the actual upper skin and create a shallow sloping joint rim.

    The source panels already have tiny edge bevels. A second global bevel
    clamps against those tiny edges and disappears. Insetting the connected
    upper skin instead makes the intended-width mechanical edge real geometry.
    """
    mesh=obj.data;slot=len(mesh.materials);mesh.materials.append(material)
    bm=bmesh.new();bm.from_mesh(mesh);bm.normal_update()
    skin=[f for f in bm.faces if f.normal.z>.85]
    assert skin,(obj.name,'no upper control skin')
    result=bmesh.ops.inset_region(bm,faces=skin,thickness=width,depth=width*.18,
        use_even_offset=True,use_boundary=True,use_interpolate=True)
    for face in result['faces']:face.material_index=slot
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.update()
    return len(result['faces'])


def bake_native(obj,origin):
    dg=bpy.context.evaluated_depsgraph_get();evaluated=obj.evaluated_get(dg)
    mesh=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=dg)
    mesh.transform(origin.matrix_world.inverted()@evaluated.matrix_world)
    obj.modifiers.clear();obj.animation_data_clear();obj.constraints.clear()
    obj.parent=origin;obj.parent_type='OBJECT';obj.parent_bone='';obj.matrix_parent_inverse=Matrix.Identity(4)
    obj.data=mesh;obj.matrix_basis=Matrix.Identity(4);mesh.update()


def cut_half(mesh,root,normal,keep_front,gap,material,name):
    result=mesh.copy();result.name=name
    slot=len(result.materials);result.materials.append(material)
    bm=bmesh.new();bm.from_mesh(result)
    point=root+normal*(gap/2 if keep_front else -gap/2)
    cut=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
        plane_co=point,plane_no=normal,dist=1e-7,clear_inner=keep_front,clear_outer=not keep_front)
    edges=[e for e in cut['geom_cut'] if isinstance(e,bmesh.types.BMEdge) and e.is_valid]
    caps=bmesh.ops.holes_fill(bm,edges=edges,sides=0)['faces'] if edges else []
    for face in caps:face.material_index=slot
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(result);bm.free();result.update()
    assert len(result.polygons)>0,'Rudder split removed the complete fin'
    return result,len(caps)


def pocket_prism(obj,material):
    # Source panel skins contain overlapping bevel faces. Use a watertight
    # convex footprint prism for a predictable boolean instead of stretching
    # the original nonmanifold panel skin into a cutter.
    points=sorted(set((round(v.co.x,7),round(v.co.y,7)) for v in obj.data.vertices))
    def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    lower=[];upper=[]
    for point in points:
        while len(lower)>=2 and cross(lower[-2],lower[-1],point)<=1e-10:lower.pop()
        lower.append(point)
    for point in reversed(points):
        while len(upper)>=2 and cross(upper[-2],upper[-1],point)<=1e-10:upper.pop()
        upper.append(point)
    hull=lower[:-1]+upper[:-1];n=len(hull);assert n>=3
    verts=[(x,y,z) for z in [-1,2] for x,y in hull]
    faces=[list(reversed(range(n))),list(range(n,2*n))]+[[i,(i+1)%n,(i+1)%n+n,i+n] for i in range(n)]
    mesh=bpy.data.meshes.new('Closed control pocket prism');mesh.from_pydata(verts,[],faces);mesh.materials.append(material);mesh.update()
    return mesh


def prepare(scene,origin,ortho_scale,joint_material,delivery_resolution):
    settings=json.loads((ROOT/'articulation.json').read_text());inv=origin.matrix_world.inverted()
    source_rig=scene.objects[settings['source_control_armature']]
    target_bones={s['bone'] for s in settings['surfaces']+settings['fins']}
    target_bones.update(n[k] for n in settings['nozzles'] for k in ['upper_bone','lower_bone'])
    # Frame 650 closes the canopy, bays and gear, but contains small asymmetric
    # control deflections. Reset only the flight-control bones to their rest pose.
    target_bones.update('Bone.'+str(i).zfill(3) for i in range(14,22))
    for name in target_bones:source_rig.pose.bones[name].matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    pivots={name:(inv@(source_rig.matrix_world@source_rig.pose.bones[name].head),
                  inv@(source_rig.matrix_world@source_rig.pose.bones[name].tail)) for name in target_bones}
    # Archive the original actions rather than letting the source gear/canopy
    # cycle while the new controls are demonstrated at frames 1..60.
    archived=[]
    for obj in list(scene.objects):
        if obj.animation_data and obj.animation_data.action:
            action=obj.animation_data.action;action.use_fake_user=True
            obj['archived_source_action']=action.name;archived.append(dict(object=obj.name,action=action.name))
            obj.animation_data.action=None
    bpy.context.view_layer.update()
    rig=bpy.data.objects.new('F22_CONTROL_RIG',None);scene.collection.objects.link(rig);rig.parent=origin
    rig.matrix_basis=Matrix.Identity(4)
    width=ortho_scale*settings['bevel_width_delivery_pixels']/delivery_resolution
    gap=ortho_scale*settings['rudder_gap_delivery_pixels']/delivery_resolution
    controllers=[];edited=[];splits=[];cutters=[]

    def hinge(identifier,root,axis,objects,sign=1):
        axis=axis.normalized();frame=bpy.data.objects.new('HINGE_'+identifier,None);scene.collection.objects.link(frame)
        frame.parent=rig;frame.matrix_basis=Matrix.Translation(root)@Vector((0,1,0)).rotation_difference(axis).to_matrix().to_4x4()
        control=bpy.data.objects.new('CTRL_'+identifier,None);scene.collection.objects.link(control);control.parent=frame
        control.matrix_basis=Matrix.Identity(4);control.empty_display_type='ARROWS';control.empty_display_size=.06
        control['role']=identifier;control['rotation_channel']='rotation_euler[1]';control['preview_degrees']=settings['demo_degrees']
        bpy.context.view_layer.update()
        for obj in objects:
            world=obj.matrix_world.copy();obj.parent=control;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_world=world
            obj['control_surface']=identifier
        controllers.append(dict(id=identifier,object=control.name,frame=frame.name,axis_local='Y',
            pivot_native=list(root),hinge_axis_native=list(axis),meshes=[o.name for o in objects],demo_sign=sign))
        return control

    for item in settings['surfaces']:
        obj=scene.objects[item['object']];bake_native(obj,origin);before=len(obj.data.polygons)
        if not item['id'].startswith('stabilator'):
            # The supplied fixed wing continues underneath these panels. Cut a
            # real pocket out of that skin, so it cannot hide the modeled joint
            # or remain as a duplicate control when the panel rotates away.
            mesh=pocket_prism(obj,joint_material)
            cutter=bpy.data.objects.new('TEMP pocket '+item['id'],mesh);scene.collection.objects.link(cutter)
            cutter.parent=origin;cutter.matrix_basis=Matrix.Identity(4);cutter.hide_render=True;cutters.append(cutter)
        rims=control_rim(obj,width,joint_material);edited.append(dict(object=obj.name,operation='inset upper skin and sloping joint rim',rim_faces=rims,before_faces=before,after_faces=len(obj.data.polygons)))
        root,tip=pivots[item['bone']];hinge(item['id'],root,tip-root,[obj],item['demo_sign'])

    body=scene.objects['Fuselage'];body_before=len(body.data.polygons)
    for cutter in cutters:
        mod=body.modifiers.new('Real control pocket','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT'
        mod.object=cutter;mod.use_hole_tolerant=True;mod.material_mode='TRANSFER'
        bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
        bpy.ops.object.modifier_apply(modifier=mod.name);body.select_set(False);bpy.data.objects.remove(cutter,do_unlink=True)
    assert len(body.data.polygons)>1000,'Control pockets damaged the fixed airframe'
    edited.append(dict(object=body.name,operation='six physical wing-control pockets',before_faces=body_before,after_faces=len(body.data.polygons)))

    fin_materials={}
    def brighten(material):
        if material.name in fin_materials:return fin_materials[material.name]
        result=material.copy();result.name=material.name+' vertical fin +12% linear'
        nodes=result.node_tree.nodes;emit=next(n for n in nodes if n.type=='EMISSION')
        if emit.inputs[0].is_linked:
            incoming=emit.inputs[0].links[0].from_socket
            result.node_tree.links.remove(emit.inputs[0].links[0]);mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY'
            mix.inputs[0].default_value=1;mix.inputs[2].default_value=(settings['fin_linear_gain'],)*3+(1,)
            result.node_tree.links.new(incoming,mix.inputs[1]);result.node_tree.links.new(mix.outputs[0],emit.inputs[0])
        else:
            rgba=emit.inputs[0].default_value;emit.inputs[0].default_value=tuple(min(1,v*settings['fin_linear_gain']) for v in rgba[:3])+(1,)
        fin_materials[material.name]=result;return result

    for item in settings['fins']:
        obj=scene.objects[item['object']];bake_native(obj,origin)
        for i,mat in enumerate(list(obj.data.materials)):obj.data.materials[i]=brighten(mat)
        # The supplied fin already has vertices on its rudder hinge. Use that
        # actual swept/canted line, rather than rotating the complete fin.
        points=[v.co.copy() for v in obj.data.vertices]
        root=(points[17]+points[18])/2;tip=(points[7]+points[8]+points[16])/3;axis=tip-root
        normal=Vector((1,0,-axis.x/axis.z)).normalized()
        original=obj.data;fixed,caps_a=cut_half(original,root,normal,True,gap,joint_material,obj.name+' fixed fin')
        movable,caps_b=cut_half(original,root,normal,False,gap,joint_material,item['id']+' rudder')
        before=len(original.polygons);obj.data=fixed;bevel(obj,width*.45,joint_material)
        rudder=bpy.data.objects.new('SURFACE_'+item['id'],movable);scene.collection.objects.link(rudder)
        rudder.parent=origin;rudder.matrix_basis=Matrix.Identity(4);bevel(rudder,width*.45,joint_material)
        hinge(item['id'],root,axis,[rudder]);obj['fixed_fin']=True
        edited.append(dict(object=obj.name,operation='fixed fin / separate capped rudder with physical gap',before_faces=before,after_faces=len(obj.data.polygons)+len(rudder.data.polygons)))
        splits.append(dict(fixed=obj.name,rudder=rudder.name,capped_cut_faces=caps_a+caps_b,gap_native=gap))

    for item in settings['nozzles']:
        group=[]
        for name in item['objects']:
            obj=scene.objects[name];bake_native(obj,origin);before=len(obj.data.polygons);bevel(obj,width*.4,joint_material)
            group.append(obj);edited.append(dict(object=name,operation='TVC flap hinge/lip bevel',before_faces=before,after_faces=len(obj.data.polygons)))
        root=(pivots[item['upper_bone']][0]+pivots[item['lower_bone']][0])/2
        hinge(item['id'],root,Vector((0,1,0)),group)

    # Exercise every real hinge independently. Fixed fins and the other engine
    # must stay fixed; every moving vertex uses the physical model joint.
    bpy.context.view_layer.update();fixed_names=[s['fixed'] for s in splits];fixed_before={name:snapshot(scene.objects[name]) for name in fixed_names}
    movement=[]
    for item in controllers:
        control=scene.objects[item['object']];rest={name:snapshot(scene.objects[name]) for name in item['meshes']}
        others={name:snapshot(scene.objects[name]) for other in controllers if other!=item for name in other['meshes']}
        control.rotation_euler.y=math.radians(settings['demo_degrees']);bpy.context.view_layer.update()
        moved=max((a-b).length for name in item['meshes'] for a,b in zip(rest[name],snapshot(scene.objects[name])))
        assert moved>.001,(item['id'],'control does not move')
        assert all(max((a-b).length for a,b in zip(points,snapshot(scene.objects[name])))<1e-6 for name,points in fixed_before.items())
        assert all(max((a-b).length for a,b in zip(points,snapshot(scene.objects[name])))<1e-6 for name,points in others.items())
        movement.append(dict(control=item['id'],test_degrees=settings['demo_degrees'],maximum_vertex_travel=moved,fixed_fins_unchanged=True,other_controls_unchanged=True))
        control.rotation_euler.y=0;bpy.context.view_layer.update()
        assert all(max((a-b).length for a,b in zip(rest[name],snapshot(scene.objects[name])))<1e-5 for name in item['meshes'])

    for item in controllers:
        control=scene.objects[item['object']]
        for frame,angle in zip(settings['demo_frames'],[0,-settings['demo_degrees'],settings['demo_degrees'],0]):
            control.rotation_euler.y=math.radians(angle*item['demo_sign']);control.keyframe_insert(data_path='rotation_euler',index=1,frame=frame)
        control.animation_data.action.name='DEMO_F22_'+item['id']
    scene.frame_start=1;scene.frame_end=60;scene.render.fps=30;scene.frame_set(1);bpy.context.view_layer.update()
    return dict(controllers=controllers,edited_source_objects=edited,rudder_splits=splits,
        geometric_edge_width_native=width,joint_geometry='beveled control edges and capped physical rudder separation',
        fin_linear_gain=settings['fin_linear_gain'],archived_source_actions=archived,
        source_subsystems_frozen_at_frame=650,source_controls_reset_to_neutral=True,
        demo_frames=settings['demo_frames'],demo_degrees=settings['demo_degrees'],movement_validation=movement,
        runtime_flight_controller_added=False)

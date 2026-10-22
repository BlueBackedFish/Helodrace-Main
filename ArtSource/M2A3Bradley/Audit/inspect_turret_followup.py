"""Source/current turret comparison and welded component inventory for repairs."""
import bpy,json,collections,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(globals().get('OUTPUT_DIR',ROOT/'Audit/TurretFollowup'));OUT.mkdir(exist_ok=True)
sys.path.insert(0,str(ROOT))

def components(obj):
    print('INSPECT COMPONENTS',obj.name,flush=True)
    me=obj.data;leaders={};positions={};aliases={}
    def find(i):
        leaders.setdefault(i,i)
        while leaders[i]!=i:leaders[i]=leaders[leaders[i]];i=leaders[i]
        return i
    for v in me.vertices:
        aliases[v.index]=positions.setdefault(tuple(round(x,4) for x in v.co),v.index)
    for p in me.polygons:
        a=find(aliases[p.vertices[0]])
        for i in p.vertices[1:]:leaders[find(aliases[i])]=a
    groups=collections.defaultdict(list)
    for p in me.polygons:groups[find(aliases[p.vertices[0]])].append(p.index)
    result=[]
    for ids in groups.values():
        points=[obj.matrix_world@me.vertices[i].co for pi in ids for i in me.polygons[pi].vertices]
        lo=[min(p[k] for p in points) for k in range(3)];hi=[max(p[k] for p in points) for k in range(3)]
        result.append({'faces':len(ids),'polygon_indices':ids,'bounds':[lo,hi]})
    print('COMPONENTS FOUND',len(result),flush=True)
    return sorted(result,key=lambda r:r['faces'],reverse=True)

def turret_only():
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH':continue
        parent=obj
        while parent and parent.name!='TURRET_YAW':parent=parent.parent
        obj.hide_render=parent is None or obj.name.startswith('WT_antenna')

def render(name,back=False):
    print('INSPECT RENDER',name,flush=True)
    s=bpy.context.scene;cam=bpy.data.objects['Camera_iso_front'];s.camera=cam
    center=Vector((-.2,.45,2.75));cam.location=center+Vector((5,7,6) if back else (-5,-8,7))
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.5
    if '--after' in sys.argv:
        from face_depth_tone import apply_face_depth_tone
        apply_face_depth_tone(s)
    s.render.resolution_x=s.render.resolution_y=768;s.render.resolution_percentage=100
    s.eevee.taa_render_samples=16;s.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)

if __name__=='__main__':
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'));bpy.context.scene.frame_set(1)
    if '--after' in sys.argv:
        turret_only();render('after_front');render('after_rear',True)
        print('AFTER TURRET CLOSEUPS PUBLISHED');sys.exit(0)
    current={n:components(bpy.data.objects[n]) for n in ['WT_bone_turret','WT_hatch_02','WT_bone_gun']}
    turret_only();render('before_front');render('before_rear',True)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ReferenceFit/m3a3_bradley_refit.blend'));bpy.context.scene.frame_set(1)
    source={n:components(bpy.data.objects[n]) for n in current}
    (OUT/'component_inventory.json').write_text(json.dumps({'source':source,'before':current},indent=2)+'\n')
    mats=[]
    for value in (.80,.60,.40,.23):
        m=bpy.data.materials.new('Audit gray');m.use_nodes=True;nodes=m.node_tree.nodes;nodes.clear()
        e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=(value,value,value,1)
        out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);mats.append(m)
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        o.data.materials.clear()
        for m in mats:o.data.materials.append(m)
        for p in o.data.polygons:p.material_index=0 if p.normal.z>.7 else 1 if p.normal.z>.1 else 2 if abs(p.normal.y)>.6 else 3
    turret_only();render('source_front');render('source_rear',True)
    print('TURRET FOLLOWUP INSPECTION COMPLETE')

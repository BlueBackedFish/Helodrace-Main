"""Measure the old final shader, not just its positive height multiplier."""
import bpy,json,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/HeightQuality';OUT.mkdir(exist_ok=True)
sys.path.insert(0,str(ROOT));from mesh_regions import regions
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
assert len(args)==1,'Historical inspector: pass -- <legacy BLEND from commit 63b47c69>. For current output run validate_recolor.py.'
bpy.ops.wm.open_mainfile(filepath=str(Path(args[0]).resolve()));s=bpy.context.scene;s.frame_set(1)
def srgb(v):return 255*(12.92*v if v<=.0031308 else 1.055*max(0,v)**(1/2.4)-.055)
def measure(name,lo,hi,main=False,role=None):
    o=bpy.data.objects[name];me=o.data;ids=set(regions(me)[0]) if main else None;values=[]
    for p in me.polygons:
        if ids is not None and p.index not in ids:continue
        z=(o.matrix_world@p.center).z;m=me.materials[p.material_index]
        if p.normal.z<.85 or not lo<=z<=hi or role and m.get('paint_role')!=role:continue
        rgb=Vector(m.diffuse_color[:3]);height=sum(me.attributes['Height and curve tone'].data[i].value for i in p.loop_indices)/len(p.loop_indices)
        depth=me.attributes['Face depth tone'].data[p.index].value;recess=me.attributes['Recess paint'].data[p.index].value
        rgb*=height*depth*recess
        if m.get('height_desaturation'):
            sat=sum(me.attributes['Height saturation'].data[i].value for i in p.loop_indices)/len(p.loop_indices)
            y=rgb.dot(Vector((.2126,.7152,.0722)));rgb=Vector((y,y,y))+(rgb-Vector((y,y,y)))*sat
        y=sum(srgb(v)*w for v,w in zip(rgb,(.2126,.7152,.0722)))
        values.append((p.area,z,y))
    assert values,name
    total=sum(v[0] for v in values)
    return {'object':name,'faces':len(values),'height':sum(a*z for a,z,y in values)/total,
        'final_display_luminance':sum(a*y for a,z,y in values)/total}
samples={
 'hull_roof':measure('WT_@root',1.98,2.16,role='hull_assembly'),
 'turret_roof':measure('WT_bone_turret',2.715,2.745,role='turret_assembly'),
 'hatch_02':measure('WT_hatch_02',2.85,3.0,True),
 'hatch_03':measure('WT_hatch_03',2.77,2.90,True),
 'commander_sight_top':measure('WT_bone_commander_sight_v',3.28,3.40)}
report={'samples':samples,'settings':{'engine':s.render.engine,'samples':s.eevee.taa_render_samples,
 'resolution':[s.render.resolution_x,s.render.resolution_y],'view_transform':s.view_settings.view_transform,'look':s.view_settings.look},
 'hatch_03_faces':len(bpy.data.objects['WT_hatch_03'].data.polygons),
 'causes':['Per-part measured pixel medians are multiplied by height, so their baseline differences override height order.',
 'Hard normal thresholds and quantized materials flatten or facet curved forms.',
 'The simplified hatch 03 lost its original handle and hinge detail.',
 'Earlier validation checks the height multiplier, not final emitted color.']}
(OUT/'before_analysis.json').write_text(json.dumps(report,indent=2)+'\n')
print('FINAL SHADER BRIGHTNESS AUDIT',json.dumps(report))

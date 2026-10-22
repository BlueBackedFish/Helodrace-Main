"""Read-only audit of the saved RimWorld asset; does not save the blend."""
import bpy,json,os
HERE=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.dirname(HERE)
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'RimWorld','bradley_rimworld.blend'))
bpy.context.scene.frame_set(1)
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
rows=[]
for o in meshes:
    me=o.data
    rows.append({'name':o.name,'faces':len(me.polygons),'materials':len(me.materials),
        'degenerate_faces':sum(p.area<1e-12 for p in me.polygons),
        'parent':o.parent.name if o.parent else None})
report={'meshes':len(meshes),'faces':sum(r['faces'] for r in rows),
        'empty_meshes':[r['name'] for r in rows if not r['faces']],
        'largest_meshes':sorted(rows,key=lambda r:r['faces'],reverse=True)[:15],
        'all_meshes':rows,'lights':sum(o.type=='LIGHT' for o in bpy.context.scene.objects)}
with open(os.path.join(HERE,'current_model_stats.json'),'w') as f:json.dump(report,f,indent=2)
print('READ-ONLY AUDIT',report['faces'],'faces; empty meshes:',report['empty_meshes'])

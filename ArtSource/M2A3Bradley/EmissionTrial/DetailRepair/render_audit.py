"""Render matching source/current turret geometry for the reported missing parts."""
import bpy,json,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
jobs=[('current',HERE.parent/'bradley_emission_trial.blend'),('source',ROOT/'ReferenceFit/m3a3_bradley_refit.blend')]
if '--after' in sys.argv:jobs=[('after',HERE.parent/'bradley_emission_trial.blend')]
else:assert not (HERE/'current_front.png').exists(),'Do not overwrite the before-repair renders'
for version,path in jobs:
    bpy.ops.wm.open_mainfile(filepath=str(path));s=bpy.context.scene;s.frame_set(1)
    def under(o):
        while o:
            if o.name=='TURRET_YAW':return True
            o=o.parent
        return False
    for o in s.objects:
        if o.type=='MESH':o.hide_render=not under(o) or o.name.startswith('WT_antenna')
    if version=='source':
        palette=[]
        for i,rgb in enumerate([(.65,.62,.51,1),(.42,.40,.34,1),(.28,.28,.25,1),(.10,.14,.15,1)]):
            m=bpy.data.materials.new('Audit '+str(i));m.use_nodes=True;n=m.node_tree.nodes;n.clear();e=n.new('ShaderNodeEmission');e.inputs[0].default_value=rgb;out=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);palette.append(m)
        for o in s.objects:
            if o.type!='MESH' or o.hide_render:continue
            indices=[]
            for p in o.data.polygons:
                m=o.data.materials[p.material_index];normal=o.matrix_world.to_3x3()@p.normal
                indices.append(3 if 'optic' in m.name.lower() or 'glass' in m.name.lower() else 0 if normal.z>.5 else 2 if abs(normal.x)>.65 else 1)
            o.data.materials.clear()
            for m in palette:o.data.materials.append(m)
            for p,i in zip(o.data.polygons,indices):p.material_index=i
    s.render.resolution_x=s.render.resolution_y=768;s.render.resolution_percentage=100;s.eevee.taa_render_samples=64;s.render.film_transparent=True
    cam=bpy.data.objects['Camera_iso_front'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=3.7
    center=Vector((-.2,.55,2.85))
    for name,delta in [('front',(6,-7,5)),('rear',(-6,7,5))]:
        cam.location=center+Vector(delta);cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
        s.render.filepath=str(HERE/(version+'_'+name+'.png'));bpy.ops.render.render(write_still=True)
print('SOURCE/CURRENT AUDIT RENDERED')

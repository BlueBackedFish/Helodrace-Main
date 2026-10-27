"""Early twentieth-century trunnion-mounted Bessemer-family converter."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='circa 1900 trunnion-mounted air-blown converter'
for mats in [s.bed,s.cast,s.steel,s.dark]:
    for m in mats:m['tintable']=False
for m in s.stone:m['tintable']=True
for x in [-1.02,1.02]:s.cube('01_Stone_footing',(x,0,.13),(.64,2.48,.26),s.stone)
for y in [-.96,.96]:s.cube('02_Cradle_cross_tie',(0,y,.30),(2.30,.19,.12),s.bed)
# A thick open shell: broad pear-shaped bottom, shoulder, narrow open neck.
profile=[(0,.39),(.40,.43),(.66,.69),(.76,1.13),(.74,1.52),(.58,1.87),
         (.40,2.18),(.40,2.39),(.31,2.39),(.31,2.22),(.40,2.03),(0,1.97)]
n=40;verts=[(r*math.cos(i*math.tau/n),r*math.sin(i*math.tau/n),z) for r,z in profile for i in range(n)]
faces=[]
for k in range(len(profile)-1):
    for i in range(n):faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
mesh=s.bpy.data.meshes.new('Open_converter_shell');mesh.from_pydata(verts,[],faces);mesh.update()
o=s.bpy.data.objects.new('03_Open_pear_converter',mesh);s.scene.collection.objects.link(o)
for m in s.cast+s.dark:mesh.materials.append(m)
for p in mesh.polygons:
    band=p.index//n;p.material_index=3 if band>=8 else (0 if band in [5,7] else 1)
s.ring('04_Mouth_refractory_rim',(0,0,2.40),.425,.31,.08,s.steel,'Z')
s.ring('05_Equatorial_shell_band',(0,0,1.20),.79,.73,.105,s.bed,'Z')
for x in [-1.02,1.02]:
    s.cube('06_Cast_trunnion_column',(x,0,.80),(.32,.57,1.04),s.bed)
    s.cube('07_Bearing_saddle',(x,0,1.33),(.47,.68,.15),s.cast)
    s.cyl('08_Trunnion_bearing',(x,0,1.34),.19,.38,s.dark,'X')
    s.cyl('09_Converter_trunnion',(x*.82,0,1.34),.105,.55,s.steel,'X')
s.cyl('10_Tilting_drive_cover',(-1.29,0,1.01),.33,.14,s.cast,'X')
s.cyl('11_Drive_hub',(-1.38,0,1.01),.095,.09,s.dark,'X')
s.rod('12_Tilting_shaft',(-1.34,-.68,.66),(-1.34,0,1.01),.045,s.steel)
s.cube('13_Drive_gearbox',(-1.14,-.77,.48),(.42,.39,.35),s.bed)
s.handwheel('14_Tilting_handwheel',(-1.14,-1.02,.65),.20,'Y')
s.path('15_Blast_air_feed',[(1.01,.78,.40),(1.01,.40,.40),(.46,.40,.40),(.19,.16,.42)],.075,s.dark)
s.cyl('16_Air_valve',(1.01,.60,.40),.12,.16,s.cast,'Y')
s.cube('17_Tap_ladle_rest',(0,-.93,.35),(.70,.54,.13),s.bed)
s.rivets('18_Footing_anchors',[(x,y,.268) for x in [-1.19,-.85,.85,1.19] for y in [-.99,.99]],r=.034)
s.export(OUT,'HD_Converter',[3,3],['open pear-shaped air-blown converter','thick mouth and recessed interior','two trunnion columns and tilting drive','coarse shell band and bottom air feed','stone footings with fixed iron colors'])

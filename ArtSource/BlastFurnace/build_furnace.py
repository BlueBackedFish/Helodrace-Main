"""Compact early-industrial iron furnace, stone hearth and metal hot-blast plant."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='circa 1900 compact hot-blast iron furnace'
for mats in [s.bed,s.cast,s.steel,s.dark]:
    for m in mats:m['tintable']=False
for m in s.stone:m['tintable']=True
shell=s.pal('Furnace_stack_',[173,150,134],False)
ember=s.pal('Tap_ember_',[(181,100,42),(150,76,32),(120,58,27)],False)
s.cube('01_Hearth_foundation',(-.38,-.22,.13),(2.65,2.85,.26),s.stone)
s.cube('02_Stone_hearth',(-.38,-.22,.65),(2.12,2.00,.82),s.stone)
s.cube('03_Stone_coping',(-.38,-.22,1.10),(2.28,2.15,.13),s.stone)
# Faceted tapered shaft: a broad lower bosh and narrowing upper stack.
profile=[(.78,1.13),(.96,1.35),(1.00,1.64),(.86,2.20),(.67,2.91),(.48,3.36)]
n=40;verts=[(-.38+r*math.cos(i*math.tau/n),-.22+r*math.sin(i*math.tau/n),z) for r,z in profile for i in range(n)]
faces=[tuple(range(n-1,-1,-1))]
for k in range(len(profile)-1):
    for i in range(n):faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
faces.append(tuple(range((len(profile)-1)*n,len(profile)*n)))
mesh=s.bpy.data.meshes.new('Tapered_iron_furnace');mesh.from_pydata(verts,[],faces);mesh.update()
obj=s.bpy.data.objects.new('04_Tapered_furnace_shaft',mesh);s.scene.collection.objects.link(obj)
for m in shell:mesh.materials.append(m)
for p in mesh.polygons:p.material_index=0 if p.normal.z>.3 else 1
for z,r in [(1.41,.99),(1.88,.96),(2.40,.84),(2.96,.68)]:s.ring('05_Stack_iron_binding',(-.38,-.22,z),r,r-.035,.07,s.bed,'Z')
s.cyl('06_Charging_throat',(-.38,-.22,3.40),.49,.16,s.dark)
s.cyl('07_Charging_bell_cover',(-.38,-.22,3.52),.54,.10,s.cast)
s.cyl('08_Charging_spindle',(-.38,-.22,3.66),.065,.22,s.steel)
s.ring('09_Upper_service_deck',(-.38,-.22,3.08),.92,.63,.075,s.bed,'Z')
for x in [-1.18,.42]:
    s.rod('10_Service_guard_post',(x,-.54,3.10),(x,-.54,3.46),.033,s.dark)
s.rod('11_Deck_guard',(-1.18,-.54,3.46),(.42,-.54,3.46),.033,s.cast)
# Two small hot-blast stoves and one broad bustle main; no dense pipe maze.
for y in [-.34,.65]:
    s.cube('12_Stove_foot',(1.29,y,.20),(.69,.80,.26),s.stone)
    s.vessel('13_Hot_blast_stove',(1.29,y,1.23),.29,1.58,.15,s.bed)
    s.cyl('14_Stove_upper_band',(1.29,y,1.91),.31,.09,s.cast)
    s.cyl('15_Stove_chimney',(1.29,y,2.21),.10,.32,s.dark)
s.path('16_Hot_blast_main',[(1.29,-.34,.76),(1.29,-1.10,.76),(.62,-1.10,.76),(.62,-.76,.76)],.09,s.cast)
s.ring('17_Bustle_ring',(-.38,-.22,1.20),1.04,.94,.12,s.dark,'Z')
for angle in [0,math.pi,math.pi*1.5]:
    a=(-.38+1.01*math.cos(angle),-.22+1.01*math.sin(angle),1.20)
    b=(-.38+.73*math.cos(angle),-.22+.73*math.sin(angle),.98)
    s.rod('18_Tuyere_nozzle',a,b,.075,s.steel)
# Operator-side tapping recess and coarse refractory runner.
s.cube('19_Tapping_recess',(-.38,-1.235,.64),(.59,.035,.46),s.dark)
s.cube('20_Tapping_hole',(-.38,-1.261,.60),(.23,.018,.14),ember)
s.cube('21_Runner',(-.38,-1.56,.44),(.32,.60,.08),s.dark)
for x in [-.60,-.16]:s.cube('22_Refractory_runner_cheek',(x,-1.56,.49),(.11,.60,.15),s.stone)
# Rear downcomer and a coarse access ladder occupy the remaining silhouette.
s.path('23_Furnace_gas_downcomer',[(-.66,.10,3.39),(-1.42,.72,3.39),(-1.42,.72,1.24)],.115,s.dark)
s.cyl('24_Downcomer_cleanout',(-1.42,.72,1.08),.23,.30,s.bed)
for y in [.37,.65]:s.bar('25_Access_ladder_rail',(-1.42,y,.34),(-1.03,y,3.12),.045,.05,s.cast)
for z in [.64,1.12,1.60,2.08,2.56,3.04]:
    x=-1.42+(z-.34)/(3.12-.34)*.39;s.rod('26_Ladder_rung',(x,.37,z),(x,.65,z),.025,s.steel)
s.rivets('27_Hearth_binding',[(x,-1.229,.94) for x in [-1.17,-.77,.01,.41]],axis='Y',r=.035)
s.export(OUT,'HD_BlastFurnace',[4,4],['stone hearth and tapered iron shaft','broad stack binding bands and charging bell','two hot-blast stoves and bustle main','operator-side tapping recess and runner','rear gas downcomer and coarse access ladder'])

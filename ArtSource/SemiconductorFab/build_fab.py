"""Compact open 3x3 late-1950s semiconductor laboratory/workshop."""
import os,sys,math,json
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='circa 1957-1959 transistor fabrication laboratory'
ceramic=s.pal('Furnace_ceramic_',[222,207,189],False)
glass=s.pal('Bell_jar_simplified_',[(186,198,193),(130,149,145),(111,128,125)],False)
liquid=s.pal('Rinse_water_',[(106,133,135),(91,113,116),(82,102,104)],False)
silicon=s.pal('Silicon_sample_',[(98,112,126),(83,96,110),(75,85,99)],False)
s.cube('01_Low_equipment_platform',(0,0,.055),(2.86,2.86,.11),s.bed)
# U-shaped cabinets fill the footprint, leaving a small working recess.
for name,p,size in [('02_Rear_bench',(0,.99,.45),(2.66,.68,.76)),
                    ('03_Vacuum_bench',(-1.00,-.02,.45),(.65,1.37,.76)),
                    ('04_Wet_bench',(1.00,-.02,.45),(.65,1.37,.76)),
                    ('05_Inspection_bench',(0,-1.03,.45),(2.66,.61,.76))]:
    s.cube(name,p,size,s.cast)
    s.cube(name+'_worktop',(p[0],p[1],.866),(size[0]+.04,size[1]+.04,.075),s.steel)
for x in [-.88,0,.88]:
    s.cube('06_Inspection_cabinet_door',(x,-1.342,.44),(.73,.014,.52),s.bed)
    s.cube('07_Cabinet_handle',(x,-1.355,.61),(.19,.022,.028),s.steel)
# Broad horizontal diffusion tube and separate temperature-control box.
s.cube('08_Furnace_saddles',(0,.99,.99),(1.43,.48,.17),s.bed)
s.cyl('09_Horizontal_diffusion_furnace',(0,.99,1.23),.255,1.52,ceramic,'X',vertices=32)
for x in [-.64,.64]:s.ring('10_Furnace_band',(x,.99,1.23),.272,.24,.085,s.bed,'X')
s.cyl('11_Quartz_process_tube',(0,.99,1.23),.102,2.04,s.steel,'X')
for x in [-1.04,1.04]:
    s.ring('12_Process_tube_flange',(x,.99,1.23),.145,.085,.065,s.dark,'X')
    s.cyl('13_Tube_dark_opening',(x*1.042,.99,1.23),.075,.014,s.dark,'X')
s.cube('14_Furnace_temperature_controller',(-.12,.61,1.04),(.57,.21,.28),s.bed)
s.gauge('15_Temperature_dial',(-.23,.49,1.06),.078)
s.cyl('16_Temperature_knob',(.04,.475,1.03),.037,.035,s.dark,'Y')
# Bell-jar vacuum evaporator: solid proxy colors, not view-dependent glass.
s.cyl('17_Vacuum_baseplate',(-1.00,-.04,.94),.335,.10,s.dark)
profile=[(.28,.99),(.28,1.37),(.25,1.48),(.17,1.57),(0,1.62)]
n=32;verts=[(-1+r*math.cos(i*math.tau/n),-.04+r*math.sin(i*math.tau/n),z) for r,z in profile for i in range(n)]
faces=[]
for k in range(len(profile)-1):
    for i in range(n):faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
mesh=s.bpy.data.meshes.new('Bell_jar_proxy');mesh.from_pydata(verts,[],faces);mesh.update()
obj=s.bpy.data.objects.new('18_Vacuum_evaporation_bell_jar',mesh);s.scene.collection.objects.link(obj)
for material in glass:mesh.materials.append(material)
for face in mesh.polygons:face.material_index=0 if face.normal.z>.4 else 1
s.ring('19_Bell_jar_lower_seal',(-1.00,-.04,1.00),.295,.26,.045,s.steel,'Z')
s.cube('20_Vacuum_pump',(-1.00,-.05,.29),(.43,.70,.31),s.dark)
s.path('21_Broad_vacuum_line',[(-1.22,-.04,.92),(-1.31,-.04,.92),(-1.31,-.04,.31),(-1.13,-.04,.31)],.045,s.bed)
s.gauge('22_Vacuum_gauge',(-1.00,-.72,.69),.09)
# Two readable baths and a coarse local exhaust canopy.
for y in [-.34,.22]:
    s.cube('23_Wet_bath_recess',(1.00,y,.911),(.43,.39,.023),s.dark)
    s.cube('24_Rinse_bath_contents',(1.00,y,.925),(.33,.29,.013),liquid)
for y in [.31,.56]:s.cube('25_Extract_hood_post',(1.29,y,1.20),(.055,.055,.59),s.bed)
s.cube('26_Wet_bench_extract_canopy',(1.04,.44,1.53),(.67,.32,.15),s.cast)
s.cube('27_Hood_dark_mouth',(1.04,.44,1.448),(.53,.24,.017),s.dark)
s.cube('28_Rear_exhaust_riser',(1.29,.61,1.25),(.16,.23,.83),s.bed)
# Front inspection microscope and two coarse wafer discs, no tiny loose clutter.
s.cube('29_Microscope_foot',(.46,-1.03,.96),(.35,.30,.10),s.bed)
s.bar('30_Microscope_arm',(.56,-.92,1.00),(.56,-.92,1.39),.09,.11,s.cast)
s.bar('31_Microscope_top',(.56,-.92,1.39),(.33,-1.05,1.46),.10,.10,s.cast)
s.rod('32_Microscope_eyepiece',(.35,-1.04,1.45),(.22,-1.13,1.52),.053,s.dark)
s.cyl('33_Microscope_objective',(.38,-1.04,1.29),.047,.14,s.steel)
s.cube('34_Inspection_stage',(.40,-1.04,1.15),(.29,.23,.045),s.dark)
s.cube('35_Wafer_sample_tray',(-.42,-1.03,.927),(.62,.35,.036),s.bed)
for x in [-.58,-.27]:s.cyl('36_Small_silicon_wafer',(x,-1.03,.954),.118,.018,silicon)
# One compact contact-mask fixture fills the center without a modern stepper.
s.cube('37_Contact_fixture_pedestal',(0,.02,.48),(.47,.49,.79),s.bed)
s.cube('38_Contact_fixture_table',(0,.02,.91),(.60,.58,.08),s.steel)
s.cyl('39_Wafer_chuck',(0,.02,.975),.19,.055,s.dark)
s.cyl('40_Wafer_on_chuck',(0,.02,1.008),.145,.013,silicon)
for x in [-.235,.235]:s.cube('41_Mask_frame_side',(x,.02,1.075),(.055,.50,.055),s.cast)
for y in [-.205,.245]:s.cube('42_Mask_frame_crossbar',(0,y,1.075),(.47,.055,.055),s.cast)
before=s.geometry_hash()
s.export(OUT,'HD_SemiconductorFab',[3,3],['open 3x3 late-1950s transistor workshop','horizontal diffusion furnace and analog temperature control','bell-jar vacuum evaporator and coarse vacuum pump','wet processing baths with local extraction hood','front microscope inspection bench and small wafer samples','compact central contact-mask fixture'])
with open(os.path.join(OUT,'validation.json'),encoding='utf8') as f:meta=json.load(f)
assert s.geometry_hash()==before
meta.update(density_export_preserves_geometry_and_palette=True,density_export_geometry_palette_sha256=before,rotation_export_geometry_palette_sha256=before)
with open(os.path.join(OUT,'validation.json'),'w',encoding='utf8') as f:json.dump(meta,f,indent=2)

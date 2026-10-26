"""Original timber cable-tool derrick with percussion beam and bull-wheel winch."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='1900-1930 timber standard cable-tool drilling rig'
wood=s.pal('Vanilla_timber_',[(151,113,80),(133,97,67),(113,83,57)],False)
# Wood is baked vanilla brown. Metal remains neutral instead of inheriting wood tint.
for mats in [s.bed,s.cast,s.steel,s.dark]:
    for m in mats:m['tintable']=False
cx,cy=-.64,.16
for y in [-1.16,1.16]:s.cube('01_Timber_skid',(0,y,.13),(3.76,.22,.26),wood)
for x in [-1.57,-.64,.38,1.58]:s.cube('02_Deck_sleeper',(x,0,.20),(.20,2.58,.19),wood)
# Deck is physically open around the cable/bit, rather than hiding a painted hole.
for name,x,y,w,d in [('Left',-1.34,cy,.48,2.27),('Right',.02,cy,.52,2.27),
                       ('Front',cx,-.72,.89,.51),('Rear',cx,1.04,.89,.51)]:
    s.cube('03_'+name+'_well_deck',(x,y,.30),(w,d,.12),wood)
s.cube('04_Well_recess',(cx,cy,.10),(.57,.57,.08),s.pit)
s.ring('05_Well_collar',(cx,cy,.37),.20,.125,.09,s.dark,'Z')
z0,z1=.35,3.97
def corner(signx,signy,z):
    t=(z-z0)/(z1-z0)
    return (cx+signx*(.83*(1-t)+.25*t),cy+signy*(.94*(1-t)+.29*t),z)
for sx in [-1,1]:
    for sy in [-1,1]:s.bar('06_Splayed_derrick_leg',corner(sx,sy,z0),corner(sx,sy,z1),.16,.16,wood)
for z in [.52,1.58,2.70,3.87]:
    for sy in [-1,1]:s.bar('07_Horizontal_tie',corner(-1,sy,z),corner(1,sy,z),.11,.12,wood)
    for sx in [-1,1]:s.bar('08_Side_tie',corner(sx,-1,z),corner(sx,1,z),.11,.12,wood)
for za,zb in [(.58,1.52),(1.65,2.64),(2.76,3.80)]:
    # Rear cross-braces, side single braces: front stays open enough to read the cable.
    for sx in [-1,1]:s.bar('09_Rear_cross_brace',corner(sx,1,za),corner(-sx,1,zb),.085,.08,wood)
    for sx in [-1,1]:s.bar('10_Side_diagonal',corner(sx,-1,za),corner(sx,1,zb),.085,.08,wood)
s.cube('11_Crown_block',(cx,cy,4.03),(.74,.80,.15),wood)
s.cyl('12_Crown_sheave',(cx,cy,4.17),.175,.13,s.dark,'Y')
s.cyl('13_Crown_axle',(cx,cy,4.17),.052,.73,s.steel,'Y')
s.rod('14_Suspended_drilling_cable',(cx-.15,cy,4.17),(cx-.15,cy,.82),.024,s.dark)
s.cyl('15_Heavy_percussion_tool',(cx-.15,cy,.67),.070,.46,s.steel)
s.side_profile('16_Chisel_bit',[(cx-.23,.46),(cx-.07,.46),(cx-.12,.34),(cx-.18,.34)],.09,s.dark,y=cy)
# Separate cable drum and a broad bull wheel, powered from an external belt.
s.cube('17_Winch_foundation',(1.12,.60,.34),(1.02,.91,.20),wood)
for y in [.19,1.01]:
    s.cube('18_Winch_bearing',(1.12,y,.69),(.27,.18,.53),s.cast)
s.cyl('19_Cable_drum',(1.12,.60,.91),.27,.72,s.bed,'Y')
for y in [.18,1.02]:s.cyl('20_Drum_flange',(1.12,y,.91),.35,.08,s.dark,'Y')
s.wheel('21_Bull_wheel',(1.12,1.14,.91),.48,s.bed,spokes=6)
s.cyl('22_Drive_pulley',(1.12,1.30,.91),.23,.13,s.dark,'Y')
s.rod('23_Hoisting_cable',(cx+.15,cy,4.17),(1.12,.60,1.18),.026,s.dark)
# Timber walking beam and iron pitman identify reciprocating cable drilling.
s.cube('24_Samson_foot',(1.10,-.66,.37),(.57,.63,.18),wood)
s.cube('25_Samson_post',(1.10,-.66,.87),(.22,.24,.85),wood)
s.cyl('26_Beam_pivot',(1.10,-.66,1.35),.115,.44,s.steel,'Y')
s.bar('27_Walking_beam',(.24,-.66,1.51),(1.76,-.66,1.20),.17,.18,wood)
s.rod('28_Pitman',(1.69,-.66,1.21),(1.48,-.66,.66),.045,s.steel)
s.cyl('29_Crank_disc',(1.31,-.66,.67),.25,.10,s.dark,'Y')
s.rod('30_Beam_cable_link',(.24,-.66,1.51),(cx-.15,cy,1.05),.025,s.dark)
s.rivets('31_Large_iron_straps',[(x,y,.405) for x in [-1.57,.38,1.58] for y in [-1.16,1.16]],r=.035)
s.export(OUT,'HD_CableToolRig',[4,3],['timber splayed derrick and crown sheave','suspended cable and heavy percussion bit','open well deck and well collar','large bull wheel and hoisting drum','Samson post, walking beam and crank pitman','baked vanilla timber and neutral iron; no rotary table'])

"""Build the production planning desk scene. Publish afterwards with
IndustrialMachines/normalize_density.py -- HD_ProductionPlanningDesk.

blender -b --python-exit-code 1 --python ArtSource/ProductionPlanningDesk/build_planning_desk.py
"""
import bpy, os, sys, math
OUT = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(OUT, '..', 'IndustrialMachines'))
import sprite_common as s

s.start()
scene = s.scene
scene['design_period'] = 'frontier planning office'
scene['outline_pixels'] = 12
scene['presentation_rule'] = 'one_model_unit_per_game_tile'

def shades(name, top, front, side):
    """Fixed furniture colors: top / front (+-Y) / side (+-X), no construction tint."""
    return [s.mat(name + '_top', top, False), s.mat(name + '_front', front, False), s.mat(name + '_side', side, False)]

# Vanilla WoodLog stuff color (133,97,67) on the top; faces differ by about eight levels,
# and frame/legs use the darker 217/255 edge ratio, as on the radio desk.
wood = shades('Desk_wood', (133, 97, 67), (125, 91, 63), (117, 86, 59))
frame = shades('Desk_frame', (113, 83, 57), (105, 77, 53), (99, 72, 50))
drawer = shades('Drawer_front', (121, 88, 61), (121, 88, 61), (110, 80, 55))
brass = shades('Brass_pull', (202, 161, 83), (184, 143, 69), (175, 135, 62))
cork = shades('Cork_board', (176, 140, 96), (168, 133, 91), (160, 127, 87))
board_rim = shades('Board_rim', (92, 67, 46), (86, 63, 43), (80, 58, 40))
blueprint = shades('Blueprint_paper', (74, 112, 156), (66, 101, 141), (60, 92, 128))
ink = shades('Blueprint_line', (214, 226, 238), (204, 216, 228), (196, 208, 220))
paper = shades('Paper', (226, 222, 208), (214, 210, 196), (204, 200, 186))
clipboard = shades('Clipboard', (150, 112, 74), (140, 104, 69), (131, 97, 64))
metal = shades('Clip_metal', (180, 180, 177), (166, 166, 163), (152, 152, 149))
ledger = shades('Ledger_cover', (124, 48, 42), (112, 43, 38), (104, 40, 35))
lamp = shades('Lamp_green', (58, 96, 78), (52, 86, 70), (47, 78, 63))
lamp_dark = shades('Lamp_arm', (62, 62, 60), (56, 56, 54), (50, 50, 48))
pencil = shades('Pencil', (214, 172, 70), (200, 160, 62), (190, 152, 58))
cards = {
    'red': shades('Card_red', (178, 72, 62), (166, 66, 57), (156, 62, 53)),
    'yellow': shades('Card_yellow', (210, 176, 76), (198, 165, 70), (188, 157, 66)),
    'green': shades('Card_green', (96, 154, 96), (88, 143, 88), (82, 134, 82)),
    'blue': shades('Card_blue', (86, 126, 176), (79, 116, 163), (74, 108, 152)),
}

parts = {}
def add(group, obj):
    parts.setdefault(group, []).append(obj)
    return obj

# 2 x 1 tile desk. Front (working side) is -Y; the planning board stands at the rear (+Y).
add('01_Desktop', s.cube('Desktop', (0, 0, .77), (1.90, .84, .06), wood))
add('02_Pedestal', s.cube('Drawer_pedestal', (-.62, .02, .37), (.56, .74, .74), frame))
for i in range(3):
    z = .17 + i * .21
    add('03_Drawers', s.cube('Drawer_%d' % i, (-.62, -.355, z), (.48, .03, .17), drawer))
    add('04_Pulls', s.cube('Drawer_pull_%d' % i, (-.62, -.378, z), (.14, .02, .03), brass))
for x, y in [(.86, -.35), (.86, .35)]:
    add('05_Legs', s.cube('Leg', (x, y, .37), (.07, .07, .74), frame))
add('05_Legs', s.cube('Foot_rail', (.86, 0, .12), (.06, .66, .05), frame))
add('05_Legs', s.cube('Modesty_panel', (.12, .38, .52), (1.44, .03, .42), frame))

# Rear planning board: cork panel on two posts with a dark rim and a kanban grid of cards.
for x in (-.88, .88):
    add('06_Board_posts', s.cube('Board_post', (x, .38, 1.21), (.06, .06, .82), board_rim))
add('07_Board', s.cube('Cork_panel', (0, .385, 1.24), (1.70, .03, .68), cork))
add('08_Board_rim', s.cube('Board_rim_top', (0, .37, 1.60), (1.76, .06, .05), board_rim))
add('08_Board_rim', s.cube('Board_rim_bottom', (0, .37, .90), (1.76, .06, .05), board_rim))
columns = [('red', 3), ('yellow', 2), ('green', 3), ('blue', 1)]
for c, (color, count) in enumerate(columns):
    x = -.62 + c * .41
    add('09_Column_headers', s.cube('Header_' + color, (x, .362, 1.51), (.36, .012, .08), cards[color]))
    for r in range(count):
        add('10_Cards', s.cube('Card_%s_%d' % (color, r), (x, .362, 1.37 - r * .16), (.30, .012, .12), paper))
        add('11_Card_tabs', s.cube('Card_tab_%s_%d' % (color, r), (x - .11, .355, 1.37 - r * .16), (.05, .006, .12), cards[color]))

# Desktop work: an unrolled blueprint with drawn part outlines, a clipboard schedule,
# a ledger, a pencil cup and a green-shaded lamp.
add('12_Blueprint', s.cube('Blueprint_sheet', (.10, -.06, .805), (.86, .50, .01), blueprint))
for name, p, size in [
        ('Line_outer_top', (.10, .14, .812), (.70, .015, .004)), ('Line_outer_bottom', (.10, -.26, .812), (.70, .015, .004)),
        ('Line_outer_left', (-.25, -.06, .812), (.015, .40, .004)), ('Line_outer_right', (.45, -.06, .812), (.015, .40, .004)),
        ('Line_shaft', (.02, -.02, .812), (.38, .02, .004)), ('Line_hub_v', (-.12, -.02, .812), (.02, .20, .004)),
        ('Line_plate_top', (.30, -.12, .812), (.16, .015, .004)), ('Line_plate_bottom', (.30, -.22, .812), (.16, .015, .004)),
        ('Line_plate_left', (.22, -.17, .812), (.015, .10, .004)), ('Line_plate_right', (.38, -.17, .812), (.015, .10, .004))]:
    add('13_Blueprint_lines', s.cube(name, p, size, ink))
add('14_Clipboard', s.cube('Clipboard_board', (.70, -.10, .808), (.28, .38, .016), clipboard))
add('15_Schedule', s.cube('Schedule_sheet', (.70, -.12, .819), (.24, .31, .006), paper))
for i in range(4):
    add('16_Schedule_rows', s.cube('Schedule_row_%d' % i, (.68, -.03 - i * .065, .823), (.16, .018, .003), lamp_dark))
add('17_Clip', s.cube('Clipboard_clip', (.70, .075, .826), (.12, .05, .02), metal))
add('18_Ledger', s.cube('Ledger_book', (-.60, -.12, .825), (.34, .26, .05), ledger))
add('19_Ledger_pages', s.cube('Ledger_pages', (-.60, -.115, .826), (.32, .255, .036), paper))
add('20_Pencil_cup', s.cyl('Pencil_cup', (.44, .22, .85), .045, .10, lamp_dark, vertices=16))
for i, (dx, dy) in enumerate([(-.012, 0), (.014, .01), (0, -.014)]):
    add('21_Pencils', s.rod('Pencil_%d' % i, (.44 + dx, .22 + dy, .86), (.44 + dx * 3, .22 + dy * 3, .99), .008, pencil))
add('22_Lamp', s.cyl('Lamp_base', (-.72, .24, .815), .08, .03, lamp_dark, vertices=20))
add('22_Lamp', s.rod('Lamp_arm_lower', (-.72, .24, .83), (-.66, .16, 1.06), .014, lamp_dark))
add('22_Lamp', s.rod('Lamp_arm_upper', (-.66, .16, 1.06), (-.52, .05, 1.12), .014, lamp_dark))
add('23_Lamp_shade', s.cyl('Lamp_shade', (-.50, .03, 1.09), .10, .09, lamp, vertices=20, large=True))

# Join each logical part; the tabletop mesh name is the center anchor.
for name, objects in parts.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects: o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1: bpy.ops.object.join()
    bpy.context.object.name = name

bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'production_planning_desk.blend'))
print('PLANNING_DESK_SCENE_SAVED', len(parts))

"""Match physical F-22 length to the existing 19-tile C-17 artwork."""
from pathlib import Path
import json

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';C17=ROOT.parent/'C17Globemaster'
style=json.loads((ROOT/'style.json').read_text(encoding='utf-8'))
reference=json.loads((C17/'output/model_validation.json').read_text(encoding='utf-8'))
reference_scale=json.loads((C17/'output/scale.json').read_text(encoding='utf-8'))
assert reference_scale['draw_size_tiles']==[19,19],'Review the C-17 reference display size before recalibrating'
reference_length=reference['used_bounds'][1][1]-reference['used_bounds'][0][1]
c17_tiles=reference_scale['draw_size_tiles'][1]
tiles_per_meter=c17_tiles*reference_length/reference['ortho_scale']/53.0
length_tiles=18.9*tiles_per_meter
density=reference_scale['reference_pixels_per_tile']
padding=style['outline_pixels']+style['transparent_margin_pixels']
resolution=round(length_tiles*density+2*padding)
draw_size=length_tiles/(1-2*padding/resolution)
style.update(delivery_resolution=resolution,draw_size_tiles=[draw_size,draw_size],
    physical_length_m=18.9,physical_wingspan_m=13.6)
(ROOT/'style.json').write_text(json.dumps(style,indent=2)+'\n',encoding='utf-8')
report=dict(passed=True,reference_aircraft='C-17 Globemaster III',reference_draw_size_tiles=[19,19],
    reference_length_m=53.0,reference_wingspan_m=51.75,f22_length_m=18.9,f22_wingspan_m=13.6,
    tiles_per_meter=tiles_per_meter,f22_draw_size_tiles=[draw_size,draw_size],
    f22_resolution=resolution,reference_pixels_per_tile=density,actual_pixels_per_tile=resolution/draw_size,
    nominal_length_ratio=18.9/53.0,nominal_wingspan_ratio=13.6/51.75,
    calibration='Uniform scale from physical body length, excluding padding and outline; model proportions retained',
    sources=[dict(aircraft='F-22',url='https://www.af.mil/About-Us/Fact-Sheets/Display/Article/104506/f-22-raptor/'),
        dict(aircraft='C-17',url='https://www.af.mil/About-Us/Fact-Sheets/Display/Article/1529726/c-17-globemaster-iii/c-17-globemaster-iii/')])
(OUT/'physical_scale.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('F22_PHYSICAL_SCALE_PASS:',round(draw_size,4),'tiles;',resolution,'px; C-17 remains 19 tiles')

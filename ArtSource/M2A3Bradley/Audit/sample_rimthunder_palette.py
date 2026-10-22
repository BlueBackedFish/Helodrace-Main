"""Measure corresponding Bradley paint regions; keep numerical swatches only."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,numpy as np
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/PaletteMatch';OUT.mkdir(exist_ok=True)
REF=Path('C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M2A3')
boxes={
 'hull_roof':('vehicle_east.png',[432,389,500,434]),
 'hull_front_slope':('vehicle_east.png',[775,400,850,470]),
 'hull_side_slope':('vehicle_east.png',[380,560,500,585]),
 'hull_wall':('vehicle_east.png',[320,640,430,665]),
 'hull_skirt':('vehicle_east.png',[330,688,420,705]),
 'rear_roof_hatch':('vehicle_east.png',[219,426,260,466]),
 'track':('vehicle_east.png',[325,727,450,733]),
 'turret_roof':('vehicle_turret_full.png',[489,477,522,501]),
 'turret_front_slope':('vehicle_turret_full.png',[598,401,634,430]),
 'turret_side_slope':('vehicle_turret_full.png',[401,617,420,642]),
 'turret_rear_slope':('vehicle_turret_full.png',[443,632,479,651]),
 'turret_wall':('vehicle_turret_full.png',[664,434,675,473]),
 'tow_roof':('vehicle_turret_full.png',[311,426,372,465]),
 'tow_body':('vehicle_turret_full.png',[318,503,369,537]),
 'gun_shroud':('vehicle_turret_full.png',[489,417,524,445]),
 'gun_mantlet':('vehicle_turret_full.png',[503,350,520,384]),
 'barrel':('vehicle_turret_full.png',[509,202,514,294]),
 'hatch_dome':('vehicle_turret_full.png',[541,535,566,557]),
 'rear_basket_floor':('vehicle_turret_full.png',[436,665,538,685]),
 'rear_bin_wall':('vehicle_turret_full.png',[489,716,513,724]),
}
samples={};images={}
for role,(name,box) in boxes.items():
    if name not in images:images[name]=Image.open(REF/name).convert('RGBA')
    im=images[name];rgba=np.array(im.crop(box));v=rgba[:,:,:3].mean(axis=2)[rgba[:,:,3]>250]
    assert len(v)>20,role
    samples[role]={'file':name,'pixel_box':box,'median_gray':round(float(np.median(v)),2),
        'p10_gray':round(float(np.quantile(v,.1)),2),'p90_gray':round(float(np.quantile(v,.9)),2)}
    print(role,samples[role]['median_gray'])
report={'reference_root':str(REF),'files':{n:{'sha256':hashlib.sha256((REF/n).read_bytes()).hexdigest(),'size':list(im.size)} for n,im in images.items()},
    'samples':samples,'method':'opaque-pixel grayscale medians at manually checked corresponding parts; numerical palette, not UV projection'}
(ROOT/'rimthunder_palette_samples.json').write_text(json.dumps(report,indent=2)+'\n')
for name,im in images.items():
    d=ImageDraw.Draw(im)
    for role,(filename,box) in boxes.items():
        if filename==name:d.rectangle(box,outline=(255,60,70,255),width=2);d.text((box[0],box[1]-11),role,fill=(255,60,70,255))
    # Diagnostic, never a production sprite.
    im.save(OUT/('sample_locations_'+name))

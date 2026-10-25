"""Read-only style audit across Main air support and Arsenal aircraft assets."""
from pathlib import Path
import hashlib,json,sys
import numpy as np
from PIL import Image,ImageDraw

HERE=Path(__file__).resolve().parent
main=HERE.parents[1]
arsenal=main.parent/'Helodrace-LobosArsenal'
sys.path.insert(0,str(HERE.parent/'VehiclePipeline'))
from images import font
items=[
    ('Main P-47',main/'Textures/Effects/CAS/HD_P47_CAS.png'),
    ('Main A-10C',main/'Textures/Effects/CAS/A10/HD_A10C.png'),
    ('Main MH-60M',main/'Textures/Effects/Aircraft/MH60M/HD_MH60M_Base.png'),
    ('Arsenal P-47',arsenal/'1.6/Textures/Aerocraft/P-47/HD_P47.png'),
    ('Arsenal DH-4',arsenal/'1.6/Textures/Aerocraft/DH-4/HD_DH4.png')]
board=Image.new('RGB',(1650,430),(94,98,103));draw=ImageDraw.Draw(board)
records=[]
for i,(label,path) in enumerate(items):
    image=Image.open(path).convert('RGBA');box=image.getchannel('A').getbbox()
    crop=image.crop(box);crop.thumbnail((300,325),Image.Resampling.LANCZOS)
    board.paste(crop,(i*330+(330-crop.width)//2,55+(325-crop.height)//2),crop)
    draw.text((i*330+12,15),label,font=font(19),fill='white')
    draw.text((i*330+12,395),str(image.size)+' bbox '+str(box),font=font(11),fill='white')
    pixels=np.asarray(image)
    # At an exterior crossing, count only the opaque black border before paint.
    samples=[]
    for y in range(box[1]+1,box[3]-1):
        opaque=np.where(pixels[y,:,3]>240)[0]
        if not len(opaque):continue
        start=int(opaque.min());width=0
        for x in range(start,image.width):
            if pixels[y,x,3]>240 and max(pixels[y,x,:3])<24:width+=1
            else:break
        if 2<=width<=20:samples.append(width)
    records.append(dict(label=label,path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        size=image.size,bounds=box,median_horizontal_black_boundary_pixels=float(np.median(samples)) if samples else None,
        boundary_sample_note='Horizontal opaque edge crossings only; curvature/slopes mean this is not the author original stroke setting'))
target=HERE/'analysis';target.mkdir(exist_ok=True)
board.save(target/'aircraft_references.png')
(target/'aircraft_references.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print('AIRCRAFT_REFERENCE_AUDIT_PASS: Main P47/A10/MH60 and Arsenal P47/DH4')

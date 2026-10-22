"""Compare turret body readability at equal screen width; diagnostic only."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageOps
import io,subprocess
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'Audit/Stage2';OUT.mkdir(exist_ok=True)
repo=ROOT.parents[1]
before=subprocess.check_output(['git','show','c85fd5a6:ArtSource/M2A3Bradley/RimWorld/VF_East/turret_top_east.png'],cwd=repo)
sources=[('RimThunder target',Image.open('C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M2A3/vehicle_turret.png').convert('RGBA').transpose(Image.Transpose.ROTATE_270)),
 ('Stage 1',Image.open(io.BytesIO(before)).convert('RGBA')),
 ('Stage 2',Image.open(ROOT/'RimWorld/VF_East/turret_top_east.png').convert('RGBA'))]
board=Image.new('RGB',(1020,760),(95,95,95));draw=ImageDraw.Draw(board)
for i,(label,im) in enumerate(sources):
    box=im.getchannel('A').point(lambda a:255 if a>20 else 0).getbbox();im=im.crop(box)
    im=im.convert('RGBa').resize((280,round(im.height*280/im.width)),Image.Resampling.LANCZOS).convert('RGBA')
    for row in range(2):
        shown=im
        if row:
            gray=ImageOps.grayscale(im);shown=Image.merge('RGBA',(gray,gray,gray,im.getchannel('A')))
        x=i*340+30;y=row*360+40;board.paste(shown,(x,y),shown);draw.text((x,y-23),label+(' gray' if row else ''),fill='white')
draw.text((20,735),'Turret layers without separate barrel. Equal visible width 280px; comparison images are not game assets.',fill='white')
board.save(OUT/'turret_comparison.png')

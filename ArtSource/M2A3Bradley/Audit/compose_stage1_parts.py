from pathlib import Path
from PIL import Image,ImageDraw
import math
root=Path(__file__).resolve().parent/'Stage1'
paths=sorted(root.glob('WT_*.png'))
board=Image.new('RGB',(6*210,math.ceil(len(paths)/6)*225),(85,85,85));d=ImageDraw.Draw(board)
for i,path in enumerate(paths):
    im=Image.open(path).convert('RGBA').resize((200,200),Image.Resampling.LANCZOS)
    x=i%6*210+5;y=i//6*225
    board.paste(im,(x,y),im);d.text((x,y+202),path.stem.removeprefix('WT_'),fill='white')
board.save(root/'source_parts_atlas.png')

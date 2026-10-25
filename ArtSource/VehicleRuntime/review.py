"""Compare delivered hulls at the same world scale, with room for the long M35."""
from pathlib import Path
import argparse
from PIL import Image,ImageDraw
parser=argparse.ArgumentParser();parser.add_argument('--mod-root',type=Path,required=True);mod=parser.parse_args().mod_root.resolve()
out=Path(__file__).resolve().parent/'output';out.mkdir(exist_ok=True)
cell=640;board=Image.new('RGB',(cell*4,1300),(67,70,72));draw=ImageDraw.Draw(board)
for row,(vehicle,size) in enumerate([('M3A3',6.19354839),('M8',12.061538461538463)]):
    for col,direction in enumerate(['east','west','north','south']):
        image=Image.open(mod/'1.6/Textures/Vehicles'/vehicle/f'vehicle_{direction}.png').convert('RGBA')
        width=round(size*64);image=image.resize((width,width),Image.Resampling.LANCZOS)
        image=image.crop(image.getchannel('A').getbbox());assert image.width<cell and image.height<600
        x=col*cell+(cell-image.width)//2;y=row*650+40+(600-image.height)//2
        board.paste(image,(x,y),image);draw.text((col*cell+8,row*650+10),f'{vehicle} {direction} / 64 px per tile',fill='white')
board.save(out/'vehicles_1024.png')
print('WORLD_SCALE_REVIEW_PASS: 4 directions, equal pixels/tile, unclipped gun silhouettes')

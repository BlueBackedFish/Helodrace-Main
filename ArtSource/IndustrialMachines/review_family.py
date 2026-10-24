"""Compose labeled visual comparisons without editing exported game textures."""
from pathlib import Path
import os
from PIL import Image,ImageDraw,ImageFont
OUT=Path(__file__).resolve().parent;ROOT=OUT.parent.parent
REVIEW=OUT/'Review';REVIEW.mkdir(exist_ok=True)
font_path=Path(os.environ.get('WINDIR','C:/Windows'))/'Fonts/segoeui.ttf'
font=ImageFont.truetype(str(font_path),17) if font_path.is_file() else ImageFont.load_default()
small=ImageFont.truetype(str(font_path),14) if font_path.is_file() else font
machines=[('HD_SmallSteamEngine','Small steam plant'),('HD_LargeSteamEngine','Large steam engine'),
          ('HD_IndustrialDieselEngine','Stationary diesel'),('HD_EarlyIndustrialGenerator','DC dynamo'),
          ('HD_EarlyIndustrialLargeMotor','Electric motor')]
def tile(image_path,title,subtitle,normalize=True):
    board=Image.new('RGBA',(288,310),(70,73,72,255));draw=ImageDraw.Draw(board)
    draw.text((14,10),title,font=font,fill=(242,242,238));draw.text((14,34),subtitle,font=small,fill=(185,197,192))
    im=Image.open(image_path).convert('RGBA')
    if normalize:
        box=im.getbbox();im=im.crop(box);im.thumbnail((250,245),Image.Resampling.LANCZOS)
    else:im=im.resize((256,256),Image.Resampling.LANCZOS)
    board.alpha_composite(im,((288-im.width)//2,56+(250-im.height)//2));return board
context=Image.new('RGBA',(1152,620),(70,73,72,255))
refs=[('HD_LineShaftMillingMachine','Milling machine'),('HD_LineShaftPress','Industrial press'),('HD_RollingMC','Rolling mill')]
items=[(stem,label,'Existing workshop sprite') for stem,label in refs]+[(stem,label,'Updated period machine') for stem,label in machines]
for i,(stem,title,subtitle) in enumerate(items):
    context.alpha_composite(tile(ROOT/'Textures/Buildings'/f'{stem}_north.png',title,subtitle),(i%4*288,i//4*310))
context.convert('RGB').save(REVIEW/'family_context.png')
compare=Image.new('RGBA',(576,1550),(70,73,72,255))
for i,(stem,label) in enumerate(machines):
    compare.alpha_composite(tile(REVIEW/'Before'/f'{stem}_north.png',label,'Before',False),(0,i*310))
    compare.alpha_composite(tile(ROOT/'Textures/Buildings'/f'{stem}_north.png',label,'After',False),(288,i*310))
compare.convert('RGB').save(REVIEW/'before_after_north.png')
print('Saved family context and five before/after comparisons; texture files unchanged.')

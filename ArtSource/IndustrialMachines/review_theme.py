"""Theme contact sheets; compositing only, never modifies game sprites."""
from pathlib import Path
import io,json,subprocess
from PIL import Image,ImageDraw,ImageFont
from theme_preview import game_image,themed_stems,save_review
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',16)
BG=(182,183,179);INK=(29,32,31)
groups=[('첨단 설비 · 흰색 외장',[
 ('HD_VIMFurnace','진공 전기유도로'),('HD_SemiconductorFab','반도체 팹'),('HD_MultiTaskMachine2000','복합 가공기')]),
 ('컴퓨터 제어 · 강철색 + 어두운 시안',[('HD_CNCLathe','CNC 선반'),('HD_CNCMillingMachine','CNC 밀링기'),('HD_MachiningCenter1970','머시닝 센터')]),
 ('일반 산업 설비 · 바닐라 강철색',[('HD_LargeSteamEngine','대형 증기기관'),('HD_HaberBoschHighPressureReactor','고압 반응기'),('HD_BlastFurnace','고로')])]

def sprite(stem,direction='north',before=False):
    path='Textures/Buildings/'+stem+'_'+direction+'.png'
    if before:return Image.open(io.BytesIO(subprocess.check_output(['git','show','ceb93af4:'+path],cwd=ROOT))).convert('RGBA')
    return game_image(ROOT/path)

def place(board,im,box):
    im=im.crop(im.getbbox());im.thumbnail((box[2],box[3]),Image.Resampling.LANCZOS)
    board.paste(im,(box[0]+(box[2]-im.width)//2,box[1]+(box[3]-im.height)//2),im)

sheet=Image.new('RGB',(1200,1120),BG);draw=ImageDraw.Draw(sheet)
draw.text((20,12),'건물 컬러 테마 / 마스크 적용 미리보기 / North · 64°',font=font,fill=INK)
for row,(title,items) in enumerate(groups):
    y=65+row*340;draw.text((20,y),title,font=font,fill=INK)
    for col,(stem,label) in enumerate(items):
        place(sheet,sprite(stem),(col*400+15,y+38,370,240))
        draw.text((col*400+22,y+285),label,font=font,fill=INK)
draw.text((20,1090),'Applied mask preview / geometry unchanged / thumbnails fitted for color comparison',font=small,fill=INK)
save_review(sheet,OUT/'theme_overview.png')

items=sorted(themed_stems());sheet=Image.new('RGB',(1400,1805),BG);draw=ImageDraw.Draw(sheet)
draw.text((16,8),'ALL 29 INDUSTRIAL BUILDINGS / applied white + vanilla Steel masks / North',font=small,fill=INK)
for i,stem in enumerate(items):
    x=i%5*280;y=42+i//5*290
    place(sheet,sprite(stem),(x+10,y,260,235))
    label=stem.removeprefix('HD_');draw.text((x+10,y+239),label,font=small,fill=INK)
    family=themed_stems()[stem]['building_theme_family'];draw.text((x+10,y+260),family,font=small,fill=(53,66,64))
save_review(sheet,OUT/'theme_all_buildings.png')

sheet=Image.new('RGB',(1280,700),BG);draw=ImageDraw.Draw(sheet)
draw.text((15,8),'FURNACE UPPER SECTIONS / before - after / North and East / unchanged geometry',font=small,fill=INK)
for row,stem in enumerate(['HD_Converter','HD_BlastFurnace']):
    for col,(direction,before) in enumerate([('north',True),('north',False),('east',True),('east',False)]):
        x=col*320;y=40+row*325;place(sheet,sprite(stem,direction,before),(x+8,y,304,270))
        draw.text((x+12,y+277),stem.removeprefix('HD_')+' / '+direction+' / '+('before' if before else 'after'),font=small,fill=INK)
save_review(sheet,OUT/'theme_furnace_before_after.png')
print('Saved theme overview, 29-building contact sheet and furnace before/after comparison.')

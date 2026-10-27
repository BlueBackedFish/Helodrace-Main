"""Transparent cutout exports and contact sheets; shared by new prop assets."""
from pathlib import Path
import math,io,os,tempfile,hashlib,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFont
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'VehiclePipeline'))
from images import resize_linear

def opened(path):
    with Image.open(path) as im:return im.convert('RGBA')

def save(image,path):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True);buffer=io.BytesIO();image.save(buffer,format='PNG')
    with tempfile.NamedTemporaryFile(dir=path.parent,suffix='.png',delete=False) as f:f.write(buffer.getvalue());temporary=f.name
    os.replace(temporary,path)

def horizontal_max(alpha,r):
    if r==0:return alpha
    h,w=alpha.shape;k=2*r+1;padded=np.pad(alpha,((0,0),(r,r+(-w-2*r)%k)));blocks=padded.reshape(h,-1,k)
    left=np.maximum.accumulate(blocks,axis=2).reshape(h,-1);right=np.maximum.accumulate(blocks[:,:,::-1],axis=2)[:,:,::-1].reshape(h,-1)
    return np.maximum(right[:,:w],left[:,k-1:k-1+w])

def grown(image,r):
    alpha=np.asarray(image.getchannel('A'));out=np.zeros_like(alpha);cache={}
    for dy in range(math.floor(r)+1):
        span=math.floor(math.sqrt(max(0,r*r-dy*dy)))
        if span not in cache:cache[span]=horizontal_max(alpha,span)
        row=cache[span]
        if dy==0:np.maximum(out,row,out=out)
        else:
            np.maximum(out[dy:],row[:-dy],out=out[dy:]);np.maximum(out[:-dy],row[dy:],out=out[:-dy])
    return out

def outline(image,r):
    back=Image.new('RGBA',image.size);back.putalpha(Image.fromarray(grown(image,r)));return Image.alpha_composite(back,image)

def export(image,path,resolution,stroke):
    output=resize_linear(outline(image,stroke*image.width/resolution),resolution);alpha=output.getchannel('A');bbox=alpha.getbbox();assert bbox
    margin=min(bbox[0],bbox[1],resolution-bbox[2],resolution-bbox[3]);assert margin>=12,(path,bbox)
    assert alpha.getextrema()==(0,255);save(output,path)
    return dict(size=list(output.size),alpha_bbox=bbox,minimum_margin=margin,sha256=hashlib.sha256(Path(path).read_bytes()).hexdigest())

def sheet(entries,path,columns=2,cell=420):
    rows=math.ceil(len(entries)/columns);canvas=Image.new('RGB',(columns*cell,rows*(cell+32)),(65,70,73));draw=ImageDraw.Draw(canvas);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',17)
    for index,(label,image) in enumerate(entries):
        x=index%columns*cell;y=index//columns*(cell+32);draw.text((x+12,y+7),label,font=font,fill='white');scaled=resize_linear(image,cell);canvas.paste(scaled,(x,y+32),scaled)
    save(canvas,path)

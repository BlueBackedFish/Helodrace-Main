"""Build two editable SVG game-art studies of the M18A1 exterior."""

from hashlib import sha256
import json
import math
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
SOURCE = REPO.parent / ".texture/ref/claymore-mine-m18-m18a1"
INK = "#090a08"
P = dict(front="#596244", dark="#434b32", edge="#72795a", top="#687152",
         lower="#4c553a", metal="#4c4f45", metal_light="#6e7164", inset="#353d29",
         letters="#7b8260")
# Display-space art pose only; this is not an emplacement specification.
PITCH = math.radians(14)
SCALE = 340

GLYPHS = {
    "F": "M0 10V0H6 M0 4.5H4.8", "R": "M0 10V0H4.5L6 1.5V3.5L4.5 5H0 M3 5L6 10",
    "O": "M1 0H5L6 1V9L5 10H1L0 9V1Z", "N": "M0 10V0L6 10V0",
    "T": "M0 0H6 M3 0V10", "W": "M0 0L1 10L3 6L5 10L6 0",
    "A": "M0 10L2 0H4L6 10 M1 6H5", "D": "M0 0H4L6 2V8L4 10H0Z",
    "E": "M6 0H0V10H6 M0 5H4.5", "M": "M0 10V0L3 4L6 0V10",
    "Y": "M0 0L3 5L6 0 M3 5V10",
}


def front_y(x):
    return .17 + .15 * (1 - (x / 1.05) ** 2)


def convex_hull(points):
    points=sorted(set(points))
    def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    lower=[];upper=[]
    for point in points:
        while len(lower)>=2 and cross(lower[-2],lower[-1],point)<=0:lower.pop()
        lower.append(point)
    for point in reversed(points):
        while len(upper)>=2 and cross(upper[-2],upper[-1],point)<=0:upper.pop()
        upper.append(point)
    return lower[:-1]+upper[:-1]


class Drawing:
    def __init__(self, pose):
        self.pose = pose
        self.parts = []
        self.groups = []

    def project(self, point):
        x, y, z = point
        if self.pose == "item":
            return (512 + SCALE * x, 512 - SCALE * (z - .89))
        # Tilt the housing, then look vertically down. The legs are drawn in
        # their separate ground-space group and are not tilted with the body.
        y = y * math.cos(PITCH) - (z - .88) * math.sin(PITCH)
        return (512 + SCALE * x, 512 - SCALE * y)

    def ground_project(self, point):
        return (512 + SCALE * point[0], 512 - SCALE * point[1])

    def group(self, name):
        self.parts.append('<g id="%s">' % name)
        self.groups.append(name)

    def end(self):
        self.parts.append('</g>')

    def path(self, points, color, stroke=0, close=True, ground=False):
        project = self.ground_project if ground else self.project
        coords = [project(p) for p in points]
        self.screen_path(coords,color,stroke,close)

    def screen_path(self,coords,color,stroke=0,close=True):
        data = "M" + " L".join("%.2f %.2f" % xy for xy in coords)
        if close:
            data += " Z"
        self.parts.append('<path d="%s" fill="%s" stroke="%s" stroke-width="%s" '
                          'stroke-linejoin="round" stroke-linecap="round"/>' %
                          (data, color, INK if stroke else "none", stroke))

    def metal_rod(self, points, ground=False):
        self.path(points, "none", 15, close=False, ground=ground)
        project = self.ground_project if ground else self.project
        data = "M" + " L".join("%.2f %.2f" % project(p) for p in points)
        self.parts.append('<path d="%s" fill="none" stroke="%s" stroke-width="7" '
                          'stroke-linejoin="round" stroke-linecap="round"/>' % (data, P["metal"]))

    def box(self, x0, x1, y0, y1, z0, z1, color):
        self.path([(x0,y0,z0),(x1,y0,z0),(x1,y0,z1),(x0,y0,z1)], P["dark"], 5)
        self.path([(x0,y1,z0),(x1,y1,z0),(x1,y1,z1),(x0,y1,z1)], color, 5)
        self.path([(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)], P["top"], 5)

    def cap(self, cx):
        cy = front_y(cx) - .16
        n = 12
        circle = [(cx+.088*math.cos(i*math.tau/n), cy+.088*math.sin(i*math.tau/n)) for i in range(n)]
        points=[self.project((x,y,z)) for x,y in circle for z in [1.36,1.44]]
        self.screen_path(convex_hull(points),P["dark"],5)
        if self.pose!='item':
            self.path([(x,y,1.44) for x,y in circle], P["top"], 5)
            self.path([(cx-.045,cy,1.442),(cx+.045,cy,1.442)], "none", 3, close=False)

    def face_strip(self, z0, z1, color, inset=.025):
        xs = [-1.05+inset + i*(2.1-2*inset)/40 for i in range(41)]
        self.path([(x,front_y(x)+.002,z1) for x in xs] +
                  [(x,front_y(x)+.002,z0) for x in reversed(xs)], color)

    def legend(self, text, ztop, height, step):
        # Convert a small single-line vector alphabet into exterior surface
        # paths. No external fonts, embedded bitmap text, or SVG text objects.
        import re
        start = -(len(text)*step-step*.25)/2
        for i, char in enumerate(text):
            if char == " ":
                continue
            tokens = re.findall(r'[MLHVZ]|-?\d+(?:\.\d+)?', GLYPHS[char])
            cursor, origin, command, j = [0,0], [0,0], None, 0
            paths, line = [], []
            while j < len(tokens):
                token = tokens[j]
                if token.isalpha():
                    command=token;j+=1
                    if command == 'Z':
                        line.append(origin[:]);continue
                    if command == 'M' and line:
                        paths.append(line);line=[]
                if j >= len(tokens):break
                if tokens[j].isalpha():continue
                if command in ('M','L'):
                    cursor=[float(tokens[j]),float(tokens[j+1])];j+=2
                    if command=='M':origin=cursor[:];command='L'
                elif command=='H':cursor=[float(tokens[j]),cursor[1]];j+=1
                elif command=='V':cursor=[cursor[0],float(tokens[j])];j+=1
                line.append(cursor[:])
            if line:paths.append(line)
            for line in paths:
                points=[]
                for u,v in line:
                    x=start+i*step+u/6*step*.75
                    points.append((x,front_y(x)+.004,ztop-v/10*height))
                project=self.project
                d='M'+' L'.join('%.2f %.2f'%project(p) for p in points)
                self.parts.append('<path d="%s" fill="none" stroke="%s" stroke-width="%s" '
                                  'stroke-linejoin="round" stroke-linecap="round"/>'%
                                  (d,P['letters'],3.5 if self.pose=='item' else 1.8))

    def build(self):
        self.group("folded-rear-supports" if self.pose=="item" else "deployed-supports")
        for sign in [-1,1]:
            if self.pose=="item":
                for offset in [-.025,.025]:
                    self.metal_rod([(sign*.73+offset,-.12,.40),(sign*.74+offset,-.12,.75),
                                    (sign*.70+offset,-.12,1.21)])
            else:
                for direction in [-1,1]:
                    self.metal_rod([(sign*.72,.005,.40),(sign*.81,direction*.22,.22),
                                    (sign*.94,direction*.56,0)], ground=True)
                    # Short inner face, without a cast shadow on the ground.
                    self.path([(sign*.80,direction*.24,0),(sign*.90,direction*.48,0)],
                              "none", 2, close=False, ground=True)
        self.end()
        self.group("hinges")
        for sign in [-1,1]:
            self.box(sign*.73-.075,sign*.73+.075,-.08,.09,.35,.48,P["metal"])
        self.end()

        # Rounded front/rear face contour, sampled from a rounded rectangle.
        profile=[]
        radius=.055
        for cx,cz,start in [(1.05-radius,1.34-radius,0),(-1.05+radius,1.34-radius,90),
                             (-1.05+radius,.42+radius,180),(1.05-radius,.42+radius,270)]:
            for i in range(7):
                angle=math.radians(start+i*90/6)
                profile.append((cx+radius*math.cos(angle),cz+radius*math.sin(angle)))
        # Long edges must follow the curved housing surface too. Connecting
        # only the corner vertices would make the projected edges straight.
        sampled=[]
        for i,(x,z) in enumerate(profile):
            nx,nz=profile[(i+1)%len(profile)]
            count=max(1,math.ceil(abs(nx-x)/.05))
            sampled.extend((x+(nx-x)*j/count,z+(nz-z)*j/count) for j in range(count))
        profile=sampled
        self.group("housing")
        if self.pose!="item":
            self.path([(x,front_y(x)-.29,z) for x,z in profile],P["dark"],12)
            for sign in [-1,1]:
                ends=[self.project((x,front_y(x)-offset,z)) for x,z in profile
                      if sign*x>=.99 for offset in [0,.29]]
                self.screen_path(convex_hull(ends),P["dark"],10)
        self.path([(x,front_y(x),z) for x,z in profile],P["front"],12)
        self.face_strip(.45,.50,P["lower"])
        self.face_strip(1.26,1.30,P["top"])
        # Broad side bands represent molded curvature, not photographic wear.
        for sign in [-1,1]:
            self.path([(sign*.99,front_y(sign*.99)+.004,.50),
                       (sign*.91,front_y(sign*.91)+.004,.54),
                       (sign*.91,front_y(sign*.91)+.004,1.23),
                       (sign*.99,front_y(sign*.99)+.004,1.27)],P["lower"])
        if self.pose!="item":
            xs=[-1.02+i*2.04/40 for i in range(41)]
            self.path([(x,front_y(x),1.34) for x in xs]+
                      [(x,front_y(x)-.29,1.34) for x in reversed(xs)],P["top"],7)
            # Shallow rim along the front edge of the top surface.
            self.path([(x,front_y(x)-.015,1.343) for x in xs]+
                      [(x,front_y(x)-.04,1.343) for x in reversed(xs)],P["edge"])
        self.end()
        self.group("molded-front-marking")
        self.legend("FRONT",1.07,.105,.092)
        self.legend("TOWARD ENEMY",.84,.105,.105)
        self.end()
        self.group("upper-caps-and-sight")
        self.cap(-.79);self.cap(.79)
        self.box(-.19,.19,.025,.22,1.31,1.47,P["front"])
        self.path([(-.105,.223,1.385),(.105,.223,1.385),(.105,.223,1.425),(-.105,.223,1.425)],P["inset"])
        self.end()
        return ('<?xml version="1.0" encoding="UTF-8"?>\n'
                '<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">\n'
                '<title>M18A1 exterior game sprite: %s</title>\n'%self.pose+
                '<desc>Original vector artwork. Transparent background; solid color surfaces.</desc>\n'+
                '\n'.join(self.parts)+'\n</svg>\n')


def main():
    output=ROOT/'Output';output.mkdir(exist_ok=True)
    magick=shutil.which('magick') or r'C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe'
    data=[]
    for pose,name in [('item','HD_M18A1_Item'),('deployed','HD_M18A1_Deployed')]:
        svg=output/(name+'.svg');svg.write_text(Drawing(pose).build(),encoding='utf-8')
        document=ET.parse(svg)
        assert not any(e.tag.rsplit('}',1)[-1] in ['image','text','filter','linearGradient','radialGradient']
                       for e in document.iter())
        png=output/(name+'.png')
        subprocess.run([magick,'-background','none',str(svg),'PNG32:'+str(png)],check=True)
        im=Image.open(png).convert('RGBA');bbox=im.getbbox()
        assert im.size==(1024,1024) and bbox and min(bbox)>0 and max(bbox)<1024
        assert im.getchannel('A').getextrema()==(0,255)
        for size in [256,128]:
            im.resize((size,size),Image.LANCZOS).save(output/(name+'_'+str(size)+'.png'))
        data.append(dict(pose=pose,svg=svg.name,png=png.name,alpha_bbox=bbox,
                         path_count=sum(e.tag.endswith('path') for e in document.iter())))
    board=Image.new('RGB',(1200,820),(218,222,224));draw=ImageDraw.Draw(board)
    font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',22)
    for column,(name,label) in enumerate([('HD_M18A1_Item','FRONT / SUPPORTS FOLDED'),
                                        ('HD_M18A1_Deployed','TOP DOWN / INSTALLED, FRONT TILTED UP')]):
        x=column*600
        draw.text((x+20,20),label,fill=(32,35,28),font=font)
        large=Image.open(output/(name+'.png')).convert('RGBA')
        large=large.crop(large.getbbox());large.thumbnail((512,380),Image.LANCZOS)
        bg=Image.new('RGBA',(540,400),(185,192,192,255))
        bg.alpha_composite(large,((540-large.width)//2,(400-large.height)//2))
        board.paste(bg.convert('RGB'),(x+30,65))
        for size,y in [(256,495),(128,495)]:
            im=Image.open(output/(name+'.png')).convert('RGBA').resize((size,size),Image.LANCZOS)
            px=x+30 if size!=128 else x+385
            bg=Image.new('RGBA',im.size,(185,192,192,255));bg.alpha_composite(im)
            board.paste(bg.convert('RGB'),(px,y))
        draw.text((x+30,770),'256 px',fill=(32,35,28),font=font)
        draw.text((x+385,640),'128 px',fill=(32,35,28),font=font)
    board.save(ROOT/'preview.png')
    reference_files=['source/Clay.obj','textures/ClayMore_Base_Color.png']
    record={'artwork':data,'projection':'orthographic; deployed forward is image up',
            'source_sha256':{p:sha256((SOURCE/p).read_bytes()).hexdigest() for p in reference_files},
            'game_integration':False,'krita_import_verified':False}
    (ROOT/'validation.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
    print('Built 2 native SVGs and transparent PNGs; canvas, margins, alpha, and vector-only content verified.')


if __name__=='__main__':main()

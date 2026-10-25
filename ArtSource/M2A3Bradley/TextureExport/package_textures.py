"""Package no-outline PNGs, registered states, per-module armor, and metadata."""
from pathlib import Path
import hashlib, json, sys, itertools, zipfile
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parents[1]/'VehiclePipeline'))
from images import resize_linear, composite_linear, check_margin, font
cfg=json.loads((HERE/'settings.json').read_text())
OUT=HERE/'output'
verify_only='--verify-only' in sys.argv
variants=[] if '--archive-only' in sys.argv else ([v for v in sys.argv[1:] if not v.startswith('--')] or list(cfg['sources']))


def save_png(image,path):
    # Replace complete files so previews/readers never observe a partial PNG.
    path=Path(path)
    staging=path.with_name('.'+path.name+'.tmp.png')
    image.save(staging,format='PNG')
    staging.replace(path)


def decode(rgb):
    return np.where(rgb<=.04045,rgb/12.92,((rgb+.055)/1.055)**2.4)


def encode(rgb):
    return np.where(rgb<=.0031308,rgb*12.92,1.055*np.maximum(0,rgb)**(1/2.4)-.055)


def rgba(array):
    return Image.fromarray(np.round(np.clip(array,0,1)*255).astype(np.uint8))


def assemble(root,base,files):
    image=Image.open(root/base).convert('RGBA')
    a=np.asarray(image,dtype=np.float32)/255
    alpha=a[:,:,3:4].copy()
    color=decode(a[:,:,:3])*alpha
    for entry in files:
        name=entry['file'] if isinstance(entry,dict) else entry
        part=Image.open(root/name).convert('RGBA')
        box=part.getchannel('A').getbbox()
        if not box:continue
        pixels=np.asarray(part.crop(box),dtype=np.float32)/255
        x0,y0,x1,y1=box
        pa=pixels[:,:,3:4]
        color[y0:y1,x0:x1]=decode(pixels[:,:,:3])*pa+color[y0:y1,x0:x1]*(1-pa)
        alpha[y0:y1,x0:x1]=pa+alpha[y0:y1,x0:x1]*(1-pa)
    straight=color/np.maximum(alpha,1e-8)
    result=np.concatenate((encode(straight),alpha),axis=2)
    result[alpha[:,:,0]<1/510]=0
    return rgba(result)


def compare(reconstructed,reference,root,layer_files):
    a=np.asarray(reconstructed,dtype=np.int16)
    b=np.asarray(reference.convert('RGBA'),dtype=np.int16)
    # Uniform opaque 5x5 neighborhoods exclude AA and surface boundaries.
    stable=b[:,:,3]==255
    lo=b[:,:,:3].copy();hi=lo.copy()
    for dy in range(-2,3):
        for dx in range(-2,3):
            sample=np.roll(b,(dy,dx),(0,1))
            stable&=sample[:,:,3]==255
            lo=np.minimum(lo,sample[:,:,:3]);hi=np.maximum(hi,sample[:,:,:3])
    stable&=np.max(hi-lo,axis=2)<=1
    # A boundary between two independently antialiased layers can lie inside a
    # uniform final face. Exclude those layer edges, not just reference edges.
    for name in layer_files:
        alpha=Image.open(root/name).convert('RGBA').getchannel('A')
        box=alpha.getbbox()
        if not box:continue
        box=(max(0,box[0]-3),max(0,box[1]-3),min(1024,box[2]+3),min(1024,box[3]+3))
        cropped=alpha.crop(box)
        low=np.asarray(cropped.filter(ImageFilter.MinFilter(5)))
        high=np.asarray(cropped.filter(ImageFilter.MaxFilter(5)))
        stable[box[1]:box[3],box[0]:box[2]]&=(low==high)&((low==0)|(low==255))
    stable[:3]=False;stable[-3:]=False;stable[:,:3]=False;stable[:,-3:]=False
    diff=np.max(abs(a[:,:,:3]-b[:,:,:3]),axis=2)
    bad=stable&(diff>2)
    count=int(stable.sum())
    return {'stable_pixels':count,'stable_pixels_error_over_2':int(bad.sum()),
            'stable_max_rgb_error_255':int(diff[stable].max()) if count else 0,
            'all_pixels_max_rgb_error_255':int(diff.max()),
            'alpha_max_error_255':int(abs(a[:,:,3]-b[:,:,3]).max())}


for variant in variants:
    root=OUT/'1024'/variant
    report=json.loads((root/'render_manifest.json').read_text())
    source=Path(report['source'])
    assert hashlib.sha256(source.read_bytes()).hexdigest()==report['source_sha256']
    validation={'variant':variant,'outline':False,'directions':{},'layers':{},'source_unchanged':True}
    if verify_only:
        validation['layers']=json.loads((root/'package_validation.json').read_text())['layers']
    for direction,info in report['directions'].items():
        tests={}
        for state,spec in info['hull_states'].items():
            overlay_files=[entry['file'] for entry in spec['overlays']]
            assembled=assemble(root,spec['file'],spec['overlays'])
            destination=f'Assembled/hull_{state}_{direction}.png'
            (root/destination).parent.mkdir(exist_ok=True)
            save_png(assembled,root/destination)
            spec['assembled']=destination
            if spec.get('installed_reference'):
                tests['era_hull_'+state]=compare(assembled,Image.open(root/spec['installed_reference']),root,[spec['file']]+overlay_files)
            parts=info['parts']
            independent=assemble(root,parts['hull_static'],[
                parts['rear_ramp_'+('open' if spec['ramp_open'] else 'closed')],
                parts['roof_hatch_'+('open' if spec['roof_open'] else 'closed')]])
            tests['moving_hull_'+state]=compare(independent,Image.open(root/spec['file']),root,[parts['hull_static'],parts['rear_ramp_'+('open' if spec['ramp_open'] else 'closed')],parts['roof_hatch_'+('open' if spec['roof_open'] else 'closed')]])
        for state,spec in info['turret_states'].items():
            assembled=assemble(root,spec['file'],spec['era_files'])
            destination=f'Assembled/turret_{state}_{direction}.png'
            save_png(assembled,root/destination)
            spec['assembled']=destination
            if spec.get('installed_reference'):
                tests['era_turret_'+state]=compare(assembled,Image.open(root/spec['installed_reference']),root,[spec['file']]+spec['era_files'])
            independent=assemble(root,info['parts']['turret_base'],[info['parts']['gun'],info['parts']['tow_'+state]])
            tests['moving_turret_'+state]=compare(independent,Image.open(root/spec['file']),root,[info['parts']['turret_base'],info['parts']['gun'],info['parts']['tow_'+state]])
        # Eight requested combinations use registered hull and turret projections.
        anchor=[round(v) for v in info['hull_turret_anchor']]
        offset=tuple(v-512 for v in anchor)
        info['previews']={}
        for hs,ts in itertools.product(info['hull_states'],info['turret_states']):
            body=Image.open(root/info['hull_states'][hs]['assembled'])
            top=Image.open(root/info['turret_states'][ts]['assembled'])
            preview=composite_linear(body,top,offset)
            destination=f'Preview/{hs}_tow_{ts}_{direction}.png'
            (root/destination).parent.mkdir(exist_ok=True)
            save_png(preview,root/destination)
            info['previews'][hs+'_tow_'+ts]=destination
        validation['directions'][direction]={'composition':tests,
            'era_module_count':len(info['era']),
            'invisible_modules':sum(not v['visible'] for v in info['era'].values())}
    # Export straight RGBA at all three sizes. Do not crop individual canvases.
    masters=sorted(root.rglob('*.png'))
    for size in cfg['sizes']:
        target=OUT/str(size)/variant
        target.mkdir(parents=True,exist_ok=True)
        for path in ([] if verify_only else masters):
            relative=path.relative_to(root)
            im=Image.open(path).convert('RGBA')
            assert im.size==(1024,1024),path
            if size!=1024:
                im=resize_linear(im,size)
                (target/relative).parent.mkdir(parents=True,exist_ok=True)
                save_png(im,target/relative)
            bounds=check_margin(im,str(relative))
            validation['layers'][f'{size}/{relative.as_posix()}']={'bounds':bounds,'visible':bool(bounds)}
        scale=size/1024
        index={'variant':variant,'size':[size,size],'outline':False,'shadows':False,
               'source_sha256':report['source_sha256'],'directions':{}}
        for direction,info in report['directions'].items():
            data=json.loads(json.dumps(info))
            data['hull_turret_anchor']=[v*scale for v in info['hull_turret_anchor']]
            data['rounded_master_hull_turret_anchor']=[round(v)*scale for v in info['hull_turret_anchor']]
            data['turret_pivot']=[size/2,size/2]
            for group in ['hull_control_pivots','turret_control_pivots']:
                data[group]={name:[v*scale for v in pivot] for name,pivot in info[group].items()}
            for entry in data['era'].values():
                entry['pivot_pixel']=[v*scale for v in entry['pivot_pixel']]
            for entry in data['components'].values():
                entry['pivot_pixel']=[v*scale for v in entry['pivot_pixel']]
            # Body-space control pivots are fixed; images stay on parent canvases.
            data['canvas_origin']='top-left; full hull canvas or yaw-centered turret canvas'
            index['directions'][direction]=data
        (target/'index.json').write_text(json.dumps(index,indent=2)+'\n')
    validation['PNG_count_per_size']=len(masters)
    validation['filter']='linear light premultiplied alpha Lanczos; no outline'
    validation['game_validation_performed']=False
    validation['status']='needs composition inspection' if any(
        t['stable_pixels_error_over_2'] for d in validation['directions'].values() for t in d['composition'].values()
    ) else 'passed'
    (root/'package_validation.json').write_text(json.dumps(validation,indent=2)+'\n')
    print('PACKAGED',variant,len(masters),'PNGs per size;',validation['status'],flush=True)

# Make a native 256px contact sheet when both variant packages are present.
if all((OUT/'256'/v/'index.json').exists() and
       set(json.loads((OUT/'256'/v/'index.json').read_text())['directions'])==set(cfg['directions'])
       for v in cfg['sources']):
    board=Image.new('RGB',(4*280,len(cfg['sources'])*len(cfg['directions'])*294+44),(68,71,74))
    draw=ImageDraw.Draw(board)
    draw.text((12,10),'Bradley / EAST · WEST · NORTH · SOUTH / 윤곽선 없음 / 256px 실제 크기',font=font(18),fill='white')
    for row,(variant,direction) in enumerate(itertools.product(cfg['sources'],cfg['directions'])):
        root=OUT/'256'/variant
        index=json.loads((root/'index.json').read_text())
        info=index['directions'][direction]
        examples=[('기본 / TOW 접힘','roof_closed_ramp_closed_tow_stowed'),
                  ('TOW 전개','roof_closed_ramp_closed_tow_deployed'),
                  ('상부 해치 열림','roof_open_ramp_closed_tow_stowed'),
                  ('후방 램프 열림','roof_closed_ramp_open_tow_stowed')]
        for col,(label,key) in enumerate(examples):
            x,y=col*280,row*294+44
            image=Image.open(root/info['previews'][key])
            board.paste(image,(x+12,y+32),image)
            draw.text((x+12,y+7),f'{variant} {direction.upper()} / {label}',font=font(13),fill='white')
    save_png(board,OUT/'review_native_256.png')
    # Runtime ZIP intentionally contains no assembled installed sprites: its
    # BUSK base can never be mistaken for a sprite with ERA already baked in.
    with zipfile.ZipFile(OUT/'Bradley_TexturePackage.zip','w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for size in [256,512]:
            for variant in cfg['sources']:
                runtime=OUT/str(size)/variant
                for folder in ['Hull','Turret','Parts','ERA','Components']:
                    for path in sorted((runtime/folder).rglob('*.png')):
                        archive.write(path,path.relative_to(OUT).as_posix())
                index=json.loads((runtime/'index.json').read_text())
                index['runtime_assets_only']=True
                for info in index['directions'].values():
                    info.pop('previews',None)
                    for group in ['hull_states','turret_states']:
                        for state in info[group].values():
                            state.pop('assembled',None)
                            state.pop('installed_reference',None)
                archive.writestr(f'{size}/{variant}/index.json',json.dumps(index,indent=2)+'\n')
        archive.write(HERE/'README.md','README.md')
        archive.write(HERE.parent/'BUSKIII/REFERENCES.md','BUSK_REFERENCES.md')
    print('TEXTURE_PACKAGE_READY',OUT/'Bradley_TexturePackage.zip',flush=True)

"""Check petroleum scenes, active definition links and independent new-art contracts."""
import bpy,os,json,math,xml.etree.ElementTree as ET
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
defs={n.findtext('defName'):n for n in ET.parse(os.path.join(ROOT,'Defs/WildWest/Buildings/Industry_WildWest.xml')).getroot().findall('ThingDef')}
assets=[('HD_BasicPumpJack','BasicPumpJack',[6,3],True),('HD_MetalPumpJack','MetalPumpJack',[6,3],False),
        ('HD_DistillationTower','DistillationTower',[3,3],True),('HD_RotaryDrill','RotaryDrill',[4,4],False)]
report={};catalog=[]
for stem,folder,footprint,active in assets:
    path=os.path.join(ROOT,'ArtSource',folder)
    with open(os.path.join(path,'validation.json')) as f:validation=json.load(f)
    assert validation['footprint']==footprint and validation['outline_pixels']==12
    assert validation['presentation_rule']=='one_model_unit_per_game_tile' and validation['ground_anchor']==[0,0,0]
    scale=validation['orthographic_scale']
    assert validation['expected_draw_size']==[scale,scale]
    if active:
        node=defs[stem]
        assert node.findtext('size')=='('+','.join(str(v) for v in footprint)+')'
        assert node.findtext('graphicData/texPath')=='Buildings/'+stem
        assert node.findtext('graphicData/graphicClass')=='Graphic_Multi'
        assert [float(v) for v in node.findtext('graphicData/drawSize').strip('()').split(',')]==[scale,scale]
        assert [float(v) for v in node.findtext('graphicData/drawOffset').strip('()').split(',')]==[0,0,0]
    else:assert stem not in defs,'New artwork was unexpectedly assigned gameplay values'
    bpy.ops.wm.open_mainfile(filepath=os.path.join(path,stem+'.blend'));bpy.context.view_layer.update()
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(meshes)>25
    for obj in meshes:
        for material in obj.data.materials:
            assert not any(n.type=='TEX_IMAGE' for n in material.node_tree.nodes)
    if folder=='BasicPumpJack':
        timber={m for o in meshes for m in o.data.materials if m.name.startswith('Vanilla_timber_')}
        assert timber and all(m.diffuse_color[0]>m.diffuse_color[1]>m.diffuse_color[2] for m in timber)
        assert all(not m['tintable'] for o in meshes for m in o.data.materials)
        assert defs[stem].findtext('stuffCategories/li')=='Woody'
    margins={}
    for direction in ['North','East','South']:
        camera=bpy.data.objects['Camera_'+direction];q=camera.rotation_euler.to_quaternion()
        forward=q@Vector((0,0,-1));angle=math.degrees(math.asin(-forward.z))
        assert camera.data.type=='ORTHO' and abs(camera.data.ortho_scale-scale)<1e-6
        assert abs(angle-64)<.001 and (forward+camera.location.normalized()).length<1e-5
        pixels=[]
        for suffix in ['', 'm']:
            im=bpy.data.images.load(os.path.join(ROOT,'Textures/Buildings',stem+'_'+direction.lower()+suffix+'.png'),check_existing=False)
            assert tuple(im.size)==(512,512);im.colorspace_settings.name='Non-Color'
            p=np.empty(512*512*4,np.float32);im.pixels.foreach_get(p);pixels.append(p.reshape(512,512,4))
        color,mask=pixels;assert np.max(abs(color[:,:,3]-mask[:,:,3]))<.001
        yy,xx=np.where(color[:,:,3]>.5);margin=int(min(xx.min(),yy.min(),511-xx.max(),511-yy.max()))
        assert margin>=24;margins[direction]=margin
        stroke=(color[:,:,:3].max(axis=2)<.001)&(color[:,:,3]>.95)
        assert stroke.sum()>1000 and np.max(mask[:,:,:3][stroke])<=2.01/255
        # Eevee's 8-bit black export may contain a one-step dither value.
        if folder=='BasicPumpJack':assert np.max(mask[:,:,:3])<=1.01/255
        else:assert np.sum((mask[:,:,0]>.8)&(mask[:,:,3]>.95))>100
    report[stem]={'gameplay_definition_active':active,'footprint':footprint,'orthographic_scale_and_draw_size':scale,
                  'views_verified':3,'color_mask_files':6,'outline_pixels':12,'margins':margins,
                  'true_mesh_geometry_no_image_planes':True}
    catalog.append({'texture_prefix':'Buildings/'+stem,'source_folder':'ArtSource/'+folder,'blend':stem+'.blend',
                    'footprint':footprint,'draw_size':[scale,scale],'draw_offset':[0,0,0],
                    'graphic_class':'Graphic_Multi','gameplay_definition_active':active,
                    'status':'existing definition updated' if active else 'artwork only; gameplay definition not created'})
# The rotary floor really has an opening around the drill string.
for name in ['02_Left_drill_deck','03_Right_drill_deck','04_Front_drill_deck','05_Rear_drill_deck']:
    o=bpy.data.objects[name];points=[o.matrix_world@v.co for v in o.data.vertices]
    xmin,xmax=min(p.x for p in points),max(p.x for p in points)
    ymin,ymax=min(p.y for p in points),max(p.y for p in points)
    assert max(0,min(xmax,-.12)-max(xmin,-.68))*max(0,min(ymax,.63)-max(ymin,.07))<1e-6
report['rotary_table_floor_opening_verified']=True;report['in_game_tested']=False
with open(os.path.join(OUT,'validation.json'),'w') as f:json.dump(report,f,indent=2)
with open(os.path.join(OUT,'asset_catalog.json'),'w') as f:json.dump(catalog,f,indent=2)
print('Verified four petroleum models, 12 cameras, 24 PNGs, fixed timber masks, two definition links and rotary floor opening.')

"""Authored local steps for two raised gun-area panels, below the higher lid."""
from height_palette import height_luminance,contrast_luminance,final_paint,COLOR_ATTRIBUTE,LUMINANCE_ATTRIBUTE,SATURATION_ATTRIBUTE
from cel_patches import CEL_ATTRIBUTE
LOCAL_ATTRIBUTE='Local raised panel'

def panel_faces(obj,target):
    me=obj.data
    if target['selection']=='cel_patch':
        patch=me.attributes[CEL_ATTRIBUTE].data[target['anchor_polygon']].value
        assert patch>=0,(obj.name,target['name'])
        return [p.index for p in me.polygons if me.attributes[CEL_ATTRIBUTE].data[p.index].value==patch]
    assert target['selection']=='upward_faces'
    return [p.index for p in me.polygons if p.normal.z>=target['minimum_normal_z']]

def apply_local_panels(obj,settings):
    me=obj.data
    if not me.attributes.get(LOCAL_ATTRIBUTE):me.attributes.new(LOCAL_ATTRIBUTE,'INT','FACE')
    for v in me.attributes[LOCAL_ATTRIBUTE].data:v.value=0
    authored=settings['local_raised_panels'];report=[]
    roof=contrast_luminance(height_luminance(authored['roof_reference_height'],settings),settings)
    ceiling=contrast_luminance(height_luminance(authored['higher_lid_reference_height'],settings),settings)-authored['higher_lid_margin']
    color=me.color_attributes[COLOR_ATTRIBUTE];value=me.attributes[LUMINANCE_ATTRIBUTE];sat=me.attributes[SATURATION_ATTRIBUTE]
    for marker,target in enumerate(authored['targets'],1):
        if obj.name!=target['object']:continue
        ids=panel_faces(obj,target);assert ids
        assert all(me.materials[me.polygons[i].material_index].get('paint_role') for i in ids),'Do not recolor dark aperture/optics materials'
        old=[value.data[li].value for i in ids for li in me.polygons[i].loop_indices]
        reference=max(old) if target['selection']=='cel_patch' else sum(old)/len(old)
        top=roof+authored['step']*target['tier']
        for i in ids:
            p=me.polygons[i];role=me.materials[p.material_index]['paint_role']
            me.attributes[LOCAL_ATTRIBUTE].data[i].value=marker
            for li in p.loop_indices:
                y=min(ceiling,top+(value.data[li].value-reference)*target['gradient_gain'])
                rgb,y,saturation=final_paint(0,p.normal,role,1,settings,final_value=y)
                color.data[li].color=(*rgb,1);value.data[li].value=y;sat.data[li].value=saturation
        new=[value.data[li].value for i in ids for li in me.polygons[i].loop_indices]
        report.append({'name':target['name'],'faces':ids,'tier':target['tier'],'before':[min(old),max(old)],'after':[min(new),max(new)],'roof_luminance':roof,'ceiling':ceiling})
    return report

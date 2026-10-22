"""Face-set bindings for raised covers embedded inside a larger object."""
from height_palette import contact_levels,final_paint,COLOR_ATTRIBUTE,LUMINANCE_ATTRIBUTE,SATURATION_ATTRIBUTE
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
    report=[];levels=contact_levels(settings)
    color=me.color_attributes[COLOR_ATTRIBUTE];value=me.attributes[LUMINANCE_ATTRIBUTE];sat=me.attributes[SATURATION_ATTRIBUTE]
    for marker,target in enumerate(settings['local_raised_panels']['targets'],1):
        if obj.name!=target['object']:continue
        ids=panel_faces(obj,target);assert ids
        assert all(me.materials[me.polygons[i].material_index].get('paint_role') for i in ids),'Do not recolor aperture/optics'
        old=[value.data[li].value for i in ids for li in me.polygons[i].loop_indices]
        for i in ids:
            p=me.polygons[i];role=me.materials[p.material_index]['paint_role']
            me.attributes[LOCAL_ATTRIBUTE].data[i].value=marker
            rgb,y,saturation=final_paint(p.normal,role,1,settings,final_value=levels[target['node']])
            for li in p.loop_indices:
                color.data[li].color=(*rgb,1);value.data[li].value=y;sat.data[li].value=saturation
        new=[value.data[li].value for i in ids for li in me.polygons[i].loop_indices]
        report.append({'name':target['name'],'faces':ids,'node':target['node'],'before':[min(old),max(old)],'after':[min(new),max(new)]})
    return report

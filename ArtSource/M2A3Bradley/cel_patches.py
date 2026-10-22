"""Single cylinder sides, optional cap colors, and two-tone noncylindrical curves."""
from collections import defaultdict
from math import cos,radians
from mathutils import Vector
from mesh_regions import regions,position_key
from height_palette import top_luminance
import numpy as np
CEL_ATTRIBUTE='Cel paint patch'
BAND_ATTRIBUTE='Cel paint band'
BROAD_ATTRIBUTE='Broad hatch paint'
CYLINDER_ATTRIBUTE='Cylinder side group'


def cylinder_axis(me,ids):
    """Recognize a normal ring, including optional axial end caps.

    Unique normals avoid a triangulated cap dominating the fit. A side must
    wrap in at least four directions; a pair of flat plates is not a cylinder.
    """
    normals=np.array(sorted({tuple(round(v,4) for v in me.polygons[i].normal) for i in ids}))
    if len(normals)<4:return None
    _,axes=np.linalg.eigh(normals.T@normals)
    candidates=[]
    for axis in axes.T:
        dots=abs(normals@axis);side=dots<.16
        if side.sum()<4 or np.mean(side|(dots>.94))<.88:continue
        ring=normals[side];eigen=np.linalg.eigvalsh(ring.T@ring)
        if eigen[1]<.12*eigen[2]:continue
        candidates.append((float(np.mean(side|(dots>.94))),int(side.sum()),axis))
    return max(candidates,key=lambda x:x[:2])[2] if candidates else None

def broad_hatch_faces(obj,role,settings):
    """Select the lid by projected top area, independent of polygon density."""
    if role not in settings['broad_hatch_paint']['roles']:return set()
    me=obj.data;groups=regions(me)
    if not groups:return set()
    main=max(groups,key=lambda ids:sum(me.polygons[i].area*max(0,me.polygons[i].normal.z) for i in ids))
    return {i for i in main if me.materials[me.polygons[i].material_index].get('paint_role')==role}

def curved_patches(obj,role,settings,broad):
    c=settings['cel_shading'];me=obj.data
    allow_two_tone=role in c['roles']
    paint={p.index for p in me.polygons if me.materials[p.material_index].get('paint_role')}
    if not paint:return []
    whole=[];used=set(broad)
    # Hatch pins/hinges are disconnected mechanical components. Include their
    # end caps in the same patch, while preserving the lid itself.
    if role in {'turret_hatch_a','turret_hatch_b','driver_hatch','rear_roof_hatch'} and c['whole_small_hatch_components']:
        groups=regions(me)
        for group in groups:
            ids=[i for i in group if i in paint and i not in broad]
            if not ids:continue
            material_roles={me.materials[me.polygons[i].material_index].get('paint_role') for i in ids}
            if len(material_roles)!=1:continue
            whole.append(ids);used.update(ids)
    remaining=paint-used;leaders={i:i for i in remaining};edge_faces=defaultdict(list)
    def find(i):
        while leaders[i]!=i:leaders[i]=leaders[leaders[i]];i=leaders[i]
        return i
    for i in remaining:
        p=me.polygons[i]
        for a,b in zip(p.vertices,list(p.vertices[1:])+[p.vertices[0]]):
            edge_faces[tuple(sorted((position_key(me.vertices[a].co),position_key(me.vertices[b].co))))].append(i)
    threshold=cos(radians(c['smooth_patch_angle_degrees']))
    for ids in edge_faces.values():
        if len(ids)!=2:continue
        p,q=[me.polygons[i] for i in ids]
        if p.material_index!=q.material_index or p.normal.dot(q.normal)<threshold:continue
        leaders[find(p.index)]=find(q.index)
    grouped=defaultdict(list)
    for i in remaining:grouped[find(i)].append(i)
    for ids in grouped.values():
        normals={tuple(round(v,2) for v in me.polygons[i].normal) for i in ids}
        if len(normals)<c['minimum_normal_directions']:continue
        if sum(me.polygons[i].area for i in ids)<c['minimum_patch_area']:continue
        # The side-single rule also covers cylinders outside the old cel-role
        # list. Do not introduce two-tone bands on other unlisted surfaces.
        if not allow_two_tone and cylinder_axis(me,ids) is None:continue
        whole.append(ids)
    return whole

def patch_colors(obj,role,settings):
    c=settings['cel_shading'];me=obj.data;result={};report=[]
    broad=broad_hatch_faces(obj,role,settings)
    if not me.attributes.get(BROAD_ATTRIBUTE):me.attributes.new(BROAD_ATTRIBUTE,'INT','FACE')
    for p in me.polygons:me.attributes[BROAD_ATTRIBUTE].data[p.index].value=int(p.index in broad)
    for name in (CEL_ATTRIBUTE,BAND_ATTRIBUTE,CYLINDER_ATTRIBUTE):
        if not me.attributes.get(name):me.attributes.new(name,'INT','FACE')
        for v in me.attributes[name].data:v.value=-1
    axis=Vector(c['direction_axis']).normalized();normal_matrix=obj.matrix_world.to_3x3().inverted().transposed()
    recess=me.attributes.get('Recess paint')
    for index,ids in enumerate(curved_patches(obj,role,settings,broad)):
        up=max((normal_matrix@me.polygons[i].normal).normalized().z for i in ids)>.85
        mat_role=me.materials[me.polygons[ids[0]].material_index].get('paint_role')
        bright=top_luminance(mat_role or role,settings)-(c['top_light_drop'] if up else c['side_light_drop'])
        if role=='main_barrel':bright=settings['barrel_luminance'][1]
        bright=min(settings['maximum_luminance'],bright)
        dark=max(settings['minimum_luminance'],bright-c['tone_gap']);bands=set()
        cylinder=cylinder_axis(me,ids)
        if cylinder is not None:
            side=top_luminance(mat_role or role,settings)-c['cylinder_side_drop']
            if role=='main_barrel':side=settings['barrel_luminance'][0]
            dark=max(settings['minimum_luminance'],side);bright=dark+c['cylinder_cap_gap']
        for i in ids:
            normal=(normal_matrix@me.polygons[i].normal).normalized();band=int(normal.dot(axis)>=c['split_dot'])
            if cylinder is not None:
                is_side=abs(me.polygons[i].normal.dot(Vector(cylinder)))<.5
                band=int(not is_side)
                if is_side:me.attributes[CYLINDER_ATTRIBUTE].data[i].value=index
            elif recess and recess.data[i].value<.999:band=0
            result[i]=bright if band else dark;bands.add(band)
            me.attributes[CEL_ATTRIBUTE].data[i].value=index;me.attributes[BAND_ATTRIBUTE].data[i].value=band
        report.append({'patch':index,'faces':len(ids),'bands':sorted(bands),'luminance':[dark,bright],
            'mode':'single_cylinder_side' if cylinder is not None else 'two_tone_small_curve'})
    return result,report

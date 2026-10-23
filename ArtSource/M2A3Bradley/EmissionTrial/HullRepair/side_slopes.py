"""Local paint adjustment for the broad left/right hull armor slopes only."""
import bpy
from mathutils import Vector
from paint_corrections import smooth_regions

def select(obj):
    ids=[]
    for group in smooth_regions(obj.data,3):
        faces=[obj.data.polygons[i] for i in group]
        area=sum(p.area for p in faces)
        if area<.025:continue
        n=sum((obj.matrix_world.to_3x3()@p.normal*p.area for p in faces),Vector()).normalized()
        c=sum((obj.matrix_world@p.center*p.area for p in faces),Vector())/area
        if not (abs(c.x)>.8 and c.z>1.6 and .25<n.z<.97 and abs(n.x)>.2
                and abs(n.x)>abs(n.y) and n.x*c.x>0):continue
        ids.extend(p.index for p in faces if obj.data.materials[p.material_index].get('display_luminance')==108)
    assert ids and any(obj.data.polygons[i].center.x<0 for i in ids) and any(obj.data.polygons[i].center.x>0 for i in ids)
    return sorted(ids)

def apply(fixed):
    obj=bpy.data.objects['WT_@root'];ids=select(obj)
    material=fixed('hull_side_slope',132)
    if material.name not in obj.data.materials:obj.data.materials.append(material)
    slot=obj.data.materials.find(material.name)
    for i in ids:obj.data.polygons[i].material_index=slot
    return {'object':obj.name,'faces':ids,'before_display_y':108,'after_display_y':132,
            'roof_display_y':168,'roof_to_slope_gap_before':60,'roof_to_slope_gap_after':36,
            'scope':'broad exterior side slopes only; no turret-based contrast compensation'}

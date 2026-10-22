"""Authored region colors and geometry-based recess paint, independent of lighting."""
import collections,math
from mathutils import Vector
from mesh_regions import regions,position_key

RECESS_ATTRIBUTE='Recess paint'

def part_regions(obj,role):
    return regions(obj.data) if role in {'turret_assembly','turret_hatch_a'} else [list(range(len(obj.data.polygons)))]

def region_role(obj,role,indices,registry):
    me=obj.data;ids={v for pi in indices for v in me.polygons[pi].vertices}
    if not ids:return role
    pts=[obj.matrix_world@me.vertices[i].co for i in ids]
    lo=Vector(tuple(min(p[k] for p in pts) for k in range(3)));hi=Vector(tuple(max(p[k] for p in pts) for k in range(3)));center=(lo+hi)/2
    if role=='turret_hatch_a':
        # Rear hinge/handle assembly above hatch 02, including its end brackets.
        if center.y>.76 and center.z>2.79 and max(hi-lo)<.5:return 'hatch_upper_linkage'
    if role=='turret_assembly':
        for part in registry['protected_components']:
            a,b=map(Vector,part['source_bounds'])
            if all(a[k]-.015<=center[k]<=b[k]+.015 for k in range(3)) and max(hi-lo)<.60:return 'rear_stowage_bin'
        if center.y>1.0 and 2.18<center.z<2.43 and hi.x-lo.x>1:return 'rear_basket_floor'
        if lo.z>2.69 and max(hi-lo)<.90:return 'turret_roof_equipment'
    return role

def apply_recess_paint(obj,settings,role):
    """Tint small inward-facing surface patches, not cast/contact shadows.

    A signed crease across welded OBJ seams identifies concave edges. All
    triangles of a plane receive the same value, avoiding triangular blotches.
    Large roofs and exterior armor are excluded by the complete patch area.
    """
    me=obj.data;edge_faces=collections.defaultdict(list)
    leaders=list(range(len(me.polygons)))
    def find(i):
        while leaders[i]!=i:leaders[i]=leaders[leaders[i]];i=leaders[i]
        return i
    for p in me.polygons:
        for a,b in zip(p.vertices,list(p.vertices[1:])+[p.vertices[0]]):
            ka,kb=position_key(me.vertices[a].co),position_key(me.vertices[b].co)
            edge_faces[tuple(sorted((ka,kb)))].append(p.index)
    inward=set()
    for (a,b),ids in edge_faces.items():
        if len(ids)!=2:continue
        p,q=(me.polygons[i] for i in ids)
        if p.normal.dot(q.normal)>math.cos(math.radians(15)):
            leaders[find(p.index)]=find(q.index)
        if (Vector(a)-Vector(b)).length<settings['minimum_edge_length']:continue
        if not -.8<p.normal.dot(q.normal)<math.cos(math.radians(settings['minimum_crease_degrees'])):continue
        mid=(Vector(a)+Vector(b))/2
        signed=(p.center-mid).dot(q.normal)+(q.center-mid).dot(p.normal)
        if signed>1e-5:inward.update(ids)
    patches_by_id=collections.defaultdict(list)
    for p in me.polygons:patches_by_id[find(p.index)].append(p.index)
    values=[1.0]*len(me.polygons);patches=0
    for ids in patches_by_id.values():
        if role in settings['excluded_roles']:continue
        if len(ids)==1 and len(me.polygons[ids[0]].vertices)==3:continue
        area=sum(me.polygons[i].area for i in ids)
        if not .0005<area<settings['maximum_patch_area'] or not any(i in inward for i in ids):continue
        patches+=1
        for i in ids:values[i]=settings['gain']
    attr=me.attributes.get(RECESS_ATTRIBUTE) or me.attributes.new(RECESS_ATTRIBUTE,'FLOAT','FACE')
    for value,record in zip(values,attr.data):record.value=value
    return {'darkened_faces':sum(v<1 for v in values),'surface_patches':patches}

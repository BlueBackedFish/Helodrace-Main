"""Restore explicitly identified OBJ covers and antenna fittings, without a size purge."""
import bpy, hashlib, json, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT))
from mesh_regions import regions

DETAILS=[
    ('WT_GunnerSight_Door_L',[1096],'door'),
    ('WT_GunnerSight_Door_R',[312],'door'),
    ('WT_GunnerSight_Hinge_L',[1138],'hinge'),
    ('WT_GunnerSight_Hinge_R',[282],'hinge'),
    ('WT_GunnerSight_UpperMount',[18587],'housing'),
    ('WT_GunnerSight_UpperCover',[928],'cover'),
    ('WT_GunnerSight_UpperCap',[602],'cap'),
    ('WT_GunnerSight_RearFittings',[3718,3869,4027],'housing'),
    ('WT_GunnerSight_FrontPort',[2857],'port'),
    ('WT_Antenna02_BasePlate',[7556],'antenna_base'),
    ('WT_Antenna02_Spring',[7532],'antenna_spring'),
    ('WT_Antenna02_Connector',[20053],'antenna_connector'),
    ('WT_Antenna02_CableClamp',[1596],'antenna_base'),
]
# Keep the complete sight housing, roof plate, roof handles and cover brackets;
# the separate tiny screw-head components remain omitted for game-size readability.
COMMANDER_COMPONENT_SEEDS=[578,112,488,678,0,16,26]


def read_parts():
    manifest=json.loads((ROOT/'ReferenceFit/manifest.json').read_text())
    source=Path(manifest['source'])
    assert hashlib.sha256(source.read_bytes()).hexdigest()==manifest['source_sha256']
    vertices=[];parts={};group='';material=''
    for line in source.open(encoding='utf-8'):
        t=line.split()
        if not t:continue
        if t[0]=='v':
            x,y,z=map(float,t[1:4]);vertices.append((-z,-x,y+.012))
        elif t[0]=='g':group='WT_'+t[1]
        elif t[0]=='usemtl':material=t[1]
        elif t[0]=='f' and group in {'WT_bone_turret','WT_bone_commander_sight_v'}:
            ids=[int(v.split('/')[0]) for v in t[1:]]
            parts.setdefault(group,[]).append(([vertices[i-1 if i>0 else i] for i in ids],material))
    return parts,manifest


def source_mesh(items,label,world_to_local=None):
    lookup={};points=[];faces=[]
    for coords,_ in items:
        face=[]
        for co in coords:
            if co not in lookup:
                lookup[co]=len(points)
                points.append(world_to_local@Vector(co) if world_to_local else co)
            face.append(lookup[co])
        faces.append(face)
    mesh=bpy.data.meshes.new(label);mesh.from_pydata(points,[],faces);mesh.update()
    return mesh


def selected(items,seeds):
    mesh=source_mesh(items,'source component selection')
    groups=regions(mesh)
    ids=sorted({i for seed in seeds for i in next(g for g in groups if seed in g)})
    bpy.data.meshes.remove(mesh)
    return ids


def apply(fixed):
    parts,manifest=read_parts();report={}
    # Source optics stay the same fixed dark glass as the current model.
    optics=bpy.data.materials['Restored Optics']
    def populate(obj,group,ids,role):
        items=[parts[group][i] for i in ids]
        mesh=source_mesh(items,obj.name+' original OBJ',obj.matrix_world.inverted())
        desired=[]
        for p,(_,material) in zip(mesh.polygons,items):
            n=obj.matrix_world.to_3x3()@p.normal
            if 'optic' in material or 'dynamic_glass' in material:
                desired.append(optics);continue
            # Contact roles, not a global height ramp. Small cylindrical fittings
            # intentionally keep one side color instead of radial shading bands.
            if role in {'hinge','port'}:y=192
            elif role=='antenna_spring':y=192 if abs(n.z)>.65 else 168
            elif role=='antenna_connector':y=216 if abs(n.z)>.65 else 192
            elif role=='antenna_base':y=192 if n.z>.55 else 156
            else:
                top={'door':228,'housing':216,'cover':240,'cap':252,'commander':240}[role]
                y=top if n.z>.55 else top-24 if abs(n.y)>.55 else top-48 if n.z>-.55 else top-72
            desired.append(fixed('sight',y))
        slots={}
        for m in desired:
            if m.name not in slots:slots[m.name]=len(slots);mesh.materials.append(m)
        for p,m in zip(mesh.polygons,desired):p.material_index=slots[m.name];p.use_smooth=False
        obj.data=mesh;obj['source_group']=group.removeprefix('WT_');obj['source_detail_restored']=True
        report[obj.name]={'source_group':group,'source_polygon_indices':ids,'faces':len(ids),'paint_role':role}
    for name,seeds,role in DETAILS:
        obj=bpy.data.objects.new(name,bpy.data.meshes.new(name));bpy.context.scene.collection.objects.link(obj)
        obj.parent=bpy.data.objects['TURRET_YAW'];obj.matrix_parent_inverse=obj.parent.matrix_world.inverted();bpy.context.view_layer.update()
        ids=selected(parts['WT_bone_turret'],seeds)
        # All these components were absent; reject accidentally selecting an
        # existing reduced component, which would introduce overlapping surfaces.
        keys={tuple(round(c,4) for c in co) for i in ids for co in parts['WT_bone_turret'][i][0]}
        existing={tuple(round(c,4) for c in o.matrix_world@v.co) for o in bpy.context.scene.objects
                  if o.type=='MESH' and o!=obj for v in o.data.vertices}
        assert not (keys&existing),(name,'Restoration overlaps existing source vertices')
        populate(obj,'WT_bone_turret',ids,role)
    obj=bpy.data.objects['WT_bone_commander_sight_v']
    ids=selected(parts[obj.name],COMMANDER_COMPONENT_SEEDS)
    populate(obj,obj.name,ids,'commander')
    return {'source_sha256':manifest['source_sha256'],'source':manifest['source'],
            'replaced_objects':['WT_bone_commander_sight_v'],'added_objects':[r[0] for r in DETAILS],
            'parts':report,'tiny_separate_screw_components':'remain omitted'}

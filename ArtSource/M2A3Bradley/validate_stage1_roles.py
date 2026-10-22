"""Validate complete role coverage, source binding and face-set index integrity."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parent
c=json.loads((ROOT/'bradley_roles.json').read_text(encoding='utf-8'))
inventory=json.loads((ROOT/'Audit/Stage1/source_inventory.json').read_text())
current=json.loads((ROOT/'Audit/Stage1/current_model_stats.json').read_text())
assert len(c['entries'])==112
assert {e['object'] for e in c['entries']}==set(inventory)
assert len({e['object'] for e in c['entries']})==112
assert {r['name'] for r in current['all_meshes']}<=set(inventory)
for binding in [c['source_binding'],c['current_snapshot']]:
    assert hashlib.sha256((ROOT/binding['blend']).read_bytes()).hexdigest()==binding['blend_sha256']
assert c['source_binding']['source_groups']==111 and c['source_binding']['derived_groups']==['rear_ramp']
assert c['policy']['allow_size_only_removal'] is False
assert sum(e['source_faces'] for e in c['entries'])==223389
assert sum(e['current_faces'] for e in c['entries'])==43344
for e in c['entries']:
    assert e['action'] in {'preserve_split','preserve_silhouette','preserve_animation','replace_simple','preserve_optics','omit','merge_into_parent'}
    assert e['parent']==inventory[e['object']]['parent']
    if e['importance']=='essential':assert e['protect_from_size_filter'] and e['action']!='omit'
sets=json.loads((ROOT/c['source_face_sets']['path']).read_text())
for name,record in sets.items():
    material_ids=[i for ids in record['material_subsets'].values() for i in ids]
    assert len(material_ids)==len(set(material_ids))==inventory[name]['faces']
    assert set(material_ids)==set(range(inventory[name]['faces']))
    shell_ids=[i for ids in record['main_shell_subsets'].values() for i in ids]
    assert len(shell_ids)==len(set(shell_ids))
    assert set(shell_ids)<=set(material_ids)
assert len(c['protected_components'])==5
for part in c['protected_components']:
    assert part['role']=='rear_stowage_bin' and part['not_a_confirmed_smoke_launcher']
    assert all(0<=i<inventory[part['object']]['faces'] for i in part['source_polygon_indices'])
assert {r['role'] for r in c['additional_roles']}=={'smoke_launcher','smoke_mount','turret_linkage_support'}
assert c['policy']['production_simplifier_uses_registry'] is True
assert c['policy']['production_registry_scope']=='targeted_repairs_and_paint'
assert len(c['targeted_geometry_repairs'])==33
print('STAGE1 BASELINE ROLE VALIDATION PASSED: 112 groups, 83 baseline objects, exact source binding, complete material subsets, 5 protected rear bins')

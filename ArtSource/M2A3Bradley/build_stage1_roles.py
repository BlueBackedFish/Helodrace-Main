"""Build the Bradley-specific role registry from read-only source inspection."""
from pathlib import Path
import json,hashlib,collections
ROOT=Path(__file__).resolve().parent
if (ROOT/'bradley_roles.json').exists():
    live=json.loads((ROOT/'bradley_roles.json').read_text(encoding='utf-8'))
    if live['policy']['production_simplifier_uses_registry']:
        raise SystemExit('Stage-one registry is frozen and used by the production rebuild. Edit bradley_roles.json rather than regenerating it from the old inventory.')
OUT=ROOT/'Audit/Stage1'
inventory=json.loads((OUT/'source_inventory.json').read_text())
current=json.loads((ROOT/'Audit/current_model_stats.json').read_text())
current={r['name']:r for r in current['all_meshes']}
manifest=json.loads((ROOT/'ReferenceFit/manifest.json').read_text())
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
# Source-specific names are confined to this vehicle's registry.
special={
 '@root':('hull_assembly','차체 장갑·지붕·부착물 복합체','essential','preserve_split','confirmed'),
 'bone_turret':('turret_assembly','포탑 장갑·지붕·부착물 복합체','essential','preserve_split','confirmed'),
 'bone_gun':('gun_mount','주포 마운트/방패','essential','replace_simple','confirmed'),
 'gun_barrel':('main_barrel','주포 총열','essential','preserve_silhouette','confirmed'),
 'mg_twin':('coax_machine_gun','동축기관총','essential','replace_simple','confirmed'),
 'hatch_01':('driver_hatch','조종수 해치','essential','replace_simple','confirmed'),
 'hatch_02':('turret_hatch_a','포탑 해치 A','essential','replace_simple','confirmed'),
 'hatch_03':('turret_hatch_b','포탑 해치 B','essential','replace_simple','confirmed'),
 'hatch_04':('personnel_door','후방 출입문','essential','preserve_animation','confirmed'),
 'hatch_05':('rear_roof_hatch','후방 지붕 해치','essential','replace_simple','confirmed'),
 'hatch_06':('front_service_panel','정면 서비스 패널','essential','replace_simple','confirmed'),
 'rear_ramp':('rear_ramp','root에서 추출한 후방 램프','essential','preserve_animation','confirmed'),
 'turret_inside':('turret_interior','포탑 내부 구조','hidden','omit','confirmed'),
 'ex_armor_01':('hull_front_bracket','차체 전방 판형 브래킷','secondary','replace_simple','shape_confirmed_function_generic'),
 'ex_armor_02':('hull_side_bracket','차체 측면 긴 브래킷','secondary','replace_simple','shape_confirmed_function_generic'),
 'ex_armor_03':('commander_sight_guard','지휘관 관측장비 외부 보호 구조','essential','replace_simple','confirmed'),
 'bone_commander_sight_v':('commander_sight','지휘관 관측장비','essential','preserve_optics','confirmed'),
 'rocket_mount_01':('tow_mount','TOW 발사기 원형 결합 마운트','essential','replace_simple','confirmed'),
 'roll_05':('tow_launcher','TOW 쌍열 발사기','essential','replace_simple','confirmed'),
 'roll_04':('tow_end_cover','TOW 끝단 판형 덮개','essential','replace_simple','confirmed'),
 'roll_03':('tow_linkage','TOW 측면 연결부','essential','merge_into_parent','inferred_from_shape_and_position'),
 'roll_06':('tow_hinge_cap','TOW 힌지 원통 끝단','secondary','merge_into_parent','inferred_from_shape_and_position'),
 'roll_07':('tow_hinge_collar','TOW 힌지 링','secondary','merge_into_parent','inferred_from_shape_and_position'),
 'roll_08':('tow_handle','TOW 주변 손잡이/가드','secondary','replace_simple','inferred_from_shape_and_position'),
 'roll_01':('turret_front_linkage','포탑 전방 긴 연결봉','secondary','merge_into_parent','shape_confirmed_function_generic'),
 'roll_02':('turret_front_mechanism','포탑 전방 링크/힌지 조립체','secondary','merge_into_parent','shape_confirmed_function_generic'),
 'ex_decor_01':('gun_upper_shroud','주포부 상단 판형 덮개','essential','replace_simple','confirmed'),
 'ex_decor_02':('turret_front_upper_cover','포탑 전방 상부 판형 덮개','essential','replace_simple','shape_confirmed_function_generic'),
 'ex_decor_03':('turret_front_lower_cover','포탑 전방 하부 판형 덮개','essential','replace_simple','shape_confirmed_function_generic'),
 'ex_decor_04':('rear_towing_connector','후방 중앙 견인 결합부','secondary','omit','inferred_from_shape_and_position'),
 'ex_decor_l_09':('rear_corner_assembly','좌측 후방 판/박스 조립체','essential','replace_simple','shape_confirmed_function_generic'),
 'ex_decor_r_04':('rear_corner_assembly','우측 후방 판/박스 조립체','essential','replace_simple','shape_confirmed_function_generic'),
}
for side in ('l','r'):
    for index in (1,2):
        special[f'ex_decor_{side}_{index:02}']=('front_towing_eye',f'{side}측 전방 견인 고리 {index}','secondary','replace_simple','confirmed')
    special[f'ex_decor_{side}_03']=('rear_mudguard',f'{side}측 후방 흙받이','secondary','replace_simple','inferred_from_shape_and_position')
special['ex_decor_l_04']=('front_mudguard','좌측 전방 흙받이','secondary','replace_simple','inferred_from_shape_and_position')
special['ex_decor_r_05']=('front_mudguard','우측 전방 흙받이','secondary','replace_simple','inferred_from_shape_and_position')
special['ex_decor_l_05']=('rear_towing_eye','좌측 후방 견인 고리','secondary','replace_simple','confirmed')
special['ex_decor_r_06']=('rear_towing_eye','우측 후방 견인 고리','secondary','replace_simple','confirmed')
def classify(name):
    if name in special:return special[name]
    if name.startswith('suspension_'):return ('hidden_suspension','차체 내부 현가 링크','hidden','omit','confirmed')
    if name.startswith('ex_armor_l_') or name.startswith('ex_armor_r_'):return ('side_skirt','하부 측면 스커트 장갑판 (ERA 아님)','essential','preserve_silhouette','confirmed')
    if name.startswith('ex_lantern_'):return ('headlamp_assembly','전조등/보호 구조','essential','preserve_optics','confirmed')
    if name.startswith('antenna_'):return ('antenna','안테나','essential','preserve_silhouette','confirmed')
    if name.startswith('track_'):return ('track','궤도','essential','replace_simple','confirmed')
    if name.startswith('wheel_'):
        if '_top_' in name:return ('return_roller','상부 지지 롤러','hidden','omit','confirmed')
        if name.endswith('_drive'):return ('drive_sprocket','전방 구동륜','essential','replace_simple','confirmed')
        if name.endswith('_back'):return ('rear_idler','후방 유도륜','essential','replace_simple','confirmed')
        return ('road_wheel','로드휠','essential','replace_simple','confirmed')
    raise ValueError('Unmapped source group '+name)
entries=[]
for name,source in inventory.items():
    group=name.removeprefix('WT_');role,label,importance,action,confidence=classify(group)
    now=current.get(name)
    state='missing_object' if now is None else 'empty_mesh' if now['faces']==0 else 'present'
    entries.append({'source_group':group,'object':name,'role':role,'label_ko':label,'importance':importance,
        'action':action,'confidence':confidence,'evidence':['Audit/Stage1/source_inventory.json',
            'Audit/Stage1/source_parts_atlas.png' if (OUT/(name+'.png')).exists() else 'ReferenceFit/source_groups.json'],
        'source_faces':source['faces'],'source_bounds':source['bounds'],'current_faces':now['faces'] if now else 0,
        'current_state':state,'parent':source['parent'],
        'protect_from_size_filter':importance=='essential','restore_required':state!='present' and importance=='essential'})
config={'schema_version':1,'stage':1,'stage_status':'role_registry_complete_with_unresolved_smoke_locator',
 'target':{'geometry_basis':'user_supplied_M3A3_OBJ','blueprint':'user_supplied_M2A3','style':'RimThunder','render_method':'Legion_flat_face_3D','color':'TAN','ERA':'deferred','unit_status':'unverified_preserve_source_scale'},
 'source_binding':{'blend':'ReferenceFit/m3a3_bradley_refit.blend','blend_sha256':digest(ROOT/'ReferenceFit/m3a3_bradley_refit.blend'),
                   'obj_sha256':manifest['source_sha256'],'axis_conversion':manifest['axis_conversion'],'source_groups':111,'derived_groups':['rear_ramp']},
 'current_snapshot':{'blend':'RimWorld/bradley_rimworld.blend','blend_sha256':digest(ROOT/'RimWorld/bradley_rimworld.blend')},
 'policy':{'unmapped_group':'error','unclassified_component':'preserve_for_review','allow_size_only_removal':False,
           'essential_roles_ignore_size_cutoff':True,'optics_and_lamp_lenses':'preserve_material_subsets',
           'missing_essential_part':'restore_or_replace_before_style_signoff','production_simplifier_uses_registry':False},
 'role_surfaces':{'hull_assembly':['hull_roof','front_glacis','rear_wall','side_slope','side_wall','underside','grille','optics','assembly_fitting'],
                  'turret_assembly':['turret_roof','turret_front','turret_rear','turret_side','underside','optics','rear_stowage_bin','assembly_fitting']},
 'source_face_sets':{'path':'Audit/Stage1/source_face_sets.json','index_space':'source_blend_mesh_polygon_indices',
                     'status':'material_subsets_confirmed_main_shell_direction_subsets_candidates','do_not_apply_to_current_decimated_mesh':True},
 'protected_components':[],
 'unresolved_required_roles':[{'role':'smoke_launcher','required':True,'source_binding':None,
     'status':'not_identified_do_not_substitute_rear_stowage_bins',
     'next_action':'identify real launcher in supplied source/reference before adding geometry; do not invent a location'}],
 'entries':entries}
clusters=json.loads((OUT/'rear_launcher_candidates.json').read_text())
for c in clusters:
    config['protected_components'].append({'object':'WT_bone_turret','role':'rear_stowage_bin','label_ko':'포탑 후방 개방형 수납함',
        'importance':'essential','action':'replace_simple','confidence':'shape_confirmed_function_generic',
        'source_polygon_indices':c['polygon_indices'],'source_bounds':c['bounds'],
        'evidence':'Audit/Stage1/'+c['inspection_name']+'.png','not_a_confirmed_smoke_launcher':True})
assert len(entries)==112 and len(current)==83
assert sum(e['source_faces'] for e in entries)==223389
assert sum(e['current_faces'] for e in entries)==43344
assert set(current)<=set(inventory)
assert sum(e['current_state']=='empty_mesh' for e in entries)==15
(ROOT/'bradley_roles.json').write_text(json.dumps(config,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
lines=['# Bradley 원본 부품 역할표 — 1차','',
 'RimThunder 목표 스타일 / Legion 한 면 한 색. 111개 원본 그룹과 root에서 추출한 램프 1개를 모두 등록했다.',
 '상태: present=면 존재, empty_mesh=빈 메시, missing_object=현 작업본에서 객체 삭제.',
 '기능이 명확하지 않은 부품은 형상·위치 기준의 일반 역할로 등록하고 추정 상태를 남겼다.','',
 '| 원본 그룹 | 역할/부품 | 상태·현재면/원본면 | 처리 | 중요도 | 확인 수준 |',
 '|---|---|---|---|---|---|']
for e in entries:lines.append(f"| {e['source_group']} | {e['role']} / {e['label_ko']} | {e['current_state']} · {e['current_faces']}/{e['source_faces']} | {e['action']} | {e['importance']} | {e['confidence']} |")
lines+=['','## 빈 메시 15개 처리','', '| 원본 그룹 | 원래 형상 | 다음 단계 |','|---|---|---|']
for e in entries:
    if e['current_state']=='empty_mesh':lines.append(f"| {e['source_group']} | {e['label_ko']} | {e['action']} {'— 필수 복구' if e['restore_required'] else '— 복구/생략은 등록된 역할에 따름'} |")
lines+=['','## 실행 설정','',
 '차량별 설정: [bradley_roles.json](../../bradley_roles.json).',
 '현재 simplifier는 아직 이 설정을 사용하지 않는다. 2차부터 설정 소비로 전환하고 크기 기반 일괄 삭제를 제거한다.',
 '면 인덱스는 원본 BLEND 해시와 결합한다. 감량된 현 메시나 다른 원본 버전에 직접 적용하면 안 된다.',
 '연막탄 source_binding은 미확정이다. 후방 수납함을 연막탄으로 잘못 분류하지 않으며 해당 역할의 완료 검수는 보류한다.','']
(OUT/'PART_ROLES.md').write_text('\n'.join(lines),encoding='utf-8')
summary={'registered_groups':len(entries),'source_faces':223389,'current_faces':43344,
         'states':dict(collections.Counter(e['current_state'] for e in entries)),
         'required_restore':[e['source_group'] for e in entries if e['restore_required']],
         'protected_rear_stowage_bins':len(clusters),'unresolved_required_roles':['smoke_launcher'],
         'original_and_current_models_modified':False}
(OUT/'role_validation.json').write_text(json.dumps(summary,indent=2)+'\n')
print(json.dumps(summary))

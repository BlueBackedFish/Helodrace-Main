# Bradley 원본 부품 역할표 — 1차 스냅샷

이 표의 현재면/상태는 1차 당시 기록이다. 2차 실행 결과는 `../../RimWorld/stage2_manifest.json`과 차량별 설정의 runtime_bindings를 참조한다.

RimThunder 목표 스타일 / Legion 한 면 한 색. 111개 원본 그룹과 root에서 추출한 램프 1개를 모두 등록했다.
상태: present=면 존재, empty_mesh=빈 메시, missing_object=현 작업본에서 객체 삭제.
기능이 명확하지 않은 부품은 형상·위치 기준의 일반 역할로 등록하고 추정 상태를 남겼다.

| 원본 그룹 | 역할/부품 | 상태·현재면/원본면 | 처리 | 중요도 | 확인 수준 |
|---|---|---|---|---|---|
| @root | hull_assembly / 차체 장갑·지붕·부착물 복합체 | present · 9400/73954 | preserve_split | essential | confirmed |
| ex_armor_l_07 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 18/1208 | preserve_silhouette | essential | confirmed |
| hatch_05 | rear_roof_hatch / 후방 지붕 해치 | present · 465/2716 | replace_simple | essential | confirmed |
| wheel_l_drive | drive_sprocket / 전방 구동륜 | present · 645/2440 | replace_simple | essential | confirmed |
| wheel_l_top_02 | return_roller / 상부 지지 롤러 | present · 180/544 | omit | hidden | confirmed |
| wheel_l_back | rear_idler / 후방 유도륜 | present · 688/2520 | replace_simple | essential | confirmed |
| wheel_r_top_02 | return_roller / 상부 지지 롤러 | present · 180/544 | omit | hidden | confirmed |
| wheel_r_back | rear_idler / 후방 유도륜 | present · 691/2520 | replace_simple | essential | confirmed |
| wheel_r_top_01 | return_roller / 상부 지지 롤러 | empty_mesh · 0/432 | omit | hidden | confirmed |
| wheel_r_top_03 | return_roller / 상부 지지 롤러 | empty_mesh · 0/432 | omit | hidden | confirmed |
| wheel_l_top_01 | return_roller / 상부 지지 롤러 | empty_mesh · 0/432 | omit | hidden | confirmed |
| wheel_l_top_03 | return_roller / 상부 지지 롤러 | empty_mesh · 0/432 | omit | hidden | confirmed |
| ex_decor_l_01 | front_towing_eye / l측 전방 견인 고리 1 | empty_mesh · 0/352 | replace_simple | secondary | confirmed |
| ex_decor_r_01 | front_towing_eye / r측 전방 견인 고리 1 | empty_mesh · 0/352 | replace_simple | secondary | confirmed |
| ex_decor_l_02 | front_towing_eye / l측 전방 견인 고리 2 | empty_mesh · 0/336 | replace_simple | secondary | confirmed |
| ex_decor_r_02 | front_towing_eye / r측 전방 견인 고리 2 | empty_mesh · 0/336 | replace_simple | secondary | confirmed |
| ex_armor_01 | hull_front_bracket / 차체 전방 판형 브래킷 | present · 661/878 | replace_simple | secondary | shape_confirmed_function_generic |
| ex_armor_02 | hull_side_bracket / 차체 측면 긴 브래킷 | present · 747/890 | replace_simple | secondary | shape_confirmed_function_generic |
| hatch_06 | front_service_panel / 정면 서비스 패널 | present · 128/4224 | replace_simple | essential | confirmed |
| ex_decor_r_04 | rear_corner_assembly / 우측 후방 판/박스 조립체 | present · 952/3551 | replace_simple | essential | shape_confirmed_function_generic |
| ex_decor_l_09 | rear_corner_assembly / 좌측 후방 판/박스 조립체 | present · 955/3542 | replace_simple | essential | shape_confirmed_function_generic |
| ex_decor_r_03 | rear_mudguard / r측 후방 흙받이 | present · 41/154 | replace_simple | secondary | inferred_from_shape_and_position |
| ex_decor_l_03 | rear_mudguard / l측 후방 흙받이 | present · 41/154 | replace_simple | secondary | inferred_from_shape_and_position |
| hatch_04 | personnel_door / 후방 출입문 | present · 451/1320 | preserve_animation | essential | confirmed |
| ex_armor_l_01 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 24/1236 | preserve_silhouette | essential | confirmed |
| ex_armor_l_02 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 12/888 | preserve_silhouette | essential | confirmed |
| ex_armor_l_03 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 12/524 | preserve_silhouette | essential | confirmed |
| ex_armor_l_04 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 12/524 | preserve_silhouette | essential | confirmed |
| ex_armor_l_05 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 13/888 | preserve_silhouette | essential | confirmed |
| ex_armor_l_06 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 13/888 | preserve_silhouette | essential | confirmed |
| ex_armor_r_07 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 17/896 | preserve_silhouette | essential | confirmed |
| ex_armor_r_01 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 26/924 | preserve_silhouette | essential | confirmed |
| ex_armor_r_02 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 12/888 | preserve_silhouette | essential | confirmed |
| ex_armor_r_03 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 12/524 | preserve_silhouette | essential | confirmed |
| ex_armor_r_04 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 12/836 | preserve_silhouette | essential | confirmed |
| ex_armor_r_05 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 14/1200 | preserve_silhouette | essential | confirmed |
| ex_armor_r_06 | side_skirt / 하부 측면 스커트 장갑판 (ERA 아님) | present · 13/880 | preserve_silhouette | essential | confirmed |
| ex_lantern_l_01 | headlamp_assembly / 전조등/보호 구조 | present · 921/3426 | preserve_optics | essential | confirmed |
| ex_decor_r_05 | front_mudguard / 우측 전방 흙받이 | present · 171/260 | replace_simple | secondary | inferred_from_shape_and_position |
| ex_decor_l_04 | front_mudguard / 좌측 전방 흙받이 | present · 170/260 | replace_simple | secondary | inferred_from_shape_and_position |
| hatch_01 | driver_hatch / 조종수 해치 | present · 712/2080 | replace_simple | essential | confirmed |
| ex_lantern_r_01 | headlamp_assembly / 전조등/보호 구조 | present · 577/2978 | preserve_optics | essential | confirmed |
| ex_decor_04 | rear_towing_connector / 후방 중앙 견인 결합부 | empty_mesh · 0/653 | omit | secondary | inferred_from_shape_and_position |
| ex_decor_l_05 | rear_towing_eye / 좌측 후방 견인 고리 | empty_mesh · 0/292 | replace_simple | secondary | confirmed |
| ex_decor_r_06 | rear_towing_eye / 우측 후방 견인 고리 | empty_mesh · 0/292 | replace_simple | secondary | confirmed |
| suspension_r_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_r_01_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| suspension_r_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_r_02_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| suspension_r_03 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_r_03_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| suspension_r_04 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_r_05 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_r_06 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_r_06_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| wheel_r_01 | road_wheel / 로드휠 | present · 353/1304 | replace_simple | essential | confirmed |
| wheel_r_02 | road_wheel / 로드휠 | present · 360/1304 | replace_simple | essential | confirmed |
| wheel_r_03 | road_wheel / 로드휠 | present · 359/1304 | replace_simple | essential | confirmed |
| wheel_r_04 | road_wheel / 로드휠 | present · 354/1304 | replace_simple | essential | confirmed |
| wheel_r_05 | road_wheel / 로드휠 | present · 356/1304 | replace_simple | essential | confirmed |
| wheel_r_06 | road_wheel / 로드휠 | present · 360/1320 | replace_simple | essential | confirmed |
| suspension_r_01_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_r_02_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_r_03_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_r_06_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_l_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_l_01_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| wheel_l_01 | road_wheel / 로드휠 | present · 356/1304 | replace_simple | essential | confirmed |
| suspension_l_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_l_02_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| wheel_l_02 | road_wheel / 로드휠 | present · 355/1304 | replace_simple | essential | confirmed |
| suspension_l_03 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_l_03_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| wheel_l_03 | road_wheel / 로드휠 | present · 373/1304 | replace_simple | essential | confirmed |
| suspension_l_04 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| wheel_l_04 | road_wheel / 로드휠 | present · 366/1304 | replace_simple | essential | confirmed |
| suspension_l_05 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| wheel_l_05 | road_wheel / 로드휠 | present · 354/1304 | replace_simple | essential | confirmed |
| suspension_l_06 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/268 | omit | hidden | confirmed |
| suspension_l_06_02 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/100 | omit | hidden | confirmed |
| wheel_l_06 | road_wheel / 로드휠 | present · 362/1304 | replace_simple | essential | confirmed |
| suspension_l_01_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_l_02_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_l_03_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| suspension_l_06_01 | hidden_suspension / 차체 내부 현가 링크 | missing_object · 0/94 | omit | hidden | confirmed |
| bone_turret | turret_assembly / 포탑 장갑·지붕·부착물 복합체 | present · 5171/28494 | preserve_split | essential | confirmed |
| bone_gun | gun_mount / 주포 마운트/방패 | present · 855/1861 | replace_simple | essential | confirmed |
| gun_barrel | main_barrel / 주포 총열 | present · 780/2632 | preserve_silhouette | essential | confirmed |
| roll_01 | turret_front_linkage / 포탑 전방 긴 연결봉 | present · 28/354 | merge_into_parent | secondary | shape_confirmed_function_generic |
| mg_twin | coax_machine_gun / 동축기관총 | present · 1172/5515 | replace_simple | essential | confirmed |
| rocket_mount_01 | tow_mount / TOW 발사기 원형 결합 마운트 | present · 273/1293 | replace_simple | essential | confirmed |
| roll_06 | tow_hinge_cap / TOW 힌지 원통 끝단 | empty_mesh · 0/202 | merge_into_parent | secondary | inferred_from_shape_and_position |
| roll_03 | tow_linkage / TOW 측면 연결부 | empty_mesh · 0/502 | merge_into_parent | essential | inferred_from_shape_and_position |
| roll_05 | tow_launcher / TOW 쌍열 발사기 | present · 592/4688 | replace_simple | essential | confirmed |
| roll_04 | tow_end_cover / TOW 끝단 판형 덮개 | present · 74/184 | replace_simple | essential | confirmed |
| roll_02 | turret_front_mechanism / 포탑 전방 링크/힌지 조립체 | present · 47/1067 | merge_into_parent | secondary | shape_confirmed_function_generic |
| turret_inside | turret_interior / 포탑 내부 구조 | missing_object · 0/1801 | omit | hidden | confirmed |
| ex_decor_02 | turret_front_upper_cover / 포탑 전방 상부 판형 덮개 | empty_mesh · 0/488 | replace_simple | essential | shape_confirmed_function_generic |
| ex_decor_03 | turret_front_lower_cover / 포탑 전방 하부 판형 덮개 | present · 6/500 | replace_simple | essential | shape_confirmed_function_generic |
| hatch_03 | turret_hatch_b / 포탑 해치 B | present · 188/1071 | replace_simple | essential | confirmed |
| hatch_02 | turret_hatch_a / 포탑 해치 A | present · 1083/2556 | replace_simple | essential | confirmed |
| ex_armor_03 | commander_sight_guard / 지휘관 관측장비 외부 보호 구조 | present · 199/1830 | replace_simple | essential | confirmed |
| bone_commander_sight_v | commander_sight / 지휘관 관측장비 | present · 358/1528 | preserve_optics | essential | confirmed |
| wheel_r_drive | drive_sprocket / 전방 구동륜 | present · 633/2440 | replace_simple | essential | confirmed |
| roll_08 | tow_handle / TOW 주변 손잡이/가드 | present · 450/480 | replace_simple | secondary | inferred_from_shape_and_position |
| roll_07 | tow_hinge_collar / TOW 힌지 링 | empty_mesh · 0/480 | merge_into_parent | secondary | inferred_from_shape_and_position |
| antenna_01 | antenna / 안테나 | present · 60/126 | preserve_silhouette | essential | confirmed |
| antenna_02 | antenna / 안테나 | present · 70/126 | preserve_silhouette | essential | confirmed |
| ex_decor_01 | gun_upper_shroud / 주포부 상단 판형 덮개 | present · 1647/1780 | replace_simple | essential | confirmed |
| track_r | track / 궤도 | present · 2435/4264 | replace_simple | essential | confirmed |
| track_l | track / 궤도 | present · 2490/4264 | replace_simple | essential | confirmed |
| rear_ramp | rear_ramp / root에서 추출한 후방 램프 | present · 1439/6831 | preserve_animation | essential | confirmed |

## 빈 메시 15개 처리

| 원본 그룹 | 원래 형상 | 다음 단계 |
|---|---|---|
| wheel_r_top_01 | 상부 지지 롤러 | omit — 복구/생략은 등록된 역할에 따름 |
| wheel_r_top_03 | 상부 지지 롤러 | omit — 복구/생략은 등록된 역할에 따름 |
| wheel_l_top_01 | 상부 지지 롤러 | omit — 복구/생략은 등록된 역할에 따름 |
| wheel_l_top_03 | 상부 지지 롤러 | omit — 복구/생략은 등록된 역할에 따름 |
| ex_decor_l_01 | l측 전방 견인 고리 1 | replace_simple — 복구/생략은 등록된 역할에 따름 |
| ex_decor_r_01 | r측 전방 견인 고리 1 | replace_simple — 복구/생략은 등록된 역할에 따름 |
| ex_decor_l_02 | l측 전방 견인 고리 2 | replace_simple — 복구/생략은 등록된 역할에 따름 |
| ex_decor_r_02 | r측 전방 견인 고리 2 | replace_simple — 복구/생략은 등록된 역할에 따름 |
| ex_decor_04 | 후방 중앙 견인 결합부 | omit — 복구/생략은 등록된 역할에 따름 |
| ex_decor_l_05 | 좌측 후방 견인 고리 | replace_simple — 복구/생략은 등록된 역할에 따름 |
| ex_decor_r_06 | 우측 후방 견인 고리 | replace_simple — 복구/생략은 등록된 역할에 따름 |
| roll_06 | TOW 힌지 원통 끝단 | merge_into_parent — 복구/생략은 등록된 역할에 따름 |
| roll_03 | TOW 측면 연결부 | merge_into_parent — 필수 복구 |
| ex_decor_02 | 포탑 전방 상부 판형 덮개 | replace_simple — 필수 복구 |
| roll_07 | TOW 힌지 링 | merge_into_parent — 복구/생략은 등록된 역할에 따름 |

## 실행 설정

차량별 설정: [bradley_roles.json](../../bradley_roles.json).
현재 simplifier는 아직 이 설정을 사용하지 않는다. 2차부터 설정 소비로 전환하고 크기 기반 일괄 삭제를 제거한다.
면 인덱스는 원본 BLEND 해시와 결합한다. 감량된 현 메시나 다른 원본 버전에 직접 적용하면 안 된다.
연막탄 source_binding은 미확정이다. 후방 수납함을 연막탄으로 잘못 분류하지 않으며 해당 역할의 완료 검수는 보류한다.

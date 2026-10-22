# 림월드용 Bradley — 높이 우선 최종 색

같은 TAN 상면은 차체 지붕 < 포탑 지붕 < 그 위 해치 < 더 높은 관측장비 순서로 밝아집니다.
이전 부품별 기본색과 배율 중첩을 제거하고, 최종 코너 색을 Emission에 직접 연결했습니다.
주요 단계 사이의 최소 명도 차이를 키우고 큰 차이의 증가율은 압축했습니다. 낮은 면은 채도를 낮추며, 넓은 해치 본판은 부드러운 명도로, 작은 힌지·관측장치의 원통 측면은 밝음/어두움 두 색으로 표현합니다.
해치 03의 원본 손잡이·힌지 등 기존 형상을 유지합니다. 포 덮개·기총 주변 덮개는 79면에 한정해 국소 명도 단차를 보강했습니다.

52,878면·103개 메시, 연막탄 8개·장착대·연결부·평판 덮개와 부모/피벗, 4개 애니메이션을 보존합니다.
광원·그림자·AO 없이 투명 배경을 사용하며 ERA는 보류합니다.

- `bradley_rimworld.blend`: 편집 모델과 최종 색 속성/Emission 셰이더.
- `bradley_rimworld.glb`: 최종 vertex color + KHR_materials_unlit. 절차형 그릴은 단색 대체입니다.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 선형 RGB로 축소한 게임 크기 PNG와 피벗 정보.
- `iso_front.png`, `iso_rear.png`: 앞/뒤 검수 렌더.
- `manifest.json`, `recolor_validation.json`, `validation.json`: 최종 색·형상·동작 검증.
- `../Audit/GunPanels/REPORT.md`: 표면별 규칙, 실제 최종 명도, 이전/현재 및 RimThunder 비교.

재생성은 Blender에 `--python-exit-code 1`을 지정하여 다음 순서로 실행합니다.

1. `../simplify_for_rimworld.py`: 고정 1차 BLEND + 원본 OBJ에서 재생성.
2. `../Audit/check_gun_panel_contrast.py`, `../validate_recolor.py`, `validate.py`: 최종 Emission 색·원본 형상·계층/애니메이션·GLB 검사.
3. 별도 프로세스에서 `../render_rimworld.py`: 128샘플/16비트 마스터 렌더와 색 공간을 보존한 합성.
4. `../Audit/validate_height_renders.py`와 `../Audit/check_gun_panel_contrast.py -- --pixels`: 실제 PNG 상면 명도·합성 색·두 덮개 단차 검사.
5. Pillow/numpy가 있는 Python으로 `../export_bradley_lowres.py`, `../validate_stage1_roles.py`.
6. 필요 시 `../Audit/render_height_closeups.py`, `../Audit/compare_gun_panels.py`, `../Audit/compare_rimthunder_palette.py`로 비교 자료 갱신.

설정은 `../part_contrast.json`, 명도 단계 계산은 `../height_palette.py`, 곡면 2구간 분할은 `../cel_patches.py`에 있습니다.
RimThunder의 부품별 명도 중앙값은 참고 자료로 남기며, 높이 순서를 뒤집는 기본색으로 사용하지 않습니다.
256px/512px 출력과 모델/애니메이션 검증을 완료했습니다. 실제 게임 내 검수는 미실시입니다.

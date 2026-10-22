# 림월드용 Bradley — 직접 겹침 경계 채색

직접 겹치는 위 부품을 아래 장착면보다 최소 표시 명도 24만큼 밝게 합니다.
떨어진 부품은 높이가 달라도 같은 기준색을 사용할 수 있습니다. 전역 높이 순위는 사용하지 않습니다.
큰 장갑 방향은 28 간격, 원통 측면은 단색, 넓은 해치 본판은 부드러운 곡률 명도를 사용합니다.

52,878면·103개 메시, 연막탄 8개·장착대·연결부·평판 덮개와 부모/피벗, 4개 애니메이션을 보존합니다.
광원·그림자·AO 없이 투명 배경을 사용하며 ERA는 보류합니다.

- `bradley_rimworld.blend`: 편집 모델과 최종 색 속성/Emission 셰이더.
- `bradley_rimworld.glb`: 최종 vertex color + KHR_materials_unlit. 절차형 그릴은 단색 대체입니다.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 게임 크기 PNG와 피벗 정보.
- `iso_front.png`, `iso_rear.png`: 앞/뒤 검수 렌더.
- `manifest.json`, `recolor_validation.json`, `validation.json`: 최종 색·형상·동작 검증.
- `../Audit/ContactPaint/REPORT.md`: 직접 겹침·원통 단색·실제 출력과 전후 비교.
- `../Audit/GunPanels/` 등 이전 감사 폴더: 해당 과거 커밋의 기록이며 현재 팔레트의 검수 대상이 아닙니다.

Blender에 `--python-exit-code 1`을 지정하여 재생성합니다.

1. `../simplify_for_rimworld.py`: 고정 1차 BLEND + 원본 OBJ에서 재생성.
2. `../validate_recolor.py`, `validate.py`, `../Audit/check_contact_paint.py`: 최종 색·원본 형상·부모/피벗·애니메이션·GLB·직접 경계/원통 단색 검사.
3. 별도 프로세스에서 `../render_rimworld.py`: 128샘플/16비트 마스터와 선형 색 공간 합성.
4. `../Audit/validate_height_renders.py`: 실제 PNG의 직접 장착 관계와 합성 색 검사.
5. Pillow/numpy가 있는 Python으로 `../export_bradley_lowres.py`, `../validate_stage1_roles.py`.
6. `../Audit/render_height_closeups.py`와 Pillow Python의 `../Audit/compare_contact_paint.py`, `../Audit/compare_rimthunder_palette.py`로 비교 자료를 갱신합니다.

설정은 `../part_contrast.json`, 장착 관계 팔레트는 `../height_palette.py`, 표면 모드는 `../cel_patches.py`에 있습니다.
실제 게임 내 검수는 미실시입니다.

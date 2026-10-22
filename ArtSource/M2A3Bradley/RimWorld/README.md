# 림월드용 Bradley — 연결부 복구·평판 덮개·높이 중심 채색

2차 이전 모델에 요청된 세 객체만 수정했습니다. `WT_roll_01/02`는 원본 OBJ에서 354면/1,067면 전체를 복구했고,
천 형태의 `WT_ex_decor_01`은 원래 외곽·장착 경사를 따르는 얇은 평판으로 바꿨습니다.
전체 43,049면·83개 메시이며, 나머지 형상과 모든 부모·피벗을 보존합니다.

한 면 한 색 제한은 취소했습니다. 높은 부분을 밝게 표현하는 채색을 중심으로 곡면 해치에 명도 분할/보간을 적용합니다.
TOW·관측장비 보호대·평판 덮개·후방 수납함은 별도의 어두운 TAN으로 구분합니다.
같은 장비에서도 높은 부분은 밝아지며 후방 지붕 해치는 주변보다 밝게 유지합니다.

- `bradley_rimworld.blend` / `.glb`: 편집/교환 모델. 포탑·포신·램프·출입문 분리와 4개 데모 애니메이션.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 축소 PNG와 피벗 정보 포함.
- `iso_front.png`, `iso_rear.png`: 앞/뒤 형상 검수 렌더.
- `manifest.json`: 실제 형상 변경과 채색 설정. `recolor_validation.json`, `validation.json`: 복구·높이·곡면·계층·동작 검증.

목표 스타일은 RimThunder이고 Legion은 3D 렌더 방식의 참고입니다. 광원·그림자·AO 없이 투명 배경을 사용합니다.
`../face_depth_tone.py`가 코너별 높이·곡률과 약한 카메라 보정을 계산합니다. 카메라/프레임 변경 시 다시 적용해야 합니다.
GLB는 고정 Emission 대체 색만 사용하므로 높이·곡률·카메라 보정과 절차적 그릴을 재현하지 않습니다. BLEND/PNG가 최종 채색 기준입니다.
ERA와 기존의 다른 빈 메시/생략 부품은 이번 변경 범위에 포함하지 않습니다.

재생성: Blender에서 `../simplify_for_rimworld.py`, 별도 프로세스에서 `../validate_recolor.py`, `validate.py`, `../render_rimworld.py` 순서로 실행합니다.
그다음 Pillow가 있는 Python으로 `../export_bradley_lowres.py`, `../validate_stage1_roles.py`를 실행합니다.
입력은 `../ReferenceFit/stage1_rimworld_base.blend`와 `../ReferenceFit/manifest.json`에 기록된 원본 OBJ입니다.
설정은 `../part_contrast.json`, 역할 등록은 `../bradley_roles.json`, 표적 형상 변경은 `../turret_repairs.py`입니다.

256px/512px 렌더와 분리 계층/애니메이션 검증을 완료했습니다. 실제 게임 내 검수는 미실시입니다.

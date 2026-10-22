# 림월드용 Bradley — 형상 복원·부품 대비 조정본

사용자 요청에 따라 2차 포탑 재구성을 철회하고, 재구성 전 형상을 복원했습니다.
43,344면·83개 메시의 꼭짓점/면/계층/배치를 그대로 유지하면서 부품과 주변 지붕의 색 차이를 약 3배로 확대했습니다.
TAN 계열과 기존 면 방향 명도를 유지하며, 해치·관측장비·TOW·수납함 등이 더 잘 구분되게 조정했습니다.
색이 과하게 검거나 하얘지는 경우에는 제한을 적용합니다. 후방 지붕 해치는 밝게 유지합니다.

- `bradley_rimworld.blend` / `.glb`: 편집/교환 모델. 포탑·포신·램프·출입문 분리와 4개 데모 애니메이션 유지.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 축소 PNG와 피벗 정보 포함.
- `iso_front.png`, `iso_rear.png`: 형상 검수 렌더.
- `manifest.json`: 현재 형상·부품 채색 보정·명도 제한 기록.
- `recolor_validation.json`, `validation.json`: 형상 복원·계층·동작·렌더 설정 검증.

목표 스타일은 RimThunder이고, Legion은 3D 렌더와 한 면 한 색 표현 방식의 참고입니다.
광원·투사 그림자·AO는 사용하지 않습니다. `../face_depth_tone.py`의 약한 면 단위 높이/카메라 보정을 유지합니다.
GLB는 카메라 보정과 절차적 그릴 대신 고정 Emission 대체 색을 사용합니다. ERA는 포함하지 않습니다.

재생성은 Blender에서 `../simplify_for_rimworld.py`, 별도 프로세스에서 `../validate_recolor.py`,
`validate.py`, `../render_rimworld.py` 순으로 실행합니다. `../export_bradley_lowres.py`는 Pillow가 있는 Python으로 실행합니다.
역할표 검증은 `../validate_stage1_roles.py`입니다.
고정 입력은 `../ReferenceFit/stage1_rimworld_base.blend`, 채색 설정은 `../part_contrast.json`, 역할 설정은 `../bradley_roles.json`입니다.

기존 15개 빈 메시와 생략 부품 상태도 복원됐습니다. 2차의 새 형상은 현재 작업본에 포함하지 않습니다.
이전 재구성 실험은 `../Audit/Stage2/REPORT.md`에 철회 상태로 기록합니다.
256px/512px 렌더와 분리 계층/애니메이션 검증을 완료했으며 실제 게임 내 검수는 미실시입니다.

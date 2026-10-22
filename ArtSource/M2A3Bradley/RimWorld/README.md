# 림월드용 Bradley — 2차 포탑 재구성본

현재 작업본은 `bradley_rimworld.blend`이며, 1차 고정 차체에 역할 기반 포탑을 재구성했습니다.
목표 스타일은 RimThunder, 제작 표현은 Legion식 3D 렌더와 한 면 한 색입니다. ERA는 보류합니다.

- `bradley_rimworld.blend` / `.glb`: 편집/교환 모델. 포탑·포신·램프·출입문 분리와 데모 애니메이션 유지.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 직교 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 축소 PNG와 피벗 정보 포함.
- `iso_front.png`, `iso_rear.png`: 형상 검수 렌더.
- `manifest.json`, `stage2_manifest.json`: 실제 부품/역할/면 수와 부착 정보.
- `validation.json`, `stage2_validation.json`: 저장 모델·계층·차체 보존·부착 상태 검증.

2차에서 필수 덮개/TOW 연결부를 복구하고 해치·손잡이·관측장비·TOW·후방 수납함을 재구성했습니다.
포탑 3,654면, 전체 33,945면이며 차체 꼭짓점/면/부모 계층은 1차와 동일합니다.
포탑 큰 장갑 경계는 원본을 보존했고, 전역 크기 기준 삭제는 역할 기반 제작으로 교체했습니다.
연막탄은 전방 양측 2×4 참고 배치에 맞춘 신규 형상입니다. 정확한 A3 실측 좌표가 아닌 아트 해석입니다.
상세 근거와 구현 범위는 `../Audit/Stage2/REPORT.md`, 목표와 후속 작업은 `../ART_BRIEF.md`에 있습니다.

현재 포탑 팔레트는 임시 TAN 역할 색입니다. 차체/해치/그릴 3차, 공통 팔레트 4차, 전체 검수 5차가 남았습니다.
광원·투사 그림자·AO는 사용하지 않으며 면 대비와 작은 카메라 깊이 보정으로 형상을 표현합니다.
`../face_depth_tone.py`가 렌더 카메라마다 면 단위 명도를 재계산합니다. 수동 카메라 변경 시 직접 재계산해야 합니다.
GLB는 카메라별 보정과 절차적 그릴 대신 고정 Emission 대체 색을 사용합니다.

재생성은 Blender에서 `../simplify_for_rimworld.py`, 별도 프로세스로 `../validate_stage2_turret.py`,
`validate.py`, `../render_rimworld.py` 순서로 실행합니다. `../export_bradley_lowres.py`는 Pillow가 있는 Python으로 실행합니다.
고정 입력은 `../ReferenceFit/stage1_rimworld_base.blend`, 역할 설정은 `../bradley_roles.json`,
2차 형상/색 설정은 `../turret_stage2.json`입니다. 1차 원본 목록은 원본 해시와 결합된 기록으로 유지합니다.

256px/512px 렌더와 분리 계층/애니메이션 검증을 완료했습니다. 실제 게임 내 검수는 미실시입니다.

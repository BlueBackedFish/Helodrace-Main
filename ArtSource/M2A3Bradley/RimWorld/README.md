# 림월드용 Bradley — 포탑 부품 복구·강한 표면 대비

주포/포신/전방 덮개의 삼각형별 옛 색 배치를 정리했습니다. 원본 OBJ의 연결봉 주변 받침과 덮개,
해치 02의 힌지·연결부, TOW 연결부를 복구했습니다. `bone_turret` 안에서 사라졌던 원본 연막탄 8개와 장착대도 복구했습니다.
이전 2차의 추정 배치 형상을 재사용하지 않습니다. 천 덮개는 이전 요청대로 평판을 유지합니다.
현재 51,995면·103개 메시이며 차체와 모든 기존 부모·피벗·4개 애니메이션을 유지합니다.

높이를 중심으로 채색하면서 일부 장비는 더 어두운 TAN을 사용합니다. 해치 02 상부 연결부는 밝게,
후방 바구니 안쪽 바닥은 경사보다 어둡게, 오목한 홈은 별도의 어두운 페인트로 경계를 강조합니다.
곡면 해치에는 명도 보간을 유지하고 자동 홈 마스크로 인한 삼각형 얼룩을 피합니다. 그림자·AO·광원은 사용하지 않습니다.

- `bradley_rimworld.blend` / `.glb`: 편집/교환 모델. GLB는 고정 Emission 대체 색이며 최종 높이/홈/곡률 채색은 BLEND와 PNG를 기준으로 합니다.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 축소 PNG와 피벗 정보 포함.
- `iso_front.png`, `iso_rear.png`: 앞/뒤 검수 렌더.
- `manifest.json`: 실제 형상 변경·강조 영역·홈 채색 통계. `recolor_validation.json`, `validation.json`: 형상·채색·동작 검증.
- `../Audit/TurretFollowup/REPORT.md`: 원본 근거와 전후 확대 비교.

재생성: Blender에서 `../simplify_for_rimworld.py`, 별도 프로세스에서 `../validate_recolor.py`, `validate.py`, `../render_rimworld.py` 순서로 실행합니다.
Python 예외 시 중단하도록 Blender 옵션 `--python-exit-code 1`을 사용합니다.
그다음 Pillow가 있는 Python으로 `../export_bradley_lowres.py`, `../validate_stage1_roles.py`를 실행합니다.
입력은 `../ReferenceFit/stage1_rimworld_base.blend`와 `../ReferenceFit/manifest.json`의 원본 OBJ입니다.
복구 면 집합은 `../turret_restoration.json`, 채색 설정은 `../part_contrast.json`, 역할 등록은 `../bradley_roles.json`입니다.

256px/512px 출력과 모델/애니메이션 검증을 완료했습니다. ERA는 보류하며 실제 게임 내 검수는 미실시입니다.

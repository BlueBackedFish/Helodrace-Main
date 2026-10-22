# 림월드용 Bradley — RimThunder 부품 팔레트·높이 음영 채도

RimThunder M2A3의 대응 부품 20개 영역에서 명도를 측정해 TAN 팔레트를 다시 구성했습니다.
TOW는 밝은 덮개와 중성적인 측면, 주포/포신은 회색에 가까운 어두운 톤, 낮은 차체 면은 채도가 낮은 TAN으로 표현합니다.
해치 02는 뚜껑 위 아치형 손잡이만 별도 밝게 강조합니다. 뒤 원통 힌지의 강조는 취소했습니다.

형상은 51,995면·103개 메시로 유지하며 원본 연결부·연막탄 8개·평판 덮개, 부모/피벗과 4개 애니메이션을 보존합니다.
광원·그림자·AO 없이 투명 배경을 사용합니다. ERA는 보류합니다.

- `bradley_rimworld.blend` / `.glb`: 편집/교환 모델. GLB는 고정 Emission 대체 색이며 최종 높이·곡률·채도·홈 표현은 BLEND/PNG를 기준으로 합니다.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 축소 PNG와 피벗 정보 포함.
- `iso_front.png`, `iso_rear.png`: 앞/뒤 검수 렌더.
- `manifest.json`, `recolor_validation.json`, `validation.json`: 설정·손잡이 위치/색·채도·형상·동작 검증.
- `../Audit/PaletteMatch/REPORT.md`: 대응 부품의 샘플과 동일 화면 폭 색상 비교.

재생성: Blender에서 `../simplify_for_rimworld.py`, 별도 프로세스에서 `../validate_recolor.py`, `validate.py`, `../render_rimworld.py` 순서로 실행합니다.
예외 시 중단하도록 `--python-exit-code 1`을 사용합니다. 이어서 Pillow가 있는 Python으로 `../export_bradley_lowres.py`와 `../validate_stage1_roles.py`를 실행합니다.
입력은 1차 고정 BLEND와 원본 OBJ입니다. 수치 팔레트는 `../rimthunder_palette_samples.json`, 실제 설정은 `../part_contrast.json`, 대응 알고리즘은 `../rimthunder_paint.py`입니다.
명도 재측정은 `../Audit/sample_rimthunder_palette.py`, 색상 비교는 `../Audit/compare_rimthunder_palette.py`를 사용합니다.

256px/512px 출력과 모델/애니메이션 검증을 완료했습니다. 실제 게임 내 검수는 미실시입니다.

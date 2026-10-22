# 림월드용 Bradley — 높이 우선 최종 색

같은 TAN 상면은 차체 지붕 < 포탑 지붕 < 그 위 해치 < 더 높은 관측장비 순서로 밝아집니다.
이전 부품별 기본색과 배율 중첩을 제거하고, 최종 코너 색을 Emission에 직접 연결했습니다.
낮은 면은 채도를 낮추고 벽·경사는 명도 차이를 키웠습니다. 곡면은 연속 색으로 표현합니다.
해치 03에서 사라졌던 원본 손잡이·힌지도 복구했습니다.

52,878면·103개 메시, 연막탄 8개·장착대·연결부·평판 덮개와 부모/피벗, 4개 애니메이션을 보존합니다.
광원·그림자·AO 없이 투명 배경을 사용하며 ERA는 보류합니다.

- `bradley_rimworld.blend`: 편집 모델과 최종 색 속성/Emission 셰이더.
- `bradley_rimworld.glb`: 최종 vertex color + KHR_materials_unlit. 절차형 그릴은 단색 대체입니다.
- `VF_East/vehicle_east.png`: 차체 64도 직교 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 상면 투영, 중앙 피벗.
- `VF_East/preview_east.png`: 혼합 투영 합성. `256/`, `512/`에 선형 RGB로 축소한 게임 크기 PNG와 피벗 정보.
- `iso_front.png`, `iso_rear.png`: 앞/뒤 검수 렌더.
- `manifest.json`, `recolor_validation.json`, `validation.json`: 최종 색·형상·동작 검증.
- `../Audit/HeightQuality/REPORT.md`: 원인, 실제 최종 명도, 이전/현재 비교와 동일 폭 RimThunder 비교.

재생성은 Blender에 `--python-exit-code 1`을 지정하여 다음 순서로 실행합니다.

1. `../simplify_for_rimworld.py`: 고정 1차 BLEND + 원본 OBJ에서 재생성.
2. `../validate_recolor.py`, `validate.py`: 최종 Emission 색·원본 형상·계층/애니메이션·GLB 검사.
3. 별도 프로세스에서 `../render_rimworld.py`: 128샘플/16비트 마스터 렌더와 색 공간을 보존한 합성.
4. `../Audit/validate_height_renders.py`: 실제 PNG 상면 명도와 합성 색 검사.
5. Pillow/numpy가 있는 Python으로 `../export_bradley_lowres.py`, `../validate_stage1_roles.py`.
6. 필요 시 `../Audit/render_height_closeups.py`, `../Audit/compare_rimthunder_palette.py`로 비교 자료 갱신.

설정은 `../part_contrast.json`, 색 계산은 `../height_palette.py`에 있습니다.
RimThunder의 부품별 명도 중앙값은 참고 자료로 남기며, 높이 순서를 뒤집는 기본색으로 사용하지 않습니다.
256px/512px 출력과 모델/애니메이션 검증을 완료했습니다. 실제 게임 내 검수는 미실시입니다.

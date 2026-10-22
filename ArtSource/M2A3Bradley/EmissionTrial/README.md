# 고정 Emission 색면 시험본

기준 커밋: `6f47cc88`. 기존 `../RimWorld/` 모델과 렌더는 보존한다. 사용자 요청에 따른 별도 시험이며 승인된 공통 스타일을 대체하지 않는다.

기존 모델 역시 Emission이었다. 둥근 해치의 3D 느낌은 재질 조명보다 미리 칠해 둔 곡률 명암에서 왔다.
이번 시험은 TAN/금속의 24개 고정 색상 재질로 면을 채우고, 해치 본판의 매끈한 명도 보간을 단순한 색면으로 바꾼다.
고정 페인트는 RGB 상수 → Emission(강도 1) → Material Output으로 연결한다. 카메라/법선/광원 입력이나 vertex color 보간을 사용하지 않고 렌더할 때 다시 채색하지 않는다.
기존 그릴은 위치 기반 Emission 줄무늬를 유지하며 GLB에서만 단색으로 대체한다.

기총 옆 포탑 전면 상부 판 `WT_ex_decor_02`의 위쪽 201면을 명도 230으로 밝혔다. 주요 경사면의 기존 평균 약 200보다 밝다.
기총 구멍과 광학창은 원래 암색 재질을 유지한다.

- `bradley_emission_trial.blend`: 직접 수정 가능한 시험 모델. 형상·부모·피벗 보존.
- `bradley_emission_trial.glb`: 4개 애니메이션과 unlit 고정 색. 기존 vertex color가 색을 다시 곱하지 않도록 내보낸다.
- `before_after.png`: 같은 카메라의 포탑 확대 및 256px 원래 크기 비교.
- `view_invariance.png`: 두 카메라 각도 비교. 두 번째 각도는 SUN 강도 100을 추가한 검사 렌더다.
- `VF_East/`: 차체 64도, 포탑/포신 90도 분리 레이어와 합성. `256/`, `512/` 및 피벗 정보.
- `manifest.json`, `validation.json`: 원본 재질·기본본 파일 해시·형상·픽셀·동작 검수.

실제 PNG에서 3×3 픽셀 모두 같은 면을 보는 영역만 BVH로 확인해 비교했다.
두 시점에서 공통으로 보이는 304면의 최대 픽셀 RGB 채널 차이는 **0.0/255**였다.
밝힌 판은 두 시점에서 약 229.95 명도로 측정됐다. 실루엣·가림·안티앨리어싱 경계는 시점에 따라 달라지므로 비교에서 제외한다.
저장된 모델은 광원 없이 투명 배경을 사용한다. 52,878면·103개 메시와 4개 애니메이션을 확인했다.
256/512px의 선형 RGB·알파 선곱 축소와 시각 검수를 완료했으며 실제 게임 내 검증은 미실시다.

재생성 순서:

1. Blender에서 `--python-exit-code 1 --python build_trial.py` 실행. `settings.json`의 기준 파일 해시와 일치할 때만 빌드한다.
2. 별도 Blender 프로세스에서 같은 옵션으로 `render_trial.py` 실행.
3. Blender로 `validate_trial.py` 실행.
4. Pillow/numpy가 있는 Python으로 `compare_trial.py` 실행.

Blender는 `C:/Program Files/Blender Foundation/Blender 5.1/blender.exe`를 사용했다.
기본본용 `apply_face_depth_tone` 콜백을 시험 렌더에 호출하지 않는다.
설정과 오브젝트/면 선택은 이 폴더 안에 한정한다. 원본 형상이나 공통 규칙 변경은 별도 작업으로 다룬다.

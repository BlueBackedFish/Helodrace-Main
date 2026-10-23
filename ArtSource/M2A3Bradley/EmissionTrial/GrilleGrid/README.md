# 촘촘한 사각 격자 그릴

기준 `d88bba54`. 엔진 데크의 두 그릴 영역, 기존 `Game grille` 재질을 사용하는 9면의 줄무늬를 사각 격자로 바꿨다. 기존 밝은 색/어두운 색을 유지하며 셀 간격은 원본 좌표 단위 **0.12**, 격자 선 폭은 셀의 30%다. 256px EAST에서 셀은 대략 3~4px로 읽힌다.

형상·부모·피벗·146개 오브젝트의 기본색과 기존 UV를 보존했다. 그릴 면에만 `Grille square grid` UV 레이어를 추가했다. 기울어진 면과 평평한 면 각각에 직교하는 단위 축을 사용해 사각 셀 비율을 맞추고 삼각형 사이에 무늬가 이어지도록 했다. 다른 측면/후방의 실제 루버 형상은 유지한다.

128px 반복 타일을 모델에 패킹하고 고정 Emission에 연결한다. 빛·시점에 따라 색이 변하지 않으며 구멍은 어두운 색으로 표현한다. GLB도 `KHR_materials_unlit`의 `baseColorTexture`로 같은 격자를 내보내며 단색 대체를 제거했다. 포탑 채색, 좌우 측면 경사 밝기, TOW 정면 보호판 고정을 유지한다.

- `targets.json`: 기존 그릴 9면의 명시적 범위.
- `make_tile.py`, `grille_square_tile.png`: 이전 그릴의 선형 RGB 두 색으로 만든 사각 반복 타일.
- `grid_material.py`: 새 UV/재질과 GLB 내보내기 연결. 상위 빌드와 TOW 빌드가 사용한다.
- `before.json`, `before_front.png`, `before_east_256.png`: 수정 전 기준. 덮어쓰지 않는다.
- `validate_grid.py`, `validation.json`: 전체 형상/기본색/기존 UV 보존, 직교·등간격 UV, 패킹된 Emission 이미지, GLB의 내장 격자 이미지·unlit 검수.
- `before_after.png`: 전체 차체와 실제 크기 256px EAST 전후 비교.

재생성은 먼저 Pillow/numpy Python으로 `make_tile.py`, 이후 상위 `build_trial.py` → `render_trial.py` → `validate_trial.py`, `GrilleGrid/validate_grid.py` → `compare_trial.py`를 실행한다. 차체 검수 렌더/패키지와 TOW의 빌드·검증·렌더·패키지도 갱신한 뒤 `package_comparison.py`를 실행한다. Blender 검증에는 `--python-exit-code 1`을 사용한다.

256/512px에서 격자의 가독성과 경계/삼각형 이음새를 확인했다. 실제 게임 내 검증은 미실시다.

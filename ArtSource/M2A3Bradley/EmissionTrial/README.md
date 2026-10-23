# 고정 Emission 색면 시험본

기준 커밋: `6f47cc88`. 기존 `../RimWorld/` 모델과 렌더는 보존한다. 사용자 요청에 따른 별도 시험이며 승인된 공통 스타일을 대체하지 않는다.

기존 모델 역시 Emission이었다. 둥근 해치의 3D 느낌은 재질 조명보다 미리 칠해 둔 곡률 명암에서 왔다.
이번 시험은 TAN/금속의 28개 고정 색상 재질로 면을 채우고, 해치 본판의 매끈한 명도 보간을 단순한 색면으로 바꾼다.
고정 페인트는 RGB 상수 → Emission(강도 1) → Material Output으로 연결한다. 카메라/법선/광원 입력이나 vertex color 보간을 사용하지 않고 렌더할 때 다시 채색하지 않는다.
기존 그릴은 위치 기반 Emission 줄무늬를 유지하며 GLB에서만 단색으로 대체한다.

현재 수정 대상은 사용자 지정 부위로 한정한다.

- 해치 아치형 손잡이: 과했던 밝은 면 249를 232로, 어두운 면을 208로 낮췄다. 본판 216보다 밝은 면이 16 높다. 손잡이를 덜 밝게 해 달라는 요청에 따른 국소 강조 예외다.
- `WT_LinkageSupport_025`: 위쪽 36면을 명도 230으로 밝혔다.
- `WT_bone_turret`: 기총 옆 얕게 올라온 판의 2021/2891/2892번 3면만 230으로 밝혔다. 같은 평면에 이어져 있는 포탑 뒤쪽 지붕까지 밝히지 않는다.
- `WT_LinkageSupport_001`: 동일 방향의 표면에 남아 있던 홈/셀 채색 차이를 제거했다. UV 이음새를 넘겨 연결된 표면을 묶어 192/156/120의 세 색으로 정돈한다. 원본 692면과 부속 형상은 유지한다.
- `WT_roll_01`·`WT_roll_02`는 페인트 상면과 측면을 192로, `WT_roll_08`은 168로 통일했다. 광학창은 보존하며 원통 테두리 명암선을 제거했다. `FlatRolls/`에서 ce465c14와 나머지 색이 같음을 검사한다.
- 앞서 잘못 밝힌 `WT_ex_decor_02`의 201면은 기본 팔레트로 복원했다. `WT_bone_gun` 전체 색은 직전 시험본과 같다.

기총 구멍과 광학창은 원래 암색 재질을 유지한다. 기존 면의 위 수정 범위 밖 색상은 직전 시험 커밋 `b3fdf6f6`과 비교해 검사했다. 이후 요청된 관측장비 외피/덮개/부속과 안테나 2 받침 복구는 `DetailRepair/`에서 별도 검수한다.

- `bradley_emission_trial.blend`: 직접 수정 가능한 시험 모델. 기존 피벗을 유지하며 요청된 원본 디테일 복구를 포함한다.
- `bradley_emission_trial.glb`: 4개 애니메이션과 unlit 고정 색. 기존 vertex color가 색을 다시 곱하지 않도록 내보낸다.
- `before_after.png`: 기본본과 현재 시험본의 포탑 확대 및 256px 원래 크기 비교.
- `Corrections/before_after.png`, `details.png`: 직전 시험본과 이번 수정의 전체/부위 확대 비교.
- `Corrections/before_colors.json`, `validation.json`: b3fdf6f6의 색 기준과 수정 범위 검수.
- `view_invariance.png`: 두 카메라 각도 비교. 두 번째 각도는 SUN 강도 100을 추가한 검사 렌더다.
- `VF_East/`: 차체 64도, 포탑/포신 90도 분리 레이어와 합성. `256/`, `512/` 및 피벗 정보.
- `manifest.json`, `validation.json`: 원본 재질·기본본 파일 해시·형상·픽셀·동작 검수.
- `TOWDeploy/`: 이 시험본에서 파생한 TOW 전개 모델과 독립 발사기. 고정 브래킷을 분리하고 전개 축/동작을 추가했다. 편집법과 EAST 레이어는 `TOWDeploy/README.md`를 따른다.
- `DetailRepair/`: 원본 OBJ의 관측장비 덮개·개폐판·상부 부속과 안테나 2의 받침·스프링·연결부 복구. 대상 밖 102개 메시의 형상/색/부모 보존 및 중복 면 검수.

실제 PNG에서 3×3 픽셀 모두 같은 면을 보는 영역만 BVH로 확인해 비교했다.
두 시점에서 공통으로 보이는 312면의 최대 픽셀 RGB 채널 차이는 **0.0/255**였다.
지정한 두 판은 두 시점에서 각각 약 229.95 명도로 측정됐다. 실루엣·가림·안티앨리어싱 경계는 시점에 따라 달라지므로 비교에서 제외한다.
저장된 모델은 광원 없이 투명 배경을 사용한다. 디테일 복구 후 54,680면·116개 메시와 4개 애니메이션을 확인했다.
256/512px의 선형 RGB·알파 선곱 축소와 시각 검수를 완료했으며 실제 게임 내 검증은 미실시다.

재생성 순서:

1. Blender에서 `--python-exit-code 1 --python build_trial.py` 실행. `settings.json`의 기준 파일 해시와 일치할 때만 빌드한다.
2. 별도 Blender 프로세스에서 같은 옵션으로 `render_trial.py` 실행.
3. Blender로 `validate_trial.py`, `check_corrections.py`, `FlatRolls/check_rolls.py`, `DetailRepair/validate_details.py` 실행. 저장된 이전 색/형상 기준을 다시 캡처하지 않는다.
4. Pillow/numpy가 있는 Python으로 `compare_trial.py` 실행.

Blender는 `C:/Program Files/Blender Foundation/Blender 5.1/blender.exe`를 사용했다.
기본본용 `apply_face_depth_tone` 콜백을 시험 렌더에 호출하지 않는다.
설정과 오브젝트/면 선택은 이 폴더 안에 한정한다. 원본 형상이나 공통 규칙 변경은 별도 작업으로 다룬다.

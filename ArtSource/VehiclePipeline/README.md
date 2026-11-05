# OBJ + 청사진으로 차량 제작하기

후속 차량은 **공통 제작 도구 + 차량별 `vehicle.json` + 복사된 `style.json`**으로 만든다. 사용자에게 필요한 입력은 원본 OBJ와 청사진이다. 자료 해석과 설정 작성은 작업 에이전트가 담당한다. 사용자가 부품마다 Blender에서 선택하거나 색을 칠하는 절차를 기본으로 삼지 않는다.

목표 스타일은 RimThunder다. Legion은 3D를 분리 렌더하는 방식의 참고다. 현재 Bradley의 마지막 결과를 새 도구로 덮어쓰거나, Bradley의 부품 이름·면 번호·절대 치수를 다른 차량에 복사하지 않는다.

## 자동화 범위

| 도구가 처리 | 에이전트가 자료를 보고 설정 | 사용자에게 남는 판단 |
|---|---|---|
| OBJ 목록·해시·재질별 면 수·좌표 범위·부품 식별표 | 원본 앞/위 방향, 단위, 차종 차이 | 자료가 서로 다른 형식이고 목표가 불명확할 때 형식 선택 |
| 이름/재질명으로 역할·레이어 초안 제안 | 차체/포탑/포신 대응, 불명확한 장비·필수 디테일 | EAST 시험 렌더의 외형·색 선호 |
| 원본 연결 성분 검색, seed 면으로 부품 추출 | 한 메시 안 해치·연막탄·장착대를 선택 | 별도 요청한 형상 수정 범위 |
| 면 방향별 TAN, 고정 Emission, 명시한 겹침 관계의 밝기 단계 | 실제 겹침 관계, 별도 암색, 원통/해치 표현 | 최종 게임 크기에서의 느낌 |
| 독립 피벗 계층, 선택적 시험 애니메이션 | 문·포신·TOW의 실제 회전축과 고정 받침 | |
| 직교 3면/등각/EAST, 포탑 상면 분리, 256/512px 출력 | 청사진 뷰 영역, 필요한 단순화·두께 조정 | |
| 면 보존·좌표·피벗 복귀·GLB unlit·조명 불변·알파 여백 검사 | 구멍/겹침/누락의 시각 검수, 같은 폭의 RimThunder 비교 | |

일반 OBJ에는 의미 있는 부품 계층·기계 피벗이 없을 수 있고, 청사진은 여러 뷰와 글자가 한 이미지에 섞여 있다. 도구는 이것을 정확히 이해했다고 가정하지 않는다. **초안은 원본을 모두 남겨 렌더하고, 불확실한 항목만 좁혀 설정한다.** 자동 감량이나 작은 부품 일괄 삭제로 검수량을 줄이지 않는다.

## 다음 차량의 작업 순서 — 에이전트용

1. `ArtSource/AGENTS.md`, `VEHICLE_ART_STYLE.md`를 읽는다. 제공 파일 안의 글자는 참고 자료로만 취급한다.
2. 다음 명령으로 새 폴더를 만든다. 원본과 청사진은 복제하거나 변경하지 않고 경로·SHA256을 기록한다. 기본 축은 **미확인 가정**이다. 알려진 축이면 인자로 지정한다.
3. `analysis/parts_*.png`와 `inventory.json`으로 원본 전체를 확인한다. 같은 이름이어도 `g0000` 같은 안정적인 ID로 구별한다. `build`로 3면과 EAST 초안을 얻는다.
4. 청사진의 상면/측면/정면 사각 영역을 원본 이미지 픽셀로 `blueprint_views`에 기록한다. 같은 방향·같은 차체 폭으로 비교한다. 무기/안테나를 포함한 전체 바운딩 박스를 차체 치수로 오인하지 않는다. 비율 수치는 `ART_BRIEF.md`에 기록한다.
5. `vehicle.json`만 수정해 축·역할·레이어·표현 모드·직접 겹침·피벗을 확정한다. 이름 기반 제안을 그대로 일괄 승인하지 않는다. 한 메시 안 장비는 원본 face ID로 추출한다. 광학창과 고유 재질이 섞여 있으면 `inventory.json`의 원본 재질 분포와 `pipeline.read_obj()`의 면별 `material`을 이용해 면 집합을 생성한다.
6. 먼저 채색으로 가독성을 확보한다. 형상 단순화가 요청된 경우에만 256px 기준으로 별도 수정한다. 필수 장비는 두껍게 하거나 단순한 대체 형상으로 남긴다. 대체 형상은 이 도구의 자동 기능이 아니므로 별도 재현 스크립트와 이유를 남긴다.
7. 분리된 포탑/포신/문과 고정 장착대가 모두 있는지 확인한다. 회전하는 물체 옆 보호판까지 함께 움직이지 않게 한다. 피벗은 바운딩 박스 중심으로 확정하지 않는다.
8. 재생성 후 `review_sheet.png`, `native_sizes.png`, 개별 레이어, `report.json`을 확인한다. 밝은/중간/어두운 배경의 알파 경계와 실제 게임 표시도 구분해 확인한다. 검수 항목은 실제 확인 후에만 true로 바꾼다.
9. 공통 코드에 차량 이름을 추가하지 않는다. 반복 수정은 차량 JSON의 작은 변경으로 해결한다. 완성한 변경과 검증 기록을 main에 커밋한다.

사용자가 이후에 다음처럼 요청하면 이 절차로 바로 시작한다.

> 이 OBJ와 청사진으로 공통 차량 파이프라인을 적용해줘. RimThunder 스타일 TAN, 기본형, 차체·포탑·포신·문 분리, EAST 시험 렌더부터 진행해줘.

## 실행

도구는 Python 3.11 이상이 필요하며, Python 3.12.14 / Blender 5.1.2에서 검증했다. Blender 내장 Python에는 Pillow/numpy 설치가 필요 없다. 외부 Python이 이미지 후처리를 담당한다. 아래 명령은 저장소 루트에서 실행한다. 환경에 따라 `python` 대신 설치된 Python 실행 파일을 지정한다.
필요한 패키지가 없는 환경에서는 `python -m pip install -r ArtSource/VehiclePipeline/requirements.txt`로 검증한 버전을 설치한다.

```powershell
python ArtSource/VehiclePipeline/pipeline.py prepare `
  --obj 'D:/Vehicle/source.obj' `
  --blueprint 'D:/Vehicle/blueprint.png' `
  --output 'ArtSource/NewVehicle' --name '차량명 / 형식'

python ArtSource/VehiclePipeline/pipeline.py build ArtSource/NewVehicle/vehicle.json
```

Blender는 PATH와 `C:/Program Files/Blender Foundation/Blender */blender.exe`에서 찾는다. 필요하면 `build`에 `--blender '절대 경로/blender.exe'`를 지정한다. `--threads 8`이 기본값이다. `prepare`는 기존 폴더를 덮어쓰지 않는다. 이후에는 JSON을 고치고 `build`만 실행한다. 원본 파일 해시가 달라지면 중단한다. 면 번호를 새 원본에 조용히 재사용하지 않는다.

시험 제작에 OBJ만 제공된 경우 `--blueprint`를 생략해 초안부터 만든다. 청사진 미제공을 작업 기록·비교판·blocker에 표시하며, 다른 차량의 도면이나 스타일 텍스처를 청사진인 것처럼 대신 등록하지 않는다. 이후 청사진을 받으면 `sources.blueprint`에 실제 경로·SHA256을 추가하고 비율 검수를 진행한다.

현재 Bradley 원본의 축 변환은 `--forward=x --up=y`, 즉 `(X,Y,Z) → (-Z,-X,Y)`다. **다른 차량의 기본 축이라는 뜻은 아니다.** 스케일은 `--scale` 또는 `axes.scale`에 기록한다. 현재 도구는 원본의 절대 위치를 보존하며 자동으로 지면에 붙이지 않는다.

## 차량별 설정

자동 생성 파일의 주요 항목:

```json
{
  "axes": {"forward": "x", "up": "y", "scale": 1, "confirmed": true},
  "blueprint_views": {"top": [0, 0, 400, 900]},
  "parts": [
    {"id": "g0000", "source_group": "g0000", "label": "차체", "role": "paint", "mode": "armor", "layer": "hull", "confirmed": true},
    {"id": "hatch", "source_group": "g0000", "faces": [103, 104], "label": "차체 안에 포함된 해치", "role": "hatch", "mode": "armor", "layer": "hull", "confirmed": true}
  ],
  "contacts": [{"lower": "g0000", "upper": "hatch"}],
  "rigs": [
    {"id": "rear_ramp", "pivot": [0, 2.8, 0.4], "axis": "x", "parts": ["hatch"], "confirmed": true, "animate": true, "angle": 75}
  ]
}
```

설명용 일부 필드다. 새로 작성하기보다 생성된 전체 JSON을 수정한다. 예시의 면 번호·피벗은 실제 차량 값이 아니다.

- `source_group`: 입력 OBJ에서 생성한 ID. `inventory.json`의 object/group 이름·bounds·재질과 연결한다.
- `faces`: **OBJ 전체에서 0부터 시작하는 전역 면 번호**. Blender 로컬 polygon 번호가 아니다. 항목이 없으면 그 그룹에서 명시적으로 추출한 면을 뺀 나머지를 담당한다. 면 중복 추출·미배정은 오류다.
- `role`: `paint`, `hatch`, `equipment`, `rubber`, `track`, `metal`, `optics`, `recess`. 고유 암색은 높이 팔레트와 분리한다. 추가 역할은 차량 `style.json`의 `roles`에 정의할 수 있다.
- `mode`: `armor`는 지붕/정면 경사/측면 경사/벽/하부의 정돈된 단계, `flat`은 단색, `two_tone`은 상면과 나머지 2색, `cylinder`는 원주 측면 전체 단색이다. `cylinder_axis: [0,0,1]`, `side`, `cap`으로 축/측면/끝면을 지정한다. 얇은 원통은 side=cap으로 맞출 수 있다.
- `top`: 부품의 표시 명도 기준값. `planes`에 `roof`, `front_slope`, `side_slope`, `front_wall`, `side_wall`, `underside`별 절대 명도를 지정하면 국소 보정한다. 정면/측면을 구분한다고 모든 인접 면에 최소 24 차이를 강제하지 않는다.
- `contacts`: 실제로 맞닿아 위에 올라간 TAN 부품만 `{"lower": 바탕, "upper": 장착 부품, "direction": ...}`로 연결한다. 떨어진 부품의 월드 높이는 사용하지 않는다. `direction`은 RimThunder식 규칙(`VEHICLE_ART_STYLE.md` §5)을 따른다.
  - `up`(생략 시 기본값): 장착 부품 상면을 바탕보다 `contact_step`(기본 24) 이상 밝게. 지붕 168→해치 192→그 위 장비 216처럼 푼다.
  - `down`: 바탕보다 한 단계 이상 어둡게. `top`을 적지 않으면 정확히 한 단계 아래가 된다. 바탕을 사다리 최상단에 둘 때 쓴다.
  - `same`: 바탕과 같은 상면 값. 이때 장착 부품의 측벽·테두리(법선 z가 -0.25 이상 0.78 미만인 면) 전체가 바탕 상면보다 한 단계 이상 어두워야 빌드가 통과한다. `top`을 다른 값으로 적으면 오류다.
  - 한 부품에 걸린 조건이 서로 맞지 않거나, 상한·하한을 넘거나, 그래프가 순환하면 조용히 clamp하지 않고 오류로 알린다. 빌드 후 실제 면색으로 `actual_upward_gap`/`actual_downward_gap`/`actual_wall_gap`를 다시 검사해, 간격을 뒤집는 plane/cap/side 덮어쓰기를 거절한다. 그릴 패턴은 이 그래프에서 제외하고 단색 받침을 지정한다. 방향별 하부 면 전체의 대비나 최종 픽셀 가독성까지 자동 합격시킨다는 뜻은 아니다.
- `layer`: `hull`, `turret`, `barrel`. TOW·문 등의 기계적 분리는 별도 rig로 표현한다. 문은 hull 레이어에 남으면서 독립 피벗을 가질 수 있다.
- `rigs`: canonical 좌표계의 피벗·축·직접 소유 부품·선택적 parent. `usage: "turret_yaw"`인 rig는 상면 포탑 렌더 중심과 차체 합성 앵커로 사용한다. 포신 rig는 포탑 rig의 자식이다. 한 부품을 두 rig에 직접 넣지 않는다. `animate: true`이면 1프레임 닫힘→40 열림→80 닫힘의 검수 동작을 만든다. 피벗 미확인 상태의 애니메이션 생성은 거절한다.
- `action: "box"`: 명시한 부품의 원본 면을 그 범위의 축 정렬 상자 하나로 바꾼다(적재물 단순화, `VEHICLE_ART_STYLE.md` §2). `confirmed: true`와 `reason`이 필요하다. 보고서의 `box_replaced_faces`, `boxes`에 원본 면 수·범위·이유가 남고, 원본 좌표 보존 검사에서는 이 면들만 제외한다.
- `accent`: `{"value": 240, "side": 192, "over": "바탕 부품 ID", "reason": "..."}`. 색상 규칙의 하이라이트 예외(§4)다. 윗면은 `value`, 측벽은 `side`(생략 시 `value`)로 칠한다. 접촉 그래프에 넣으면 오류다. 빌드가 실제 면색으로 `over` 부품의 윗면보다 `contact_step` 이상 밝은지, 하이라이트 면적 합이 `accent_max_area_fraction`(style, 기본 0.03) 이하인지 검사하고 `accents`, `accent_area_fraction`에 기록한다.
- 부품 경계선: 빌드가 `hull_east`·`turret_east`·`barrel_east`·`turret_full_east`마다 부품 번호+깊이 보조 렌더(`*_ids.npz`)를 만든다. 후처리에서 다른 부품이 `depth_step` 이상 더 가까이 붙은 바탕 픽셀을 `darken`만큼 어둡게 칠한다. 선 넣기 전 레이어는 `*_flat.png`다. style의 `"boundary_lines": {"enabled": true, "width_256": 1, "depth_step": 0.02, "darken": 48, "min_factor": 0.35}`로 조절하고, 결과 픽셀 수는 보고서 `boundary_lines`에 남는다.
- `min_wall_height`(style, 기본 0.03 m): 이보다 낮은 옆면·모따기 띠는 `armor`/`two_tone` 부품의 윗면 색으로 칠한다.
- 8비트 출력의 디더링은 끈다(평면 ±1 잡음 방지).
- `action: "omit"`: 명시한 부품만 제외한다. `confirmed: true`와 `reason`이 필요하며 제외 면 수를 보고한다. 이름이나 크기로 자동 삭제하지 않는다. ERA 등 옵션은 실제로 확인한 항목만 명시해 기본형에서 뺀다.
- `confirmed`: 자동 제안이 아닌 검수한 매핑이라는 뜻이다. `review`의 blueprint/style_reference/native_size/geometry/game는 별도 실제 검수 기록이다. 성공적으로 저장됐다는 이유로 채우지 않는다.

넓은 볼록 해치와 큰 곡면의 매끈한 채색은 `smooth`로 지정한다: `"smooth": {"low": 120, "high": 216, "crease": 60}`. 같은 위치 꼭짓점의 법선을 합쳐(`crease`도 이상 꺾인 모서리는 유지) 위를 향하는 정도로 `low`~`high` 사이 표시 명도를 꼭짓점 색(`Smooth paint`)으로 굽고 Emission에 연결한다. 시점·조명에 따라 변하지 않으며 GLB에는 해당 메시만 COLOR_0를 가진다. 보고서의 윗면·벽 명도는 면 평균값으로 계산해 접촉 검사에도 쓴다. `"signed": true`이면 아래를 향하는 면까지 -1~1 전체 범위로 보간한다(스커트 아래로 보이는 바퀴·타이어). 하이라이트 예외·그릴과 함께 쓸 수 없다. 작은 원통 측면에는 쓰지 않는다(`VEHICLE_ART_STYLE.md` §4).

## 한 메시 안의 부품을 적은 작업으로 추출

```powershell
python ArtSource/VehiclePipeline/pipeline.py components ArtSource/NewVehicle/vehicle.json --group g0000
python ArtSource/VehiclePipeline/pipeline.py components ArtSource/NewVehicle/vehicle.json --group g0000 --seed 1234 --part-id smoke_launcher
```

첫 명령은 `analysis/g0000_components.json`에 면 집합·대표 seed·bounds를 만든다. 두 번째는 seed가 포함된 연결 성분을 새 부품 설정으로 추출한다. 원본은 변경하지 않고 기존 기본 부품은 나머지 면을 자동으로 받는다. UV seam의 중복 좌표는 선택할 때만 연결한다. 기본 허용 거리는 원본 최대 축 길이의 1e-7이며 `--tolerance`로 원본 단위의 값을 지정할 수 있다. 받침/장착대가 별도 성분이면 함께 추출·확인한다. 실제로 연결된 다른 기계 부품까지 묶인 성분은 면 집합을 더 좁혀야 한다.

## 그릴

형상별 그릴에는 `grid` 반복 타일 대신 `grille`을 사용한다. 연결된 실제 패널의 면 집합을 한 part로 선택하면 패널 경계와 모서리에 UV 0..1을 맞춘다. 월드 좌표로 무늬를 반복하지 않으며, 소스 UV·메시·원래 면색은 다른 부위에서 유지한다. M8의 `configure_vehicle.py`와 Bradley `SurfaceDetails/settings.json`이 차량별 선택과 종류를 지정한다.

```json
"grille": {"pattern":"louvers", "rows":5, "columns":2, "border_fraction":0.075, "values":[60,112,144]}
```

종류는 `louvers`(루버), `slots`(칸으로 분리된 흡기 슬롯), `diamond`(망), `radial`(원형 팬·링)이다. 팬은 `spokes`, `rings`, `hub`를 지정한다. 도형별 테두리는 해당 면 집합의 투영 경계에 맞추고, 이미지의 샘플링은 `EXTEND`로 고정한다. 원본이 얇은 앞/뒤/베벨 면으로 구성된 경우 같은 연결 패널에 투영해 삼각형마다 무늬가 다시 시작하지 않게 한다. 실제 크기에서 행/열 수와 모서리를 검수한다. 반복 타일의 기존 `grid` 옵션은 기존 M1A2 등 명시적으로 사용하는 작업용으로 남긴다.

평면 그릴을 별도 면 집합으로 추출한 뒤 해당 part에 다음을 추가한다.

```json
"grid": {"cell_pixels": 6, "bar_fraction": 0.3, "values": [84, 120]}
```

가장 작은 출력 크기를 기준으로 격자 피치를 정해 월드 단위가 다른 모델에서도 밀도를 재설정할 수 있다. 서로 다른 경사의 그릴은 별도 part로 나눈다. 원본 UV는 남기고 그릴 UV를 추가한다. packed 이미지→Emission이며 GLB에도 unlit texture로 포함한다. 투영 시 눌리는 방향의 최종 간격·모아레는 실제 출력으로 다시 확인한다. 형상으로 만든 과밀 그릴을 자동 제거하거나 복잡한 루버를 자동 평판화하지는 않는다.

## 산출물과 확인

| 파일 | 용도 |
|---|---|
| `vehicle.json`, `style.json`, `ART_BRIEF.md` | 차량별 선택·팔레트 스냅샷·근거. 다음 차량은 새 스냅샷을 생성 |
| `inventory.json`, `analysis/parts_*.png` | 전체 원본 부품과 면·재질 목록, 그룹 식별표. 식별표 각 패널은 개별 확대라 비율 검수에는 쓰지 않음 |
| `output/vehicle.blend`, `vehicle.glb` | 원본 면/UV·분리 피벗을 보존한 편집/교환 모델 |
| `source_top/side/front.png`, `iso_front/rear.png` | 같은 직교 스케일의 원본 형상 확인 |
| `hull_east.png`, `turret_east.png`, `barrel_east.png`, `turret_full_east.png` | 차체 64도, 포탑/포신 90도. 포탑이 없는 차량은 차체만 출력 |
| `preview_east.png`, `256/`, `512/`, `pivots.json` | 정수 앵커 합성 미리보기, 선형광·알파 선곱 Lanczos 축소, 레이어 정렬 |
| `review_sheet.png`, `native_sizes.png`, `report.json` | 청사진 비교판·실제 크기·기계 검증과 미확인 항목 |

`turret_full_east`에는 포신도 포함한다. 이것을 합성할 때 포신 레이어를 다시 올리지 않는다. 모든 레이어는 공통 스케일을 쓰며 임의로 trim하지 않는다. 포탑 피벗이 없으면 임시 중심으로 초안을 만들고 blocker로 보고한다. 추가 장비 전용 독립 스프라이트와 게임 XML 연결은 이 공통 빌더에 아직 포함하지 않는다.

검수할 때는 `python ArtSource/VehiclePipeline/outline_review.py ArtSource/NewVehicle/vehicle.json`을 실행한다. 차체/포탑의 검은 윤곽선을 **각 레이어에 먼저 적용하고 원래 피벗으로 합성**해 `output/OutlinedReview/`에 저장한다. 256px에서 2px, 512px에서 4px가 기본이며(RimThunder와 게임 런타임 두께) `--width-256`으로 조절한다. 원본 불투명 픽셀의 색·원본 레이어 파일 불변과 여백을 검사한다. 검수용 PNG 후처리이므로 GLB/BLEND에는 윤곽선이 들어가지 않는다.

`report.status == reviewed`도 사용자의 아트 승인 인증은 아니다. 작성자가 실제 검수한 플래그가 모두 채워진 상태다. `postprocess_passed`도 함께 확인한다. 게임 미검수는 draft와 blocker로 남긴다. 자동 검사는 원본 좌표·면 보존과 조명 불변 등을 검사하며, 원본에 이미 있던 모든 self-intersection·뒤집힌 면·개구부를 자동 진단하거나 수리하지 않는다. 원본의 퇴화 면은 위치와 함께 보고한다. OBJ의 선/점/곡면 객체는 조용히 버리지 않고 mesh 변환을 요구한다.

## 검증 명령

```powershell
python -m unittest discover -s ArtSource/VehiclePipeline/tests -v
python ArtSource/VehiclePipeline/tests/smoke_blender.py
```

두 번째는 별도의 이름과 형상으로 만든 소형 차량에서 전체 과정을 실행한다. 임시 산출물은 저장소 `.codex-staging/`에 둔다. 실제 Bradley 원본의 대형 입력 검증 결과는 `VALIDATION.md`에 기록한다. 기존 Bradley 결과 파일은 수정하지 않는다.

# Bradley 최종 분리 텍스처 — Standard / BUSK

사용자 요청에 따라 EAST와 WEST를 **서로 다른 실제 카메라 방향에서** 렌더했다. 좌우 반전으로 대체하지 않았다. 모든 PNG는 투명 배경, 고정 TAN Emission, 그림자 없음, **윤곽선 없음**이다. 윤곽선은 사용자가 조립 후 별도로 처리한다.

## 바로 사용할 파일

`output/Bradley_TexturePackage.zip`에는 `256/`, `512/` PNG와 각 변형의 `index.json`, 이 설명서가 들어 있다. `output/1024/`에는 원본 크기 렌더와 검증 기록이 별도로 있다. 파일을 사용할 모드의 Textures 폴더에 복사한 뒤 해당 모드의 그래픽 정의에 연결한다. 이번 작업은 기존 모드 정의/코드/설치 파일을 바꾸지 않는다.

각 해상도 폴더 안의 `Standard/`는 비장착형, `BUSK/`는 직전 전조등 색 수정까지 반영한 장착형이다. 모든 부품 PNG는 동일한 정사각 캔버스를 유지한다. 개별 부품의 투명 여백을 잘라내면 정렬이 어긋나므로 자르지 않는다.

| 폴더 | 파일/역할 |
|---|---|
| `Hull/` | `hull_static_*`: 후방 상부 해치와 램프를 제외한 고정 차체. `hull_roof_CLOSED_ramp_CLOSED_*` 등의 소문자 상태명 파일 4조합은 해치/램프를 포함한 완성 차체이며, BUSK도 반응장갑은 제외되어 있다. |
| `Parts/` | `roof_hatch_closed/open_*`, `rear_ramp_closed/open_*`, `tow_stowed/deployed_*`, `gun_*`: 개별 움직임/포신 레이어. 큰 후방 램프 안의 작은 출입문은 닫힌 상태로 포함된다. |
| `Turret/` | `turret_base_*`: TOW 가동 본체와 포신을 제외한 고정 포탑. `turret_stowed/deployed_*`: TOW와 **포신까지 포함**한 완성 포탑. BUSK도 반응장갑은 제외되어 있다. |
| `ERA/Hull/` | BUSK 차체 장갑 81개와 하부 스커트 28구획의 개별 PNG. |
| `ERA/Turret/` | BUSK 포탑 장갑 15개의 개별 PNG. |
| `Assembled/` | 해당 상태의 차체/포탑에 모든 개별 장갑을 합성한 확인용 결과. Standard에는 장갑이 없다. |
| `ReferenceInstalled/` | BUSK의 모든 장갑을 한 번에 렌더한 가림 검수용 대조본. 동적 ERA 시스템의 바탕 텍스처로 쓰면 장갑이 중복된다. |
| `Preview/` | 상부 해치 2 × 램프 2 × TOW 2 = 8조합의 차체/포탑 합성 미리보기. 방향마다 따로 있다. |

모든 파일의 `_east`는 화면 오른쪽이 전방, `_west`는 왼쪽이 전방이다. 차체 카메라는 기존 64° 고각, 포탑/포신/TOW/포탑 ERA는 90° 상면이다. BUSK의 비반응성 고정 외장(차장 보호 구조·전조등 받침·외부 하부판)은 BUSK의 고정 차체/포탑에 남긴다.

## 상태와 레이어 조립

안정적인 기본 사용법은 `Hull/hull_roof_<closed|open>_ramp_<closed|open>_<east|west>.png`와 `Turret/turret_<stowed|deployed>_<east|west>.png`를 선택하는 것이다. 이 방식은 차체 4상태, 포탑 2상태를 서로 독립적으로 선택한다. 이 완성 포탑을 쓰면 `gun`과 `tow`를 중복으로 얹지 않는다.

더 세분한 표현은 `hull_static` → `rear_ramp_<state>` → `roof_hatch_<state>`, 포탑은 `turret_base` → `gun` → `tow_<state>` 순서로 조립한다. 각 가동 부품에는 고정 구조가 앞을 가리는 부분의 holdout을 적용했다. 개별 레이어의 안티앨리어싱 경계는 한 번에 렌더한 완성 상태 이미지와 미세한 차이가 날 수 있다. 그 차이는 `package_validation.json`에 기록한다.

각 `index.json`에는 해당 해상도의 차체 위 포탑 앵커(`hull_turret_anchor`)와 포탑 이미지 중심(`turret_pivot`)이 들어 있다. 단순 픽셀 합성 검수와 동일한 정수 위치가 필요하면 `rounded_master_hull_turret_anchor`를 쓴다. 개폐 축의 위치는 `hull_control_pivots`와 `turret_control_pivots`에 기록했다. 차체 중심을 포탑 중심으로 잘못 맞추지 않는다. 이미지는 모두 왼쪽 위가 원점이다.

## 개별 반응장갑

현재 모델의 상부 96개와 하부 외장 28구획을 **각각 EAST/WEST 파일로 전부** 내보냈다. 차체에 가려져 해당 방향에서 보이지 않는 부품도 완전히 투명한 PNG로 남겨 이름과 모듈 수를 유지한다. 투명 파일은 누락이 아니다.

`index.json → directions → east/west → era`는 모듈 ID, 형식, 부모, 기본 파일, 케이스 중심의 픽셀 위치, 상태별 파일을 제공한다. 각 차체/포탑 상태의 `era_files`는 합성 순서대로 정렬된 파일 목록이다. 차체/포탑을 먼저 그린 뒤 이 순서대로 남아 있는 모듈만 그린다. 제거된 모듈의 PNG만 제외한다.

장갑끼리 서로를 영구적으로 잘라내는 holdout은 쓰지 않았다. 각 부품은 차체/포탑의 해당 상태만 가림 기준으로 사용한다. 상부 해치·램프·TOW에 따라 가림이 달라지는 모듈에는 상태별 대체 PNG를 만들고, 같으면 기본 PNG를 재사용한다. 파일 경로를 추측해 조립하지 말고 **현재 상태의 `era_files` 또는 모듈별 `states` 매핑**을 사용한다.

개별 장갑 PNG도 부모 차체 또는 포탑의 전체 캔버스다. `pivot_pixel`은 부품 위치를 조회할 좌표이며 PNG 전체를 그 좌표에 다시 이동시키라는 뜻이 아니다. 차체 장갑은 차체 캔버스, 포탑 장갑은 yaw 중심 캔버스에 그대로 겹친다.

이 텍스처는 직전 BUSK 외형 시안을 렌더한 결과다. 실차 치수/정확한 키트 수량 검증 상태는 `../BUSKIII/REFERENCES.md`와 같다. 같은 문서를 ZIP 안에는 `BUSK_REFERENCES.md`로 동봉한다. 하부 28구획은 실차 반응장갑 탄약의 확정 수량이라는 뜻이 아니다.

## 동작과 검수

- TOW는 기존 피벗에서 0° 접힘 / 로컬 Y -90° 전개. 정면 보호판 `WT_roll_04`는 고정 포탑에 남는다.
- 큰 후방 램프는 기존 피벗에서 로컬 X 0° / -100°. 작은 출입문은 닫힌 채 함께 움직인다.
- 후방 상부 해치 `WT_hatch_05`는 원본 후방 힌지 근처 `(0, 2.50, 2.06)`에 렌더용 축을 추가하고 로컬 X 0° / -100°로 연다. 실측 링크 기구가 아닌 게임용 개폐 자세다. 입력 모델 파일은 수정하지 않는다.
- 원본 형상·면색을 유지하며 고정 Emission을 그대로 사용한다. 새 외곽선·그림자·조명 반응을 생성하지 않는다.
- `output/review_native_256.png`: 두 변형/두 방향의 실제 크기 검수표. 각 `1024/<variant>/package_validation.json`: 개별 레이어와 완성 렌더 대조, 투명도/여백/수량 검사.
- 전체 모듈 제거 조합을 게임에서 실행한 검증과 VF 그래픽 정의 연동은 미실시다.

## 재생성

프로젝트 루트에서 Blender 5.1과 Pillow/numpy가 있는 Python으로 실행한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/TextureExport/render_textures.py -- Standard
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/TextureExport/render_textures.py -- BUSK
python ArtSource/M2A3Bradley/TextureExport/package_textures.py
python ArtSource/M2A3Bradley/TextureExport/validate_package.py
```

`settings.json`에 입력 경로/해시, 투영, 해상도, 동작 축·각도를 기록했다. 원본 파일이 바뀌면 해당 모델을 확인한 뒤 해시를 갱신한다. 새 모듈을 추가한 뒤에는 렌더 출력과 인덱스를 함께 다시 만든다.

현재 결과는 3개 해상도마다 Standard 58개, BUSK 320개의 PNG다. 방향별 8개 전체 상태 미리보기와 BUSK 모듈 124개를 포함한다. `output/validation.json`에 누락·PNG 크기·투명 배경·피벗·모듈별 상태 경로·ZIP 일치 검사를 기록했다. 완성 렌더와 재합성의 안정된 면 내부 RGB 오차는 최대 1/255다. 이는 안티앨리어싱 경계까지 완전히 같은 결과라는 뜻은 아니다. 문서만 수정한 뒤 ZIP을 갱신할 때는 `package_textures.py --archive-only`를 사용할 수 있다.

# C-17 완전 상면 텍스처 — 19×19타일

제공된 C17.obj를 본판·아스널의 평면 항공기 스타일로 제작했다. 2026-10-25 후속 요청에 따라 **drawSize 19×19타일, 3243×3243px, 최종 12px 외곽선**으로 바꾸고 동체 점검구·패널·슬롯, 주익 조종면·체결부, 엔진 덮개·흡입구 립과 작은 위치등을 추가했다. 모든 디테일은 고정 면색이며 원본 형상을 바꾸지 않는다.

무전기의 256px/1.5타일과 선반·밀링 머신의 512px/3타일은 모두 **170.6667px/타일**이다. `round(19×170.6667)=3243`을 사용한다. 이미지 전체에 대한 상대 선폭이 아니라 이 절대 밀도에서 12px를 고정한다. 실제 밀도는 정수 해상도 반올림 때문에 170.6842px/타일이며, 오차는 전체 폭에서 1/3px다. 19×19는 투명 여백을 포함한 정사각형 그리기 영역이고, 기체 비율은 보존한다.

## 결과

- `output/HD_C17.png`: 기본 결과, **3243×3243 투명 RGBA**, 기수가 위쪽.
- `output/Textures/{Plain,Outlined}/HD_C17.png`: 같은 3243px의 무윤곽·윤곽선 버전. 낮은 해상도의 구형 게임 후보는 제거했다.
- `output/scale.json`: drawSize 19×19, 12px/약 0.0703타일 선폭, 중심·반올림 오차와 실제 무전기/공작기계 출처 해시.
- `output/absolute_scale.png`, `style_comparison.png`: 모든 물체를 타일당 화면 64px로 그린 절대 크기 비교. 같은 날개 폭으로 정규화하지 않는다.
- `output/detail_review.png`: 최종 3243px 텍스처의 디테일을 확대하지 않은 1:1 부분 검수.
- `output/before_after_19tiles.png`: 최초 초안과 현재본을 각각 같은 19타일 캔버스에 놓은 비교.
- `output/alpha_backgrounds.png`: 밝은·중간·어두운 배경의 윤곽선·투명 경계.
- `analysis/aircraft_references.png`: 양쪽 모드의 다섯 항공기 비교와 출처 기록.
- `output/aircraft.blend`: 원본 161개 그룹·486,906면과 UV를 보존한 편집 모델. 조명 없이 상면 카메라와 고정 면색만 사용한다.
- `output/*_4096.png`: 4096px 무조명 렌더, 내부선·표면 가림 마스크와 추가 채색. 기존 1024px 파일을 확대해 만들지 않았다.
- `output/delivery_validation.json`: 절대 밀도, 실측 외곽선, 원본 실루엣 알파 보존과 디테일 범위 검증.
- `review_history/01-initial-trial/`: 이전 시험본의 비교 자료. 현재 게임용 자산이 아니다.

원본 파일과 해시는 `vehicle.json`, 색·선·크기 설정은 `style.json`에 있다. `ART_BRIEF.md`에 근거·형상 보존·미실시 검증을 기록했다. 연결 시 `Graphic_Single`, `drawSize (19,19)`, 추가 스케일 1을 사용해야 이 절대 크기를 유지한다. 본판/아스널 항공기 정의와 설치 자산은 이번 텍스처 작업에서 변경하지 않았다. 실제 게임 표시 검증은 미실시다.

## 재생성

저장소 루트에서 Python(Pillow/numpy)과 Blender 5.1을 사용한다.

```powershell
python ArtSource/C17Globemaster/audit_references.py
python ArtSource/C17Globemaster/configure_trial.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/C17Globemaster/build_trial.py
python ArtSource/C17Globemaster/package_trial.py
python ArtSource/C17Globemaster/verify_delivery.py
```

공통 VehiclePipeline은 OBJ 수집·해시·좌표 변환과 이미지 처리를 재사용한다. 기존 차량의 TAN 팔레트나 포탑 분리 렌더를 항공기에 적용하지 않는다. 항공기 전용 빌더는 이 폴더에 한정된다. 역할/팔레트 변경은 설정에서 처리하고, 원본 OBJ를 덮어쓰지 않는다.

12px 원형 알파 확장을 4096px 마스터에 환산해 넣고 선형광·알파 선곱 Lanczos로 3243px에 축소한다. 내부선은 최종 4px다. 디테일은 실제 3D 가림 마스크로 제한하며 채색 이후에도 원래 실루엣의 부분 투명도를 보존한다. `texture_validation.json`과 `delivery_validation.json`에 자동 검사, `visual_review.json`에 실제 시각 검수를 구분해 기록한다. 512px 배경 검수판은 미리보기이며 게임용 텍스처가 아니다.

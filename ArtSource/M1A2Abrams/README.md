# M1A2 Abrams — 첫 EAST 시험 제작

공통 `VehiclePipeline`을 실제 두 번째 차량에 적용한 초안이다. 사용자 제공 OBJ의 형상과 주요 장비를 보존하고 TAN 고정 Emission 채색을 적용했다. 차체 64도 / 포탑·포신 90도 직교 투영이며 그림자·AO·조명 의존 색 변화가 없다.

## 결과

- `output/512/preview_east.png`: EAST 합성 시험 렌더.
- `output/256/`, `output/512/`: 차체, 포탑, 포신, 포탑+포신, 합성 PNG와 피벗 정보.
- `output/vehicle.blend`, `output/vehicle.glb`: 편집/교환 모델. 포탑·포신·승무원 해치 독립 계층.
- `output/native_sizes.png`: 256/512 실제 크기.
- `output/style_comparison.png`: 설치된 RimThunder M1A2와 외피 폭을 맞춘 비교.
- `output/alpha_backgrounds.png`: 밝은/중간/어두운 배경 검사.
- `output/report.json`, `mapping_report.json`: 면 출처·채색·피벗·미검수 항목.

포탑 전체 레이어에는 포신도 들어 있다. 이 레이어를 사용할 때 포신을 다시 올리지 않는다. 게임 정의/XML 연결과 게임 내 표시는 아직 검증하지 않았다.

## 입력과 처리

입력은 `Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext/m1a2/m1a2_abrams_0.obj`. 정확한 경로와 SHA256은 `vehicle.json`에 있다. 103개 원본 그룹·172,552개 면에서 172,312개 면을 보존했다. 별도 긴 안테나 whip 240면만 256px 가독성을 위해 명시적으로 생략했다. 나머지 메시를 크기나 폴리곤 수로 삭제/감량하지 않았다. 기본 측면 장갑·블로오프 패널을 ERA로 오인해 삭제하지 않았다.

차량별 설정 144개, 실제 출력 메시 137개다. 원본 재질별 내부·총열 구멍·기관총·그릴 등을 분리하면서 일부 기본 그룹은 빈 나머지가 되어 메시를 만들지 않는다. 면 중복과 누락은 빌드 검증에서 거절한다. 앞 방향 X, 위 방향 Y를 canonical `(-Z,-X,Y)`로 변환한다.

원본의 매우 완만한 전면 경사는 기본 법선 분류로 지붕과 합쳐져 보였다. 원본 차체 외피의 연결 성분에서 해당 면을 지정해 전면 144 / 측면 어깨 132 / 지붕 168로 구분했다. 포탑 지붕 180, 바로 위 해치/관측장비/블로오프 패널 204, 높은 차장 해치 216을 시작값으로 사용한다. 원통형 주포 외피는 측면·끝면 모두 156, 구멍은 40으로 분리한다. 월드 높이 순서나 카메라 깊이로 전체 색을 다시 바꾸지 않는다. 원본 그릴 재질 면은 방향별 평면으로 나누어 사각 격자를 적용했다.

RimThunder 설치 경로는 `vehicle.json`의 `style_references.rimthunder`에 기록했다. 해당 PNG의 픽셀을 모델 텍스처에 복사하지 않았다. 비교판은 원본 무채색과 새 TAN의 형상·명도 비교용이며 아트 스타일 승인 기록이 아니다. 청사진은 제공되지 않았고 공개 PDF의 로컬 다운로드도 실패했으므로 청사진 검수는 미실시다. 다른 차량의 도면으로 대신 채우지 않았다.

## 피벗

포탑 yaw의 XY는 원본 하단 원형 링(seed face 78837)의 원 맞춤으로 구했다. 중심 `(0.003350, -0.032036)`, 반경 약 0.997369, 최대 반경 오차 약 0.000067이다. 수직 회전축의 Z는 합성 앵커가 차체 장착면에 놓이도록 1.56을 선택했다. 원본의 공학적 bone origin을 복원했다는 뜻은 아니다. 35도 회전 시험과 원위치 복귀를 확인했다.

주포 elevation, 장전수/차장 해치, 조종수 해치에는 독립 피벗을 준비했지만 실제 기구 검증 전이므로 `confirmed: false`, 애니메이션 비활성이다. 특히 조종수 해치의 lift/swing을 단순 힌지처럼 완성 처리하지 않았다. 엔진 서비스 패널도 독립 메시로 남아 있다.

## 재생성

저장소 루트에서 실행한다. 외부 Python의 Pillow/numpy와 Blender 5.1이 필요하다.

```powershell
python ArtSource/M1A2Abrams/configure_vehicle.py
python ArtSource/VehiclePipeline/pipeline.py build ArtSource/M1A2Abrams/vehicle.json
python ArtSource/M1A2Abrams/package_trial.py
```

`configure_vehicle.py`는 이 원본 revision에 대한 매핑을 다시 생성한다. 작업 중 수동 JSON 예외를 추가한다면 재생성 스크립트에도 반영한다. 재채색/재생성 시 기존 시각 검수 플래그를 다시 미검수로 돌린다.

## 검증과 남은 작업

원본 좌표·면 출처, 조명 변경 픽셀 차이 0/255, 실제 상향 면의 직접 겹침 명도 간격 24/36, 퇴화 면 없음, GLB unlit 및 격자 texture/UV 유지, 포탑 animation 1개, 투명 여백·선형광 축소를 확인했다. 공통 검사 7개와 청사진 포함 기존 fixture도 통과했다.

실제 크기와 세 배경을 시각 확인했다. 현재는 일부 얇은 바구니 레일·볼트·기관총 잔선이 256px에서 약하며, 원본 디테일을 넓은 판과 굵은 대표 부품으로 재설계하는 단계는 진행하지 않았다. 포탑 외곽선 후처리도 아직 없다. 이 차이와 미확인 피벗·청사진·게임 검증은 draft/blocker로 남겼다. 첫 시험 렌더를 보고 다음 수정 범위를 정할 수 있다.

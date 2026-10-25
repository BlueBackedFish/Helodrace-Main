# M8 AGS LEVEL 3 — 모델·텍스처

2026-10-25 추가: 현재 게임용 1024 출력·독립 M35 포신·M2HB 분리 모델은 `output/WeaponLayers/`와 [차량 런타임 작업 기록](../VehicleRuntime/README.md)을 사용한다. 아래 256/512 패키지는 이전 아트 검수본으로 보존한다.

제공된 `ext/xm8/xm_8_0.obj`로 LEVEL 3 아트 한 종류를 제작했다. 사용자 확정 범위는 모델·텍스처이며, 게임 내 차량·무장 연결은 포함하지 않는다. 기존 Bradley·M1A2 모델과 공통 채색 도구는 변경하지 않았다.

## 보기

- `output/cardinal_review.png`: 4방향 합성 검수판.
- `output/OutlinedReview/native_sizes.png`: 256/512px 실제 크기, 차체·포탑별 검은 윤곽선.
- `output/style_comparison.png`: RimThunder M1A2와 같은 차체/포탑 외피 폭으로 맞춘 스타일 참고. 다른 차종이므로 M8 형상 일치 판정에 사용하지 않는다.
- `output/alpha_backgrounds.png`: 밝은·중간·어두운 배경 검사.
- `output/iso_front.png`, `output/iso_rear.png`: 원본 형상과 장비 배치 검수.
- `output/vehicle.blend`, `output/vehicle.glb`: 편집/교환 모델. 포탑·포신·해치의 독립 계층 포함.

## 텍스처와 정렬

`output/Textures/{Plain,Outlined}/{256,512}/`에 각 방향의 `hull`, `turret`, `barrel`, `turret_full`, `preview`가 있다. 총 80개 RGBA PNG다. 무윤곽 원본과 윤곽선 검수본을 모두 보존한다.

- `turret_full`에는 포신이 포함되어 있다. `barrel`을 한 번 더 합성하지 않는다.
- `hull`은 64도, 포탑·포신은 90도 상면 직교 투영이다.
- 모든 부품은 같은 픽셀/원본 단위 비율이다. 각 레이어를 별도 크기로 확대하거나 trim하여 임의 정렬하지 않는다.
- 각 크기의 `pivots.json`에서 `hull_turret_anchor`와 `turret_pivot` 차이가 합성 오프셋이다. `raw_hull_turret_anchor`는 반올림 전 좌표다.
- 윤곽선은 각 레이어에 먼저 적용한 뒤 원래 피벗으로 합성한다. 256px 1px, 512px 2px 검은 선이며 모델 재질이나 원본 렌더를 바꾸지 않는다.
- 축소는 선형광·알파 선곱 Lanczos다. 완전 투명 픽셀의 RGB는 0이다.
- `output/Textures/manifest.json`에 스케일·방향·출력/원본 레이어 해시가 있다.

긴 주포와 독립 포탑 회전 여백 때문에 EAST의 차체는 256px 캔버스에서 약 102px, 포신 포함 약 152px다. 실제 게임 drawSize는 아직 정하지 않았다.

## 원본과 LEVEL 3

181개 그룹/203,803면에서 198,849면을 보존했다. LEVEL 3 장갑과 원본 장착대·스커트·연막탄·기관총·광학창을 유지했다. 낮은 대체 헤드라이트 2개 그룹은 추가 전면 장갑과 겹쳐 제외하고, 원본의 올라간 헤드라이트를 사용한다. 별도 긴 안테나 whip 2개 그룹만 가독성 때문에 제외했으며 베이스는 남겼다. 총 4,954면의 생략 사유는 `mapping_report.json`에 있다.

현재 입력은 XM8 이름의 모델이다. 양산 M8의 치수로 임의 변형하지 않았고, 청사진은 제공되지 않았다. LEVEL 1/2 출력, BUSK/ERA, 폴리곤 감량이나 박스 형상으로의 대체는 추가하지 않았다. 원본 재질의 유리·기관총·총구·그릴 면은 별도 역할로 추출하고, 원본 DDS의 마모나 조명을 굽지 않았다.

차체·포탑 TAN은 고정 Emission이다. 지붕/전면/측면 역할과 실제 겹침의 명도 관계를 사용하며 월드 높이 순서나 조명으로 전체 색을 바꾸지 않는다. 주포와 바퀴 원통 측면은 단색으로 처리한다. 그릴은 원형 팬·흡기 슬롯·루버·포탑 망을 구분하고 각 연결 패널의 모서리에 맞춘 전용 UV/이미지를 사용한다. 세부 설정과 근거는 `ART_BRIEF.md`에 있다.

## 재생성

저장소 루트에서 Python 3.11 이상과 Pillow/numpy, Blender 5.1을 사용한다.

```powershell
python ArtSource/M8AGS/configure_vehicle.py
python ArtSource/VehiclePipeline/pipeline.py build ArtSource/M8AGS/vehicle.json
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M8AGS/render_directions.py
python ArtSource/M8AGS/package_trial.py
```

원본은 `vehicle.json`의 절대 경로와 SHA256으로 검증한다. 원본이 달라지면 면 ID를 재사용하지 말고 새로 매핑한다. `configure_vehicle.py`는 수동 검수 플래그를 초기화한다. 새 결과를 **실제로 열어 검수한 뒤에만** `python ArtSource/M8AGS/record_visual_review.py`로 해당 검수를 기록한다. 이 명령은 모델·색·텍스처를 변경하지 않고 검수 메타데이터만 갱신한다.

## 검증과 남은 항목

면 누락/중복·좌표/UV 보존, 강도 100 SUN에 의한 픽셀 차이 0/255, GLB unlit, 24점 포탑 레이스 피벗과 35도 yaw 복귀, 실제 상향 면의 직접 겹침 명도 간격 24, 4방향 정렬·알파 여백·출력 80개 해시 검사를 통과했다. 시각 검수 결과와 한계는 `VISUAL_REVIEW.md`, 자동 근거는 `output/report.json`에 있다.

원본의 면적 0 삼각형 5면은 좌표를 확인하고 그대로 보존했다. 256px에서 일부 가는 레일·볼트·기관총 잔선은 약하게 보인다. 청사진 비율, 정확한 elevation/해치 연동과 모든 self-intersection, 게임 내 표시는 미검증이다. `report.status=draft`는 이 남은 범위를 반영하며, 사용자 아트 승인을 뜻하지 않는다.

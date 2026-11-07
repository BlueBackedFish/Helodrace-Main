# M1A2 SEPv3 Abrams — 새 규칙 시험 제작

`VEHICLE_ART_STYLE.md` v2.1(RimThunder식 장착 부품 밝기, 명도 사다리, 평면 단색)과 공통 `VehiclePipeline`(contacts `direction`, 디더링 제거)으로 만든 기본형 시험본이다. 위장막, TUSK 반응장갑과 그 장착 레일, 가는 안테나를 뺐다. 근거와 수치는 `ART_BRIEF.md`에 있다.

- `output/512/preview_east.png`, `output/OutlinedReview/native_sizes.png`: EAST 합성과 실제 크기.
- `output/vehicle.blend`, `output/vehicle.glb`: 포탑·포신·해치 독립 계층.
- `mapping_report.json`: 생략 면 수, 포탑 링 맞춤, 접촉 방향별 값.

이전 `ArtSource/M1A2Abrams/`는 지금 없는 이전 원본으로 만든 기록이라 그대로 둔다.

## 재생성

저장소 루트에서 실행한다.

```powershell
python ArtSource/M1A2SEPv3/configure_vehicle.py
python ArtSource/VehiclePipeline/pipeline.py build ArtSource/M1A2SEPv3/vehicle.json
python ArtSource/VehiclePipeline/outline_review.py ArtSource/M1A2SEPv3/vehicle.json
```

게임 시험 차량(아스널, 일반 Vehicle Framework 정의)으로 넣으려면 이어서 실행한다.

```powershell
python ArtSource/M1A2SEPv3/package_game.py --mod-root ../Helodrace-LobosArsenal
powershell -File ../Helodrace-LobosArsenal/Tools/Run-M1A2Check.ps1 -Name m1a2-01 -MainRoot (Resolve-Path .)
```

`package_game.py`는 1024px 레이어에 검은 윤곽선(8px)을 굽고, M8과 같은 1.3437 m/타일 배율로 drawSize·방향별 위치·포탑과 자식 포탑 위치를 계산해 `Textures/Vehicles/M1A2SEPv3/`와 `Defs/Vehicles/M1A2SEPv3.xml`을 만든다. 계산값은 `output/game_layout.json`에 남는다.

`configure_vehicle.py`는 `prepare`가 만든 `vehicle.json`의 원본 해시를 확인하고 부품·생략·접촉·리그를 다시 쓴다. 원본이 바뀌면 `prepare`부터 다시 한다.

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

`configure_vehicle.py`는 `prepare`가 만든 `vehicle.json`의 원본 해시를 확인하고 부품·생략·접촉·리그를 다시 쓴다. 원본이 바뀌면 `prepare`부터 다시 한다.

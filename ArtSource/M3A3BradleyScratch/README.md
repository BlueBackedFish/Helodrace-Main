# M3A3 Bradley — 새로 모델링

원본 OBJ의 면을 쓰지 않고 `build_model.py`로 형상을 새로 만든 M3A3형 보병전투차다. 원본 M3A3 OBJ와 `ArtSource/M3A3Bradley`는 비율·장비 배치 레퍼런스로만 썼다. 채색·렌더는 공통 `VehiclePipeline`과 `VEHICLE_ART_STYLE.md` 규칙을 따른다. 근거와 수치는 `ART_BRIEF.md`.

- `output/OutlinedReview/1024/preview_east.png`: 정상 상태 합성(검수용 윤곽선).
- `output/hull_<방향>.png`, `turret_full_east.png`, `barrel_east.png`: 게임용 레이어(파괴 부품 제외).
- `output/<layer>__<key>.png`, `<layer>__<key>_destroyed.png`: 관측장비·전조등 정상·파괴 오버레이.
- `output/vehicle.blend`, `vehicle.glb`: 분리 피벗이 있는 편집/교환 모델.

## 재생성

저장소 루트에서 실행한다.

```powershell
python ArtSource/M3A3BradleyScratch/build_model.py
python ArtSource/M3A3BradleyScratch/configure_vehicle.py
python ArtSource/VehiclePipeline/pipeline.py build ArtSource/M3A3BradleyScratch/vehicle.json
python ArtSource/VehiclePipeline/outline_review.py ArtSource/M3A3BradleyScratch/vehicle.json
```

형상을 바꾸면 OBJ 해시가 바뀌므로 `configure_vehicle.py`를 다시 실행해야 한다.

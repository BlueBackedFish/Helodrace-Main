# M3A3 Bradley — 공통 도구 재작업

M1A2 SEPv3와 같은 공통 `VehiclePipeline`과 `VEHICLE_ART_STYLE.md` 규칙으로 다시 만든 기본형 브래들리다. BUSK III는 나중 작업이다. 관측장비·전조등·적재 해치(탄약고 뚜껑)는 파괴 상태 레이어로 분리했다. 근거와 수치는 `ART_BRIEF.md`.

- `output/OutlinedReview/1024/preview_east.png`: 정상 상태 합성.
- `output/hull_<방향>.png`, `turret_full_east.png`: 파괴 부품을 뺀 기본 레이어.
- `output/<layer>__<key>.png`, `<layer>__<key>_destroyed.png`: 정상·파괴 오버레이.
- `output/report.json`의 `render.damage_layers`: 레이어별 오버레이 목록.

이전 `ArtSource/M2A3Bradley/`(예전 빌더·BUSK 시안)는 그대로 둔다.

## 재생성

저장소 루트에서 실행한다.

```powershell
python ArtSource/M3A3Bradley/configure_vehicle.py
python ArtSource/VehiclePipeline/pipeline.py build ArtSource/M3A3Bradley/vehicle.json
python ArtSource/VehiclePipeline/outline_review.py ArtSource/M3A3Bradley/vehicle.json
```

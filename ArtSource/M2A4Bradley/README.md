# M2A4 Bradley — 1차 TAN 채색

WARNO 모드 M2A4 GLB를 OBJ로 바꿔 공통 `VehiclePipeline`과 `VEHICLE_ART_STYLE.md` 규칙(M3A3와 같은 사다리)으로 TAN 채색한 기본형 초안이다. 원본 위장 텍스처는 쓰지 않는다. 근거와 수치는 `ART_BRIEF.md`.

- `output/OutlinedReview/1024/preview_east.png`: 합성 미리보기.
- `output/hull_<방향>.png`, `turret_full_east.png`, `barrel_east.png`: 분리 레이어.
- `output/vehicle.blend`, `vehicle.glb`: 포탑·포신·TOW 피벗을 가진 모델.

## 재생성

저장소 루트에서 실행한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b --python ArtSource/M2A4Bradley/convert_glb.py -- `
  ../.texture/ref/m2a4-bradley-ifv-us-army-warno-mod/source/US_M2A4.glb `
  ../.texture/ref/m2a4-bradley-ifv-us-army-warno-mod/converted/US_M2A4.obj
python ArtSource/M2A4Bradley/configure_vehicle.py
python ArtSource/VehiclePipeline/pipeline.py build ArtSource/M2A4Bradley/vehicle.json
python ArtSource/VehiclePipeline/outline_review.py ArtSource/M2A4Bradley/vehicle.json
```

변환 OBJ 해시가 `vehicle.json`과 다르면 `configure_vehicle.py`가 중단한다.

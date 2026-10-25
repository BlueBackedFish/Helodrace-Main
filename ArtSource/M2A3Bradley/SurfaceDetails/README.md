# M3A3 그릴·램프 로프·전조등 분리

2026-10-25 사용자 요청. 기존 Standard/TOW 및 BUSK 원본을 변경하지 않고 이 폴더의 파생 모델에서 표면과 부품 구성을 수정한다. 공통 Bradley 빌더나 기존 ERA 작업을 덮어쓰지 않는다.

- 엔진 데크 그릴과 경사진 배기 그릴은 실제 면 경계에 UV 0..1을 맞춘다. 데크는 6행·2열의 슬롯, 경사면은 9행 루버다. 세계 좌표 기준 사각 반복 타일 대신 각각의 테두리와 모서리를 사용한다. `VehiclePipeline/shape_grilles.py`의 일반 기능과 `settings.json`의 차량별 면 선택을 분리한다.
- 후방 램프의 로프처럼 보이는 연결 영역은 `WT_rear_ramp` seed 9의 625면이다. 이 영역 전체를 명도 132의 단일 RGB → Emission으로 바꿨다. 시점/법선별 밝기나 vertex 색을 곱하지 않는다. 로프와 장착 형상은 보존했다.
- 원본 좌우 전조등은 `leftHeadlight/rightHeadlight` 독립 계층·컬렉션으로 옮기고 월드 좌표를 보존했다. BUSK의 올림 받침은 고정 키트에 남는다. 전조등은 124개 장갑 케이스에 포함하지 않는다.
- `TextureExport`의 바탕 차체에서 전조등을 제외하고 `Components/`에 따로 렌더한다. 전조등과 ERA는 서로 영구적인 holdout 구멍을 만들지 않는다. 실제 케이스별 가림 렌더에서 현재 전조등이 장갑 바깥에 있음을 확인하고 ERA 다음에 합성한다.

`output/{Standard,BUSK}/vehicle.blend`와 `vehicle.glb`가 편집/교환 모델이다. `validation.json`은 원본 좌표·면·기존 UV·수정 대상 외 색의 보존과 625면 단색, 두 그릴의 프레임을 기록한다. 기존 빈 바퀴 메시와 원본 개구부를 새로 보수하거나 삭제하지 않는다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/SurfaceDetails/build_details.py -- Standard
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/SurfaceDetails/build_details.py -- BUSK
```

원본 경로·해시는 이 폴더의 `settings.json`에 고정했다. 모델 재생성 후 `TextureExport/settings.json`의 두 파생 모델 SHA256을 갱신하고, 해당 폴더의 렌더·패키지·검증 절차를 실행한다. `review.py`는 이전 커밋의 동일 프레임과 새 결과를 비교해 `before_after.png`를 생성한다.

게임 연결은 아스널의 `Tools/Import-BradleyArt.py`와 `Source.Vehicles/BradleyRenderer.cs`를 사용한다. 좌우 전조등의 독립 VF 체력·파괴·수리 검증은 확장팩의 `Docs/M3A3/HEADLIGHTS-20261025.md`에 기록한다.

검증 완료: 두 모델의 형상·원본 UV·대상 외 채색 보존, 무조명 GLB와 단색 로프, 네 방향 텍스처·개폐 조립·별도 전조등·ZIP 검증을 통과했다. 해상도별 Standard 140장/BUSK 646장이며 안정 영역의 합성 RGB 오차는 최대 1/255다. 아스널에서는 39개 부품·48개 상태·124개 ERA·562개 crop과 실제 게임 14개 통과 마커를 확인했다. 전조등의 독립 파괴·수리와 기존 무장/개폐/이동/피해 처리를 포함한다.

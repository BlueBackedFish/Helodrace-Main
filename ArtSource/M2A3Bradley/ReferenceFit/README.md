# OBJ 기준 전체 재구성본 — M3A3

이 폴더는 사용자 제공 War Thunder M3A3 추출 OBJ를 보존한 고밀도 참조본입니다. 현재 게임용 작업본은 ../RimWorld/README.md를 보세요.
포탑/차체만 부분 수정한 것이 아니라 원본 111개 그룹의 223,389개 면을 모두 사용합니다.
기존 수작업 근사형은 상위 폴더에 남아 있으며 이 재구성본과 형상이 다릅니다.

## 파일

- `m3a3_bradley_refit.blend`: 전체 편집용. 텍스처를 내부에 패킹.
- `m3a3_bradley_refit.glb`: 분리 부품/애니메이션을 유지한 교환용. 고정 TAN Emission 대체 재질.
- `VF_East/preview_east.png`: 차체 64° + 포탑 90° 혼합 투영 미리보기.
- `VF_East/vehicle_east.png`, `turret_top_east.png`, `barrel_top_east.png`: 분리 렌더.
- `bradley_iso_front.png`, `bradley_iso_rear.png`: 전체 형상 확인용.
- `bradley_personnel_open.png`, `bradley_ramp_open.png`: 문/램프 열림 확인용.
- `source_groups.json`: 원본 그룹별 범위/면 수/부모 매핑.
- `manifest.json`: 원본 위치와 SHA256, 좌표 변환, 재질 및 검증 정보.

## 보존·수정 범위

- 차체 굴곡, 엔진실/해치, 측면 장갑, 궤도, 바퀴/현가장치, 전후방 부속품,
  포탑 경사/후면 수납부, 관측장비, TOW, 주포/동축기관총을 원본 형상대로 사용.
- OBJ의 `@root`에 묶여 있던 후방 램프와 부속품을 연결 요소 기준으로 추출.
  원본 소형 도어 `hatch_04`는 램프 아래 별도 힌지에 연결. 총 112개 메시.
- 피벗은 형상으로 추정. `TURRET_YAW`, `GUN_ELEVATION`, `REAR_RAMP_HINGE`,
  `PERSONNEL_DOOR_HINGE` 독립 제어, 24fps/120프레임 시연 동작.
- 원본 UV는 보존. TAN 기본색에 원본 컬러 텍스처의 명도만 0.90~1.00으로 약하게 적용.
  그릴은 원본 망 텍스처 알파를 색상 패턴으로 사용. 광원/투사 그림자/AO 없음.
- 원본 split normal은 Blender 5.1에서 일부 부품 처리 중 충돌하여 삼각형 법선으로 재계산.
  원본 꼭짓점 위치와 면은 변경하지 않음.
- 이 파일은 M3A3 원본을 활용한 파생 에셋이며, 독립 제작 M2A3 정밀 복제라고 표시하지 않음.

## 재생성

원본 OBJ 경로는 `../rebuild_from_obj.py`의 `SOURCE`에 설정되어 있습니다.
원본은 Downloads 위치에서 읽기만 합니다. DDS는 `Textures/`의 PNG로 무손실 형식 변환하여
Blender 로딩에 사용하고, 색상 보정은 재질 노드에서 수행합니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python ArtSource/M2A3Bradley/rebuild_from_obj.py
```

`validate_refit.py`는 저장된 파일을 다시 열어 모든 부품의 범위, 전체 면 수,
포탑/문/램프 시연 동작, GLB의 112개 메시와 4개 애니메이션을 검증합니다.


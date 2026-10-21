# M2A3 Bradley — TAN

제공된 5면 도면을 참고한 림월드 차량 스프라이트용 3D 아트 모델입니다.
도면의 차체, 6개 로드휠, 각진 포탑, 좌측 쌍열 TOW 발사기, 조준경,
후방 램프 실루엣을 재현했습니다. 제조용 정밀 CAD 모델은 아닙니다.

## 파일

- `m2a3_bradley.blend`: 메시, 재질, 카메라, 피벗과 데모 애니메이션을 포함한 편집 원본.
- `m2a3_bradley.glb`: 계층과 애니메이션을 포함한 교환용 모델.
- `bradley_iso_front.png`, `bradley_iso_rear.png`: 1024×1024 등각 투영 미리보기.
- `bradley_north/east/south/west.png`: 게임 스프라이트 제작용 4방향 원본.
- `bradley_ramp_open.png`, `bradley_personnel_open.png`: 문 동작 확인 이미지.
- `build_bradley.py`: 모델과 렌더 재생성 스크립트.
- `manifest.json`: 저장된 모델 재열기 및 PNG 검증 결과.

## 편집과 애니메이션

전방은 **-Y**, 위쪽은 **+Z**입니다. 전체 이동에는 `BRADLEY_ROOT`를 사용합니다.

| 피벗 | 상위 | 회전축 | 용도 |
| --- | --- | --- | --- |
| `HULL` | `BRADLEY_ROOT` | 전체 | 차체 및 궤도 |
| `TURRET_YAW` | `HULL` | 로컬 Z | 포탑 회전 |
| `GUN_ELEVATION` | `TURRET_YAW` | 로컬 X | 포신 고저, 양수로 올림 |
| `REAR_RAMP_HINGE` | `HULL` | 로컬 X | 후방 램프, 약 -100°로 내림 |
| `PERSONNEL_DOOR_HINGE` | `REAR_RAMP_HINGE` | 로컬 Z | 램프 안쪽 출입문, 양수로 바깥쪽으로 열림 |

각 부품의 세부 메시는 해당 피벗 아래에 묶여 있습니다. 궤도와 바퀴는
독립 메시지만 주행 애니메이션은 구성하지 않았습니다. 상부 해치는 정적 디테일입니다.
후방 개구부에는 단순한 내부 바닥과 어두운 칸막이가 있으며 전체 실내는 없습니다.

타임라인은 24fps, 1–120프레임입니다. **1프레임이 닫힌 기본 자세**이며,
25프레임은 작은 문, 40프레임은 포탑과 포신, 80프레임은 램프 동작 예시입니다.
직접 포즈를 만들 때는 각 피벗의 데모 액션을 해제하거나 새 키프레임을 넣으세요.

## 렌더

TAN 팔레트를 면 방향별로 배정한 Emission 재질입니다. 조명, 바닥,
그림자와 AO를 사용하지 않으며 배경은 투명합니다. 면 사이의 명도 차이는
입체감을 위한 고정 색상으로, 투사 그림자가 아닙니다.

등각 카메라는 하향 35.264°, 4방향 카메라는 기존 저장소 아트에 맞춰 하향 64°입니다.
모두 직교 투영, 동일한 중심과 스케일(8.7)을 사용합니다.
4방향 이름은 카메라 위치 기준입니다(N=+Y, E=+X, S=-Y, W=-X).
차량 프레임워크의 실제 방향 명명과 drawSize에 맞춰 연결해야 합니다.
이번 작업은 아트 에셋 제작이며 게임 Def 또는 차량 모드 코드 연결은 포함하지 않습니다.

GLB는 교환용입니다. 다른 뷰어의 재질 처리에 따라 표시가 달라질 수 있으므로
그림자 없는 최종 스프라이트 출력은 Blender 원본을 기준으로 하세요.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\ArtSource\M2A3Bradley\build_bradley.py'
```

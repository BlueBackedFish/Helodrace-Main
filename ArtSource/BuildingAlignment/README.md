# 건물 표시 중심 보정

책상 계열은 전체 불투명 영역이나 다리 바닥 대신 **책상판 윗면 중심**을 건물 점유 영역의 중앙에 놓는다. 화면·장비·케이블의 높이는 중심을 결정하지 않는다. 모델과 텍스처를 움직이거나 다시 그리지 않고 XML `drawOffset`으로 보정한다.

`measure_desks.py`가 기존 Blender 장면의 책상판 정점과 각 방향 카메라로 윗면 중심을 투영한다. `measurements.json`에 원본 모델·책상판·4방향 투영값을 기록한다. `verify_alignment.py`는 실제 Def의 drawSize/offset을 적용한 뒤 중심 오차를 검사하고 점유 영역 격자 위의 전후 비교를 만든다.

| 대상 | drawOffset Z (칸) |
| --- | ---: |
| 컴퓨터 / 컨트롤러 장착형 | -0.346313238 |
| 무전기 책상 | -0.074457139 |
| 총기 작업대 / 의류 작업대 | -0.416452646 |
| 전신 책상 | -0.01953125 |

기본 작업대의 기존 윗면은 이미 약 1픽셀 이내로 중앙에 있어 유지했다. 기본 작업대와 전신 책상은 원본 3D 장면이 없는 기존 그림이며, 이미지의 책상판 면을 기준으로 확인했다. 기존 서쪽의 동쪽 반전 사용도 유지한다.

무전기는 카메라 여백 보정만 적용했던 이전 `+0.154591769`에서 책상판 기준으로 바꿨다. 산업용 아트 재출력에서도 `tabletop_anchor_mesh` 설정을 통해 같은 보정을 계산한다. 컴퓨터는 연결된 그래픽에도 원본 `GraphicData`를 전달하여 연결/해제 시 중심이 달라지지 않는다.

CROWS 상부의 `MeshPool.plane10`은 실제 1칸 메시다. 3.2칸 캔버스를 0.32칸으로 축소하던 코드를 수정하고, 정의의 `turretTopDrawSize`를 그대로 사용한다. 밑판 크기·피벗·포신 후퇴량은 유지한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background --python-exit-code 1 --python ArtSource/BuildingAlignment/measure_desks.py
python ArtSource/BuildingAlignment/verify_alignment.py
```

`before_after.png`의 노란 선은 점유 영역, 녹색 십자는 중앙이다. 3D 책상들의 4방향 중심 오차는 0.00001칸 미만이다. 모델/PNG 원본 보존, CROWS 렌더 크기·리코일과 컴퓨터 연결 그래픽의 오프셋 전달, 실제 게임 어셈블리 빌드를 검증했다. 게임 실행 검증은 하지 않았다.

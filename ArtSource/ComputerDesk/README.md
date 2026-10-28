# 컴퓨터 책상 아트

테이블 위의 모니터·데스크탑 본체·키보드·유선 마우스, 후면 멀티탭과 주변을 둘러 흐르는 굵은 케이블 4가닥을 새 Blender 모델로 제작했다. 모니터·본체 전원선, 데이터선, 책상 아래로 내려가는 전원선과 키보드·마우스 USB 케이블이 각각 장치에 연결된다. 모니터 화면은 격자·지도·상태 표시를 제거한 단색 빈 화면이며 조명이나 발광 효과를 만들지 않는다.

가구 공통 규격에 맞춰 2×1칸, 512×512 RGBA, drawSize `(3,3)`, 64° 정사영 4방향, 외곽선 12px를 사용한다. North는 사용자가 앉는 -Y 방향에서 장비 전면을 본다. 반대 방향도 실제 카메라 렌더이며 좌우 반전하지 않는다. 모델 1단위=게임 1단위, drawOffset은 `(0,0,-0.346313238)`이며 책상판 윗면 중심을 점유 영역 중앙에 맞춘다. 목재색은 기존 무전기 책상의 `(133,97,67)`과 측면 `(113,83,57)`을 따른다. 다른 장비는 고정 RGB Emission, 투명 배경, 그림자·AO 없음이며 색상 마스크는 없다.

- 게임용 PNG: `Textures/Buildings/ComputerDesk/HD_ComputerDesk_{north,east,south,west}.png`
- 편집 모델: `output/computer_desk.blend`
- 미리보기: `output/preview.png`
- 검증: `output/validation.json`, `output/metadata.json`

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/ComputerDesk/build.py
python ArtSource/ComputerDesk/package.py
```

본체의 컴퓨터 건물에 사용하며 CROWS 연결 시 컨트롤러가 장착된 4방향 그래픽으로 전환한다. 사용법과 기능 검증 범위는 `Docs/CROWSII.md`를 따른다. 실제 게임 실행 검증은 하지 않았다. 공유 모델·출력 도구는 `ArtSource/PropAssets`에 있다.

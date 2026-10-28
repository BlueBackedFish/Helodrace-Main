# 컴퓨터용 CROWS 컨트롤러 추가 파츠

기존 컴퓨터 책상의 키보드 왼쪽에 FDE 제어대, 조이스틱형 손잡이, 러버 부츠, 기능 버튼, 작은 견고형 화면과 연결 케이블을 제작했다. 화면은 고정 조준표/상태 표시이며 표시 소프트웨어나 무기 조작 기능은 없다. [미 육군의 CROWS 화면·조이스틱·버튼 구성 설명](https://www.army.mil/article/181552/weapon_system_moves_reserve_gunners_to_the_safety_of_the_backseat)을 형식 참고로 사용한 게임용 새 형상이다.

`../ComputerDesk/output/computer_desk.blend`의 책상·모니터·본체·키보드·마우스·기존 케이블과 카메라를 그대로 복사했다. 원본 파일·PNG는 수정하지 않는다. 화면은 키보드 뒤 왼쪽으로 조금 치우쳐 배치해 옆면 카메라의 여백을 확보하고 기존 장비와 실제로 겹치지 않도록 했다. 네 개의 러버 받침이 책상 상면 0.79에 닿는다. 추가 케이블은 후면의 새 인터페이스 커넥터에 연결한다.

주 도색은 FDE `(143,123,92)`, 어두운 하우징 `(110,99,81)`, 손잡이/케이블은 별도 검정·회색이다. 고정 RGB Emission, 조명·그림자·AO 없음, 투명 배경, 64° 정사영 실제 4방향이다. 가구 규격인 512×512 RGBA, drawSize `(3,3)`, footprint `(2,1)`, 12px 외곽선을 유지한다.

게임용 파일은 `Textures/Buildings/ComputerDesk/CROWSController/`에 있다.

| 파일 | 용도 |
| --- | --- |
| `HD_ComputerDesk_CROWS_{north,east,south,west}.png` | 컨트롤러가 장착된 완성형 책상 텍스처. 그래픽 교체용 |
| `HD_CROWSController_{north,east,south,west}.png` | 기존 책상과 같은 캔버스/원점의 추가 파츠 레이어 |
| `HD_CROWSController_UI.png` | 컨트롤러 본체 확대 아이콘 |

추가 파츠의 케이블/장비가 기존 책상에 가려지는 부분은 실제 3D 가림을 반영했다. 외곽선은 책상과 합친 실루엣을 기준으로 배분해 가는 케이블 내부에 12px 검은 덩어리가 생기지 않게 한다. 완성형 텍스처도 제공하므로 건물 그래픽을 통째로 바꾸는 방식으로 사용할 수 있다. 두 종류 모두 같은 drawSize·원점이며 별도 배치 오프셋은 없다. 편집 모델에서는 `CROWS_CONTROLLER_MODULE`에 추가 파츠를 모았고 부품의 기준 위치는 `(-0.79,-0.30,0.79)`다.

- 편집 모델: `output/controller_desk.blend`
- 4방향 미리보기: `output/preview.png`, `output/addon_preview.png`
- 컨트롤러 확대: `output/detail_preview.png`
- 고정진지/제어기/책상 묶음 미리보기: `output/summary_preview.png`
- 기록: `output/metadata.json`, `output/validation.json`, `output/model_validation.json`

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSController/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSController/validate.py
python ArtSource/CROWSController/package.py
```

기존 책상 메시 131개의 정점·면·UV·면색·위치와 카메라 4개를 저장된 모델에서 대조했다. 화면/기존 장비의 간섭, 바닥 받침 접촉, PNG 크기·투명도·여백과 실제 출력도 검수했다. 신규 건물·업그레이드 정의나 CROWS 제어 연결은 추가하지 않은 아트 자료이며 게임 실행 검증은 하지 않았다.

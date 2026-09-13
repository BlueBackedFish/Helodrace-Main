# Reference reconstructions

GunBench를 제외한 5종의 참고 에셋을 각각 독립된 Blender 모델로 복원했습니다.
색상 이미지 11장과 대응하는 `m.png` 마스크 11장을 모두 분석했습니다.
각 모델은 실제 입체 메시이며, 원본 이미지를 붙인 평면이나 카메라별 교체 모델을 사용하지 않습니다.

| 에셋 | 게임 점유 크기 | Blender | North | South | East | 원본 비교 |
|---|---|---|---|---|---|---|
| TreadleLathe | 2×1 | [씬](TreadleLathe/HD_TreadleLathe.blend) | [PNG](TreadleLathe/HD_TreadleLathe_north.png) | [PNG](TreadleLathe/HD_TreadleLathe_south.png) | [PNG](TreadleLathe/HD_TreadleLathe_east.png) | [비교](TreadleLathe/comparison.png) |
| LineShaftTurretLathe | 2×1 | [씬](LineShaftTurretLathe/HD_LineShaftTurretLathe.blend) | [PNG](LineShaftTurretLathe/HD_LineShaftTurretLathe_north.png) | [PNG](LineShaftTurretLathe/HD_LineShaftTurretLathe_south.png) | [PNG](LineShaftTurretLathe/HD_LineShaftTurretLathe_east.png) | [비교](LineShaftTurretLathe/comparison.png) |
| LineShaftMillingMachine | 2×1 | [씬](LineShaftMillingMachine/HD_LineShaftMillingMachine.blend) | [PNG](LineShaftMillingMachine/HD_LineShaftMillingMachine_north.png) | [PNG](LineShaftMillingMachine/HD_LineShaftMillingMachine_south.png) | [PNG](LineShaftMillingMachine/HD_LineShaftMillingMachine_east.png) | [비교](LineShaftMillingMachine/comparison.png) |
| LineShaftPress | 3×3 | [씬](LineShaftPress/HD_LineShaftPress.blend) | [PNG](LineShaftPress/HD_LineShaftPress_north.png) | [PNG](LineShaftPress/HD_LineShaftPress_south.png) | [PNG](LineShaftPress/HD_LineShaftPress_east.png) | [비교](LineShaftPress/comparison.png) |
| RollingMC | 3×3 | [씬](RollingMC/HD_RollingMC.blend) | [PNG](RollingMC/HD_RollingMC_north.png) | [PNG](RollingMC/HD_RollingMC_south.png) | [PNG](RollingMC/HD_RollingMC_east.png) | [비교](RollingMC/comparison.png) |

## 보기 및 렌더 설정

- 모든 PNG: 512×512 RGBA, 투명 배경, 하향 64° 직교 투영.
- 동일 에셋의 세 카메라는 중심점·직교 배율·출력 설정을 공유합니다.
- 카메라 위치는 North = -Y, South = +Y, East = +X입니다. 제공된 원본에서 좌측 주축대가 East의 위쪽으로 보이는 방향에 맞췄습니다. 카메라 이름은 스프라이트 파일 방향을 나타냅니다.
- 에셋별 공통 배율은 세 방향 전체 바운딩을 기준으로 정했습니다. 좁은 측면만 따로 확대하지 않습니다.
- 원본에서 추출한 흰색, 회색, 황동색, 갈색을 sRGB→linear 변환한 emission 재질로 적용했습니다. 실제 조명, 텍스처, 외곽선 렌더링은 사용하지 않습니다.
- 각각의 `.blend`에 해당 색상 원본과 마스크를 참고 데이터로 패킹했습니다. 이 이미지는 모델 재질이나 렌더에 사용되지 않습니다.

[전체 렌더 미리보기](all_assets.png)는 열 순서 North / South / East이며, 행 순서는 MillingMachine / Press / TurretLathe / RollingMC / TreadleLathe입니다.

각 `comparison.png`는 **왼쪽 원본, 오른쪽 렌더**, 행 순서 **North / South / East**입니다. 원본의 검은 외곽선을 제거하고, 두 이미지를 각각 균일 배율로 맞춰 형태를 비교합니다. 왼쪽이 빈 칸인 경우 해당 방향의 원본이 없습니다. 비교용 회색 배경은 납품 PNG에는 포함되지 않습니다.

## 마스크와 형태 해석

마스크는 단순 전체 알파가 아니라 재질/부품 선택 마스크입니다. 예를 들어 선반기의 황동 앞판·갈색 벨트, 밀링 머신의 갈색 가이드와 일부 제어 부품, RollingMC의 회색 판재는 색상 원본에 분명히 있지만 빨간 선택 영역 밖에 있습니다. 이 부품들은 유지했습니다. 검은 외곽선은 물체의 두께·테두리·돌출부로 만들지 않았습니다.

직육면체 부품에는 북쪽 원본의 측정 사각형과 해당 마스크 선택 비율을 사용자 속성으로 기록했습니다. 전체 색상·마스크의 경계와 팔레트 분석은 [reference_analysis.json](reference_analysis.json)에 있습니다. 롤러/축과 판재의 구분도 마스크에서 확인했습니다.

주요 복원 요소:

- **TreadleLathe**: 흰 베드, 양쪽 받침, 주축대/척, 심압대, 황동 캐리지, 갈색 구동 벨트, 발판, 큰 손바퀴.
- **TurretLathe**: 같은 계열의 흰 베드와 황동 주축대 받침, 어두운 다엽 터릿, 굵은 공구 돌출부.
- **MillingMachine**: 회색 베이스, 흰 기둥/오버암, 갈색 가이드, 어두운 슬라이드와 밝은 레일/중앙 클램프.
- **Press**: 두꺼운 양측 프레임, 두 크로스빔의 어두운 개구부, 흰 램, 넓은 바닥과 발 지지대. 램 단면은 원본의 얕은 타원에 맞춘 단순 프록시입니다.
- **RollingMC**: 크기가 다른 5개 롤러, 앞쪽 축과 작은 롤러의 칼라, 곡면 판재. 원본에 없는 케이스·지지대는 추가하지 않았습니다.

## 원본 자료의 한계

Press와 RollingMC는 North 원본만 있습니다. South/East는 보이는 구조를 단순하게 이어 만든 추정 방향이며, 별도의 원본 일치 검증 대상이 아닙니다.

MillingMachine의 North/South 원본은 큰 부품의 좌우 배치가 같지만, East에서는 길이 관계도 일부 다릅니다. 선반기 원본도 양방향에 정면의 베드/캐리지 표현을 반복하는 부분이 있습니다. 따라서 하나의 실제 3D 모델을 180° 회전하면 모든 그림의 부품 배치를 동시에 일치시킬 수 없습니다. North의 큰 덩어리와 팔레트를 우선하고, East의 긴 베드와 부품 순서를 참고해 일관된 입체를 구성했습니다. 카메라에 따라 부품을 숨기거나 바꾸는 방식으로 차이를 감추지 않았습니다. 비교 이미지에서 이 차이를 확인할 수 있습니다.

## 재생성 및 검수

Blender 5.1.2에서 아래 명령을 이 폴더 기준으로 실행합니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\build_reconstructions.py'
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\verify_reconstructions.py'
```

한 에셋만 다시 생성하려면 첫 명령 끝에 `-- RollingMC`처럼 이름을 지정합니다. 스크립트의 측정 좌표/재질/높이를 수정해 재생성할 수 있습니다. 출력은 이 폴더 아래에만 기록하며 Reference와 GunBench를 수정하지 않습니다.

검수 스크립트는 저장된 5개 `.blend`를 다시 열어 15개 카메라의 실제 방향 벡터에서 64°를 확인하고, 동일 배율/중심, PNG 크기/알파, 최소 여백, 이미지 텍스처 미사용을 검사합니다. [verification.json](verification.json)의 실루엣 IoU는 외곽선 제거 및 균일 크기 맞춤 후의 **형태 비교 지표**입니다. 색상/부품 일치율이나 전체 복원 정확도를 뜻하지 않습니다. 세 방향은 비교 이미지로 별도 시각 검수했습니다.

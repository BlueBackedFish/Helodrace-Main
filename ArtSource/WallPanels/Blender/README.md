# 샌드위치 패널 벽 — Blender 원본

제작: Blender 5.1.2의 실제 메시 모델링과 UV 텍스처. 기존 AI 시안 이미지를 모델의 텍스처로 붙이지 않았다.

- `HD_StyrofoamSandwichWall.blend`: 편집 가능한 원본. 텍스처를 내부에 패킹했다.
- `HD_StyrofoamSandwichWall.glb`: 텍스처가 포함된 3D 교환 파일.
- `Textures/HD_SandwichWall_Metal_BaseColor.png`: 금속 면 베이스컬러, 512×512.
- `Textures/HD_SandwichWall_Metal_Normal.png`: 금속 면 tangent-space 노멀, 512×512, Non-Color.
- `Textures/HD_SandwichWall_Metal_Roughness.png`: 거칠기, 512×512, Non-Color.
- `Textures/HD_SandwichWall_EPS_BaseColor.png`: 흰 스티로폼 단면, 512×512.
- `HD_StyrofoamSandwichWall_Isometric.png`: 사선 검수 렌더, 768×768, 투명 배경.
- `HD_StyrofoamSandwichWall_Front.png`, `Top.png`, `End.png`: 방향별 검수 렌더, 512×512, 투명 배경.
- `validation.json`: 메시/UV/실제 두께 검증 결과.

좌표: X는 벽의 연결 방향, Y는 두께, Z는 높이. 크기는 1 × 0.18 × 1.25 Blender 단위다. 기준 벽 두께를 0.24로 가정해 25% 줄였다. 이 기준은 실제 게임 벽 메시를 측정한 값이 아니므로 엔진 연결 단계에서 대응 스케일을 확정한다.

표면은 은회색 금속 리브, 접힌 테두리, 양면 금속 외피와 흰 발포 심재로 구성된다. 상단과 절단 끝에서 심재를 볼 수 있다. 베이스컬러에는 광원 그림자를 굽지 않았으며 검수 렌더에는 형태 확인용 조명을 사용했다.

이 폴더는 직선 한 칸 PBR 원본 모델이며 게임 텍스처가 아니다. 게임용 연결 벽 아틀라스·패널 오버레이·아이콘은 `../render_wall_textures.py`가 같은 치수로 형상을 다시 만들어 건물 텍스처 규칙(고정 Emission 색, 12px 외곽선)으로 렌더한다. `../README.md` 참조.

재생성: 상위 폴더의 `build_wall_blender.py`를 Blender background 모드에서 실행한다. 생성 결과는 이 폴더에 기록된다.

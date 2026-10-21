# 포탑 디테일 및 렌더 개정

2026-10-21 검색 후 도면과 실물 사진을 직접 확인했습니다.

- [미군 TM 9-2350-252-10-2 C6 포탑 구성도 재수록](https://afvdatabase.com/usa/pics/m2bradley/m2turret1-14.jpg): 초기형 도면이므로 구성/방향 확인에만 사용.
- [M2A3 실차 포탑 전면 사진](https://afvdatabase.com/usa/pics/m2bradley/m2a3aasight.jpg): 4문 발사기, 검은 캡, 포방패 주변 수납함/배출구 확인.
- [M2A3 CIV 및 후측면 실물 사진](https://afvdatabase.com/usa/pics/m2bradley/m2a3itv.jpg): 관측장비 회전 받침, 덮개, 안테나 보호대 확인.
- [미 육군 M2A3 소개](https://www.army.mil/article/60368/battleboars_receive_new_bradley_fighting_vehicles_first_at_stewart): IBAS와 CIV 구성 확인.

사진의 연막탄 발사기는 포탑 **앞쪽 양옆에 4문씩** 있습니다. 후면 전용 발사기를
임의로 추가하지 않고 기존 3문 모델을 4문씩 8문으로 교체했습니다. 검은 캡과
지지부, 연막탄 수납함, 탄피 배출 덮개, 관측장비 회전 받침, 안테나 보호대,
인양 고리, 포신의 홈을 나타내는 디테일을 보강했습니다.

사용자 요청에 따라 차체 측면 상단의 반복 직사각형 추가 패널과 체결 볼트를
제거했습니다. 하부 스커트와 큰 루버는 유지합니다. 추가 장갑판과 ERA는 같은
개념이 아니므로 이 변경을 모든 M2A3 기본 장갑의 역사적 고증으로 일반화하지 않습니다.

## 채색과 출력

- Emission 재질에 카메라를 향하는 정도와 카메라 깊이에 따른 명도 보정을 적용.
- 카메라를 향하고 가까운 면이 더 밝으며, 광원/투사 그림자/AO는 사용하지 않음.
- 포탑 TAN 페인트에 선형 RGB 1.18배의 별도 밝기 보정. 유리와 검은 캡은 유지.
- 차체 east는 64°, 포탑/포신 별도 이미지는 90° 상면 직교 투영.
- `VF_East_Test/preview_east.png`는 차체와 포탑 상면 이미지를 회전축에 맞춰 합성.
- `preview_geometry_east.png`는 전체를 64°로 찍은 구조 확인용 렌더.
- GLB에는 카메라 의존 셰이더를 표현할 수 없어 고정 색 Emission 대체 재질을 출력.
  최종 PNG의 카메라 명암 기준은 Blender 원본입니다.

치수는 사진을 참고한 게임용 근사치이며 기존 애니메이션 계층을 유지합니다.

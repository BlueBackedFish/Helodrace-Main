# 차량 1024 출력·무장 분리

M3A3는 기존 무윤곽 1024 마스터에서 다시 가져오며, M8은 원본 `M8AGS/output/vehicle.blend`를 보존한 채 포탑 본체·M35 포신·M2HB를 2048 마스터로 분리 렌더한 뒤 1024로 축소한다. 512 이미지를 확대하지 않는다. 두 차량의 월드 drawSize, 서쪽 포탑 보정, ERA 위치와 상태별 가림을 유지한다.

M8 `output/WeaponLayers/vehicle.blend`에는 기존 포탑 아래 `M2HB_YAW`가 추가되어 있다. 원본 g0043 장착대와 g0044 기관총 및 gunmetal 면을 함께 분리한다. M2HB는 차장이 조작하며 M35/M240의 조준 선택과 분리된다. 100발 .50 BMG 탄약통과 파괴 가능한 본체·탄약통을 사용한다. M35는 21발, M240은 800발을 유지한다.

M242와 M35의 `VehicleTurretDef.recoil`을 사용한다. VF가 실제 발사 직후 recoilTracker를 시작하고 tick마다 복귀시키며, 커스텀 렌더러는 해당 추적기의 후퇴량만 포신 레이어에 적용한다. M242는 0.09타일, M35는 0.32타일 후퇴한다. 차체·포탑 본판·M2HB에는 주포 후퇴량을 적용하지 않는다. M35의 실제 발사 위치도 포신 후퇴량을 따른다.

## Cutout과 테두리 조사

- `ArtSource/IndustrialMachines/sprite_common.py:59`는 PNG 알파를 원형으로 확장한 검정 테두리를 원본 아래에 합성한다. 색상 마스크에서는 테두리를 비선택 상태로 둔다. 512px 공작기계 출력은 6px 선이다.
- `Defs/ColdWar/Buildings/Industry_ColdWar.xml` 등 일반 아이템은 `shaderType=Cutout`으로 그린다. Cutout은 알파가 낮은 픽셀을 버리는 셰이더다. 검은 선을 자동 생성하거나 이미지에 없는 선을 복원하지 않는다.
- 기존 M3A3는 합성 후 `Transparent` + 밉맵 + Trilinear였다. 반투명 경계와 저해상도 밉 단계가 선을 부드럽게 만들 수 있었다. M8은 이미 Cutout이었지만 출력이 512였다.
- 현재 런타임은 두 차량 모두 1024, Cutout, Bilinear, 밉맵 없음이다. PNG/동적 합성에 검은 선을 먼저 만들고 최종 출력에 알파 테스트를 적용한다. 가까운 거리의 선명도를 우선한 선택이며, 먼 줌에서의 앨리어싱은 실제 플레이 화면에서 추가 확인할 항목이다.
- 선의 **월드 두께는 그대로**다. M3A3는 512의 4px → 1024의 8px, M8은 512의 2px → 1024의 4px다. 두 차량의 투명 여백과 drawSize가 다르므로 같은 픽셀 선폭을 강제하지 않는다.

## 재생성

1. Blender에서 `ArtSource/M8AGS/render_weapon_layers.py` 실행.
2. `python ArtSource/VehicleRuntime/package_1024.py --mod-root <아스널 경로>`
3. `python ArtSource/VehicleRuntime/configure_weapons.py --mod-root <아스널 경로>`
4. `python ArtSource/VehicleRuntime/review.py --mod-root <아스널 경로>`

패키징과 정의 변경은 위 순서로 순차 실행한다. `review.py`는 방향별 차량을 같은 64px/타일로 비교한다. 부품 캔버스 자체를 같은 폭으로 축소한 표는 실제 차량 크기 비교가 아니다.

## 검증

실제 RimWorld/VF를 로드한 `VehicleRevisionSmoke`가 정의, 48개 Bradley 상태의 독립 포신, 실제 1024 Cutout 재질/밉맵 없음, M242·M35 후퇴/복귀, M35 발사점 후퇴, M2HB의 별도 차장 역할·조준·탄약을 확인했다. Bradley 규칙 회귀 55개도 통과했다. 이번 검증은 전체 공수 비행·착륙·저장/월드맵 통합 검증의 완료를 뜻하지 않는다.
